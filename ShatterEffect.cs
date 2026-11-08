using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.CameraModifiers;
using Terraria.Utilities;

namespace TimeStop
{
	// Client-side visuals for time resuming. The facets of the glass pane break into real 3D shards:
	// each shard is a slab of glass with a front, a back and edge faces, flying in 3D with its own spin axis,
	// drawn with perspective so pieces flying at you grow and pieces falling away shrink. Every face is lit:
	// soft diffuse shading, a Fresnel rim that brightens at grazing angles, sharp specular glints, and the
	// pale green-cyan edges real glass has. Faces are sorted back to front so overlapping shards look right.
	//
	// There is no warning: the pane is intact one frame and in pieces the next, and the pieces drop under strong gravity.
	// Normal resume: the glass falls out of the frame from around the player, with a small glass break sound.
	// Nameless Deity: a violent blast from where he is, with an inversion flash, glitter and glass thrown at the camera.
	internal static class ShatterEffect
	{
		private sealed class Shard
		{
			public Vector2[] Outline;     // in the pane's plane, relative to Position
			public Vector3 Position;      // screen x, screen y, depth (0 = the pane, positive = away from you)
			public Vector3 Velocity;
			public Matrix Orientation = Matrix.Identity;
			public Vector3 SpinAxis;
			public float SpinSpeed;
			public float Thickness;
			public float Shade;
			public int Delay;
			public int Age;
			public int Life;
		}

		private struct Glitter
		{
			public Vector2 Position;
			public Vector2 Velocity;
			public int Age;
			public int Life;
			public float Size;
		}

		private struct Face
		{
			public float Depth;
			public Vector2[] Points;
			public Vector2 Center;
			public Color CenterColor;
			public Color RimColor;
			public Color EdgeColor;
			public float EdgeThickness;
			public Vector2? Glint;
			public float GlintStrength;
			public float Alpha;
		}

		private const float FocalLength = 1150f;
		private const int FlashDuration = 14;

		// Light comes from the upper left, slightly in front of the screen.
		private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.45f, -0.75f, -0.55f));
		private static readonly Color GlassEdge = new Color(150, 235, 220);
		private static readonly Color Frost = new Color(215, 238, 255);

		private static readonly List<Shard> shards = new();
		private static readonly List<Glitter> glitter = new();
		private static readonly List<Face> faces = new();
		private static int ticksLeft;
		private static int sinceBreak;
		private static bool violentBreak;
		private static Vector2 impact;
		private static Vector2 crackImpact;

		public static bool Visible => ticksLeft > 0;

		private static bool OverlayOn => TimeStopClientConfig.Get.ShowOverlay;

		public static void Clear() {
			shards.Clear();
			glitter.Clear();
			ticksLeft = 0;
		}

		private static float FarthestCorner(Vector2 from) {
			float result = 1f;
			foreach (Vector2 corner in new[] { Vector2.Zero, new Vector2(Main.screenWidth, 0f), new Vector2(0f, Main.screenHeight), new Vector2(Main.screenWidth, Main.screenHeight) })
				result = Math.Max(result, Vector2.Distance(from, corner));
			return result;
		}

		private static Vector2 NamelessOnScreen() {
			int type = NamelessDeityCompat.NamelessType;
			if (type >= 0) {
				foreach (NPC npc in Main.ActiveNPCs) {
					if (npc.type != type)
						continue;

					Vector2 screen = npc.Center - Main.screenPosition;
					if (screen.X > 0f && screen.Y > 0f && screen.X < Main.screenWidth && screen.Y < Main.screenHeight)
						return screen;
				}
			}

			return GlassPane.Facets.Count > 0 ? GlassPane.Origin : new Vector2(Main.screenWidth, Main.screenHeight) * 0.5f;
		}

		private static Vector2 CasterOnScreen() {
			int caster = TimeStopSystem.CasterWho;
			if (caster >= 0 && caster < Main.maxPlayers && Main.player[caster].active)
				return Main.player[caster].Center - Main.screenPosition;

			return new Vector2(Main.screenWidth, Main.screenHeight) * 0.5f;
		}

		// The Nameless Deity has noticed the time stop. Remember where he is: that is where the glass will burst from.
		// Nothing is shown yet; the glass breaks without warning.
		public static void BeginCracks() {
			if (Main.dedServ)
				return;

			crackImpact = NamelessOnScreen();
		}


		// ---------- Breaking ----------

		// invertedGlass: the held strain inversion breaking away. Every shard inverts whatever is behind it, so the
		// negative image falls apart piece by piece and leaves the normal screen.
		private static bool invertedBreak;

		public static void Shatter(bool violent, bool invertedGlass = false) {
			if (Main.dedServ)
				return;

			TimeStopSounds.PlayBreak(violent);
			Clear();

			if (Main.LocalPlayer != null && Main.LocalPlayer.active) {
				Main.instance.CameraModifiers.Add(new PunchCameraModifier(Main.LocalPlayer.Center,
					Main.rand.NextVector2Unit(), Flash.Shake(violent ? 16f : 4f), violent ? 9f : 12f, violent ? 42 : 16, -1f, "TimeStopShatter"));
			}

			if (!OverlayOn && !invertedGlass)
				return;

			violentBreak = violent;
			invertedBreak = invertedGlass;
			// The god's break bursts from where he is; without him (the /tsbreak preview) it bursts from the caster.
			impact = violent && NamelessDeityCompat.NamelessPresent() ? crackImpact : CasterOnScreen();
			if (GlassPane.NeedsBuild || invertedGlass)
				GlassPane.Build(impact);   // the inverted layer covers the whole screen, so it always gets a fresh pattern

			var rand = new UnifiedRandom(Main.rand.Next());
			float farthest = FarthestCorner(impact);
			// Real glass fails almost all at once; only a couple of frames separate the first and last pieces.
			int stagger = violent ? 2 : 4;
			int longest = 0;

			foreach (GlassPane.Facet facet in GlassPane.Facets) {
				Vector2[] points = facet.Points;
				int count = points.Length;
				int pieces = count >= 6 ? 3 : count >= 4 ? 2 : 1;

				for (int piece = 0; piece < pieces; piece++) {
					// Each shard is the facet centre plus a run of its outline, so the pieces fit back together exactly.
					int start = piece * count / pieces;
					int end = (piece + 1) * count / pieces;
					var outline = new List<Vector2> { facet.Center };
					for (int i = start; i <= end; i++)
						outline.Add(points[i % count]);

					Vector2 center = Vector2.Zero;
					foreach (Vector2 point in outline)
						center += point;
					center /= outline.Count;

					var local = new Vector2[outline.Count];
					float area = 0f;
					for (int i = 0; i < local.Length; i++) {
						local[i] = outline[i] - center;
						Vector2 next = outline[(i + 1) % outline.Count] - center;
						area += local[i].X * next.Y - next.X * local[i].Y;
					}
					area = Math.Abs(area) * 0.5f;

					Vector2 away = center - impact;
					away = away.LengthSquared() > 1f ? Vector2.Normalize(away) : rand.NextVector2Unit();
					Vector2 across = new Vector2(-away.Y, away.X);
					// A normal break barely pushes the glass outward: it mostly just drops. The god's break throws it.
					float speed = violent ? rand.NextFloat(4f, 13f) : rand.NextFloat(0.2f, 1.4f);
					Vector2 flat = away * speed + across * rand.NextFloat(-1f, 1f) * (violent ? 2.5f : 0.4f)
						+ new Vector2(0f, violent ? rand.NextFloat(-6f, -1f) : rand.NextFloat(-0.8f, 0.6f));

					// Depth speed: negative flies toward you. A violent break throws a lot of glass at the camera.
					float depthSpeed = violent ? rand.NextFloat(-26f, 10f) : rand.NextFloat(-3.5f, 2.5f);

					var shard = new Shard {
						Outline = local,
						Position = new Vector3(center, 0f),
						Velocity = new Vector3(flat, depthSpeed),
						SpinAxis = Vector3.Normalize(new Vector3(rand.NextFloat(-1f, 1f), rand.NextFloat(-1f, 1f), rand.NextFloat(-0.4f, 0.4f)) + new Vector3(0.001f)),
						SpinSpeed = rand.NextFloat(violent ? 0.08f : 0.04f, violent ? 0.28f : 0.14f) * (rand.NextBool() ? 1f : -1f),
						Thickness = MathHelper.Clamp((float)Math.Sqrt(area) * 0.09f, 3f, 10f),
						Shade = facet.Shade,
						Delay = (int)(Vector2.Distance(center, impact) / farthest * stagger) + rand.Next(3),
						Life = violent ? rand.Next(75, 100) : rand.Next(60, 80),
					};
					shards.Add(shard);
					longest = Math.Max(longest, shard.Delay + shard.Life);
				}
			}

			// Fine glass dust thrown off the break.
			int glitterCount = violent ? 120 : 45;
			for (int i = 0; i < glitterCount; i++) {
				Vector2 start = violent ? impact : GlassPane.Facets[rand.Next(GlassPane.Facets.Count)].Center;
				Vector2 direction = violent ? rand.NextVector2Unit() : Vector2.Normalize(start - impact + rand.NextVector2Circular(1f, 1f));
				glitter.Add(new Glitter {
					Position = start,
					Velocity = direction * (violent ? rand.NextFloat(3f, 18f) : rand.NextFloat(0.3f, 1.8f)),
					Life = rand.Next(35, violent ? 90 : 75),
					Size = rand.NextFloat(2f, violent ? 6f : 4f),
				});
			}

			sinceBreak = 0;
			ticksLeft = longest + 2;
		}

		public static void Update() {
			if (ticksLeft <= 0)
				return;

			ticksLeft--;
			sinceBreak++;

			// Strong, constant pull: a piece at the top of the screen hits the bottom in well under a second,
			// accelerating the whole way, like glass dropping from a window.
			const float Gravity = 0.95f;
			float offScreen = Main.screenHeight + 250f;

			foreach (Shard shard in shards) {
				shard.Age++;
				if (shard.Age <= shard.Delay)
					continue;

				shard.Velocity.Y += Gravity;
				shard.Velocity.X *= 0.996f;
				shard.Velocity.Z *= 0.99f;
				shard.Position += shard.Velocity;
				shard.Orientation *= Matrix.CreateFromAxisAngle(shard.SpinAxis, shard.SpinSpeed);

				// Gone below the screen: finished.
				if (shard.Position.Y > offScreen && shard.Velocity.Y > 0f)
					shard.Age = Math.Max(shard.Age, shard.Delay + shard.Life);
			}

			float gravity = Gravity;

			for (int i = 0; i < glitter.Count; i++) {
				Glitter g = glitter[i];
				g.Age++;
				g.Velocity.Y += gravity * 0.6f;
				g.Velocity *= 0.98f;
				g.Position += g.Velocity;
				glitter[i] = g;
			}

			if (ticksLeft <= 0)
				Clear();
		}

		// ---------- 3D drawing ----------

		private static Vector2 ScreenCenter => new Vector2(Main.screenWidth, Main.screenHeight) * 0.5f;

		private static Vector3 CameraPosition => new Vector3(ScreenCenter, -FocalLength);

		private static bool Project(Vector3 point, out Vector2 screen) {
			float w = FocalLength + point.Z;
			if (w < FocalLength * 0.08f) {
				screen = Vector2.Zero;
				return false;
			}

			screen = ScreenCenter + (new Vector2(point.X, point.Y) - ScreenCenter) * (FocalLength / w);
			return true;
		}

		private static Color AddColor(Color a, Color b) {
			return new Color(Math.Min(255, a.R + b.R), Math.Min(255, a.G + b.G), Math.Min(255, a.B + b.B), Math.Min(255, a.A + b.A));
		}

		// Lighting for one face. normal is in world space; point is any point on the face.
		private static void Light(Vector3 normal, Vector3 point, out float diffuse, out float specular, out float fresnel) {
			Vector3 toCamera = Vector3.Normalize(CameraPosition - point);
			diffuse = Math.Max(0f, Vector3.Dot(normal, -LightDirection));
			Vector3 half = Vector3.Normalize(-LightDirection + toCamera);
			specular = (float)Math.Pow(Math.Max(0f, Vector3.Dot(normal, half)), 46);
			fresnel = (float)Math.Pow(1f - Math.Abs(Vector3.Dot(normal, toCamera)), 3);
		}

		private static void AddShardFaces(Shard shard, float alpha) {
			int count = shard.Outline.Length;
			float half = shard.Thickness * 0.5f;
			var front = new Vector3[count];
			var back = new Vector3[count];
			for (int i = 0; i < count; i++) {
				Vector2 p = shard.Outline[i];
				front[i] = shard.Position + Vector3.Transform(new Vector3(p, -half), shard.Orientation);
				back[i] = shard.Position + Vector3.Transform(new Vector3(p, half), shard.Orientation);
			}

			Vector3 frontNormal = Vector3.TransformNormal(-Vector3.UnitZ, shard.Orientation);
			Color tint = TimeStopSystem.TintColor;

			// Front and back faces: the glass surfaces.
			AddCap(front, frontNormal, shard, tint, alpha);
			AddCap(back, -frontNormal, shard, tint, alpha);

			// Edge faces: thin strips that glow green-cyan like real glass edges.
			Vector3 centerWorld = shard.Position;
			for (int i = 0; i < count; i++) {
				int j = (i + 1) % count;
				Vector3 mid = (front[i] + front[j] + back[i] + back[j]) * 0.25f;
				Vector3 normal = Vector3.Cross(front[j] - front[i], back[i] - front[i]);
				if (normal.LengthSquared() < 1e-6f)
					continue;

				normal.Normalize();
				if (Vector3.Dot(normal, mid - centerWorld) < 0f)
					normal = -normal;

				if (Vector3.Dot(normal, CameraPosition - mid) <= 0f)
					continue;

				if (!Project(front[i], out Vector2 a) || !Project(front[j], out Vector2 b) || !Project(back[j], out Vector2 c) || !Project(back[i], out Vector2 d))
					continue;

				Light(normal, mid, out float diffuse, out float specular, out float fresnel);
				Color body = GlassEdge * ((0.32f + 0.5f * diffuse + 0.3f * fresnel) * alpha);
				body = AddColor(body, Color.White * (0.85f * specular * alpha));
				faces.Add(new Face {
					Depth = mid.Z,
					Points = new[] { a, b, c, d },
					Center = (a + b + c + d) * 0.25f,
					CenterColor = body,
					Alpha = alpha,
					RimColor = body,
				});
			}
		}

		private static void AddCap(Vector3[] ring, Vector3 normal, Shard shard, Color tint, float alpha) {
			Vector3 center = Vector3.Zero;
			foreach (Vector3 p in ring)
				center += p;
			center /= ring.Length;

			if (Vector3.Dot(normal, CameraPosition - center) <= 0f)
				return;

			var points = new Vector2[ring.Length];
			for (int i = 0; i < ring.Length; i++) {
				if (!Project(ring[i], out points[i]))
					return;
			}

			if (!Project(center, out Vector2 screenCenter))
				return;

			Light(normal, center, out float diffuse, out float specular, out float fresnel);

			// Glass: mostly see-through, tinted, brighter at grazing angles, with a hot specular core.
			Color body = tint * ((0.22f + 0.28f * diffuse) * shard.Shade * alpha);
			Color rim = AddColor(body, Frost * (0.4f * fresnel * alpha));
			Color core = AddColor(Color.Lerp(body, Frost * (0.5f * alpha), 0.2f), Color.White * (0.75f * specular * alpha));
			rim = AddColor(rim, Color.White * (0.35f * specular * alpha));

			Color edge = Color.Lerp(Frost, Color.White, specular) * ((0.18f + 0.35f * fresnel + 0.5f * specular) * alpha);

			faces.Add(new Face {
				Depth = center.Z,
				Points = points,
				Center = screenCenter,
				CenterColor = core,
				RimColor = rim,
				EdgeColor = edge,
				EdgeThickness = 1.1f,
				Glint = specular > 0.35f ? points[0] : null,
				GlintStrength = specular * alpha,
				Alpha = alpha,
			});
		}

		// The inverted layer breaking: pieces still in place keep the screen inverted; falling pieces carry the
		// inversion with them (fading as they go); everything they leave behind is the normal screen.
		private static void DrawInvertedShards() {
			var stills = new List<(Vector2[] points, Vector2 center)>();
			foreach (Shard shard in shards) {
				int moving = shard.Age - shard.Delay;
				if (moving <= 0) {
					var points = new Vector2[shard.Outline.Length];
					Vector2 center = new Vector2(shard.Position.X, shard.Position.Y);
					for (int i = 0; i < points.Length; i++)
						points[i] = center + shard.Outline[i];
					stills.Add((points, center));
					continue;
				}

				float life = moving / (float)shard.Life;
				if (life >= 1f)
					continue;

				float alpha = life < 0.75f ? 1f : 1f - (life - 0.75f) / 0.25f;
				if (shard.Position.Z < -FocalLength * 0.55f)
					alpha *= MathHelper.Clamp((shard.Position.Z + FocalLength * 0.8f) / (FocalLength * 0.25f), 0f, 1f);
				if (alpha > 0.01f)
					AddShardFaces(shard, alpha);
			}
			faces.Sort((a, b) => b.Depth.CompareTo(a.Depth));

			// pass 1: inversion (or, with reduced flashing, a gentle dark layer instead)
			bool soft = Flash.Reduced;
			Color layer = soft ? new Color(0, 0, 10) * 0.4f : Color.White;
			GlassPane.SetBlend(soft ? Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend : GlassPane.InvertBlend);
			foreach (var (points, center) in stills)
				GlassPane.Polygon(points, center, layer, _ => layer);
			foreach (Face face in faces) {
				if (face.EdgeThickness <= 0f)
					continue;   // only the front and back surfaces carry the inversion
				Color invert = layer * face.Alpha;
				GlassPane.Polygon(face.Points, face.Center, invert, _ => invert);
			}
			GlassPane.SetBlend(Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend);

			// pass 2: the glass itself - bright edges, glints and fine glitter
			foreach (Face face in faces) {
				if (face.EdgeThickness > 0f) {
					for (int i = 0; i < face.Points.Length; i++)
						GlassPane.Line(face.Points[i], face.Points[(i + 1) % face.Points.Length], face.EdgeThickness, face.EdgeColor);
				}
				else {
					GlassPane.Polygon(face.Points, face.Center, face.CenterColor, _ => face.RimColor);   // thin side faces
				}

				if (face.Glint.HasValue)
					GlassPane.Sparkle(face.Glint.Value, 5f + 13f * face.GlintStrength, Color.White * face.GlintStrength);
			}

			foreach (Glitter g in glitter) {
				if (g.Age >= g.Life)
					continue;
				float fade = 1f - g.Age / (float)g.Life;
				GlassPane.Sparkle(g.Position, g.Size, Frost * fade);
			}
		}

		public static void DrawShards() {
			if (ticksLeft <= 0)
				return;

			faces.Clear();
			GlassPane.BeginTriangles();

			if (invertedBreak) {
				DrawInvertedShards();
				GlassPane.EndTriangles();
				return;
			}

			if (sinceBreak < FlashDuration) {
				float flash = 1f - sinceBreak / (float)FlashDuration;
				var screen = new Vector2(Main.screenWidth, Main.screenHeight);

				// The god's break inverts the whole screen for an instant before the gold flash.
				if (violentBreak && sinceBreak < 5 && !Flash.Reduced) {
					Color invert = Color.White * (0.9f * (1f - sinceBreak / 5f));
					GlassPane.SetBlend(GlassPane.InvertBlend);
					GlassPane.Triangle(Vector2.Zero, invert, new Vector2(screen.X, 0f), invert, screen, invert);
					GlassPane.Triangle(Vector2.Zero, invert, screen, invert, new Vector2(0f, screen.Y), invert);
					GlassPane.SetBlend(Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend);
				}

				Color flashColor = new Color(255, 244, 222) * Flash.Bright((violentBreak ? 0.55f : 0.09f) * flash * flash);
				GlassPane.Triangle(Vector2.Zero, flashColor, new Vector2(screen.X, 0f), flashColor, screen, flashColor);
				GlassPane.Triangle(Vector2.Zero, flashColor, screen, flashColor, new Vector2(0f, screen.Y), flashColor);
			}

			Color tint = TimeStopSystem.TintColor;
			foreach (Shard shard in shards) {
				int moving = shard.Age - shard.Delay;

				if (moving <= 0) {
					// Still part of the intact pane for a frame or two: same flat colour as the sheet, so no seams show.
					var points = new Vector2[shard.Outline.Length];
					Vector2 center = new Vector2(shard.Position.X, shard.Position.Y);
					for (int i = 0; i < points.Length; i++)
						points[i] = center + shard.Outline[i];

					Color still = tint * TimeStopSystem.TintOpacity;
					GlassPane.Polygon(points, center, still, _ => still);
					continue;
				}

				float life = moving / (float)shard.Life;
				if (life >= 1f)
					continue;

				float alpha = life < 0.75f ? 1f : 1f - (life - 0.75f) / 0.25f;

				// Shards that fly right up to the camera fade out instead of filling the screen.
				if (shard.Position.Z < -FocalLength * 0.55f)
					alpha *= MathHelper.Clamp((shard.Position.Z + FocalLength * 0.8f) / (FocalLength * 0.25f), 0f, 1f);

				if (alpha > 0.01f)
					AddShardFaces(shard, alpha);
			}

			// Painter's algorithm: farthest first.
			faces.Sort((a, b) => b.Depth.CompareTo(a.Depth));
			foreach (Face face in faces) {
				GlassPane.Polygon(face.Points, face.Center, face.CenterColor, _ => face.RimColor);
				if (face.EdgeThickness > 0f) {
					for (int i = 0; i < face.Points.Length; i++)
						GlassPane.Line(face.Points[i], face.Points[(i + 1) % face.Points.Length], face.EdgeThickness, face.EdgeColor);
				}

				if (face.Glint.HasValue)
					GlassPane.Sparkle(face.Glint.Value, 5f + 13f * face.GlintStrength, Color.White * face.GlintStrength);
			}

			foreach (Glitter g in glitter) {
				if (g.Age >= g.Life)
					continue;

				float fade = 1f - g.Age / (float)g.Life;
				float twinkle = 0.55f + 0.45f * (float)Math.Sin(g.Age * 0.6f + g.Position.X * 0.05f);
				GlassPane.Sparkle(g.Position, g.Size * (0.6f + 0.4f * twinkle), Frost * (fade * twinkle));
			}

			GlassPane.EndTriangles();
		}
	}
}

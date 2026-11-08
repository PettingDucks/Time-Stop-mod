using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Utilities;

namespace TimeStop
{
	// Client-side screen damage from Strain (spamming time stops). Cracks spread across the screen itself from
	// an impact near the player: long, mostly straight cracks radiating outward, cross cracks between them, and
	// a dense starburst of splinters at the impact. Each crack is a smooth, tapering gap that shows the live game
	// view rotated a little (its own angle per crack), like a broken glass edge catching a tilted copy of the
	// scene, traced by soft light edges. Cracks grow in as strain rises and retract as it heals.
	internal static class ScreenCracks
	{
		private sealed class Crack
		{
			public Vector2[] Points;
			public float[] Along;      // distance along the crack at each point
			public float[] Jitter;     // per-point width variation
			public float Length;
			public float Width;        // relative width (radials 1, chords 0.8, splinters 0.55)
			public float Order;        // 0..1, when it appears as strain rises
			public float Angle;        // rotation of the view inside it
			public Vector2 Pivot;
		}

		private static readonly List<Crack> cracks = new();
		private static Point builtFor;
		private static float shown;

		private static BasicEffect texturedEffect;
		private static readonly List<VertexPositionColorTexture> textured = new();

		public static bool Visible => shown > 0.01f || invertTicks > 0;

		public static void Clear() {
			cracks.Clear();
			shown = 0f;
		}

		// Leaving the world: drop everything, including a held inversion.
		public static void ClearAll() {
			Clear();
			inverted = false;
			invertTicks = 0;
		}

		// ---------- Overload inversion ----------
		// When strain truly breaks the screen, everything flips to its negative and STAYS that way for the whole
		// lockout. The moment time can be stopped again, that layer of inverted glass shatters and falls away,
		// each shard still inverting what is behind it, uncovering the normal screen.

		private static int invertTicks;     // > 0 while the inverted layer is up
		private static bool inverted;

		public static void StartInversion() {
			inverted = true;
			invertTicks = 1;
		}

		private static void BreakInversion() {
			inverted = false;
			invertTicks = 0;
			ShatterEffect.Shatter(violent: false, invertedGlass: true);
		}

		// Called every tick on clients: holds the inversion while locked out, breaks it once time stop is usable.
		private static void UpdateInversion() {
			if (!inverted)
				return;

			invertTicks++;
			if (TimeStopSystem.LocalLockoutTicks <= 0)
				BreakInversion();
		}

		public static void DrawInversion() {
			if (!inverted)
				return;

			// with reduced flashing, the "inverted" layer is a gentle dark tint instead of a full colour inversion
			bool soft = Flash.Reduced;
			Color grey = soft ? new Color(0, 0, 10) * 0.4f : Color.White;
			var screen = new Vector2(Main.screenWidth, Main.screenHeight);

			GlassPane.BeginTriangles();
			GlassPane.SetBlend(soft ? BlendState.AlphaBlend : GlassPane.InvertBlend);
			GlassPane.Triangle(Vector2.Zero, grey, new Vector2(screen.X, 0f), grey, screen, grey);
			GlassPane.Triangle(Vector2.Zero, grey, screen, grey, new Vector2(0f, screen.Y), grey);
			GlassPane.SetBlend(BlendState.AlphaBlend);
			GlassPane.EndTriangles();
		}

		// ---------- Full-screen capture (world + interface) for the view inside the cracks ----------

		private static Texture2D captured;
		private static Color[] capturedPixels;
		private static bool captureBroken;

		// Copies the finished frame, interface included, so the cracks show everything on screen rotated.
		// Only runs while cracks are visible. Falls back to the world-only image if the copy is not possible.
		private static Texture2D CaptureFullScreen() {
			if (captureBroken || !TimeStopClientConfig.Get.CracksShowInterface)
				return null;

			try {
				GraphicsDevice device = Main.graphics.GraphicsDevice;
				int w = device.PresentationParameters.BackBufferWidth;
				int h = device.PresentationParameters.BackBufferHeight;
				if (captured == null || captured.IsDisposed || captured.Width != w || captured.Height != h) {
					captured?.Dispose();
					captured = new Texture2D(device, w, h);
					capturedPixels = new Color[w * h];
				}

				device.GetBackBufferData(capturedPixels);
				captured.SetData(capturedPixels);
				return captured;
			}
			catch {
				captureBroken = true;
				return null;
			}
		}

		// target: 0..1 strain intensity. Grows in quickly, heals out slowly.
		public static void Update(float target, int caster) {
			if (Main.dedServ)
				return;

			UpdateInversion();

			shown += (target - shown) * (target > shown ? 0.2f : 0.04f);
			if (shown < 0.005f && target <= 0f) {
				shown = 0f;
				cracks.Clear();
				return;
			}

			var screen = new Point(Main.screenWidth, Main.screenHeight);
			if (shown > 0.005f && (cracks.Count == 0 || builtFor != screen))
				Build(caster, screen);
		}

		private static Vector2 ImpactPoint(int caster) {
			Player player = caster >= 0 && caster < Main.maxPlayers && Main.player[caster].active ? Main.player[caster] : Main.LocalPlayer;
			if (player != null && player.active) {
				Vector2 p = player.Center - Main.screenPosition;
				if (p.X > 0 && p.Y > 0 && p.X < Main.screenWidth && p.Y < Main.screenHeight)
					return p;
			}

			return new Vector2(Main.screenWidth, Main.screenHeight) * 0.5f;
		}

		private static List<Vector2> Kinked(UnifiedRandom rand, Vector2 start, float angle, float length, float kink, float minStep, float maxStep) {
			var pts = new List<Vector2> { start };
			Vector2 p = start;
			float travelled = 0f;
			while (travelled < length) {
				float step = rand.NextFloat(minStep, maxStep);
				angle += rand.NextFloat(-kink, kink);
				p += angle.ToRotationVector2() * step;
				travelled += step;
				pts.Add(p);
			}
			return pts;
		}

		private static Vector2 PointAlong(List<Vector2> pts, float t) {
			float total = 0f;
			for (int i = 1; i < pts.Count; i++)
				total += Vector2.Distance(pts[i - 1], pts[i]);

			float target = t * total;
			for (int i = 1; i < pts.Count; i++) {
				float seg = Vector2.Distance(pts[i - 1], pts[i]);
				if (target <= seg)
					return Vector2.Lerp(pts[i - 1], pts[i], seg > 0f ? target / seg : 0f);
				target -= seg;
			}
			return pts[^1];
		}

		private static void Add(UnifiedRandom rand, List<Vector2> pts, float width, float order) {
			var c = new Crack {
				Points = pts.ToArray(),
				Along = new float[pts.Count],
				Jitter = new float[pts.Count],
				Width = width,
				Order = order,
				Angle = MathHelper.ToRadians(rand.NextFloat(8f, 24f)) * (rand.NextBool() ? 1f : -1f),
			};
			for (int i = 0; i < pts.Count; i++) {
				if (i > 0)
					c.Along[i] = c.Along[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
				c.Jitter[i] = rand.NextFloat(0.45f, 1.15f);
			}
			c.Length = c.Along[^1];
			c.Pivot = PointAlong(pts, 0.5f);
			cracks.Add(c);
		}

		private static void Build(int caster, Point screen) {
			cracks.Clear();
			builtFor = screen;
			var rand = new UnifiedRandom(Main.rand.Next());
			Vector2 impact = ImpactPoint(caster);
			float diagonal = new Vector2(screen.X, screen.Y).Length();
			float scale = diagonal / 1468f;

			// long radial cracks, appearing innermost-first as strain rises
			const int Radials = 18;
			var angles = new List<float>();
			for (int i = 0; i < Radials; i++)
				angles.Add(rand.NextFloat(MathHelper.TwoPi));
			angles.Sort();
			var radials = new List<List<Vector2>>();
			var orders = new List<float>();
			for (int i = 0; i < Radials; i++)
				orders.Add(0.05f + 0.55f * i / Radials);
			for (int i = orders.Count - 1; i > 0; i--) {
				int j = rand.Next(i + 1);
				(orders[i], orders[j]) = (orders[j], orders[i]);
			}
			for (int i = 0; i < Radials; i++) {
				var line = Kinked(rand, impact, angles[i], diagonal * 1.3f * rand.NextFloat(0.55f, 1f), 0.1f, 60f * scale, 160f * scale);
				radials.Add(line);
				Add(rand, line, 1f, orders[i]);
			}

			// cross cracks between neighbouring radials, at varied distances
			for (int k = 0; k < 40; k++) {
				int i = rand.Next(Radials);
				int j = (i + (rand.Next(3) == 0 ? 2 : 1)) % Radials;
				Vector2 p = PointAlong(radials[i], (float)Math.Pow(rand.NextFloat(0.08f, 0.9f), 1.4));
				Vector2 q = PointAlong(radials[j], (float)Math.Pow(rand.NextFloat(0.08f, 0.9f), 1.4));
				Vector2 mid = (p + q) * 0.5f + rand.NextVector2Circular(25f * scale, 25f * scale);
				Add(rand, new List<Vector2> { p, mid, q }, 0.8f, rand.NextFloat(0.35f, 0.95f));
			}

			// dense splinters right at the impact
			for (int k = 0; k < 30; k++) {
				float a = rand.NextFloat(MathHelper.TwoPi);
				Vector2 s = impact + a.ToRotationVector2() * rand.NextFloat(0f, 30f * scale);
				Add(rand, Kinked(rand, s, a, rand.NextFloat(20f, 110f) * scale, 0.2f, 12f * scale, 40f * scale), 0.55f, rand.NextFloat(0f, 0.2f));
			}
			for (int k = 0; k < 15; k++) {
				float a = rand.NextFloat(MathHelper.TwoPi);
				Vector2 p = impact + a.ToRotationVector2() * rand.NextFloat(15f, 80f) * scale;
				float b = a + MathHelper.PiOver2 + rand.NextFloat(-0.6f, 0.6f);
				Add(rand, new List<Vector2> { p, p + b.ToRotationVector2() * rand.NextFloat(15f, 45f) * scale }, 0.5f, rand.NextFloat(0.05f, 0.3f));
			}
		}

		private static Vector2 RotatedUV(Vector2 position, Crack c, Vector2 screenSize) {
			Vector2 d = position - c.Pivot;
			float cos = (float)Math.Cos(c.Angle), sin = (float)Math.Sin(c.Angle);
			Vector2 r = c.Pivot + new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
			return new Vector2(MathHelper.Clamp(r.X / screenSize.X, 0f, 1f), MathHelper.Clamp(r.Y / screenSize.Y, 0f, 1f));
		}

		private static Texture2D LiveScreen {
			get {
				RenderTarget2D target = Main.screenTarget;
				if (target == null || target.IsDisposed || Main.drawToScreen)
					return null;
				return target;
			}
		}

		public static void Draw() {
			if (shown <= 0.01f || cracks.Count == 0)
				return;

			float intensity = shown;
			float baseWidth = MathHelper.Lerp(3f, 9f, intensity);
			var screenSize = new Vector2(Main.screenWidth, Main.screenHeight);
			Texture2D live = CaptureFullScreen() ?? LiveScreen;
			Color glassFill = new Color(235, 242, 252) * 0.8f;

			// gather every visible crack as a strip of quads (left/right edge points)
			var strips = new List<(Vector2[] left, Vector2[] right, Crack crack)>();
			foreach (Crack c in cracks) {
				float reveal = MathHelper.Clamp((intensity * 1.25f - c.Order) / 0.25f, 0f, 1f);
				if (reveal <= 0f || c.Length < 1f)
					continue;

				float visibleLength = reveal * c.Length;
				var left = new List<Vector2>();
				var right = new List<Vector2>();
				for (int i = 0; i < c.Points.Length; i++) {
					Vector2 p = c.Points[i];
					float along = c.Along[i];
					if (along > visibleLength) {
						// cut the crack where it currently ends
						float segStart = c.Along[i - 1];
						float u = (visibleLength - segStart) / Math.Max(1f, along - segStart);
						p = Vector2.Lerp(c.Points[i - 1], c.Points[i], u);
						along = visibleLength;
					}

					Vector2 dir = i < c.Points.Length - 1 ? c.Points[i + 1] - c.Points[i] : c.Points[i] - c.Points[i - 1];
					if (dir.LengthSquared() < 0.001f)
						dir = Vector2.UnitX;
					dir.Normalize();
					Vector2 normal = new Vector2(-dir.Y, dir.X);

					float t = along / Math.Max(1f, c.Length);
					float taper = Math.Min(1f, Math.Min(t * 5f, (1f - t) * 3f + 0.25f));
					if (along >= visibleLength)
						taper = Math.Min(taper, 0.15f);   // growing tip comes to a point
					float half = Math.Max(0.6f, baseWidth * c.Width * taper * c.Jitter[i]) * 0.5f;

					left.Add(p + normal * half);
					right.Add(p - normal * half);
					if (along >= visibleLength)
						break;
				}

				if (left.Count >= 2)
					strips.Add((left.ToArray(), right.ToArray(), c));
			}

			GlassPane.BeginTriangles();

			// 1. inside each crack: the live screen, rotated (or a pale glass fill if the screen image is unavailable)
			if (live != null) {
				textured.Clear();
				Color tintWhite = Color.White;
				foreach (var (left, right, c) in strips) {
					for (int i = 0; i < left.Length - 1; i++) {
						Vector2 a = left[i], b = right[i], d = left[i + 1], e = right[i + 1];
						textured.Add(new VertexPositionColorTexture(new Vector3(a, 0f), tintWhite, RotatedUV(a, c, screenSize)));
						textured.Add(new VertexPositionColorTexture(new Vector3(b, 0f), tintWhite, RotatedUV(b, c, screenSize)));
						textured.Add(new VertexPositionColorTexture(new Vector3(e, 0f), tintWhite, RotatedUV(e, c, screenSize)));
						textured.Add(new VertexPositionColorTexture(new Vector3(a, 0f), tintWhite, RotatedUV(a, c, screenSize)));
						textured.Add(new VertexPositionColorTexture(new Vector3(e, 0f), tintWhite, RotatedUV(e, c, screenSize)));
						textured.Add(new VertexPositionColorTexture(new Vector3(d, 0f), tintWhite, RotatedUV(d, c, screenSize)));
					}
				}
				DrawTextured(live);
			}

			foreach (var (left, right, _) in strips) {
				for (int i = 0; i < left.Length - 1; i++) {
					Color fill = live != null ? Color.White * 0.1f : glassFill;   // glass edges read a touch lighter
					GlassPane.Triangle(left[i], fill, right[i], fill, right[i + 1], fill);
					GlassPane.Triangle(left[i], fill, right[i + 1], fill, left[i + 1], fill);
				}
			}

			// 2. soft light edges along both sides of every crack
			Color edge = new Color(248, 251, 255) * (0.35f + 0.25f * intensity);
			foreach (var (left, right, _) in strips) {
				for (int i = 0; i < left.Length - 1; i++) {
					GlassPane.Line(left[i], left[i + 1], 1f, edge);
					GlassPane.Line(right[i], right[i + 1], 1f, edge);
				}
			}

			GlassPane.EndTriangles();
		}

		private static void DrawTextured(Texture2D texture) {
			if (textured.Count < 3)
				return;

			GraphicsDevice device = Main.graphics.GraphicsDevice;
			if (texturedEffect == null || texturedEffect.IsDisposed)
				texturedEffect = new BasicEffect(device) { VertexColorEnabled = true, TextureEnabled = true, LightingEnabled = false };

			texturedEffect.Texture = texture;
			texturedEffect.World = Matrix.Identity;
			texturedEffect.View = Matrix.Identity;
			texturedEffect.Projection = Matrix.CreateOrthographicOffCenter(0f, Main.screenWidth, Main.screenHeight, 0f, -1f, 1f);

			GlassPane.SetBlend(BlendState.AlphaBlend);   // flushes anything pending first
			device.SamplerStates[0] = SamplerState.LinearClamp;
			VertexPositionColorTexture[] data = textured.ToArray();
			foreach (EffectPass pass in texturedEffect.CurrentTechnique.Passes) {
				pass.Apply();
				device.DrawUserPrimitives(PrimitiveType.TriangleList, data, 0, data.Length / 3);
			}
		}
	}
}

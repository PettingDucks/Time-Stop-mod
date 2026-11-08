using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Utilities;

namespace TimeStop
{
	// The pane of "stopped time" glass laid over the screen. It is split into irregular facets
	// (a Voronoi pattern, densest around the player who stopped time) so it can form, crack and break
	// like real glass. Also holds a small triangle renderer, since the facets and shards are polygons.
	internal static class GlassPane
	{
		internal sealed class Facet
		{
			public Vector2[] Points;
			public Vector2 Center;
			public float DistanceFromOrigin;
			// Small per-facet brightness change so neighbouring facets catch the light differently.
			public float Shade;
		}

		public static readonly List<Facet> Facets = new();
		public static Vector2 Origin { get; private set; }
		public static float MaxDistance { get; private set; } = 1f;

		private static Point builtForScreen;

		public static bool NeedsBuild => Facets.Count == 0 || builtForScreen != new Point(Main.screenWidth, Main.screenHeight);

		public static void Build(Vector2 origin) {
			Facets.Clear();
			Origin = origin;
			builtForScreen = new Point(Main.screenWidth, Main.screenHeight);

			float width = Main.screenWidth;
			float height = Main.screenHeight;
			var rand = new UnifiedRandom(Main.rand.Next());

			MaxDistance = 1f;
			foreach (Vector2 corner in new[] { Vector2.Zero, new Vector2(width, 0f), new Vector2(0f, height), new Vector2(width, height) })
				MaxDistance = Math.Max(MaxDistance, Vector2.Distance(origin, corner));

			// Seeds: many small facets close to the origin, larger ones further out, like an impact pattern.
			var seeds = new List<Vector2> { origin };
			int attempts = 0;
			while (seeds.Count < 58 && attempts++ < 2000) {
				float distance = (float)Math.Pow(rand.NextFloat(), 1.55f) * MaxDistance * 1.05f;
				float angle = rand.NextFloat(MathHelper.TwoPi);
				Vector2 seed = origin + angle.ToRotationVector2() * distance;
				if (seed.X < -60f || seed.Y < -60f || seed.X > width + 60f || seed.Y > height + 60f)
					continue;

				// Keep seeds from bunching up so no facet is a sliver.
				float minimumGap = 26f + distance * 0.08f;
				bool tooClose = false;
				foreach (Vector2 other in seeds) {
					if (Vector2.DistanceSquared(other, seed) < minimumGap * minimumGap) {
						tooClose = true;
						break;
					}
				}

				if (!tooClose)
					seeds.Add(seed);
			}

			var screen = new List<Vector2> { new(0f, 0f), new(width, 0f), new(width, height), new(0f, height) };
			for (int i = 0; i < seeds.Count; i++) {
				List<Vector2> polygon = screen;
				for (int j = 0; j < seeds.Count && polygon.Count > 2; j++) {
					if (i == j)
						continue;

					// Keep the side of the bisector that is closer to seed i.
					Vector2 normal = seeds[j] - seeds[i];
					float limit = Vector2.Dot(normal, (seeds[i] + seeds[j]) * 0.5f);
					polygon = Clip(polygon, normal, limit);
				}

				if (polygon.Count < 3)
					continue;

				Vector2 center = Vector2.Zero;
				foreach (Vector2 point in polygon)
					center += point;
				center /= polygon.Count;

				Facets.Add(new Facet {
					Points = polygon.ToArray(),
					Center = center,
					DistanceFromOrigin = Vector2.Distance(center, origin),
					Shade = rand.NextFloat(0.75f, 1.25f),
				});
			}
		}

		// Sutherland-Hodgman: keeps the part of the polygon where dot(normal, point) <= limit.
		private static List<Vector2> Clip(List<Vector2> polygon, Vector2 normal, float limit) {
			var result = new List<Vector2>(polygon.Count + 2);
			for (int i = 0; i < polygon.Count; i++) {
				Vector2 current = polygon[i];
				Vector2 next = polygon[(i + 1) % polygon.Count];
				float currentSide = Vector2.Dot(normal, current) - limit;
				float nextSide = Vector2.Dot(normal, next) - limit;

				if (currentSide <= 0f)
					result.Add(current);

				if ((currentSide <= 0f) != (nextSide <= 0f)) {
					float t = currentSide / (currentSide - nextSide);
					result.Add(Vector2.Lerp(current, next, t));
				}
			}

			return result;
		}

		// A fixed diagonal glare band across the pane, so the glass reads as glass even while nothing moves.
		public static float Glare(Vector2 point) {
			float along = (point.X * 0.8f + point.Y) / (Main.screenWidth * 0.8f + Main.screenHeight);
			float band = Math.Max(0f, 1f - Math.Abs(along - 0.32f) / 0.05f);
			float thin = Math.Max(0f, 1f - Math.Abs(along - 0.41f) / 0.012f);
			return band * 0.6f + thin * 0.9f;
		}

		// ---------- Triangle renderer ----------

		private static BasicEffect effect;
		private static readonly VertexPositionColor[] vertices = new VertexPositionColor[6144];
		private static int vertexCount;
		private static bool drawing;

		// Ends the interface sprite batch so raw triangles can be drawn on top of the game view.
		public static void BeginTriangles() {
			if (drawing)
				return;

			Main.spriteBatch.End();
			GraphicsDevice device = Main.graphics.GraphicsDevice;
			if (effect == null || effect.IsDisposed)
				effect = new BasicEffect(device) { VertexColorEnabled = true, TextureEnabled = false, LightingEnabled = false };

			effect.World = Matrix.Identity;
			effect.View = Matrix.Identity;
			effect.Projection = Matrix.CreateOrthographicOffCenter(0f, Main.screenWidth, Main.screenHeight, 0f, -1f, 1f);
			device.BlendState = BlendState.AlphaBlend;
			device.RasterizerState = RasterizerState.CullNone;
			device.DepthStencilState = DepthStencilState.None;
			vertexCount = 0;
			drawing = true;
		}

		// Restarts the sprite batch the way the interface layer had it (no scaling).
		public static void EndTriangles() {
			if (!drawing)
				return;

			Flush();
			drawing = false;
			Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
				DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Matrix.Identity);
		}

		// Drawing grey (g, g, g) with this inverts the screen underneath by g: 0 leaves it, 1 fully inverts it.
		// result = src * (1 - dst) + dst * (1 - src)
		public static readonly BlendState InvertBlend = new() {
			ColorSourceBlend = Blend.InverseDestinationColor,
			ColorDestinationBlend = Blend.InverseSourceColor,
			ColorBlendFunction = BlendFunction.Add,
			AlphaSourceBlend = Blend.Zero,
			AlphaDestinationBlend = Blend.One,
			AlphaBlendFunction = BlendFunction.Add,
		};

		// Switches blending for the triangles that follow (call between BeginTriangles and EndTriangles).
		public static void SetBlend(BlendState blend) {
			Flush();
			Main.graphics.GraphicsDevice.BlendState = blend;
		}

		// Filled circle as a triangle fan.
		public static void Disc(Vector2 center, float radius, Color centerColor, Color rimColor, int segments = 64) {
			for (int i = 0; i < segments; i++) {
				Vector2 a = center + (MathHelper.TwoPi * i / segments).ToRotationVector2() * radius;
				Vector2 b = center + (MathHelper.TwoPi * (i + 1) / segments).ToRotationVector2() * radius;
				Triangle(center, centerColor, a, rimColor, b, rimColor);
			}
		}

		// Arc stroke from startAngle sweeping by sweep radians.
		public static void Arc(Vector2 center, float radius, float startAngle, float sweep, float thickness, Color color, int segmentsPerCircle = 96) {
			int segments = Math.Max(1, (int)(Math.Abs(sweep) / MathHelper.TwoPi * segmentsPerCircle));
			for (int i = 0; i < segments; i++) {
				float a0 = startAngle + sweep * i / segments;
				float a1 = startAngle + sweep * (i + 1) / segments;
				Vector2 inner0 = center + a0.ToRotationVector2() * (radius - thickness * 0.5f);
				Vector2 outer0 = center + a0.ToRotationVector2() * (radius + thickness * 0.5f);
				Vector2 inner1 = center + a1.ToRotationVector2() * (radius - thickness * 0.5f);
				Vector2 outer1 = center + a1.ToRotationVector2() * (radius + thickness * 0.5f);
				Triangle(inner0, color, outer0, color, outer1, color);
				Triangle(inner0, color, outer1, color, inner1, color);
			}
		}

		private static void Flush() {
			if (vertexCount < 3)
				return;

			GraphicsDevice device = Main.graphics.GraphicsDevice;
			foreach (EffectPass pass in effect.CurrentTechnique.Passes) {
				pass.Apply();
				device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertexCount / 3);
			}

			vertexCount = 0;
		}

		public static void Triangle(Vector2 a, Color colorA, Vector2 b, Color colorB, Vector2 c, Color colorC) {
			if (vertexCount + 3 > vertices.Length)
				Flush();

			vertices[vertexCount++] = new VertexPositionColor(new Vector3(a, 0f), colorA);
			vertices[vertexCount++] = new VertexPositionColor(new Vector3(b, 0f), colorB);
			vertices[vertexCount++] = new VertexPositionColor(new Vector3(c, 0f), colorC);
		}

		// Filled polygon as a fan from its centre; centre and rim colours blend for a soft glassy gradient.
		public static void Polygon(IList<Vector2> points, Vector2 center, Color centerColor, Func<Vector2, Color> rimColor) {
			for (int i = 0; i < points.Count; i++) {
				Vector2 a = points[i];
				Vector2 b = points[(i + 1) % points.Count];
				Triangle(center, centerColor, a, rimColor(a), b, rimColor(b));
			}
		}

		public static void Line(Vector2 start, Vector2 end, float thickness, Color color) {
			Vector2 direction = end - start;
			float length = direction.Length();
			if (length < 0.01f)
				return;

			Vector2 side = new Vector2(-direction.Y, direction.X) / length * (thickness * 0.5f);
			Triangle(start - side, color, start + side, color, end + side, color);
			Triangle(start - side, color, end + side, color, end - side, color);
		}

		// Four-pointed sparkle, used for glints on glass edges.
		public static void Sparkle(Vector2 center, float size, Color color) {
			Color clear = Color.Transparent;
			float thin = size * 0.16f;
			Triangle(center + new Vector2(-thin, 0f), color, center + new Vector2(0f, -size), clear, center + new Vector2(thin, 0f), color);
			Triangle(center + new Vector2(-thin, 0f), color, center + new Vector2(0f, size), clear, center + new Vector2(thin, 0f), color);
			Triangle(center + new Vector2(0f, -thin), color, center + new Vector2(-size, 0f), clear, center + new Vector2(0f, thin), color);
			Triangle(center + new Vector2(0f, -thin), color, center + new Vector2(size, 0f), clear, center + new Vector2(0f, thin), color);
		}
	}
}

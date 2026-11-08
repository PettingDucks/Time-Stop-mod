using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace TimeStop
{
	// Client-side "Frame Snap" start animation. Four frame bars slam in from the screen edges, a white flash
	// takes the frame, the world flips to its negative for an instant, and it settles under the smooth glass
	// pane that stays up for the whole time stop. No border or markings remain: only the glass.
	internal static class TimeStopIntro
	{
		private const int BarTicks = 8;          // bars slam in
		private const int FlashTicks = 3;        // white flash: the frame is taken
		private const int NegativeTicks = 8;     // the negative fades back to normal
		private const int GlareTicks = 16;       // glare streaks fade in on the finished glass
		private const int Duration = BarTicks + FlashTicks + NegativeTicks + GlareTicks;

		private static readonly Color Frost = new Color(215, 238, 255);

		private static int ticks;
		private static int caster = -1;
		private static bool snapped;

		public static bool Active => ticks > 0;

		private static int Elapsed => Duration - ticks;

		private static float Progress => 1f - ticks / (float)Duration;

		// 0 to 1 over the start animation, 1 afterwards. Used by the medallion to pop in.
		public static float AppearProgress => Active ? Progress : 1f;

		private static Vector2 CasterOnScreen {
			get {
				if (caster >= 0 && caster < Main.maxPlayers && Main.player[caster].active)
					return Main.player[caster].Center - Main.screenPosition;

				return new Vector2(Main.screenWidth, Main.screenHeight) * 0.5f;
			}
		}

		public static void Begin(int casterWho) {
			if (Main.dedServ)
				return;

			caster = casterWho;
			ticks = Duration;
			snapped = false;

			// The facet layout is only used as the break pattern later; nothing of it is drawn now.
			GlassPane.Build(CasterOnScreen);
			TimeStopHud.PickConstellation();
			TimeStopSounds.PlayStart();
		}

		public static void Clear() {
			ticks = 0;
		}

		public static void Update() {
			if (ticks <= 0)
				return;

			ticks--;

			// The jolt lands on the snap, when the flash fires.
			if (!snapped && Elapsed >= BarTicks) {
				snapped = true;
				if (Main.LocalPlayer != null && Main.LocalPlayer.active) {
					Main.instance.CameraModifiers.Add(new Terraria.Graphics.CameraModifiers.PunchCameraModifier(
						Main.LocalPlayer.Center, Main.rand.NextVector2Unit(), Flash.Shake(7f), 14f, 14, -1f, "TimeStopStart"));
				}
			}
		}

		private static float EaseOut(float t) => 1f - (float)Math.Pow(1f - t, 3);

		private static void FullScreen(Color color) {
			var screen = new Vector2(Main.screenWidth, Main.screenHeight);
			GlassPane.Triangle(Vector2.Zero, color, new Vector2(screen.X, 0f), color, screen, color);
			GlassPane.Triangle(Vector2.Zero, color, screen, color, new Vector2(0f, screen.Y), color);
		}

		private static void Rect(float x0, float y0, float x1, float y1, Color color) {
			GlassPane.Triangle(new Vector2(x0, y0), color, new Vector2(x1, y0), color, new Vector2(x1, y1), color);
			GlassPane.Triangle(new Vector2(x0, y0), color, new Vector2(x1, y1), color, new Vector2(x0, y1), color);
		}

		// Draws the glass, and while starting, the frame snap. tint is the pane colour with its opacity applied.
		public static void DrawTint(Color tint) {
			if (GlassPane.NeedsBuild)
				GlassPane.Build(CasterOnScreen);

			float width = Main.screenWidth;
			float height = Main.screenHeight;
			int elapsed = Active ? Elapsed : Duration;

			GlassPane.BeginTriangles();

			if (elapsed < BarTicks) {
				// Phase 1: the frame bars slam in from all four edges. No glass yet.
				float slam = EaseOut((elapsed + 1) / (float)BarTicks);
				float bar = Math.Min(width, height) * 0.07f * slam;
				Color barColor = Frost * 0.95f;
				Rect(0f, 0f, width, bar, barColor);
				Rect(0f, height - bar, width, height, barColor);
				Rect(0f, 0f, bar, height, barColor);
				Rect(width - bar, 0f, width, height, barColor);
			}
			else {
				int afterSnap = elapsed - BarTicks;

				// Phase 3: the world flips to its negative for an instant, fading back.
				if (afterSnap >= FlashTicks && afterSnap < FlashTicks + NegativeTicks && !Flash.Reduced) {
					float negative = 1f - (afterSnap - FlashTicks) / (float)NegativeTicks;
					GlassPane.SetBlend(GlassPane.InvertBlend);
					FullScreen(Color.White * (0.9f * negative * negative));
					GlassPane.SetBlend(Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend);
				}

				// The glass itself: one smooth sheet, from the snap onward.
				FullScreen(tint);

				// Glare streaks fade in once the glass has settled.
				int glareStart = FlashTicks + NegativeTicks;
				float glareIn = MathHelper.Clamp((afterSnap - glareStart) / (float)GlareTicks, 0f, 1f);
				if (glareIn > 0f) {
					var screen = new Vector2(width, height);
					GlareBand(screen, 0.32f, 0.05f, Color.White * (0.05f * glareIn));
					GlareBand(screen, 0.41f, 0.012f, Color.White * (0.07f * glareIn));
				}

				// Phase 2: the white flash as the frame is taken.
				if (afterSnap < FlashTicks) {
					float flash = 1f - afterSnap / (float)FlashTicks;
					FullScreen(new Color(250, 252, 255) * Flash.Bright(0.92f * (0.5f + 0.5f * flash)));
				}
			}

			GlassPane.EndTriangles();
		}

		// A soft diagonal light streak: brightest along its centre line, fading to nothing at its edges.
		private static void GlareBand(Vector2 screen, float position, float halfWidth, Color color) {
			Vector2 axis = new Vector2(0.8f, 1f);
			float span = screen.X * 0.8f + screen.Y;
			float k = position * span;
			float offset = halfWidth * span / axis.Length();
			Vector2 normal = Vector2.Normalize(axis);

			Vector2 a = new Vector2(-screen.X * 0.5f, k + 0.8f * screen.X * 0.5f);
			Vector2 b = new Vector2(screen.X * 1.5f, k - 0.8f * screen.X * 1.5f);
			Color clear = Color.Transparent;

			GlassPane.Triangle(a, color, b, color, b + normal * offset, clear);
			GlassPane.Triangle(a, color, b + normal * offset, clear, a + normal * offset, clear);
			GlassPane.Triangle(a, color, b, color, b - normal * offset, clear);
			GlassPane.Triangle(a, color, b - normal * offset, clear, a - normal * offset, clear);
		}
	}
}

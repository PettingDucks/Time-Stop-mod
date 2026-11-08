using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace TimeStop
{
	// The countdown: a glass medallion at the top of the screen holding a real constellation, picked at random
	// each time time stops (74 constellations, real star positions, brightness and colours; see ConstellationData).
	// A ring around it drains as the time stop runs down and its twelve marks go dark one by one. The stars go
	// out in turn, dimmest first and brightest last, each with a final flare. The right half of the medallion
	// is darkened glass behind a seam ("Orion's Half"). Nothing twinkles, because time is stopped.
	// When the Nameless Deity is forcing his way in, it shakes, the ring turns gold and the stars stutter.
	internal static class TimeStopHud
	{
		private static readonly Color Frost = new Color(215, 238, 255);
		private static readonly Color Gold = new Color(255, 214, 130);

		private static ConstellationData.Constellation current;

		// For each star, its place in the order stars go out (0 goes first).
		private static int[] goOutRank;

		// Chooses a new random constellation. Called each time time stops.
		public static void PickConstellation() {
			var all = ConstellationData.All;
			current = all[Main.rand.Next(all.Length)];

			int count = current.Stars.Length;
			int[] byBrightness = new int[count];
			for (int i = 0; i < count; i++)
				byBrightness[i] = i;

			// Dimmest (largest magnitude) first, brightest last.
			Array.Sort(byBrightness, (a, b) => current.Magnitudes[b].CompareTo(current.Magnitudes[a]));
			goOutRank = new int[count];
			for (int order = 0; order < count; order++)
				goOutRank[byBrightness[order]] = order;
		}

		// Star size from apparent magnitude: Sirius-bright stars are big, faint ones small.
		private static float SizeFor(float magnitude) => MathHelper.Clamp(1.3f - 0.17f * magnitude, 0.38f, 1.35f);

		// crack: 0 to 1 while the Nameless Deity is forcing his way in.
		// refilling: time is flowing and the tank is filling back up; drawn dimmer and a little smaller.
		public static void Draw(float crack, bool refilling = false) {
			if (TimeStopSystem.TotalTicks <= 0)
				return;

			if (current == null)
				PickConstellation();

			float fraction = MathHelper.Clamp(TimeStopSystem.TicksLeft / (float)TimeStopSystem.TotalTicks, 0f, 1f);
			float scale = Main.UIScale * (refilling ? 0.8f : 1f);
			float appear = refilling ? 1f : TimeStopIntro.AppearProgress;
			float pop = appear >= 1f ? 1f : 1f + 0.18f * (float)Math.Sin(appear * MathHelper.Pi) - (1f - appear) * 0.6f;
			float radius = 44f * scale * Math.Max(0.05f, pop);
			float opacity = MathHelper.Clamp(appear * 2f, 0f, 1f) * (refilling ? 0.6f : 1f);

			Vector2 center = new Vector2(Main.screenWidth * 0.5f, 30f * scale + radius + 8f * scale);
			float shake = crack * 6f;
			if (shake > 0f)
				center += Main.rand.NextVector2Circular(shake, shake);

			Color tint = TimeStopSystem.TintColor;
			Color ringColor = Color.Lerp(new Color(150, 212, 255), Gold, crack);

			GlassPane.BeginTriangles();

			// Soft dark backing so the medallion reads on bright backgrounds.
			GlassPane.Disc(center, radius * 1.35f, new Color(8, 12, 28) * (0.5f * opacity), Color.Transparent);

			// Glass body, clear on the left half, smoked on the right half.
			GlassPane.Disc(center, radius, tint * (0.42f * opacity), tint * (0.22f * opacity));
			HalfDisc(center, radius, new Color(0, 0, 8) * (0.28f * opacity));
			GlassPane.Line(center + new Vector2(0f, -radius), center + new Vector2(0f, radius), 1.2f * scale, Frost * (0.3f * opacity));

			// Highlight on the upper left of the glass.
			GlassPane.Arc(center, radius * 0.8f, -2.75f, 0.95f, 2.5f * scale, Color.White * (0.16f * opacity));

			// Rims.
			GlassPane.Arc(center, radius, 0f, MathHelper.TwoPi, 2.4f * scale, Frost * (0.55f * opacity));
			GlassPane.Arc(center, radius + 9f * scale, 0f, MathHelper.TwoPi, 1f * scale, Frost * (0.18f * opacity));

			// Twelve hour marks; the ones the time has already passed go dark.
			for (int hour = 0; hour < 12; hour++) {
				float angle = -MathHelper.PiOver2 + MathHelper.TwoPi * hour / 12f;
				bool remaining = hour / 12f < fraction;
				float length = (hour % 3 == 0 ? 8f : 5f) * scale;
				Vector2 direction = angle.ToRotationVector2();
				Color color = (remaining ? Frost * 0.8f : Frost * 0.15f) * opacity;
				GlassPane.Line(center + direction * (radius - 3f * scale), center + direction * (radius - 3f * scale - length),
					(hour % 3 == 0 ? 2.2f : 1.4f) * scale, color);
			}

			// The draining ring, with a bright head where the remaining time ends.
			float sweep = MathHelper.TwoPi * fraction;
			if (sweep > 0.01f) {
				GlassPane.Arc(center, radius + 5f * scale, -MathHelper.PiOver2, sweep, 6f * scale, ringColor * (0.25f * opacity));
				GlassPane.Arc(center, radius + 5f * scale, -MathHelper.PiOver2, sweep, 2.6f * scale, ringColor * (0.95f * opacity));
				Vector2 head = center + (-MathHelper.PiOver2 + sweep).ToRotationVector2() * (radius + 5f * scale);
				GlassPane.Sparkle(head, 9f * scale, Color.White * opacity);
			}

			// The constellation. Lines join stars that are still lit.
			int count = current.Stars.Length;
			int[] lines = current.Lines;
			for (int i = 0; i + 1 < lines.Length; i += 2) {
				int a = lines[i];
				int b = lines[i + 1];
				if (!Lit(a, fraction) || !Lit(b, fraction))
					continue;

				GlassPane.Line(StarPosition(center, radius, a), StarPosition(center, radius, b), 1f * scale, Frost * (0.28f * opacity));
			}

			for (int i = 0; i < count; i++) {
				Vector2 position = StarPosition(center, radius, i);
				Color color = current.Colors[i];
				float stutter = crack > 0f ? 0.55f + 0.45f * Main.rand.NextFloat() : 1f;
				float size = SizeFor(current.Magnitudes[i]) * scale;
				float threshold = goOutRank[i] / (float)count;

				if (fraction > threshold) {
					GlassPane.Disc(position, 6.5f * size, color * (0.22f * opacity * stutter), Color.Transparent, 16);
					GlassPane.Sparkle(position, 6.5f * size, color * (opacity * stutter));
					GlassPane.Disc(position, 1.7f * size, Color.White * (opacity * stutter), color * (opacity * stutter), 10);
				}
				else {
					// Just went out: one last flare, then a faint ember.
					float sinceOut = (threshold - fraction) * count / 0.35f;
					if (sinceOut < 1f) {
						float flare = 1f - sinceOut;
						GlassPane.Sparkle(position, (6f + 13f * flare) * size, color * (flare * opacity));
						GlassPane.Disc(position, 8f * size * flare, Color.White * (0.4f * flare * opacity), Color.Transparent, 16);
					}

					GlassPane.Disc(position, 1.1f * size, color * (0.14f * opacity), Color.Transparent, 8);
				}
			}

			GlassPane.EndTriangles();

			// The constellation's name, small and faint under the medallion.
			Vector2 labelPosition = new Vector2(center.X, center.Y + radius + 20f * scale);
			Utils.DrawBorderString(Main.spriteBatch, current.Name, labelPosition, Frost * (0.75f * opacity), 0.8f * scale, 0.5f, 0.5f);
		}

		private static bool Lit(int star, float fraction) => fraction > goOutRank[star] / (float)current.Stars.Length;

		private static Vector2 StarPosition(Vector2 center, float radius, int star) => center + current.Stars[star] * radius * 0.8f;

		private static void HalfDisc(Vector2 center, float radius, Color color) {
			const int segments = 32;
			for (int i = 0; i < segments; i++) {
				float a0 = -MathHelper.PiOver2 + MathHelper.Pi * i / segments;
				float a1 = -MathHelper.PiOver2 + MathHelper.Pi * (i + 1) / segments;
				GlassPane.Triangle(center, color, center + a0.ToRotationVector2() * radius, color, center + a1.ToRotationVector2() * radius, color);
			}
		}
	}
}

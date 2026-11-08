using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;
using Terraria.Utilities;

namespace TimeStop.Items
{
	// The animated look of Orion's Half. For now: the sand shows the time tank, and Kamutoke's lightning crackles
	// off the blade and the tip.
	//
	// The lightning is drawn the way Calamity draws its electricity: smooth, soft-edged beams instead of pixels.
	// Each bolt is a jagged path drawn three times (a wide faint glow, a brighter body, a thin white-hot core)
	// using a soft gradient strip, with round bloom at the joints so the bends stay smooth. Additive colours use
	// premultiplied alpha 0, so it works in any sprite batch (inventory, world, other mods' slots) unchanged.
	//
	//  - Small arcs crawl along the blade's edges all the time.
	//  - Bigger discharges leap off the tip and the edges, forking as they go. More often when a stop is ready.
	//  - While time is stopped, the lightning freezes in place, mid-crackle.
	//  - When strain has shattered your screen, the relic goes dark and the lightning dies.
	internal static class OrionFx
	{
		// The relic is drawn from its upright 21x120 sprite, turned 45 degrees at draw time so the pixels themselves
		// rotate. The textures are stored 4x (nearest neighbour) so smooth filtering keeps each pixel a crisp square.
		// All positions below are in upright sprite pixels.
		public const float Angle = -MathHelper.PiOver4;           // blade to the upper left
		private const int Upscale = 4;
		private static readonly Vector2 Centre = new(10.5f, 60f);
		private const int SandFrames = 13;

		private static readonly Vector2 Tip = new(10.5f, 0.5f);
		private static readonly Vector2 Down = new(0f, 1f);       // tip -> guard
		private static readonly Vector2 Side = new(1f, 0f);       // across the blade
		// half-width of the blade for each pixel row from the tip (from the pixel-art generator)
		private static readonly int[] Profile = { 0, 0, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 4, 4, 3, 2, 1, 1, 1, 2, 2, 2, 2, 2, 2, 3, 3, 2, 2, 2 };

		private static readonly Color Ice = new(110, 210, 255);
		private static readonly Color Pale = new(200, 240, 255);
		private static readonly Color Gold = new(255, 214, 120);

		private static Asset<Texture2D> sandTexture;
		internal static Asset<Texture2D> uprightTexture;
		private static Texture2D bloom, softLine;

		private static float fill = 1f;
		private static float readyGlow;
		private static uint lastUpdate;
		private static int crawlTimer, boltTimer = 20;

		private sealed class Bolt
		{
			public Vector2 From, To;     // sprite space
			public float Jag;            // how far the path wanders sideways
			public int Life, MaxLife, Seed;
			public bool Big;
			public readonly List<Vector2[]> Paths = new();
		}

		private static readonly List<Bolt> bolts = new();

		private static bool Broken => TimeStopSystem.LocalLockoutTicks > 0;

		private static bool Ready => !TimeStopSystem.Active && !Broken && TimeStopSystem.CooldownTicks <= 0
			&& TimeStopSystem.Fuel >= TimeStopSystem.MaxFuel - 0.5f;

		public static void Load() {
			if (!Main.dedServ)
			{
				sandTexture = ModContent.Request<Texture2D>("TimeStop/Items/ChronoWatch_SandUpright");
				uprightTexture = ModContent.Request<Texture2D>("TimeStop/Items/ChronoWatch_Upright");
			}
		}

		public static void Unload() {
			Texture2D b = bloom, l = softLine;
			bloom = softLine = null;
			sandTexture = uprightTexture = null;
			bolts.Clear();
			Main.QueueMainThreadAction(() => {
				b?.Dispose();
				l?.Dispose();
			});
		}

		// ---------- generated textures (premultiplied white; alpha = brightness) ----------

		internal static Texture2D Bloom => bloom ??= Make(64, 64, (u, v) => MathF.Pow(Math.Max(0f, 1f - MathF.Sqrt(u * u + v * v)), 2.2f));

		// a soft strip: bright along the middle, fading smoothly to nothing at both edges
		internal static Texture2D SoftLine => softLine ??= Make(8, 32, (u, v) => MathF.Pow(Math.Max(0f, MathF.Cos(v * MathHelper.PiOver2)), 1.6f));

		private static Texture2D Make(int width, int height, Func<float, float, float> brightness) {
			Texture2D texture = new(Main.instance.GraphicsDevice, width, height);
			Color[] data = new Color[width * height];
			for (int y = 0; y < height; y++) {
				for (int x = 0; x < width; x++) {
					float u = (x + 0.5f) / width * 2f - 1f;
					float v = (y + 0.5f) / height * 2f - 1f;
					byte a = (byte)(MathHelper.Clamp(brightness(u, v), 0f, 1f) * 255f);
					data[y * width + x] = new Color(a, a, a, a);
				}
			}
			texture.SetData(data);
			return texture;
		}

		private static Color Add(Color c, float strength) => new Color(c.R, c.G, c.B, 0) * strength;

		// ---------- the blade ----------

		private static Vector2 BladeCentre(float row) => Tip + Down * row;

		// a point on one edge of the blade at this row
		private static Vector2 BladeEdge(int row, int side) {
			int hw = Profile[Math.Clamp(row, 0, Profile.Length - 1)];
			return BladeCentre(row) + Side * side * (hw + 0.6f);
		}

		// ---------- per-frame state ----------

		public static void Update() {
			if (Main.dedServ || lastUpdate == Main.GameUpdateCount)
				return;
			lastUpdate = Main.GameUpdateCount;

			float target = TimeStopSystem.MaxFuel > 0f ? MathHelper.Clamp(TimeStopSystem.Fuel / TimeStopSystem.MaxFuel, 0f, 1f) : 1f;
			fill = MathHelper.Lerp(fill, target, 0.18f);
			readyGlow = MathHelper.Lerp(readyGlow, Ready ? 1f : 0f, 0.06f);

			if (Broken) {
				bolts.Clear();
				return;
			}
			// stopped time: the lightning hangs frozen exactly as it was
			if (TimeStopSystem.Active)
				return;

			// small arcs crawling along the edges, all the time
			if (--crawlTimer <= 0) {
				int side = Main.rand.NextBool() ? 1 : -1;
				int a = Main.rand.Next(3, Profile.Length - 4);
				int b = Math.Clamp(a + Main.rand.Next(3, 7) * (Main.rand.NextBool() ? 1 : -1), 1, Profile.Length - 1);
				Spawn(BladeEdge(a, side), BladeEdge(b, side) + Side * side * Main.rand.NextFloat(0.5f, 2f), 1.4f, Main.rand.Next(8, 14), big: false);
				crawlTimer = Main.rand.Next(6, 14) - (int)(4 * readyGlow);
			}

			// discharges leaping off the tip and the edges
			if (--boltTimer <= 0) {
				if (Main.rand.NextBool(3)) {
					// off the tip, roughly straight ahead
					Vector2 ahead = (-Down).RotatedBy(Main.rand.NextFloat(-0.6f, 0.6f));
					Spawn(Tip, Tip + ahead * Main.rand.NextFloat(14f, 26f), 3.2f, Main.rand.Next(10, 16), big: true);
				}
				else {
					// off an edge, outward and a little forward
					int side = Main.rand.NextBool() ? 1 : -1;
					int row = Main.rand.Next(4, Profile.Length - 2);
					Vector2 dir = (Side * side - Down * Main.rand.NextFloat(0.1f, 0.7f)).SafeNormalize(Side);
					Vector2 from = BladeEdge(row, side);
					Spawn(from, from + dir * Main.rand.NextFloat(9f, 18f), 2.4f, Main.rand.Next(9, 14), big: true);
				}
				boltTimer = Main.rand.Next(14, 40) - (int)(10 * readyGlow);
			}

			for (int i = bolts.Count - 1; i >= 0; i--) {
				Bolt bolt = bolts[i];
				if (--bolt.Life <= 0) {
					bolts.RemoveAt(i);
					continue;
				}
				if (bolt.Life % 2 == 0) {      // flicker: the bolt re-forms its path every couple of frames
					bolt.Seed++;
					Build(bolt);
				}
			}
		}

		private static void Spawn(Vector2 from, Vector2 to, float jag, int life, bool big) {
			if (bolts.Count > 24)
				return;
			Bolt bolt = new() { From = from, To = to, Jag = jag, Life = life, MaxLife = life, Seed = Main.rand.Next(1 << 20), Big = big };
			Build(bolt);
			bolts.Add(bolt);
		}

		private static void Build(Bolt bolt) {
			UnifiedRandom rand = new(bolt.Seed);
			bolt.Paths.Clear();
			bolt.Paths.Add(Jagged(rand, bolt.From, bolt.To, bolt.Jag, bolt.Big ? 12 : 7));
			if (bolt.Big) {
				// forks branch off part way, angled away from the main path
				int forks = rand.Next(1, 3);
				Vector2[] main = bolt.Paths[0];
				for (int f = 0; f < forks; f++) {
					int at = rand.Next(main.Length / 3, main.Length - 2);
					Vector2 dir = (bolt.To - bolt.From).SafeNormalize(Vector2.UnitX).RotatedBy((rand.NextDouble() < 0.5 ? -1 : 1) * (0.5 + rand.NextDouble() * 0.6));
					float len = (bolt.To - bolt.From).Length() * (float)(0.3 + rand.NextDouble() * 0.3);
					bolt.Paths.Add(Jagged(rand, main[at], main[at] + dir * len, bolt.Jag * 0.6f, 6));
				}
			}
		}

		private static Vector2[] Jagged(UnifiedRandom rand, Vector2 a, Vector2 b, float jag, int count) {
			Vector2 d = b - a;
			Vector2 n = new Vector2(-d.Y, d.X).SafeNormalize(Vector2.Zero);
			Vector2[] points = new Vector2[count];
			float drift = 0f;
			for (int i = 0; i < count; i++) {
				float t = i / (float)(count - 1);
				drift = drift * 0.45f + (float)(rand.NextDouble() * 2 - 1) * jag;
				float pin = MathF.Sin(t * MathHelper.Pi);              // ends stay put, the middle wanders
				points[i] = a + d * t + n * drift * (i == 0 ? 0f : MathF.Sqrt(pin) + (i == count - 1 ? 0.6f : 0f));
			}
			return points;
		}

		// ---------- drawing ----------

		private static Vector2 Map(Vector2 p, Vector2 centre, float scale) => centre + (p - Centre).RotatedBy(Angle) * scale;

		// The relic itself. scale = screen pixels per sprite pixel.
		public static void DrawItem(SpriteBatch sb, Vector2 centre, float scale, Color color) {
			Texture2D tex = uprightTexture.Value;
			sb.Draw(tex, centre, null, color, Angle, tex.Size() * 0.5f, scale / Upscale, SpriteEffects.None, 0f);
		}

		private static void Beam(SpriteBatch sb, Vector2 a, Vector2 b, Color color, float width) {
			Vector2 d = b - a;
			float length = d.Length();
			if (length < 0.01f)
				return;
			Texture2D tex = SoftLine;
			sb.Draw(tex, a, null, color, d.ToRotation(), new Vector2(0f, tex.Height * 0.5f),
				new Vector2(length / tex.Width, width / tex.Height), SpriteEffects.None, 0f);
		}

		private static void Dot(SpriteBatch sb, Vector2 at, Color color, float size) {
			Texture2D tex = Bloom;
			sb.Draw(tex, at, null, color, 0f, tex.Size() * 0.5f, size / tex.Width, SpriteEffects.None, 0f);
		}

		// Behind the item: a faint charged haze along the blade.
		public static void DrawBehind(SpriteBatch sb, Vector2 centre, float scale) {
			if (Broken)
				return;
			float flicker = 0.75f + 0.25f * MathF.Sin(Main.GlobalTimeWrappedHourly * 13f) * MathF.Sin(Main.GlobalTimeWrappedHourly * 7.3f);
			float strength = (0.12f + 0.18f * readyGlow) * flicker;
			Vector2 mid = Map(BladeCentre(14f), centre, scale);
			Texture2D tex = Bloom;
			sb.Draw(tex, mid, null, Add(Ice, strength), Angle + MathHelper.PiOver2, tex.Size() * 0.5f,
				new Vector2(44f * scale / tex.Width, 13f * scale / tex.Height), SpriteEffects.None, 0f);
		}

		// In front of the item: the sand, then the lightning.
		public static void DrawFront(SpriteBatch sb, Vector2 centre, float scale) {
			if (Broken)
				return;

			Texture2D sand = sandTexture.Value;
			int frameWidth = sand.Width / SandFrames;
			int frame = (int)Math.Round((1f - fill) * (SandFrames - 1));
			sb.Draw(sand, centre, new Rectangle(frame * frameWidth, 0, frameWidth, sand.Height), Color.White, Angle,
				new Vector2(frameWidth * 0.5f, sand.Height * 0.5f), scale / Upscale, SpriteEffects.None, 0f);

			bool frozen = TimeStopSystem.Active;
			// widths are in screen pixels with a floor, so the bolts stay smooth and readable even when tiny
			float glowW = Math.Max(5f, 7f * scale), bodyW = Math.Max(2.4f, 3f * scale), coreW = Math.Max(1.1f, 1.2f * scale);
			foreach (Bolt bolt in bolts) {
				float life = bolt.Life / (float)bolt.MaxLife;
				float k = frozen ? 0.85f : MathF.Pow(life, 0.7f) * (bolt.Big ? 1f : 0.7f);
				Color glow = frozen ? Pale : Ice;
				for (int p = 0; p < bolt.Paths.Count; p++) {
					Vector2[] path = bolt.Paths[p];
					float branch = p == 0 ? 1f : 0.65f;
					for (int i = 0; i < path.Length - 1; i++) {
						Vector2 a = Map(path[i], centre, scale), b = Map(path[i + 1], centre, scale);
						float taper = 1f - i / (float)(path.Length - 1) * 0.6f;     // thinner toward the far end
						Beam(sb, a, b, Add(glow, 0.32f * k * branch), glowW * taper * branch);
						Beam(sb, a, b, Add(Pale, 0.7f * k * branch), bodyW * taper * branch);
						Beam(sb, a, b, Add(Color.White, k * branch), coreW * taper);
						Dot(sb, a, Add(Pale, 0.35f * k * branch), bodyW * 1.6f * taper);   // smooth the bends
					}
				}
				if (bolt.Big)
					Dot(sb, Map(bolt.From, centre, scale), Add(Ice, 0.6f * k), 16f * scale + 6f);
			}

			// the tip burns brightest
			float tip = 0.35f + 0.35f * readyGlow + 0.15f * MathF.Sin(Main.GlobalTimeWrappedHourly * 9f);
			Vector2 tipAt = Map(Tip, centre, scale);
			Dot(sb, tipAt, Add(Ice, tip), 18f * scale + 6f);
			Dot(sb, tipAt, Add(Color.White, tip * 0.8f), 6f * scale + 3f);
		}

		public static void DrawBroken(SpriteBatch sb, Vector2 centre, float scale, Color light) {
			Vector2 shake = TimeStopSystem.LocalLockoutTicks > 200 ? Main.rand.NextVector2Circular(1.5f, 1.5f) : Vector2.Zero;
			DrawItem(sb, centre + shake, scale, Color.Lerp(light, new Color(70, 64, 90), 0.65f));
		}
	}

	public class OrionFxSystem : ModSystem
	{
		public override void Load() => OrionFx.Load();
		public override void Unload() => OrionFx.Unload();
		public override void UpdateUI(GameTime gameTime) => OrionFx.Update();
	}
}

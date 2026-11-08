using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace TimeStop.Items
{
	// Using Orion's Half: a short wind-up, then the relic is stabbed straight up into the air above the player and
	// held there. Time stops (or resumes) at the top of the stab.
	//
	// The held relic is drawn from the upright sprite (the pixels themselves, not a pre-turned copy), gripped by the
	// pommel, about 1.5x the player's height. Only the blade tip glows, and it leaves a long, smooth light trail
	// wherever it moves. If time is stopped when the use ends, the trail hangs frozen in the air until time resumes.
	internal static class OrionHeld
	{
		public const float HeldScale = 1.06f;        // 120 sprite pixels tall -> about 127 screen pixels, 3x the player
		public const float StabTop = 0.4f;           // fraction of the use where the stab peaks
		private const int Upscale = 4;               // the upright texture is stored 4x
		private static readonly Vector2 Grip = new(10.5f, 110f);   // pommel centre, upright sprite pixels
		private static readonly Vector2 Tip = new(10.5f, 0.5f);

		public struct Pose
		{
			public Vector2 Hand;                     // world position of the gripping hand
			public float ArmRotation;
			public Player.CompositeArmStretchAmount Stretch;
			public float Lift;                       // extra upward push at the peak of the stab (overshoot)
			public float Progress;                   // 0..1 through the use
		}

		public static Pose GetPose(Player player) {
			float max = Math.Max(1, player.itemAnimationMax);
			float t = MathHelper.Clamp(1f - player.itemAnimation / max, 0f, 1f);
			int dir = player.direction;

			Vector2 aim;
			Player.CompositeArmStretchAmount stretch;
			float lift = 0f;
			if (t < 0.2f) {
				// wind-up: hand dips to chest height, arm forward and low
				float e = t / 0.2f;
				aim = Vector2.Lerp(new Vector2(dir * 0.7f, 0.45f), new Vector2(dir * 0.55f, -0.15f), e);
				stretch = e < 0.5f ? Player.CompositeArmStretchAmount.Quarter : Player.CompositeArmStretchAmount.ThreeQuarters;
			}
			else if (t < StabTop) {
				// the stab: fast, straight up
				float e = (t - 0.2f) / (StabTop - 0.2f);
				e = 1f - MathF.Pow(1f - e, 3f);
				aim = Vector2.Lerp(new Vector2(dir * 0.55f, -0.15f), new Vector2(0f, -1f), e);
				stretch = Player.CompositeArmStretchAmount.Full;
				lift = 7f * e;
			}
			else {
				// hold it up; the overshoot settles back over a few frames
				aim = new Vector2(0f, -1f);
				stretch = Player.CompositeArmStretchAmount.Full;
				float settle = MathHelper.Clamp((t - StabTop) / 0.15f, 0f, 1f);
				lift = 7f * (1f - settle * settle);
			}

			float rotation = aim.SafeNormalize(-Vector2.UnitY).ToRotation() - MathHelper.PiOver2;
			return new Pose {
				Hand = player.GetFrontHandPosition(stretch, rotation),
				ArmRotation = rotation,
				Stretch = stretch,
				Lift = lift,
				Progress = t
			};
		}

		// where the relic is drawn (its grip) and where its tip is, in world space
		public static Vector2 GripWorld(Player player) => GetPose(player) is var p ? p.Hand + new Vector2(0f, -p.Lift) : Vector2.Zero;
		public static Vector2 TipWorld(Player player) => GripWorld(player) + (Tip - Grip) * HeldScale;

		public static bool Using(Player player) => player.itemAnimation > 0 && player.HeldItem.type == ModContent.ItemType<ChronoWatch>();

		private static Color Add(Color c, float strength) => new Color(c.R, c.G, c.B, 0) * strength;

		public static void DrawRelic(ref PlayerDrawSet info) {
			Player player = info.drawPlayer;
			Texture2D tex = OrionFx.uprightTexture.Value;
			Vector2 grip = GripWorld(player) + new Vector2(0f, player.gfxOffY);
			Color light = Lighting.GetColor((int)(grip.X / 16f), (int)(grip.Y / 16f));
			if (TimeStopSystem.LocalLockoutTicks > 0 && player.whoAmI == Main.myPlayer)
				light = Color.Lerp(light, new Color(70, 64, 90), 0.65f);
			Vector2 origin = Grip * Upscale;
			info.DrawDataCache.Add(new DrawData(tex, (grip - Main.screenPosition).Floor(), null, light, 0f, origin, HeldScale / Upscale, SpriteEffects.None, 0));

			// the tip: a steady three-layer glow, no pulse
			Vector2 tip = TipWorld(player) + new Vector2(0f, player.gfxOffY) - Main.screenPosition;
			Glow(ref info, tip, new Color(90, 190, 255), 0.35f, 64f);
			Glow(ref info, tip, new Color(150, 225, 255), 0.6f, 26f);
			Glow(ref info, tip, Color.White, 0.9f, 8f);
		}

		private static void Glow(ref PlayerDrawSet info, Vector2 at, Color color, float strength, float size) {
			Texture2D bloom = OrionFx.Bloom;
			info.DrawDataCache.Add(new DrawData(bloom, at, null, Add(color, strength), 0f, bloom.Size() * 0.5f, size / bloom.Width, SpriteEffects.None, 0));
		}

		// The trail: a tapered streak through the tip's recent positions, smoothed with a Catmull-Rom curve so fast
		// moves curve instead of zig-zagging. White-hot and thick at the tip, thin and blue at the tail, drawn as
		// soft-edged beams (wide glow, body, core) so it's smooth light, not pixels.
		public static void DrawTrail(ref PlayerDrawSet info, List<Vector2> points) {
			if (points.Count < 2)
				return;
			Vector2 offset = new Vector2(0f, info.drawPlayer.gfxOffY) - Main.screenPosition;
			List<Vector2> dense = new();
			for (int i = 0; i < points.Count - 1; i++) {
				Vector2 p0 = points[Math.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Math.Min(points.Count - 1, i + 2)];
				for (int k = 0; k < 6; k++)
					dense.Add(Vector2.CatmullRom(p0, p1, p2, p3, k / 6f) + offset);
			}
			dense.Add(points[^1] + offset);

			Texture2D line = OrionFx.SoftLine;
			for (int i = 0; i < dense.Count - 1; i++) {
				Vector2 a = dense[i], b = dense[i + 1], d = b - a;
				float length = d.Length();
				if (length < 0.05f)
					continue;
				float u = (i + 1) / (float)(dense.Count - 1);     // 1 at the tip, 0 at the tail
				float width = 1f + 13f * MathF.Pow(u, 1.3f);
				Color glow = Color.Lerp(new Color(70, 160, 255), new Color(150, 225, 255), u);
				Color core = Color.Lerp(new Color(120, 200, 255), Color.White, u);
				float rot = d.ToRotation();
				Vector2 origin = new(0f, line.Height * 0.5f);
				info.DrawDataCache.Add(new DrawData(line, a, null, Add(glow, 0.55f * MathF.Pow(u, 0.8f)), rot, origin, new Vector2(length / line.Width, width * 2.4f / line.Height), SpriteEffects.None, 0));
				info.DrawDataCache.Add(new DrawData(line, a, null, Add(core, 0.9f * MathF.Pow(u, 0.6f)), rot, origin, new Vector2(length / line.Width, Math.Max(1.2f, width * 0.5f) / line.Height), SpriteEffects.None, 0));
			}
		}
	}

	public class OrionHeldPlayer : ModPlayer
	{
		public bool ToggledThisUse;
		public readonly List<Vector2> Tips = new();
		private const int TrailLength = 30;

		public override void PostUpdate() {
			if (Main.dedServ)
				return;
			if (OrionHeld.Using(Player)) {
				Tips.Add(OrionHeld.TipWorld(Player));
				if (Tips.Count > TrailLength)
					Tips.RemoveAt(0);
			}
			else if (Tips.Count > 0 && !TimeStopSystem.Active) {
				// put away: the trail drains from the tail. While time is stopped it hangs frozen instead.
				Tips.RemoveAt(0);
				if (Tips.Count > 0)
					Tips.RemoveAt(0);
			}
		}
	}

	// Drawn just under the front arm, so the fist closes over the pommel. (HeldItem has no fixed position to sit
	// next to, and the item sets noUseGraphic so the game draws nothing of its own while it's used.)
	public class OrionHeldLayer : PlayerDrawLayer
	{
		public override Position GetDefaultPosition() => new BeforeParent(PlayerDrawLayers.ArmOverItem);

		public override bool GetDefaultVisibility(PlayerDrawSet drawInfo) {
			if (drawInfo.shadow != 0f || drawInfo.drawPlayer.dead)
				return false;
			return OrionHeld.Using(drawInfo.drawPlayer) || drawInfo.drawPlayer.GetModPlayer<OrionHeldPlayer>().Tips.Count > 1;
		}

		protected override void Draw(ref PlayerDrawSet drawInfo) {
			Player player = drawInfo.drawPlayer;
			OrionHeld.DrawTrail(ref drawInfo, player.GetModPlayer<OrionHeldPlayer>().Tips);
			if (OrionHeld.Using(player))
				OrionHeld.DrawRelic(ref drawInfo);
		}
	}
}

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TimeStop
{
	// Pauses purely visual motion on this client while time is stopped: shader and sky animation
	// (including Calamity and Wrath of the Gods skies), swaying grass and trees, tile animation, torch flicker,
	// waterfalls, twinkling stars, gore, wind and dust away from players.
	public class SceneryFreeze : ModSystem
	{
		// Dust closer than this to a player keeps moving, so weapon swings and the player's own effects
		// are not paused. Dust further out (torch flames, ambient dust, enemy effects) freezes.
		private const float PlayerDustRadius = 260f;

		private static bool holding;
		private static double heldVisualTime;
		private static float heldWind;
		private static ulong heldTileFrameSeed;
		private static float heldWaterfallFrame;
		private static float heldWaterfallCounter;

		public static bool Enabled => TimeStopSystem.Active && !Main.dedServ && TimeStopClientConfig.Get.PauseScenery;

		public override void PreUpdateEntities() => Hold();
		public override void PostUpdateEverything() => Hold();

		private static void Hold() {
			if (!Enabled) {
				holding = false;
				return;
			}

			if (!holding) {
				holding = true;
				heldVisualTime = Main.timeForVisualEffects;
				heldWind = Main.windSpeedCurrent;
				heldTileFrameSeed = Main.TileFrameSeed;
				heldWaterfallFrame = Main.wFrame;
				heldWaterfallCounter = Main.wFrCounter;
			}

			// Most shaders and skies animate from this clock (Main.GlobalTimeWrappedHourly is based on it).
			Main.timeForVisualEffects = heldVisualTime;
			Main.windSpeedCurrent = heldWind;
			// Torch flames and other flickering tiles pick their wobble from this seed.
			Main.TileFrameSeed = heldTileFrameSeed;
			Main.wFrame = heldWaterfallFrame;
			Main.wFrCounter = heldWaterfallCounter;
		}

		public override void OnWorldUnload() {
			holding = false;
		}

		private struct DustSnapshot
		{
			public bool Frozen;
			public bool Active;
			public Vector2 Position;
			public Vector2 Velocity;
			public float Scale;
			public float Rotation;
			public int Alpha;
			public float FadeIn;
		}

		private static DustSnapshot[] dustSnapshots;

		// Runs the normal dust update, then puts frozen dust back exactly as it was.
		internal static void UpdateDust(On_Dust.orig_UpdateDust orig) {
			if (!Enabled) {
				orig();
				return;
			}

			int count = Main.dust.Length;
			if (dustSnapshots == null || dustSnapshots.Length != count)
				dustSnapshots = new DustSnapshot[count];

			float radiusSquared = PlayerDustRadius * PlayerDustRadius;
			for (int i = 0; i < count; i++) {
				Dust dust = Main.dust[i];
				ref DustSnapshot snapshot = ref dustSnapshots[i];
				snapshot.Frozen = dust != null && dust.active && !NearAnyPlayer(dust.position, radiusSquared);
				if (!snapshot.Frozen)
					continue;

				snapshot.Active = dust.active;
				snapshot.Position = dust.position;
				snapshot.Velocity = dust.velocity;
				snapshot.Scale = dust.scale;
				snapshot.Rotation = dust.rotation;
				snapshot.Alpha = dust.alpha;
				snapshot.FadeIn = dust.fadeIn;
			}

			orig();

			for (int i = 0; i < count; i++) {
				ref DustSnapshot snapshot = ref dustSnapshots[i];
				if (!snapshot.Frozen)
					continue;

				Dust dust = Main.dust[i];
				dust.active = snapshot.Active;
				dust.position = snapshot.Position;
				dust.velocity = snapshot.Velocity;
				dust.scale = snapshot.Scale;
				dust.rotation = snapshot.Rotation;
				dust.alpha = snapshot.Alpha;
				dust.fadeIn = snapshot.FadeIn;
			}
		}

		private static bool NearAnyPlayer(Vector2 position, float radiusSquared) {
			for (int p = 0; p < Main.maxPlayers; p++) {
				Player player = Main.player[p];
				if (player.active && !player.dead && Vector2.DistanceSquared(player.Center, position) < radiusSquared)
					return true;
			}

			return false;
		}
	}
}

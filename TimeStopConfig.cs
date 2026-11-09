using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace TimeStop
{
	public class TimeStopConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ServerSide;

		public static TimeStopConfig Get => ModContent.GetInstance<TimeStopConfig>();

		// Stopped time works like fuel. The tank holds this many seconds of it.
		[DefaultValue(5f)]
		[Range(1f, 60f)]
		[Increment(0.5f)]
		[Slider]
		public float TankSeconds;

		// While any boss is alive the tank is smaller (fuel above it is cut off), so boss fights cannot be frozen away.
		[DefaultValue(3f)]
		[Range(0.5f, 60f)]
		[Increment(0.5f)]
		[Slider]
		public float BossTankSeconds;

		// How fast the tank refills while time flows, compared to how fast it drains (0.8 = 80% of drain speed).
		[DefaultValue(0.8f)]
		[Range(0.1f, 5f)]
		[Increment(0.1f)]
		[Slider]
		public float RefillSpeed;

		// Fixed wait after every time stop before the next one.
		[DefaultValue(0.5f)]
		[Range(0f, 30f)]
		[Increment(0.1f)]
		public float StopCooldownSeconds;

		// Hits on frozen enemies are stored and all land at once when time resumes.
		[DefaultValue(true)]
		public bool StoredDamage;

		// Damage multiplier for hits on frozen bosses (and their body parts). 0.5 = half damage.
		[DefaultValue(0.5f)]
		[Range(0.05f, 1f)]
		[Increment(0.05f)]
		[Slider]
		public float FrozenBossDamage;

		// How long the Nameless Deity lets a time stop hold (everything but him frozen) before he breaks it.
		[DefaultValue(0.5f)]
		[Range(0.1f, 0.5f)]
		[Increment(0.05f)]
		[Slider]
		public float NamelessBreakSeconds;

		// Spamming time stops as fast as possible cracks the screen, and too much shatters it (4 s lockout, fuel lost).
		[DefaultValue(true)]
		public bool StrainEnabled;

		// A restart counts as spam if the previous stop lasted at most this long...
		[DefaultValue(1f)]
		[Range(0.1f, 5f)]
		[Increment(0.1f)]
		[Slider]
		public float StrainQuickTapSeconds;

		// ...and the new one started within this long after the cooldown ended.
		[DefaultValue(0.4f)]
		[Range(0.05f, 3f)]
		[Increment(0.05f)]
		[Slider]
		public float StrainSpamWindowSeconds;

		// Spam restarts in a row before the screen shatters.
		[DefaultValue(6)]
		[Range(2, 20)]
		[Slider]
		public int StrainShatterAt;

		// Strain starts healing this long after the last spam...
		[DefaultValue(2f)]
		[Range(0f, 10f)]
		[Increment(0.5f)]
		[Slider]
		public float StrainHealDelaySeconds;

		// ...and then heals this many cracks per second.
		[DefaultValue(0.5f)]
		[Range(0.1f, 5f)]
		[Increment(0.1f)]
		[Slider]
		public float StrainHealPerSecond;

		// Locked out of stopping time for this long after shattering.
		[DefaultValue(4f)]
		[Range(1f, 30f)]
		[Increment(0.5f)]
		[Slider]
		public float StrainLockoutSeconds;

		// Share of the time tank lost when the screen shatters.
		[DefaultValue(30)]
		[Range(0, 100)]
		[Slider]
		public int StrainFuelLossPercent;

		[DefaultValue(true)]
		public bool FreezeBosses;

		[DefaultValue(true)]
		public bool FreezeTownNPCs;

		[DefaultValue(true)]
		public bool FreezeDroppedItems;

		[DefaultValue(true)]
		public bool FreezeLiquids;

		// Enables /chronowatch and /tsspawn.
		[DefaultValue(true)]
		public bool AllowTestCommands;

		// NPCs listed here never freeze.
		public List<NPCDefinition> ExemptNPCs = new();

		internal HashSet<int> ExemptTypes = new();

		public override void OnChanged() {
			StrainMeter.Settings.QuickTapTicks = (int)Math.Round(StrainQuickTapSeconds * 60f);
			StrainMeter.Settings.SpamWindowTicks = (int)Math.Round(StrainSpamWindowSeconds * 60f);
			StrainMeter.Settings.OverloadAt = StrainShatterAt;
			StrainMeter.Settings.HoldTicks = (int)Math.Round(StrainHealDelaySeconds * 60f);
			StrainMeter.Settings.DecayPerTick = StrainHealPerSecond / 60f;

			ExemptTypes = new HashSet<int>(ExemptNPCs
				.Where(definition => definition != null && !definition.IsUnloaded)
				.Select(definition => definition.Type));
		}
	}

	public class TimeStopClientConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ClientSide;

		public static TimeStopClientConfig Get => ModContent.GetInstance<TimeStopClientConfig>();

		[DefaultValue(true)]
		public bool ShowOverlay;

		[DefaultValue(true)]
		public bool ShowTimer;

		[DefaultValue(true)]
		public bool PauseScenery;

		// The cracks show the whole screen (world AND interface) rotated. Copying the frame costs some performance
		// while cracks are visible; turn off to show only the world inside them.
		[DefaultValue(true)]
		public bool CracksShowInterface;

		// Personal comfort setting: softens flashes, swaps every colour inversion for a gentle dim, and cuts screen shake.
		[DefaultValue(false)]
		public bool ReduceFlashing;

		// 0 turns the time stop start sound off.
		[DefaultValue(100)]
		[Range(0, 100)]
		[Slider]
		public int TimeStopSoundVolume;

		// 0 turns the glass break sound off.
		[DefaultValue(100)]
		[Range(0, 100)]
		[Slider]
		public int GlassSoundVolume;
	}

	// One place to ask how strong flashes, inversions and shakes should be for this player.
	internal static class Flash
	{
		public static bool Reduced => !Terraria.Main.dedServ && TimeStopClientConfig.Get.ReduceFlashing;

		// white flashes and other bright pulses
		public static float Bright(float amount) => Reduced ? amount * 0.25f : amount;

		// camera shake strength
		public static float Shake(float strength) => Reduced ? strength * 0.3f : strength;
	}
}

using System;

namespace TimeStop
{
	// Strain: what spamming time stops does to the screen. Kept separate from the game so the rules are easy to
	// read and test. The numbers come from the config (see Settings).
	//
	// A time stop only counts as spam when BOTH are true:
	//   - the previous stop was a quick tap (QuickTapTicks or shorter), and
	//   - this one started within SpamWindowTicks of the cooldown ending, i.e. (nearly) as fast as the game allows.
	// Anything slower adds nothing. After HoldTicks without spam, strain heals at DecayPerTick.
	// Cracks appear from a quarter of the way up and grow until OverloadAt, where the screen shatters.
	public struct StrainMeter
	{
		public static class Settings
		{
			public static int QuickTapTicks = 60;          // 1 second
			public static int SpamWindowTicks = 24;        // 0.4 seconds after the cooldown ends
			public static int HoldTicks = 120;             // healing waits 2 seconds after the last spam
			public static float DecayPerTick = 0.5f / 60f; // then heals half a crack per second
			public static float OverloadAt = 6f;           // spam starts in a row before the screen shatters
		}

		public static int HoldTicks => Settings.HoldTicks;

		public float Value;
		public int Hold;

		// Called when a time stop starts. Returns true when this pushes the screen into overload.
		public bool RegisterStart(int ticksSinceLastEnd, int lastStopTicks, int cooldownTicks) {
			bool spam = lastStopTicks <= Settings.QuickTapTicks && ticksSinceLastEnd <= cooldownTicks + Settings.SpamWindowTicks;
			if (spam) {
				Value += 1f;
				Hold = Settings.HoldTicks;
			}

			return Value >= Settings.OverloadAt;
		}

		public void Tick() {
			if (Hold > 0)
				Hold--;
			else
				Value = Math.Max(0f, Value - Settings.DecayPerTick);
		}

		public void Reset() {
			Value = 0f;
			Hold = 0;
		}

		// 0 when there is no visible damage, 1 just before overload. Cracks show from a quarter of the way up.
		public float Intensity {
			get {
				float light = Math.Max(1f, Settings.OverloadAt * 0.25f) - 0.5f;
				return Math.Clamp((Value - light) / (Settings.OverloadAt - light), 0f, 1f);
			}
		}
	}
}

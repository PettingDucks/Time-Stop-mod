using System;

namespace TimeStop
{
	// Strain: what spamming time stops does to the screen. Kept separate from the game so the rules are easy to
	// read and test.
	//
	// A time stop only counts as spam when BOTH are true:
	//   - the previous stop was a quick tap (QuickTapTicks or shorter), and
	//   - this one started within SpamWindowTicks of the cooldown ending, i.e. as fast as the game allows.
	// Anything slower adds nothing. After HoldTicks without spam, strain heals at DecayPerTick.
	// Screen damage starts at LightAt, grows through MediumAt and HeavyAt, and at OverloadAt the screen shatters.
	public struct StrainMeter
	{
		public const float LightAt = 2f;
		public const float MediumAt = 4f;
		public const float HeavyAt = 6f;
		public const float OverloadAt = 8f;

		public const int QuickTapTicks = 36;      // 0.6 seconds
		public const int SpamWindowTicks = 9;     // 0.15 seconds after the cooldown ends
		public const int HoldTicks = 60;          // healing waits 1 second after the last spam
		public const float DecayPerTick = 1f / 60f;

		public float Value;
		public int Hold;

		// Called when a time stop starts. Returns true when this pushes the screen into overload.
		public bool RegisterStart(int ticksSinceLastEnd, int lastStopTicks, int cooldownTicks) {
			bool spam = lastStopTicks <= QuickTapTicks && ticksSinceLastEnd <= cooldownTicks + SpamWindowTicks;
			if (spam) {
				Value += 1f;
				Hold = HoldTicks;
			}

			return Value >= OverloadAt;
		}

		public void Tick() {
			if (Hold > 0)
				Hold--;
			else
				Value = Math.Max(0f, Value - DecayPerTick);
		}

		public void Reset() {
			Value = 0f;
			Hold = 0;
		}

		// 0 when there is no visible damage, 1 just before overload.
		public float Intensity => Math.Clamp((Value - (LightAt - 0.5f)) / (OverloadAt - (LightAt - 0.5f)), 0f, 1f);
	}
}

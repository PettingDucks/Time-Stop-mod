using System;

namespace TimeStop
{
	// The time stop fuel rules, kept separate from the game so they are easy to read and test.
	//
	//   - The tank holds Max ticks of stopped time (5 seconds = 300 ticks by default).
	//   - While time is stopped it drains 1 tick of fuel per tick. At zero, time breaks on its own.
	//   - The instant time resumes it starts refilling at RefillRate ticks of fuel per tick (0.8 by default),
	//     including during the cooldown.
	//   - Every stop is followed by a short fixed cooldown (0.5 seconds = 30 ticks by default) before the next.
	//   - There is no minimum: any fuel at all is enough to start.
	//   - Breaks caused by cutscenes or the Nameless Deity carry no penalty: no cooldown.
	public struct FuelTank
	{
		public float Fuel;
		public float Max;
		public int Cooldown;

		public bool CanStart => Cooldown <= 0 && Fuel > 0f;

		public static FuelTank Full(float max) => new FuelTank { Fuel = max, Max = max, Cooldown = 0 };

		// One tick with time stopped. Returns true if the tank just ran dry.
		public bool TickStopped() {
			Fuel -= 1f;
			if (Fuel > 0f)
				return false;

			Fuel = 0f;
			return true;
		}

		// One tick with time flowing.
		public void TickFlowing(float refillRate) {
			if (Cooldown > 0)
				Cooldown--;

			Fuel = Math.Min(Max, Fuel + refillRate);
		}

		// Time resumed normally (let go, or ran dry).
		public void Ended(int cooldownTicks) {
			Cooldown = cooldownTicks;
		}

		// Time was broken by a cutscene or the Nameless Deity: no penalty.
		public void EndedWithoutPenalty() {
			Cooldown = 0;
		}

		// Keeps the tank consistent if its size changes (config or /tsduration).
		public void SetMax(float max) {
			Max = Math.Max(1f, max);
			Fuel = Math.Min(Fuel, Max);
		}
	}
}

using Terraria;
using Terraria.Audio;

namespace TimeStop
{
	// All Time Stop sounds.
	//
	// Start: 14 variations of an electronic charge-up whine that follows a rise-peak-fall curve, firing a real
	// camera shutter at its peak. One is picked at random each time. The shutter clicks are CC0 recordings from
	// BigSoundBank by Joseph Sardin (Nikon D70s and a reflex SLR); the whine is synthesized.
	//
	// Break: 6 variations built from two real CC0 glass recordings from BigSoundBank by Joseph Sardin
	// ("Glass Burst" #0148 and "Broken Glass of Champagne" #1591), debris tails replaced by a natural ring-out.
	internal static class TimeStopSounds
	{
		private const int StartVariations = 14;
		private const int BreakVariations = 6;

		private static readonly SoundStyle Start = new("TimeStop/Sounds/Start", 1, StartVariations) { PitchVariance = 0.04f, MaxInstances = 3 };
		private static readonly SoundStyle Break = new("TimeStop/Sounds/BreakGlass", 1, BreakVariations) { PitchVariance = 0.08f, MaxInstances = 3 };

		private static float StartVolume => TimeStopClientConfig.Get.TimeStopSoundVolume / 100f;
		private static float GlassVolume => TimeStopClientConfig.Get.GlassSoundVolume / 100f;

		public static void PlayStart() {
			if (Main.dedServ || StartVolume <= 0f)
				return;

			SoundEngine.PlaySound(Start with { Volume = StartVolume });
		}

		public static void PlayBreak(bool violent) {
			if (Main.dedServ || GlassVolume <= 0f)
				return;

			// Every break uses the same six glass sounds; the Nameless Deity's is just played louder.
			SoundEngine.PlaySound(Break with { Volume = (violent ? 1f : 0.8f) * GlassVolume });
		}
	}
}

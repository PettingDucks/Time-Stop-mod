using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace TimeStop
{
	// Wrath of the Gods support. The Nameless Deity is never frozen, but everything else is, his attacks included,
	// until he breaks the stop a moment later (half a second at most, set in the config).
	// You can try again as often as you like: he breaks it every time, and stopping time again right after one
	// of his breaks never counts as spam strain. He only speaks every so often, with a random line.
	// Does nothing if Wrath of the Gods is not loaded.
	public class NamelessDeityCompat : ModSystem
	{
		// Ticks after the stop before he breaks it (config, at most half a second), and when his cracks appear.
		public static int BreakDelay => System.Math.Max(1, (int)System.Math.Round(TimeStopConfig.Get.NamelessBreakSeconds * 60f));
		public static int CrackStart => System.Math.Max(1, BreakDelay / 3);
		// He says something at most this often (ticks); breaks in between happen in silence.
		private const int LineCooldown = 600;

		public static int NamelessType { get; private set; } = -1;

		private static uint lastLineTick;
		private static bool spokeYet;
		private static readonly Queue<int> recent = new();

		private static readonly string[] Lines = {
			"You borrow what was never yours.",
			"Time does not stop. It waits for Me.",
			"Your little clock. How quaint.",
			"The hours kneel to Me, not to you.",
			"Again? Then again I refuse.",
			"Glass breaks. So will you.",
			"You cannot freeze a river that flows through Me.",
			"Every second you steal, I take back.",
			"Orion held half. I hold all of it.",
			"Stillness is a gift. I decline.",
			"I was here before the first moment. I will not wait for yours.",
			"Did you think the stars would obey you?",
			"Your hourglass is a toy in My hands.",
			"Let it run.",
			"Time answers to its Author.",
			"Halt? No.",
			"You stopped everything but the one who matters.",
			"Thunder in a bottle. I uncork it.",
			"Such fragile glass.",
			"The clock moves because I wish it to.",
			"I do not pause.",
			"Mortal tricks for mortal foes.",
			"You wound the moment. I mend it.",
			"Even frozen stars burn for Me.",
			"Keep trying. It amuses Me.",
			"The hour forgets to fall. I remember for it.",
			"There is no moment I am not in.",
			"Shatter.",
			"Your stolen seconds return to their owner.",
			"Beneath My gaze, nothing stands still.",
			"You reached for eternity and grasped glass.",
			"Time is not a door you may close on Me.",
			"Go on. Stop it again.",
			"A fine relic. A finer failure.",
			"The sand falls. It always falls.",
			"I am the tick between the tocks.",
			"Stillness? I have seen the end of stillness.",
			"Kneel, little clockmaker.",
			"Your silence breaks before Mine does.",
			"Nothing stops Me. Not even time.",
			"You hold half a god's patience. I hold none.",
			"The world froze. I did not.",
			"I have broken greater things than your hour.",
			"Every clock in creation ticks at My word.",
			"Do you hear it? Time, laughing at you.",
		};

		public override void PostSetupContent() {
			NamelessType = -1;
			if (ModLoader.TryGetMod("NoxusBoss", out Mod wotg) && wotg.TryFind("NamelessDeityBoss", out ModNPC nameless))
				NamelessType = nameless.Type;
		}

		public override void OnWorldLoad() => ResetLines();
		public override void OnWorldUnload() => ResetLines();

		public override void Unload() {
			NamelessType = -1;
			recent.Clear();
		}

		private static void ResetLines() {
			spokeYet = false;
			lastLineTick = 0;
			recent.Clear();
		}

		public static bool IsNameless(NPC npc) => NamelessType >= 0 && npc.type == NamelessType;

		public static bool NamelessPresent() => NamelessType >= 0 && NPC.AnyNPCs(NamelessType);

		// A random line for this break, or null if he spoke too recently. Never repeats any of the last 8 lines.
		public static string TakeLine() {
			uint now = Main.GameUpdateCount;
			if (spokeYet && now - lastLineTick < LineCooldown)
				return null;

			int pick;
			do {
				pick = Main.rand.Next(Lines.Length);
			} while (recent.Contains(pick));
			recent.Enqueue(pick);
			if (recent.Count > 8)
				recent.Dequeue();

			spokeYet = true;
			lastLineTick = now;
			return Lines[pick];
		}
	}
}

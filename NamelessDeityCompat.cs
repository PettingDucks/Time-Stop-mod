using Terraria;
using Terraria.ModLoader;

namespace TimeStop
{
	// Wrath of the Gods support. The Nameless Deity is never frozen, and shortly after he notices
	// a time stop he shatters it. Does nothing if Wrath of the Gods is not loaded.
	public class NamelessDeityCompat : ModSystem
	{
		// Ticks the Nameless Deity lets a time stop run before breaking it.
		public const int BreakDelay = 60;

		public static int NamelessType { get; private set; } = -1;

		public override void PostSetupContent() {
			NamelessType = -1;
			if (ModLoader.TryGetMod("NoxusBoss", out Mod wotg) && wotg.TryFind("NamelessDeityBoss", out ModNPC nameless))
				NamelessType = nameless.Type;
		}

		public override void Unload() {
			NamelessType = -1;
		}

		public static bool IsNameless(NPC npc) => NamelessType >= 0 && npc.type == NamelessType;

		public static bool NamelessPresent() => NamelessType >= 0 && NPC.AnyNPCs(NamelessType);
	}
}

using Terraria;
using Terraria.ID;

namespace TimeStop
{
	// Decides what is frozen. Players and everything they make keep moving; the rest of the world stops.
	internal static class Freeze
	{
		public static bool ShouldFreeze(NPC npc) {
			if (!TimeStopSystem.Active || !npc.active)
				return false;

			var config = TimeStopConfig.Get;

			// Worm bodies and multi-part bosses follow their main part, so a boss is never half frozen.
			NPC root = npc;
			if (npc.realLife >= 0 && npc.realLife < Main.maxNPCs && Main.npc[npc.realLife].active)
				root = Main.npc[npc.realLife];

			if (config.ExemptTypes.Contains(npc.type) || config.ExemptTypes.Contains(root.type))
				return false;

			// Time does not apply to a god.
			if (NamelessDeityCompat.IsNameless(npc) || NamelessDeityCompat.IsNameless(root))
				return false;

			if (!config.FreezeTownNPCs && (root.townNPC || NPCID.Sets.ActsLikeTownNPC[root.type]))
				return false;

			if (!config.FreezeBosses && (root.boss || NPCID.Sets.ShouldBeCountedAsBoss[root.type]))
				return false;

			return true;
		}

		public static bool ShouldFreeze(Projectile projectile) {
			if (!TimeStopSystem.Active || !projectile.active)
				return false;

			return !TimeStopProjectile.IsPlayerMade(projectile);
		}
	}
}

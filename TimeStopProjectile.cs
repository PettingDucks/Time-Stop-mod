using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace TimeStop
{
	// Remembers whether each projectile came from a player (weapons, minions, hooks, accessories)
	// or from the world (enemies, bosses, traps, falling sand, falling stars).
	public class TimeStopProjectile : GlobalProjectile
	{
		public override bool InstancePerEntity => true;

		private bool decided;
		private bool fromPlayer;

		public static bool IsPlayerMade(Projectile projectile) {
			if (projectile.TryGetGlobalProjectile(out TimeStopProjectile data) && data.decided)
				return data.fromPlayer;

			return Fallback(projectile);
		}

		public override void OnSpawn(Projectile projectile, IEntitySource source) {
			fromPlayer = Classify(projectile, source);
			decided = true;
		}

		private static bool Classify(Projectile projectile, IEntitySource source) {
			switch (source) {
				// Fired by an item: weapons, tools, accessories, summons.
				case IEntitySource_WithStatsFromItem:
					return true;

				// Traps, dart traps, statues and other world sources.
				case EntitySource_Wiring:
				case EntitySource_TileBreak:
				case EntitySource_TileUpdate:
				case EntitySource_TileEntity:
				case EntitySource_WorldEvent:
				case EntitySource_WorldGen:
				case EntitySource_BossSpawn:
				case EntitySource_SpawnNPC:
					return false;

				case EntitySource_Parent parent:
					switch (parent.Entity) {
						case Player:
							return true;
						// Anything an NPC fires belongs to the world, the Nameless Deity's attacks included: they freeze
						// while he alone keeps moving, until he breaks the stop.
						case NPC:
							return false;
						case Projectile parentProjectile:
							// Split bullets, explosions and minion shots inherit from what spawned them.
							return IsPlayerMade(parentProjectile);
					}
					break;
			}

			return Fallback(projectile);
		}

		// Used when the source gives no answer, or for a projectile that arrived over the network before
		// its owner's data did. Friendly, non-hostile projectiles owned by a real player count as the player's.
		private static bool Fallback(Projectile projectile) {
			if (projectile.hostile || projectile.npcProj || !projectile.friendly)
				return false;

			if (projectile.owner < 0 || projectile.owner >= Main.maxPlayers)
				return false;

			return Main.player[projectile.owner].active;
		}

		// Keep the server and every client agreeing on what is frozen.
		public override void SendExtraAI(Projectile projectile, BitWriter bitWriter, BinaryWriter binaryWriter) {
			bitWriter.WriteBit(IsPlayerMade(projectile));
		}

		public override void ReceiveExtraAI(Projectile projectile, BitReader bitReader, BinaryReader binaryReader) {
			fromPlayer = bitReader.ReadBit();
			decided = true;
		}

		// Frozen enemy projectiles do not hurt anyone.
		public override bool CanHitPlayer(Projectile projectile, Player target) {
			return !Freeze.ShouldFreeze(projectile);
		}

		public override bool? CanHitNPC(Projectile projectile, NPC target) {
			if (Freeze.ShouldFreeze(projectile))
				return false;

			return null;
		}
	}
}

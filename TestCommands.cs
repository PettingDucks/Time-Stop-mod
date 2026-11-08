using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TimeStop.Items;

namespace TimeStop
{
	// Testing helpers so the mod can be tried without crafting or waiting for enemies.
	// Both can be switched off in the Time Stop config ("Allow test commands").

	public class ChronoWatchCommand : ModCommand
	{
		public override CommandType Type => CommandType.World;
		public override string Command => "chronowatch";
		public override string Usage => "/chronowatch";
		public override string Description => "Gives you Orion's Half (Time Stop testing)";

		public override void Action(CommandCaller caller, string input, string[] args) {
			if (!TimeStopConfig.Get.AllowTestCommands) {
				caller.Reply("Time Stop test commands are turned off in the mod config", Color.OrangeRed);
				return;
			}

			Player player = caller.Player;
			if (player == null)
				return;

			player.QuickSpawnItem(player.GetSource_Misc("TimeStopTestCommand"), ModContent.ItemType<ChronoWatch>());
			caller.Reply("Gave you Orion's Half", new Color(150, 200, 255));
		}
	}

	public class SpawnTestCommand : ModCommand
	{
		public override CommandType Type => CommandType.World;
		public override string Command => "tsspawn";
		public override string Usage => "/tsspawn <npc name or ID> [count]   e.g. /tsspawn Zombie 5, /tsspawn EyeofCthulhu, /tsspawn NoxusBoss/NamelessDeityBoss";
		public override string Description => "Spawns NPCs next to you (Time Stop testing)";

		public override void Action(CommandCaller caller, string input, string[] args) {
			if (!TimeStopConfig.Get.AllowTestCommands) {
				caller.Reply("Time Stop test commands are turned off in the mod config", Color.OrangeRed);
				return;
			}

			Player player = caller.Player;
			if (player == null || args.Length == 0) {
				caller.Reply("Usage: " + Usage, Color.OrangeRed);
				return;
			}

			if (!TryResolveNPC(args[0], out int type)) {
				caller.Reply($"No NPC called \"{args[0]}\". Use a vanilla name like Zombie, or ModName/NPCName for modded ones", Color.OrangeRed);
				return;
			}

			int count = 1;
			if (args.Length > 1 && int.TryParse(args[1], out int parsed))
				count = Utils.Clamp(parsed, 1, 20);

			for (int i = 0; i < count; i++) {
				// Spread them out in front of the player, a few blocks away.
				Vector2 position = player.Center + new Vector2(player.direction * (250 + 40 * i), -40f);
				int index = NPC.NewNPC(player.GetSource_Misc("TimeStopTestCommand"), (int)position.X, (int)position.Y, type);

				if (Main.netMode == NetmodeID.Server && index < Main.maxNPCs)
					NetMessage.SendData(MessageID.SyncNPC, number: index);
			}

			caller.Reply($"Spawned {count} x {Lang.GetNPCNameValue(type)}", new Color(150, 200, 255));
		}

		private static bool TryResolveNPC(string name, out int type) {
			if (int.TryParse(name, out type))
				return type > 0 && type < NPCLoader.NPCCount;

			if (name.Contains('/') && ModContent.TryFind(name, out ModNPC modNPC)) {
				type = modNPC.Type;
				return true;
			}

			return NPCID.Search.TryGetId(name, out type);
		}
	}
}

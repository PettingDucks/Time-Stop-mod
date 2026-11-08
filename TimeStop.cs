using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TimeStop
{
	public class TimeStop : Mod
	{
		internal enum PacketType : byte
		{
			// Client asks the server to start or end a time stop.
			RequestToggle,
			// Server tells clients the current time stop state.
			SyncState,
			// Client asks the server to end a time stop because a cutscene started on that client.
			RequestEnd,
			// Server tells one player their own strain and lockout.
			SyncStrain,
		}

		public override void HandlePacket(BinaryReader reader, int whoAmI) {
			var type = (PacketType)reader.ReadByte();
			switch (type) {
				case PacketType.RequestToggle:
					if (Main.netMode == NetmodeID.Server)
						TimeStopSystem.Toggle(whoAmI);
					break;

				case PacketType.SyncState:
					TimeStopSystem.ReadState(reader, playEffects: true);
					break;

				case PacketType.SyncStrain:
					if (Main.netMode == NetmodeID.MultiplayerClient)
						TimeStopSystem.ReadStrain(reader);
					break;

				case PacketType.RequestEnd:
					if (Main.netMode == NetmodeID.Server)
						TimeStopSystem.EndForCutscene();
					break;

				default:
					Logger.Warn($"Unknown packet type {type}");
					break;
			}
		}
	}
}

using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using TimeStop.Items;

namespace TimeStop
{
	public class TimeStopPlayer : ModPlayer
	{
		public static ModKeybind ToggleKey { get; private set; }

		public override void Load() {
			// Unbound by default. Set it in Settings > Controls > Mod Controls.
			ToggleKey = KeybindLoader.RegisterKeybind(Mod, "ToggleTimeStop", "None");
		}

		public override void Unload() {
			ToggleKey = null;
		}

		public override void ProcessTriggers(TriggersSet triggersSet) {
			if (ToggleKey == null || !ToggleKey.JustPressed)
				return;

			if (Player.HasItem(ModContent.ItemType<ChronoWatch>()))
				TimeStopSystem.RequestToggle(Player);
		}
	}
}

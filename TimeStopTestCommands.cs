using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TimeStop
{
	// Commands for trying out time stop settings in game. All of them need "Allow test commands" on in the config.
	//   /tsduration <seconds | reset>   size of the time tank
	//   /tscooldown <seconds | reset>   the fixed cooldown between stops (decimals allowed, e.g. 0.5)
	//   /tsready                         full tank, no cooldown
	//   /tsstop                          start or end a time stop without the item (ignores the recharge)
	//   /tsbreak                         end the current time stop with the Nameless Deity's violent break
	//   /tsstatus                        show the current state and settings
	//   /tssound <preset | random | break | bigbreak>   preview a sound (only you hear it)

	internal static class TestCommandHelper
	{
		public static readonly Color Info = new Color(150, 200, 255);

		public static bool Allowed(CommandCaller caller) {
			if (TimeStopConfig.Get.AllowTestCommands)
				return true;

			caller.Reply("Time Stop test commands are turned off in the mod config", Color.OrangeRed);
			return false;
		}

		// Parses "<seconds>" or "reset". Returns false and replies with usage on bad input.
		public static bool TryParseSeconds(CommandCaller caller, string[] args, string usage, float min, float max, out float? seconds) {
			seconds = null;
			if (args.Length == 0) {
				caller.Reply("Usage: " + usage, Color.OrangeRed);
				return false;
			}

			if (args[0].Equals("reset", StringComparison.OrdinalIgnoreCase) || args[0].Equals("default", StringComparison.OrdinalIgnoreCase))
				return true;

			if (!float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value)
				|| value < min || value > max) {
				caller.Reply($"Enter a number of seconds from {min} to {max} (decimals allowed), or reset", Color.OrangeRed);
				return false;
			}

			seconds = value;
			return true;
		}
	}

	public class DurationCommand : ModCommand
	{
		public override CommandType Type => CommandType.World;
		public override string Command => "tsduration";
		public override string Usage => "/tsduration <seconds | reset>";
		public override string Description => "Sets the size of the time tank (testing)";

		public override void Action(CommandCaller caller, string input, string[] args) {
			if (!TestCommandHelper.Allowed(caller) || !TestCommandHelper.TryParseSeconds(caller, args, Usage, 1f, 600f, out float? seconds))
				return;

			TimeStopSystem.DurationOverrideSeconds = seconds;
			caller.Reply(seconds.HasValue
				? $"The time tank now holds {seconds}s (until the world is closed)"
				: $"Time tank back to the config value ({TimeStopSystem.DurationSeconds}s)", TestCommandHelper.Info);
		}
	}

	public class CooldownCommand : ModCommand
	{
		public override CommandType Type => CommandType.World;
		public override string Command => "tscooldown";
		public override string Usage => "/tscooldown <seconds | reset>";
		public override string Description => "Sets the fixed cooldown between time stops (testing)";

		public override void Action(CommandCaller caller, string input, string[] args) {
			if (!TestCommandHelper.Allowed(caller) || !TestCommandHelper.TryParseSeconds(caller, args, Usage, 0f, 600f, out float? seconds))
				return;

			TimeStopSystem.CooldownOverrideSeconds = seconds;
			caller.Reply(seconds.HasValue
				? $"Cooldown between stops is now {seconds}s (until the world is closed)"
				: $"Cooldown back to the config value ({TimeStopSystem.CooldownSeconds}s)", TestCommandHelper.Info);
		}
	}

	public class ReadyCommand : ModCommand
	{
		public override CommandType Type => CommandType.World;
		public override string Command => "tsready";
		public override string Description => "Fills the time tank and clears the cooldown (testing)";

		public override void Action(CommandCaller caller, string input, string[] args) {
			if (!TestCommandHelper.Allowed(caller))
				return;

			TimeStopSystem.TestClearCooldown();
			caller.Reply("Orion's Half is full and ready", TestCommandHelper.Info);
		}
	}

	public class StopCommand : ModCommand
	{
		public override CommandType Type => CommandType.World;
		public override string Command => "tsstop";
		public override string Description => "Starts or ends a time stop without the item (testing)";

		public override void Action(CommandCaller caller, string input, string[] args) {
			if (!TestCommandHelper.Allowed(caller))
				return;

			int who = caller.Player?.whoAmI ?? 0;
			TimeStopSystem.TestToggle(who);
		}
	}

	public class BreakCommand : ModCommand
	{
		public override CommandType Type => CommandType.World;
		public override string Command => "tsbreak";
		public override string Description => "Ends the current time stop with the Nameless Deity's violent break (testing)";

		public override void Action(CommandCaller caller, string input, string[] args) {
			if (!TestCommandHelper.Allowed(caller))
				return;

			if (!TimeStopSystem.TestViolentBreak())
				caller.Reply("Time is not stopped. Use /tsstop or the watch first", Color.OrangeRed);
		}
	}

	public class StatusCommand : ModCommand
	{
		public override CommandType Type => CommandType.World;
		public override string Command => "tsstatus";
		public override string Description => "Shows the time stop state and settings (testing)";

		public override void Action(CommandCaller caller, string input, string[] args) {
			string fuel = $"{TimeStopSystem.Fuel / 60f:0.0}s of {TimeStopSystem.MaxFuel / 60f:0.0}s";
			string state = TimeStopSystem.Active ? "time stopped"
				: TimeStopSystem.CooldownTicks > 0 ? $"cooling down ({TimeStopSystem.CooldownTicks / 60f:0.0}s)"
				: TimeStopSystem.Refilling ? "refilling" : "full";

			string cooldown = TimeStopSystem.CooldownOverrideSeconds.HasValue ? $"{TimeStopSystem.CooldownSeconds}s (test override)" : $"{TimeStopSystem.CooldownSeconds}s";
			string wotg = NamelessDeityCompat.NamelessType >= 0 ? "found" : "not loaded";

			caller.Reply($"Time tank: {fuel}, {state}. Refill speed {TimeStopConfig.Get.RefillSpeed}x, cooldown {cooldown}. Wrath of the Gods: {wotg}", TestCommandHelper.Info);
		}
	}

	public class SoundCommand : ModCommand
	{
		// Runs on your own client, so only you hear the preview.
		public override CommandType Type => CommandType.Chat;
		public override string Command => "tssound";
		public override string Usage => "/tssound <start | break | bigbreak>";
		public override string Description => "Previews a Time Stop sound, a random variation each time (testing)";

		public override void Action(CommandCaller caller, string input, string[] args) {
			if (!TestCommandHelper.Allowed(caller))
				return;

			string choice = args.Length > 0 ? args[0].ToLowerInvariant() : "start";
			switch (choice) {
				case "start":
					TimeStopSounds.PlayStart();
					return;
				case "break":
					TimeStopSounds.PlayBreak(false);
					return;
				case "bigbreak":
					TimeStopSounds.PlayBreak(true);
					return;
				default:
					caller.Reply("Usage: " + Usage, Color.OrangeRed);
					return;
			}
		}
	}
}

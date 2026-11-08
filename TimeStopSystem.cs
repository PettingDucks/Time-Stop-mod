using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.Chat;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace TimeStop
{
	// Holds the world-wide time stop state. The server (or the single player game) is in charge of it,
	// and clients only display what the server sends them.
	public class TimeStopSystem : ModSystem
	{
		public static bool Active { get; private set; }

		// Stopped time works like fuel; see FuelTank for the rules.
		private static FuelTank tank = FuelTank.Full(300f);

		public static float Fuel => tank.Fuel;
		public static float MaxFuel => tank.Max;
		public static int TicksLeft => (int)Math.Ceiling(tank.Fuel);
		public static int TotalTicks => (int)tank.Max;
		public static int CooldownTicks => tank.Cooldown;
		public static bool Refilling => !Active && tank.Fuel < tank.Max;

		// How long the Nameless Deity has been present during this time stop.
		public static int CrackTicks { get; private set; }

		// True when the last time stop ended because the Nameless Deity broke it.
		public static bool LastStopShattered { get; private set; }

		// The player who stopped time; the start animation spreads out from them.
		public static int CasterWho { get; private set; } = -1;

		public override void Load() {
			TimeStopHooks.Apply();
		}

		public override void OnWorldLoad() => Reset();
		public override void OnWorldUnload() => Reset();
		public override void ClearWorld() => Reset();

		private static void Reset() {
			Active = false;
			CrackTicks = 0;
			LastStopShattered = false;
			CasterWho = -1;
			DurationOverrideSeconds = null;
			CooldownOverrideSeconds = null;
			tank = FuelTank.Full(TankTicks);
			for (int i = 0; i < strains.Length; i++) {
				strains[i].Reset();
				lockouts[i] = 0;
			}
			localStrain.Reset();
			localLockout = 0;
			lastStopTicks = int.MaxValue;
			activeTicks = 0;
			ShatterEffect.Clear();
			TimeStopIntro.Clear();
			ScreenCracks.ClearAll();
		}

		// ---------- Strain (spam damage) ----------

		// Strain is personal: each player has their own meter and their own lockout, and only that player sees
		// their cracks and inversion. The server (or single player game) keeps everyone's; each client keeps a copy
		// of just its own, for drawing.
		private static readonly StrainMeter[] strains = new StrainMeter[Main.maxPlayers + 1];
		private static readonly int[] lockouts = new int[Main.maxPlayers + 1];
		private static StrainMeter localStrain;
		private static int localLockout;

		private static uint lastEndTick;
		private static int lastStopTicks = int.MaxValue;
		private static int activeTicks;

		public static float StrainIntensity => Main.netMode == NetmodeID.MultiplayerClient ? localStrain.Intensity : strains[Main.myPlayer].Intensity;
		public static float StrainValueOf(int who) => strains[who].Value;
		public static int LocalLockoutTicks => Main.netMode == NetmodeID.MultiplayerClient ? localLockout : lockouts[Main.myPlayer];

		private const int FractureLockoutTicks = 240;     // 4 seconds, for that player only

		private static void TickStrain() {
			if (Main.netMode == NetmodeID.MultiplayerClient) {
				localStrain.Tick();
				if (localLockout > 0)
					localLockout--;
				return;
			}

			for (int i = 0; i < strains.Length; i++) {
				strains[i].Tick();
				if (lockouts[i] > 0)
					lockouts[i]--;
			}
		}

		// Server or single player: one player's screen breaks under their own strain.
		private static void Fracture(int who) {
			strains[who].Reset();
			lockouts[who] = FractureLockoutTicks;
			SendStrain(who, fractured: true);
			if (Main.netMode != NetmodeID.Server && who == Main.myPlayer)
				PlayFractureEffects();
			Notify(who, "Orion's Half fractures under the strain.");
		}

		// Server -> that one player: their strain and lockout (and whether they just fractured).
		private static void SendStrain(int who, bool fractured) {
			if (Main.netMode != NetmodeID.Server)
				return;

			ModPacket packet = ModContent.GetInstance<TimeStop>().GetPacket();
			packet.Write((byte)TimeStop.PacketType.SyncStrain);
			packet.Write(strains[who].Value);
			packet.Write(lockouts[who]);
			packet.Write(fractured);
			packet.Send(toClient: who);
		}

		internal static void ReadStrain(System.IO.BinaryReader reader) {
			localStrain.Value = reader.ReadSingle();
			localStrain.Hold = StrainMeter.HoldTicks;
			localLockout = reader.ReadInt32();
			if (reader.ReadBoolean())
				PlayFractureEffects();
		}

		private static void PlayFractureEffects() {
			if (Main.dedServ)
				return;

			ScreenCracks.Clear();
			ScreenCracks.StartInversion();
			ShatterEffect.Shatter(violent: true);
		}

		public override void PostUpdateEverything() {
			ShatterEffect.Update();
			TimeStopIntro.Update();
			tank.SetMax(TankTicks);
			TickStrain();
			ScreenCracks.Update(StrainIntensity, Main.myPlayer);
			if (Active)
				activeTicks++;

			if (Active && CutsceneCompat.CutsceneActive()) {
				// Cutscenes run on their own clock, so let time flow again before anything falls out of step.
				if (Main.netMode == NetmodeID.MultiplayerClient) {
					if (!endRequestSent) {
						endRequestSent = true;
						ModPacket packet = ModContent.GetInstance<TimeStop>().GetPacket();
						packet.Write((byte)TimeStop.PacketType.RequestEnd);
						packet.Send();
					}
				}
				else {
					EndForCutscene();
					return;
				}
			}

			if (Active) {
				if (NamelessDeityCompat.NamelessPresent()) {
					CrackTicks++;
					if (CrackTicks == 1)
						ShatterEffect.BeginCracks();

					if (CrackTicks >= NamelessDeityCompat.BreakDelay && Main.netMode != NetmodeID.MultiplayerClient) {
						Stop(shattered: true);
						return;
					}
				}
				else {
					CrackTicks = 0;
				}

				// Clients run the same fuel maths for display; only the server decides when it runs dry.
				bool ranDry = tank.TickStopped();
				if (ranDry && Main.netMode != NetmodeID.MultiplayerClient)
					Stop();
			}
			else {
				CrackTicks = 0;
				tank.TickFlowing(RefillRate);
			}
		}

		// Stops the clock, tile updates (grass, trees, falling blocks from random updates) and world events.
		public override void ModifyTimeRate(ref double timeRate, ref double tileUpdateRate, ref double eventUpdateRate) {
			if (!Active)
				return;

			timeRate = 0;
			tileUpdateRate = 0;
			eventUpdateRate = 0;
		}

		private static bool endRequestSent;

		// Server or single player. Ends the time stop without the recharge, since the player did nothing wrong.
		internal static void EndForCutscene() {
			if (!Active)
				return;

			Stop(noPenalty: true);
		}

		// Called on the client that used the watch or pressed the hotkey.
		public static void RequestToggle(Player player) {
			if (!Active && CutsceneCompat.CutsceneActive()) {
				Main.NewText("Time cannot be stopped during a cutscene", new Color(150, 200, 255));
				return;
			}

			if (Main.netMode == NetmodeID.MultiplayerClient) {
				ModPacket packet = ModContent.GetInstance<TimeStop>().GetPacket();
				packet.Write((byte)TimeStop.PacketType.RequestToggle);
				packet.Send();
			}
			else {
				Toggle(player.whoAmI);
			}
		}

		// Server or single player only.
		internal static void Toggle(int who) {
			if (Active) {
				Stop();
				return;
			}

			if (tank.Cooldown > 0)
				return;   // the half-second cooldown is too short to be worth a message

			if (tank.Fuel <= 0f) {
				Notify(who, "Orion's Half is out of time. It refills while time flows");
				return;
			}

			// A player whose own screen is fractured cannot stop time until their lockout ends.
			if (lockouts[who] > 0)
				return;

			// Strain only builds from starting again as fast as possible after a quick tap, and only for that player.
			if (TimeStopConfig.Get.StrainEnabled) {
				int sinceEnd = (int)Math.Min(int.MaxValue, Main.GameUpdateCount - lastEndTick);
				float before = strains[who].Value;
				bool overload = strains[who].RegisterStart(sinceEnd, lastStopTicks, (int)Math.Round(CooldownSeconds * 60f));
				if (overload) {
					Fracture(who);
					return;
				}
				if (strains[who].Value != before)
					SendStrain(who, fractured: false);
			}

			Start(who);
		}

		// Test overrides set by /tsduration and /tscooldown. Null means use the config. Cleared when the world unloads.
		internal static float? DurationOverrideSeconds;
		internal static float? CooldownOverrideSeconds;

		internal static float DurationSeconds => DurationOverrideSeconds ?? TimeStopConfig.Get.TankSeconds;
		internal static float CooldownSeconds => CooldownOverrideSeconds ?? TimeStopConfig.Get.StopCooldownSeconds;
		// The tank is smaller while any boss is alive (3 s by default instead of 5 s). /tsduration overrides both.
		private static float TankTicks => (DurationOverrideSeconds ?? (BossAlive ? TimeStopConfig.Get.BossTankSeconds : TimeStopConfig.Get.TankSeconds)) * 60f;

		public static bool BossAlive {
			get {
				foreach (NPC npc in Main.ActiveNPCs) {
					if (npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type])
						return true;
				}
				return false;
			}
		}
		private static float RefillRate => TimeStopConfig.Get.RefillSpeed;

		private static void Start(int who) {
			Active = true;
			activeTicks = 0;
			CasterWho = who;
			CrackTicks = 0;
			LastStopShattered = false;
			OnStateChanged();
		}

		// noPenalty: the break came from a cutscene or the Nameless Deity, so no cooldown.
		private static void Stop(bool shattered = false, bool noPenalty = false) {
			Active = false;
			lastEndTick = Main.GameUpdateCount;
			lastStopTicks = activeTicks;
			CrackTicks = 0;
			TimeStopNPC.ReleaseAll();   // stored damage lands the moment time resumes
			LastStopShattered = shattered;
			if (shattered || noPenalty)
				tank.EndedWithoutPenalty();
			else
				tank.Ended((int)Math.Round(CooldownSeconds * 60f));
			OnStateChanged();

			if (shattered && NamelessDeityCompat.NamelessPresent())
				Broadcast("The Nameless Deity shatters your stopped time.", new Color(255, 215, 120));
		}

		// ---------- Test command entry points (server or single player) ----------

		internal static void TestToggle(int who) {
			if (Active) {
				Stop();
			}
			else {
				tank.Cooldown = 0;
				if (tank.Fuel <= 0f)
					tank.Fuel = tank.Max;
				Start(who);
			}
		}

		internal static bool TestViolentBreak() {
			if (!Active)
				return false;

			Stop(shattered: true);
			return true;
		}

		// /tsready: full tank, no cooldown.
		internal static void TestClearCooldown() {
			tank.Cooldown = 0;
			tank.Fuel = tank.Max;
			for (int i = 0; i < strains.Length; i++) {
				strains[i].Reset();
				lockouts[i] = 0;
				if (i < Main.maxPlayers && Main.player[i].active)
					SendStrain(i, fractured: false);
			}
			SyncQuietly();
		}

		// Sends the current state to clients without playing any start or break effects.
		private static void SyncQuietly() {
			if (Main.netMode != NetmodeID.Server)
				return;

			ModPacket packet = ModContent.GetInstance<TimeStop>().GetPacket();
			packet.Write((byte)TimeStop.PacketType.SyncState);
			WriteState(packet);
			packet.Send();
		}

		private static void Broadcast(string message, Color color) {
			if (Main.netMode == NetmodeID.Server)
				ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(message), color);
			else
				Main.NewText(message, color);
		}

		private static void PlayStateEffects() {
			if (Main.dedServ)
				return;

			if (Active) {
				TimeStopIntro.Begin(CasterWho);
			}
			else {
				// Time always resumes by the tint breaking like glass. The Nameless Deity breaks it much harder.
				TimeStopIntro.Clear();
				ShatterEffect.Shatter(violent: LastStopShattered);
			}
		}

		private static void OnStateChanged() {
			if (Main.netMode == NetmodeID.Server) {
				ModPacket packet = ModContent.GetInstance<TimeStop>().GetPacket();
				packet.Write((byte)TimeStop.PacketType.SyncState);
				WriteState(packet);
				packet.Send();
			}
			else {
				PlayStateEffects();
			}
		}

		private static void Notify(int who, string message) {
			Color color = new Color(150, 200, 255);
			if (Main.netMode == NetmodeID.Server)
				ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral(message), color, who);
			else
				Main.NewText(message, color);
		}

		private static void WriteState(BinaryWriter writer) {
			writer.Write(Active);
			writer.Write((short)CasterWho);
			writer.Write(tank.Fuel);
			writer.Write(tank.Max);
			writer.Write(tank.Cooldown);
			writer.Write(LastStopShattered);
		}

		internal static void ReadState(BinaryReader reader, bool playEffects) {
			bool wasActive = Active;
			Active = reader.ReadBoolean();
			CasterWho = reader.ReadInt16();
			tank.Fuel = reader.ReadSingle();
			tank.Max = reader.ReadSingle();
			tank.Cooldown = reader.ReadInt32();
			LastStopShattered = reader.ReadBoolean();

			// time just resumed: release the damage this machine stored on frozen enemies
			if (wasActive && !Active)
				TimeStopNPC.ReleaseAll();

			if (Active != wasActive) {
				CrackTicks = 0;
				endRequestSent = false;
			}

			if (playEffects && wasActive != Active)
				PlayStateEffects();
		}

		// Sent to players when they join, so a late joiner sees a time stop that is already running.
		public override void NetSend(BinaryWriter writer) => WriteState(writer);
		public override void NetReceive(BinaryReader reader) => ReadState(reader, playEffects: false);

		// The whole Time Stop overlay is drawn over EVERYTHING: the world, the hotbar, inventory, minimap and the
		// fullscreen map. It is purely visual, so clicking and using the interface work exactly as normal underneath.
		// Only the mouse cursor stays on top, so you can always see where you are clicking.
		private static bool OverlayNeeded => Active || ShatterEffect.Visible || ShowRefillGauge || ScreenCracks.Visible;

		public override void PostDrawInterface(SpriteBatch spriteBatch) => DrawOverEverything(Main.UIScaleMatrix);

		public override void PostDrawFullscreenMap(ref string mouseText) => DrawOverEverything(Matrix.Identity);

		private static void DrawOverEverything(Matrix restoreMatrix) {
			if (Main.dedServ || !OverlayNeeded)
				return;

			bool wasDrawing = true;
			try {
				Main.spriteBatch.End();
			}
			catch (InvalidOperationException) {
				wasDrawing = false;
			}

			Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
				DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Matrix.Identity);
			try {
				DrawOverlay();
			}
			finally {
				Main.spriteBatch.End();
				if (wasDrawing) {
					Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
						DepthStencilState.None, RasterizerState.CullCounterClockwise, null, restoreMatrix);
				}
			}
		}

		public static readonly Color TintColor = new Color(40, 70, 150);
		public const float TintOpacity = 0.18f;

		private static void DrawOverlay() {
			var client = TimeStopClientConfig.Get;

			if (Active) {
				// 0 to 1 while the Nameless Deity is breaking the time stop.
				float crack = CrackTicks / (float)NamelessDeityCompat.BreakDelay;

				// The glass pane: crystallizing during the start animation, then a still faceted tint.
				if (client.ShowOverlay)
					TimeStopIntro.DrawTint(TintColor * TintOpacity);


				if (client.ShowTimer)
					TimeStopHud.Draw(crack);
			}
			else if (ShowRefillGauge) {
				// Refilling: the medallion stays up, dimmer, so you can see the tank filling back up.
				TimeStopHud.Draw(0f, refilling: true);
			}

			// Spam damage: cracks across the screen itself, over everything.
			ScreenCracks.Draw();
			ShatterEffect.DrawShards();

			// When strain truly breaks the screen, everything flips to its negative for a moment.
			ScreenCracks.DrawInversion();
		}

		// The refill gauge only shows for a player carrying the watch, and only while the tank is not full.
		private static bool ShowRefillGauge {
			get {
				if (!Refilling || Main.dedServ || !TimeStopClientConfig.Get.ShowTimer)
					return false;

				Player player = Main.LocalPlayer;
				return player != null && player.active && player.HasItem(ModContent.ItemType<Items.ChronoWatch>());
			}
		}
	}
}

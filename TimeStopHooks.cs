using Terraria;

namespace TimeStop
{
	// Detours that skip world updates while time is stopped.
	// tModLoader removes these automatically when the mod unloads.
	internal static class TimeStopHooks
	{
		public static void Apply() {
			On_NPC.UpdateNPC += NPC_UpdateNPC;
			On_Projectile.Update += Projectile_Update;
			On_Liquid.UpdateLiquid += Liquid_UpdateLiquid;
			On_Cloud.UpdateClouds += Cloud_UpdateClouds;
			On_Rain.Update += Rain_Update;

			// Scenery
			Terraria.Graphics.Effects.On_SkyManager.Update += SkyManager_Update;
			On_Gore.Update += Gore_Update;
			On_Main.AnimateTiles += Main_AnimateTiles;
			Terraria.GameContent.Liquid.On_LiquidRenderer.Update += LiquidRenderer_Update;
			On_Star.UpdateStars += Star_UpdateStars;
			Terraria.GameContent.Drawing.On_TileDrawing.Update += TileDrawing_Update;
			On_Dust.UpdateDust += SceneryFreeze.UpdateDust;
		}

		// Twinkling and falling background stars.
		private static void Star_UpdateStars(On_Star.orig_UpdateStars orig) {
			if (SceneryFreeze.Enabled)
				return;

			orig();
		}

		// Grass, tree, vine and banner sway counters.
		private static void TileDrawing_Update(Terraria.GameContent.Drawing.On_TileDrawing.orig_Update orig, Terraria.GameContent.Drawing.TileDrawing self) {
			if (SceneryFreeze.Enabled)
				return;

			orig(self);
		}

		private static void SkyManager_Update(Terraria.Graphics.Effects.On_SkyManager.orig_Update orig, Terraria.Graphics.Effects.SkyManager self, Microsoft.Xna.Framework.GameTime gameTime) {
			if (SceneryFreeze.Enabled)
				return;

			orig(self, gameTime);
		}

		private static void Gore_Update(On_Gore.orig_Update orig, Gore self) {
			if (SceneryFreeze.Enabled)
				return;

			orig(self);
		}

		private static void Main_AnimateTiles(On_Main.orig_AnimateTiles orig) {
			if (SceneryFreeze.Enabled)
				return;

			orig();
		}

		private static void LiquidRenderer_Update(Terraria.GameContent.Liquid.On_LiquidRenderer.orig_Update orig, Terraria.GameContent.Liquid.LiquidRenderer self, Microsoft.Xna.Framework.GameTime gameTime) {
			if (SceneryFreeze.Enabled)
				return;

			orig(self, gameTime);
		}

		// Skipping the whole update (not only the AI) also stops Calamity/Infernum style AI that runs
		// from other mods' GlobalNPC.PreAI, plus movement, gravity, animation and damage-over-time ticks.
		private static void NPC_UpdateNPC(On_NPC.orig_UpdateNPC orig, NPC self, int i) {
			if (!TimeStopSystem.Active || !Freeze.ShouldFreeze(self)) {
				orig(self, i);
				return;
			}

			self.whoAmI = i;

			// Player hit immunity normally counts down inside the NPC update. Keep it counting
			// so frozen enemies can still be hit over and over.
			for (int p = 0; p < self.immune.Length; p++) {
				if (self.immune[p] > 0)
					self.immune[p]--;
			}

			self.oldPosition = self.position;
		}

		private static void Projectile_Update(On_Projectile.orig_Update orig, Projectile self, int i) {
			if (TimeStopSystem.Active && self.active && Freeze.ShouldFreeze(self)) {
				self.whoAmI = i;
				return;
			}

			orig(self, i);
		}

		private static void Liquid_UpdateLiquid(On_Liquid.orig_UpdateLiquid orig) {
			if (TimeStopSystem.Active && TimeStopConfig.Get.FreezeLiquids)
				return;

			orig();
		}

		private static void Cloud_UpdateClouds(On_Cloud.orig_UpdateClouds orig) {
			if (TimeStopSystem.Active)
				return;

			orig();
		}

		private static void Rain_Update(On_Rain.orig_Update orig, Rain self) {
			if (TimeStopSystem.Active)
				return;

			orig(self);
		}
	}
}

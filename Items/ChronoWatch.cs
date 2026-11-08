using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace TimeStop.Items
{
	// Shown in game as "Orion's Half". The class keeps its original name so existing copies stay valid.
	public class ChronoWatch : ModItem
	{
		public override void SetStaticDefaults() {
			Item.ResearchUnlockCount = 1;
		}

		public override void SetDefaults() {
			// The sprite is a 90x90 diagonal relic (blade to the upper left). At 0.72 it stands about 65 pixels
			// tall when held up, roughly 1.5x the player.
			Item.width = 60;
			Item.height = 60;
			Item.scale = 0.72f;
			Item.maxStack = 1;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.useTime = 30;
			Item.useAnimation = 30;
			Item.autoReuse = false;
			Item.noMelee = true;
			Item.noUseGraphic = true;     // the held relic is drawn by OrionHeldLayer instead
			Item.UseSound = null;
			Item.value = Item.sellPrice(platinum: 1);

			// Calamity's post-Devourer of Gods rarity, if Calamity is loaded.
			if (ModContent.TryFind("CalamityMod/DarkBlue", out ModRarity rarity))
				Item.rare = rarity.Type;
			else
				Item.rare = ItemRarityID.Purple;
		}

		// ---------- visuals (see OrionFx) ----------

		// The item texture (ChronoWatch.png) is a pre-turned copy, used wherever the game draws the item by itself.
		// In the inventory and in the world we draw the upright sprite turned 45 degrees instead, so the pixels
		// themselves rotate, with the effects layered around it.
		public override bool PreDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale) {
			if (TimeStopSystem.LocalLockoutTicks > 0) {
				OrionFx.DrawBroken(spriteBatch, position, scale, drawColor);
				return false;
			}
			OrionFx.DrawBehind(spriteBatch, position, scale);
			OrionFx.DrawItem(spriteBatch, position, scale, drawColor);
			OrionFx.DrawFront(spriteBatch, position, scale);
			return false;
		}

		public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor, ref float rotation, ref float scale, int whoAmI) {
			// hovers over its hitbox with a slow bob instead of sinking into the floor
			float bob = MathF.Sin(Main.GlobalTimeWrappedHourly * 2f + whoAmI) * 2f;
			Vector2 centre = Item.Center - Main.screenPosition + new Vector2(0f, bob - 4f);
			if (TimeStopSystem.LocalLockoutTicks > 0) {
				OrionFx.DrawBroken(spriteBatch, centre, scale, lightColor);
				return false;
			}
			OrionFx.DrawBehind(spriteBatch, centre, scale);
			OrionFx.DrawItem(spriteBatch, centre, scale, Color.Lerp(lightColor, Color.White, 0.35f));
			OrionFx.DrawFront(spriteBatch, centre, scale);
			return false;
		}

		public override void PostUpdate() {
			Lighting.AddLight(Item.Center, 0.18f, 0.36f, 0.5f);
		}

		// ---------- use: stab it straight up into the air above you (see OrionHeld) ----------

		public override bool? UseItem(Player player) => true;    // the toggle happens at the top of the stab, below

		public override void UseStyle(Player player, Rectangle heldItemFrame) {
			OrionHeld.Pose pose = OrionHeld.GetPose(player);
			player.itemLocation = pose.Hand;
			player.itemRotation = 0f;                             // the relic stays upright, blade to the sky

			// time stops (or resumes) the moment the stab reaches the top
			OrionHeldPlayer held = player.GetModPlayer<OrionHeldPlayer>();
			if (player.itemAnimation == player.itemAnimationMax)
				held.ToggledThisUse = false;
			if (!held.ToggledThisUse && pose.Progress >= OrionHeld.StabTop) {
				held.ToggledThisUse = true;
				if (player.whoAmI == Main.myPlayer)
					TimeStopSystem.RequestToggle(player);
			}
		}

		public override void UseItemFrame(Player player) {
			OrionHeld.Pose pose = OrionHeld.GetPose(player);
			player.SetCompositeArmFront(true, pose.Stretch, pose.ArmRotation);
		}

		public override void AddRecipes() {
			foreach (int watch in new[] { ItemID.GoldWatch, ItemID.PlatinumWatch }) {
				Recipe recipe = CreateRecipe().AddIngredient(watch);

				// Calamity: post-Devourer of Gods. Cosmilite from the Devourer himself, Darksun Fragments from the
				// post-DoG eclipse, Nightmare Fuel and Endothermic Energy from the post-DoG Pumpkin and Frost Moons,
				// Ascendant Spirit Essence, and the Sentinels' Twisting Nether. Forged at the Cosmic Anvil.
				if (ModContent.TryFind("CalamityMod/CosmiliteBar", out ModItem cosmilite)
					&& ModContent.TryFind("CalamityMod/DarksunFragment", out ModItem darksun)
					&& ModContent.TryFind("CalamityMod/NightmareFuel", out ModItem nightmare)
					&& ModContent.TryFind("CalamityMod/EndothermicEnergy", out ModItem endothermic)
					&& ModContent.TryFind("CalamityMod/AscendantSpiritEssence", out ModItem ascendant)
					&& ModContent.TryFind("CalamityMod/TwistingNether", out ModItem twistingNether)) {
					recipe.AddIngredient(cosmilite.Type, 12)
						.AddIngredient(darksun.Type, 10)
						.AddIngredient(nightmare.Type, 10)
						.AddIngredient(endothermic.Type, 10)
						.AddIngredient(ascendant.Type, 4)
						.AddIngredient(twistingNether.Type, 3);

					if (ModContent.TryFind("CalamityMod/CosmicAnvil", out ModTile cosmicAnvil))
						recipe.AddTile(cosmicAnvil.Type);
					else
						recipe.AddTile(TileID.LunarCraftingStation);
					recipe.Register();
					continue;
				}
				else {
					// Without Calamity: post-Moon Lord.
					recipe.AddIngredient(ItemID.LunarBar, 10)
						.AddIngredient(ItemID.FragmentSolar, 5)
						.AddIngredient(ItemID.FragmentVortex, 5)
						.AddIngredient(ItemID.FragmentNebula, 5)
						.AddIngredient(ItemID.FragmentStardust, 5);
				}

				recipe.AddTile(TileID.LunarCraftingStation).Register();
			}
		}
	}
}

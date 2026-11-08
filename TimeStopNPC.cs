using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TimeStop
{
	public class TimeStopNPC : GlobalNPC
	{
		public override bool InstancePerEntity => true;

		// ---------- Stored damage ----------
		// Hits on a frozen enemy do not land yet: each one only chips 1 damage, and the rest is stored. When time
		// resumes, everything stored hits at once as a single blow. Frozen bosses also take reduced damage.

		private long storedDamage;
		private bool storedCrit;
		private float storedKnockback;
		private int storedDirection = 1;
		private DamageClass storedType;

		public override void ModifyIncomingHit(NPC npc, ref NPC.HitModifiers modifiers) {
			if (!Freeze.ShouldFreeze(npc))
				return;

			if (IsBossPart(npc))
				modifiers.FinalDamage *= TimeStopConfig.Get.FrozenBossDamage;

			if (!TimeStopConfig.Get.StoredDamage)
				return;

			TimeStopNPC self = this;
			modifiers.ModifyHitInfo += (ref NPC.HitInfo info) => {
				self.storedDamage += Math.Max(0, info.Damage - 1);
				self.storedCrit |= info.Crit;
				self.storedKnockback = Math.Max(self.storedKnockback, info.Knockback);
				self.storedDirection = info.HitDirection == 0 ? self.storedDirection : info.HitDirection;
				self.storedType = info.DamageType;
				info.Damage = 1;
				info.Knockback = 0f;
				info.HideCombatText = true;
			};
		}

		private static bool IsBossPart(NPC npc) {
			NPC root = npc.realLife >= 0 && npc.realLife < Main.maxNPCs && Main.npc[npc.realLife].active ? Main.npc[npc.realLife] : npc;
			return root.boss || NPCID.Sets.ShouldBeCountedAsBoss[root.type];
		}

		// Called on every machine when time resumes: each one releases the damage it stored.
		public static void ReleaseAll() {
			foreach (NPC npc in Main.ActiveNPCs) {
				if (!npc.TryGetGlobalNPC(out TimeStopNPC data) || data.storedDamage <= 0)
					continue;

				var hit = new NPC.HitInfo {
					Damage = (int)Math.Min(int.MaxValue, data.storedDamage),
					Crit = data.storedCrit,
					Knockback = data.storedKnockback,
					HitDirection = data.storedDirection,
					DamageType = data.storedType ?? DamageClass.Default,
				};
				data.storedDamage = 0;
				data.storedCrit = false;
				data.storedKnockback = 0f;

				npc.StrikeNPC(hit);
				if (Main.netMode != NetmodeID.SinglePlayer)
					NetMessage.SendStrikeNPC(npc, hit);
			}
		}

		// ---------- Freeze behaviour ----------

		// No new enemies or critters appear while time is stopped.
		public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns) {
			if (!TimeStopSystem.Active)
				return;

			// Very large but not int.MaxValue, so another mod multiplying the rate cannot overflow it.
			spawnRate = 10_000_000;
			maxSpawns = 0;
		}

		// Touching a frozen enemy does not hurt.
		public override bool CanHitPlayer(NPC npc, Player target, ref int cooldownSlot) {
			return !Freeze.ShouldFreeze(npc);
		}

		public override bool CanHitNPC(NPC npc, NPC target) {
			return !Freeze.ShouldFreeze(npc);
		}

		// Frozen NPCs look slightly cold and grey; with damage stored up they glow faintly hotter.
		public override void DrawEffects(NPC npc, ref Color drawColor) {
			if (!Freeze.ShouldFreeze(npc))
				return;

			drawColor = Color.Lerp(drawColor, new Color(150, 170, 210, drawColor.A), 0.35f);
			if (storedDamage > 0) {
				float heat = MathHelper.Clamp(storedDamage / (float)Math.Max(1, npc.lifeMax) * 4f, 0f, 0.5f);
				drawColor = Color.Lerp(drawColor, new Color(255, 230, 200, drawColor.A), heat);
			}
		}
	}
}

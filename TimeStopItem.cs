using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TimeStop
{
	// Dropped items hang in place, but players can still pick them up and pull them in.
	public class TimeStopItem : GlobalItem
	{
		public override void Update(Item item, ref float gravity, ref float maxFallSpeed) {
			if (!TimeStopSystem.Active || !TimeStopConfig.Get.FreezeDroppedItems)
				return;

			if (item.beingGrabbed)
				return;

			gravity = 0f;
			maxFallSpeed = 0f;
			item.velocity = Vector2.Zero;
		}
	}
}

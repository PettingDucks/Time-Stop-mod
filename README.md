# Time Stop

<!-- intro goes here -->

## Features

**Orion's Half** stops time for everything except players.

While time is stopped:
- Enemies, bosses, town NPCs and critters freeze in place and can't hurt you
- Enemy projectiles hang in the air and can't hurt you
- Your weapons, minions, building, mining and placing all work normally
- The clock, weather, events, liquids, grass growth, falling sand, dropped items, scenery and enemy spawns are paused

How it works:
- **Time tank:** stopped time works like fuel. A full tank holds 5 seconds, or 3 seconds while a boss is alive. Time resumes on its own when it runs dry, and the tank refills at 80% of the drain speed while time flows.
- **Cooldown:** a fixed 0.5 second wait after every stop.
- **Stored damage:** hits on frozen enemies are stored and all land at once when time resumes. Frozen bosses take half damage.
- **Strain:** spamming time stops as fast as possible cracks your screen. Too much shatters it: you're locked out for 4 seconds and lose 30% of the tank. Normal use never triggers it.
- **Cutscenes:** time resumes on its own during boss cutscenes, with no penalty.
- **Multiplayer:** one shared time stop for the whole world. Strain is per player.
- **Wrath of the Gods:** there is one being that time does not apply to.

Visuals:
- Glass pane over the screen while time is stopped, and a shattering glass break when it resumes
- Medallion timer at the top of the screen
- Scenery (skies, grass, tile animation, waterfalls, wind) pauses with time
- The relic's sand shows your time tank, and lightning crackles off the blade
- Stab it into the air to stop time; the blade tip leaves a light trail

## How to install

1. Download `TimeStop.tmod` from the [Releases](../../releases) page.
2. Put it in your tModLoader `Mods` folder:
   - Windows: `Documents\My Games\Terraria\tModLoader\Mods`
   - Linux: `~/.local/share/Terraria/tModLoader/Mods`
   - macOS: `~/Library/Application Support/Terraria/tModLoader/Mods`
3. Start tModLoader, open **Workshop > Manage Mods**, enable **Time Stop** and reload.

Works mid-playthrough on existing worlds and characters.

### Getting the item

- **With Calamity:** crafted after the Devourer of Gods at a Cosmic Anvil from a Gold or Platinum Watch, 12 Cosmilite Bars, 10 Darksun Fragments, 10 Nightmare Fuel, 10 Endothermic Energy, 4 Ascendant Spirit Essence and 3 Twisting Nether.
- **Without Calamity:** crafted after Moon Lord at the Ancient Manipulator from a Gold or Platinum Watch, 10 Luminite Bars and 5 of each Lunar Fragment.

Use the item, or press the **Toggle time stop** keybind (set it in Settings > Controls) with Orion's Half in your inventory.

## Config

**Time Stop** (server side, applies to everyone):

| Option | Default | What it does |
|---|---|---|
| Time tank outside boss fights | 5 s | How many seconds a full tank holds |
| Time tank during boss fights | 3 s | Tank size while any boss is alive |
| Refill speed | 0.8 | Refill speed compared to drain speed |
| Cooldown between stops | 0.5 s | Fixed wait after every stop |
| Stored damage | On | Hits on frozen enemies land when time resumes |
| Damage to frozen bosses | 0.5 | Damage multiplier on frozen bosses |
| Strain | On | Spamming cracks and shatters the screen |
| Freeze bosses / town NPCs / dropped items / liquids | On | Turn off to let any of these keep moving |
| Allow test commands | On | Enables the `/ts...` test commands |
| NPCs that never freeze | Empty | Add any NPC that has problems being frozen |

**Time Stop (visuals)** (client side, only affects you):

| Option | Default | What it does |
|---|---|---|
| Glass pane and shatter effects | On | The glass overlay and the break when time resumes |
| Show Orion medallion | On | The timer at the top of the screen |
| Pause scenery | On | Pause skies, grass, tile animation and so on |
| Cracks show the interface | On | Strain cracks include the hotbar and map |
| Reduce flashing | Off | Softens flashes, replaces inversions with a dim, less shake |
| Time stop sound volume | 100% | The start sound |
| Glass sound volume | 100% | The glass break sound |

## Credits

Made by PettingDucks.

Star positions for the medallion come from [d3-celestial](https://github.com/ofrohn/d3-celestial) (BSD license, see `Licenses/`).

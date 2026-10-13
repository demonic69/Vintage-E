# Changelog

All notable changes to Vintage-E. Each GitHub release uses its section from this file as its release notes.

## 1.5.0 — 2026-10-14

### Changed — closer to ProjectE
- **Matter furnaces now work like the game's firepit**: same window, fuel slot, cooking pots, crucibles and spits, lit with a firestarter or torch. The Dark Matter Furnace burns fuel 1.4x hotter, makes it last 3x as long and cooks 4x faster; the Red Matter Furnace 2x hotter, 4x as long, 12x faster. Collectors and relays next to one can keep it burning on EMC (2 or 4 EMC a second). Furnaces placed in 1.4.0 turn into the new ones by themselves; anything that was in them drops on top.
- **Hyperkinetic and Catalytic Lenses** shoot an explosive charge that flies straight and blows up where it hits, like ProjectE's. Pick the charge with the tool-mode key: radius 4, 6, 8 or 10 for 384, 768, 1,536 or 2,304 EMC. The Hyperkinetic Lens shoots on right-click; both shoot with the new **fire-projectile key (R)**, which you can rebind in the controls.
- The **Catalytic Lens** and **Destruction Catalyst** dig ProjectE's 3x3 tunnels: 1, 4, 8 or 16 deep for the catalyst, 1 and then 16 to 64 for the lens, 8 EMC a block. What they break drops where you clicked.
- **Klein Stars** use ProjectE's own artwork.
- **Swiftwolf's Rending Gale lets you fly** (jump to rise, sneak to sink) and has ProjectE's shield, which pushes hostile creatures and arrows away within 5 blocks. Pick flight + shield, flight or shield with the tool-mode key. Flying and the shield each cost 6.4 EMC a second, the ring switches off when you run out, and falling doesn't hurt while it's on or for 15 seconds after you stop flying. The 40% run speed bonus is gone.
- **Ring of Arcana**: switch each of its four effects on or off on its own with the tool-mode key, instead of only one at a time. It's free, like ProjectE's, and fire can't hurt you while you carry it.
- **Ignition Ring** and **Volcanite Amulet**: fire can't hurt you while you carry them, on or off.
- **Harvest Goddess Band** also harvests ripe crops, still grows crops slowly (for free) when switched off, and right-clicking farmland plants the seeds you carry up to 8 blocks around.
- **Black Hole Band**: right-click a water or lava source to remove it; pulls items within 7 blocks.
- **Repair Talisman** works whenever you carry it.
- **Archangel's Smite** fires homing arrows (3 damage each, costing what an arrow is worth): one on right-click, a volley of 10 on left-click.
- **Evertide and Volcanite Amulets** shoot water and lava with the fire-projectile key. The Ignition Ring (and the Ring of Arcana) shoot a fireball.
- **Pedestal**: an Evertide Amulet on it makes it rain, a Volcanite Amulet clears the sky; the Zero Ring also puts out burning creatures, the Harvest Goddess Band also harvests, the Watch also slows hostile creatures, and the Black Hole Band fills containers on any side. It now takes every ring, stone, amulet, talisman and watch.
- The **Watch of Flowing Time** now actually changes the flow of time. While it's on, time runs 10x faster for the whole world (crops, cooking, curing, drying, day and night), as when everyone sleeps, and crops near you grow faster still. The tool-mode key switches it to slow time: days last 3x longer and hostile creatures near you are slowed. It costs EMC every second it's on and stops when you run out. Before, it only grew nearby crops.
- **Interdiction Torch** is brighter: light level 20 (a torch is 14).
- Every block and item description now says what it does with the real numbers: rates, capacities, costs, ranges, temperatures.
- Emeralds are worth a quarter of a diamond (2,048 EMC).

### Added
- **Void Ring** (Black Hole Band + Gem of Eternal Density + 2 red matter): pulls in items, condenses junk, and sneak + right-click teleports you up to 64 blocks.
- **`/emc add <amount> [player]`** and **`/emc remove <amount> [player]`**: change a player's personal EMC (yours if no name is given). `/emc give` still works.
- **`/emc blacklist`**: the same window as `/emc missing`, listing everything on the blacklist. Select something to give it a value or take it off the blacklist. The `/emc missing` window gets a **Blacklist it** button. Changes are saved to the server config (`Blacklist`, and the new `Allow` list for exceptions to the built-in blacklist).
- Settings: `WatchChangesWorldTime` (turn off to keep the watch's effect local, e.g. on busy servers), `WatchSpeedUp` (default 10) and `WatchSlowDown` (default 3).

### Fixed
- **Rendered fat** and anything else cooked into an item in a cooking pot (linseed oil...) now gets its value from the pot recipe. Before it could be valued backwards from something else it's used in (one world had rendered fat at 2,494 EMC).
- The **altar** had no value and didn't show up in `/emc missing`, because it was blacklisted with the story blocks. It now has 64 EMC.

### Credits
- The Klein Star textures are ProjectE's (MIT licence); see THIRD-PARTY-NOTICES.md.

## 1.4.0 — 2026-10-13

### Added — the rest of ProjectE
- **Klein Stars** Ein, Zwei, Drei, Vier, Sphere and Omega (50,000 to 51,200,000 EMC): portable EMC. Right-click to empty one into your EMC, sneak + right-click to fill it. Rings and gadgets draw from stars you carry once your own EMC runs out, combining four stars into the next size keeps their EMC, and sneak + right-clicking a collector or relay with a star charges it.
- **Covalence Dust** (low, medium, high) and the **Iron Band**, used in the recipes below.
- **Rings, stones and amulets**: they work from anywhere in your hotbar or backpack (Vintage Story has no ring slots); right-click switches one on or off.
  - Black Hole Band: pulls loose items to you.
  - Swiftwolf's Rending Gale: run 40% faster, no fall damage.
  - Harvest Goddess Band: crops around you grow faster.
  - Ignition Ring: sets nearby hostile creatures on fire; fire can't hurt you.
  - Zero Ring: freezes water around you.
  - Ring of Arcana: all four of the above, picked with the tool-mode key.
  - Body Stone, Soul Stone and Life Stone: keep you fed and/or heal you using EMC.
  - Repair Talisman: slowly repairs everything you carry and wear.
  - Gem of Eternal Density: turns cheap junk you carry into iron, gold, diamond, dark or red matter.
  - Evertide Amulet and Volcanite Amulet: place water or lava.
  - Watch of Flowing Time: crops in a wide area grow much faster.
- **Gadgets**: Destruction Catalyst (3×3 tunnels), Hyperkinetic Lens and Catalytic Lens (explosions at range), Mercurial Eye (turn blocks into a chosen block), Archangel's Smite (strike a creature up to 48 blocks away), Tome of Knowledge (learn everything), Divining Rods (EMC in the ground below you).
- **Dark Matter Pedestal**: set a ring, stone, amulet or talisman on it and it works on the area around the pedestal for free: grows crops, burns or strikes hostile creatures, freezes water, pulls loose items into a container under it, heals and feeds nearby players, or repairs their gear.
- **Armour**: Dark Matter, Red Matter and Gem helmets, chestplates and leggings, on the game's plate armour models. They never wear out; a full gem set makes you immune to fall and fire damage.
- **Tools**: Dark and Red Matter Shears, the Red Matter Katar (sword, axe and shears) and the Red Matter Morning Star (pickaxe, shovel and sledgehammer).
- **Blocks**:
  - Alchemical Chest (104 slots) and Alchemical Bags (12 colours, 32-slot backpack bags).
  - Alchemical Coal, Mobius Fuel and Aeternalis Fuel blocks.
  - Dark and Red Matter Furnaces: smelt, fire and cook anything (ore straight to ingots), on fuel or on EMC from collectors.
  - Anti-Matter Relays MK1–MK3: store and boost collector EMC and pass it on.
  - Nova Catalyst and Nova Cataclysm explosives.
  - Interdiction Torch: keeps hostile creatures away.
- `/emc help` lists every command with a short description.
- In the `/emc missing` window, admins can click a name, type a value and press **Set value**. The value is saved like `/emc set` and the list refreshes.
- Settings: `HostileCreatures` (what the ignition ring, smite and interdiction torch affect) and `TrinketCostMultiplier`.

### Changed
- Items caught in a recipe loop that never settles now keep the first value they got instead of losing it.

### Not included
- The Mind Stone: Vintage Story has no experience points.
- Separate boots: Vintage Story armour has head, body and legs slots, so the leggings cover the feet.
- ProjectE's furnaces are adapted to how smelting works in Vintage Story, which has no furnaces.

## 1.3.1 — 2026-10-13

### Fixed
- `/emc missing` no longer crashes the game with a null pointer exception. The list now shows item icons inline (the same way the handbook does) and pages through long lists 40 entries at a time.
- The scrollbar in that window was drawn in the wrong place.

## 1.3.0 — 2026-10-13

### Added
- **Chat summary**: when you join (and after `/emc recalc`, `set` or `clear`) chat shows e.g. `Vintage-E version 1.3.0: calculated 13983 EMC values, 0 missing.` Turn it off with `AnnounceInChat`.
- **`/emc missing` window**: a scrollable list of everything without an EMC value, with item icons, a search bar, a mod filter, a blocks/items filter and a button that copies the codes ready to paste into `BaseValues`. Clicking an icon opens its handbook page.
- **3D matter tools**: the dark and red matter pickaxe, shovel, axe, sword and hoe now use the game's own 3D tool models with matter textures, so they're held, swung and displayed like vanilla metal tools (pickaxe swing, axe chop, shovel dig, falx sword swing) and fit on tool racks and the ground.
- **Sledgehammers**: the dark and red matter hammers are now sledgehammers with their own model. They smash stone, ore and other hard blocks 3×3 from the start (the red one can switch to 5×5 with the tool-mode key; sneak for a single block).
- **Trader prices**: things no recipe makes (clothes, loot armour, decorations…) are valued from what traders buy and sell them for. What a rusty gear is worth is measured each start from traded items that already have a value (about 57 EMC in vanilla), and the rusty gear itself is set to that.
- **Working backwards from crafting**: if a recipe's result has a value and only one ingredient doesn't (loot parts, gold jewellery…), that ingredient gets what's left over.
- **Value file sections**: `defaultValues` (only used when nothing else can value an item), `litreValues` (liquids per litre), `traderCurrency` and `allow` (exceptions to the blacklist).
- `/emc dump` (and the new `DumpValuesOnStart` setting) writes what every value was worked out from, e.g. `grid: 1 game:log-placed-oak-ud -> 12 game:plank-oak`.
- Soil and farmland are worth more the more fertile they are (very low 1 → terra preta 16).
- Butterflies (caught and dead) have values.
- Tooltips show liquids per litre, and buckets, bags and pots show what their contents are worth.
- New settings: `AnnounceInChat`, `InferValuesFromCrafting`, `UseTraderPrices`, `DumpValuesOnStart`.
- A "Test on a real server" GitHub workflow that compiles the mod against the real game and checks every value on a fresh 1.22.7 world.

### Changed
- Water is set per litre: 1 L of water = 1 EMC (ale and other drinks follow from their recipes: spelt ale is 45 EMC per litre).
- Coverage on vanilla 1.22.7 is now **100%** of the 8,096 blocks and items in the creative inventory (was about 80%).

### Fixed
- The mod's creative inventory tab had no name.
- Tin bronze, bismuth bronze and black bronze, and everything smithed from them, were worth almost nothing. Smithing now costs the whole ingots you have to put on the anvil, and value loops that creep down are detected and stopped.
- Rusty gears were worth far more than they buy, so selling things for gears and burning the gears made EMC from nothing.
- A few items (bear hide helmets) could lose their value on the second pass of the calculation.

## 1.2.0 — 2026-10-13

### Added
- **Drying time bonus**: drying adds 64 EMC per in-game day an item takes to dry. Other transitions (curing, ripening…) can be given a bonus too with `TransitionEmcPerDay`.
- **Casting**: items cast in tool molds cost the metal poured into them (`requiredUnits`, 100 units = 1 ingot); molds that break when filled add their own cost.
- **Processes with several outputs** share their value fairly: squeezing honeycomb (honey + beeswax), ground-storage processing such as sawing (including anything left behind) and the fruit press (juice + pressed mash).
- `relativeValues` rules in value files. Metal bits are worth 1/20 of the matching ingot, even for metals that can't be smelted.

### Changed
- Flax fibers are worth 4× as much (8 → 32).
- Beeswax and honey now get their values from squeezing honeycomb instead of fixed guesses.
- Smelting data is also read from items that work it out in code.

## 1.1.1 — 2026-10-13

### Fixed
- Barrel liquids (brine, diluted solutions, tannin) and everything made from them, such as leather, were hugely overvalued. Barrel outputs now count the portions the game actually gives.
- Crafting recipes that use a bucket or bowl of liquid now cost the liquid, not the container you get back.

### Changed
- Raw hides follow their leather yield: small 16, medium 32, large 48, huge 80.

## 1.1.0 — 2026-10-13

### Added
- **EMC cache**: values are saved per world and reused on the next start when nothing that affects them has changed (game version, mods, config, value files, recipes, item data). `/emc recalc` always recalculates; `CacheEmcValues: false` turns the cache off.

## 1.0.0 — 2026-10-13

First version (not published as a GitHub release).

- EMC values worked out automatically at server start from every loaded recipe and process, including other mods'.
- Philosopher's Stone, Transmutation Table and Tablet, alchemical fuels, dark and red matter (and blocks), unbreakable matter tools with area mining, Energy Collectors MK1–3 and Energy Condensers MK1–2.
- Base values, blacklist and philosopher's stone rules in data files any mod can extend; server config and `/emc` commands for admins.

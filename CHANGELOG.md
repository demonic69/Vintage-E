# Changelog

All notable changes to Vintage-E. Each GitHub release uses its section from this file as its release notes.

## 1.4.1 — 2026-10-13

### Fixed
- The **Watch of Flowing Time** now actually changes the flow of time, like ProjectE's. While it's on, time runs 10× faster for the whole world (crops, cooking, curing, drying, day and night), the way it does when everyone sleeps, and crops right around you get an extra boost. Switch it to **slow down time** with the tool-mode key: days last 3× longer and hostile creatures near you are slowed. It costs EMC every second it's on and stops when you run out. Before, it only grew nearby crops.

### Added
- Settings: `WatchChangesWorldTime` (turn off to keep the watch's effect local, e.g. on busy servers), `WatchSpeedUp` (default 10) and `WatchSlowDown` (default 3).

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
- **Armour**: Dark Matter, Red Matter and Gem helmets, chestplates and leggings, on the game's plate armour models. They never wear out; a full gem set makes you immune to fall and fire damage.
- **Tools**: Dark and Red Matter Shears, the Red Matter Katar (sword, axe and shears) and the Red Matter Morning Star (pickaxe, shovel and sledgehammer).
- **Blocks**:
  - Alchemical Chest (104 slots) and Alchemical Bags (12 colours, 32-slot backpack bags).
  - Alchemical Coal, Mobius Fuel and Aeternalis Fuel blocks.
  - Dark and Red Matter Furnaces: smelt, fire and cook anything (ore straight to ingots), on fuel or on EMC from collectors.
  - Anti-Matter Relays MK1–MK3: store and boost collector EMC and pass it on.
  - Dark Matter Pedestal: runs a ring's effect on the area around it.
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

# Changelog

All notable changes to Vintage-E. Each GitHub release uses its section from this file as its release notes.

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

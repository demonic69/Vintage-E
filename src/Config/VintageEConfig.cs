using System.Collections.Generic;

namespace VintageE
{
    /// <summary>
    /// Server settings, stored in ModConfig/vintagee.json. Created with defaults on first launch.
    /// Base EMC values live in assets (config/vintagee-emc.json) so other mods can ship or patch them;
    /// the dictionaries here are for the server owner and always win over asset values.
    /// </summary>
    public class VintageEConfig
    {
        /// <summary>Extra base values. Keys are codes or wildcards ("game:rock-*", "block:game:log-*", "item:*:gear-*"). Applied after all asset files, so they override them.</summary>
        public Dictionary<string, double> BaseValues = new Dictionary<string, double>();

        /// <summary>Hard overrides set by hand or with /emc set. Applied last; recipes never change them. A value of 0 removes the item's EMC entirely.</summary>
        public Dictionary<string, double> FixedValues = new Dictionary<string, double>();

        /// <summary>Codes/wildcards that never get an EMC value, on top of the asset blacklist.</summary>
        public List<string> Blacklist = new List<string>();

        /// <summary>Ignore the default value file shipped with this mod (assets/vintagee/config/vintagee-emc.json). Other mods' files still load.</summary>
        public bool DisableBuiltInBaseValues = false;

        /// <summary>When an ore, raw food etc. has no value but what it smelts/grinds/crushes/dries into does, work the input value out backwards.</summary>
        public bool InferValuesFromProcessing = true;

        /// <summary>
        /// Also work values out backwards from crafting: if a recipe's result has a value and only one of its ingredients
        /// doesn't, that ingredient is worth what's left over (e.g. a loot-only part used in a recipe). Uses the highest
        /// such value so nothing already valued gets cheaper. Needs InferValuesFromProcessing.
        /// </summary>
        public bool InferValuesFromCrafting = true;

        /// <summary>As a last resort, give anything still unvalued the value of its closest variant (e.g. the same ore in a different rock). Uses the highest sibling value, so it can't be exploited.</summary>
        public bool InferFromVariantSiblings = true;

        /// <summary>
        /// Value things nothing else can value (clothes, loot armour, decorations...) from what traders buy and sell them for.
        /// The worth of one rusty gear is measured on traded items that already have a value, so prices stay in line with everything else.
        /// </summary>
        public bool UseTraderPrices = true;

        /// <summary>
        /// Flat EMC value given to any creative-inventory block/item that still has no value after everything else (0 = off).
        /// Careful: anything valued this way can be created for that price, so a low number makes rare loot cheap.
        /// Check the server log or /emc missing first and prefer giving those items real values in BaseValues.
        /// </summary>
        public double FallbackValue = 0;

        /// <summary>
        /// Save mapped values per world (ModData/vintagee/cache) and reuse them on the next start when the game version,
        /// mods, config, value files and all recipe/item data are unchanged. /emc recalc always remaps.
        /// </summary>
        public bool CacheEmcValues = true;

        /// <summary>
        /// Extra EMC added per in-game day an item takes to transform on its own, by transition type
        /// (dry, cure, ripen, harden, melt, convert). The time counted is fresh hours + transition hours.
        /// Example: tall grass that takes 1 day to become dry grass makes the dry grass worth 64 more. 0 or missing = no bonus.
        /// </summary>
        public Dictionary<string, double> TransitionEmcPerDay = new Dictionary<string, double> { ["dry"] = 64 };

        /// <summary>Upper bound on relaxation passes over the recipe graph. Values still falling after this are recipe loops and get dropped.</summary>
        public int MaxMappingPasses = 120;

        /// <summary>Scale a damaged tool's EMC by its remaining durability.</summary>
        public bool DurabilityScalesEmc = true;

        /// <summary>Write every mapped value to the log at startup (verbose).</summary>
        public bool LogAllValues = false;

        /// <summary>
        /// Write ModData/vintagee/emc-values.csv (every value and what it was worked out from) after each mapping,
        /// like /emc dump does. Handy for modpack authors checking values.
        /// </summary>
        public bool DumpValuesOnStart = false;

        /// <summary>How close (in blocks) a player must stay to the transmutation table they opened.</summary>
        public double TableUseRange = 8;

        /// <summary>Multiplies every collector's output and every condenser's speed. Per-tier base rates live in the block JSON attributes.</summary>
        public double MachineSpeedMultiplier = 1.0;

        /// <summary>Fraction of output a collector keeps at night or underground (0 = nothing without sun).</summary>
        public double CollectorDarkFactor = 0.0;

        /// <summary>Allow sneak + right-click with an empty hand on a collector to move its stored EMC into your personal EMC.</summary>
        public bool CollectorsChargePlayers = true;

        /// <summary>Refuse to burn bags, pots and other containers that still hold something.</summary>
        public bool ProtectContainersWithContents = true;

        /// <summary>
        /// Post a chat line with the mod version and how many EMC values were calculated / are missing,
        /// to each player when they join and to everyone after /emc recalc, set or clear.
        /// </summary>
        public bool AnnounceInChat = true;

        /// <summary>
        /// Creatures the Ignition Ring burns, the Interdiction Torch repels and the Archangel's Smite targets
        /// (entity codes, wildcards allowed). Livestock and players are never affected.
        /// </summary>
        public List<string> HostileCreatures = new List<string>
        {
            "game:drifter-*", "game:locust-*", "game:bell-*", "game:shiver-*", "game:bowtorn-*", "game:wolf-*", "game:bear-*",
            "game:hyena-*", "game:eidolon-*", "game:erel-*"
        };

        /// <summary>Multiplies the EMC that rings, stones, amulets and gadgets cost to run.</summary>
        public double TrinketCostMultiplier = 1.0;

        public void Normalize()
        {
            BaseValues ??= new Dictionary<string, double>();
            FixedValues ??= new Dictionary<string, double>();
            Blacklist ??= new List<string>();
            TransitionEmcPerDay ??= new Dictionary<string, double>();
            HostileCreatures ??= new List<string>();
            if (TrinketCostMultiplier < 0) TrinketCostMultiplier = 0;
            if (MachineSpeedMultiplier <= 0) MachineSpeedMultiplier = 1.0;
            if (MaxMappingPasses < 4) MaxMappingPasses = 4;
            if (TableUseRange < 2) TableUseRange = 2;
        }
    }
}

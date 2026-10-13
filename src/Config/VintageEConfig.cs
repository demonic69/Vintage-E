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

        /// <summary>As a last resort, give anything still unvalued the value of its closest variant (e.g. the same ore in a different rock). Uses the highest sibling value, so it can't be exploited.</summary>
        public bool InferFromVariantSiblings = true;

        /// <summary>
        /// Flat EMC value given to any creative-inventory block/item that still has no value after everything else (0 = off).
        /// Careful: anything valued this way can be created for that price, so a low number makes rare loot cheap.
        /// Check the server log or /emc missing first and prefer giving those items real values in BaseValues.
        /// </summary>
        public double FallbackValue = 0;

        /// <summary>Upper bound on relaxation passes over the recipe graph. Values still falling after this are recipe loops and get dropped.</summary>
        public int MaxMappingPasses = 120;

        /// <summary>Scale a damaged tool's EMC by its remaining durability.</summary>
        public bool DurabilityScalesEmc = true;

        /// <summary>Write every mapped value to the log at startup (verbose).</summary>
        public bool LogAllValues = false;

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

        public void Normalize()
        {
            BaseValues ??= new Dictionary<string, double>();
            FixedValues ??= new Dictionary<string, double>();
            Blacklist ??= new List<string>();
            if (MachineSpeedMultiplier <= 0) MachineSpeedMultiplier = 1.0;
            if (MaxMappingPasses < 4) MaxMappingPasses = 4;
            if (TableUseRange < 2) TableUseRange = 2;
        }
    }
}

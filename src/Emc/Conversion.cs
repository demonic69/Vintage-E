using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VintageE
{
    public enum ConversionKind
    {
        /// <summary>Player-made recipe (grid, smithing, knapping, clay forming, barrel, alloy...).</summary>
        Crafting,
        /// <summary>One item turning into another through heat, grinding, crushing, drying, curing, ripening...</summary>
        Processing
    }

    /// <summary>One way of getting an item, e.g. "4 copper nuggets + 1 tin nugget -> 1 bronze".</summary>
    public sealed class Conversion
    {
        public CollectibleObject Output;
        public double OutputQuantity;
        public List<ConversionInput> Inputs = new List<ConversionInput>();
        /// <summary>Things handed back after crafting (e.g. an empty bucket); their value is subtracted.</summary>
        public List<KeyValuePair<CollectibleObject, double>> Returned;
        /// <summary>
        /// Set for one-input processes with several outputs (squeezing honeycomb → honey + beeswax, ground-storage
        /// processing, the fruit press). The single input's value is shared out among these instead of Output.
        /// </summary>
        public List<KeyValuePair<CollectibleObject, double>> JointOutputs;

        /// <summary>Flat EMC added to every run on top of the inputs (e.g. the drying-time bonus).</summary>
        public double ExtraCost;
        public ConversionKind Kind;
        public string Source;

        public override string ToString() => $"{Source}: {string.Join(" + ", Inputs)} -> {OutputQuantity}x {Output.Code}";
    }

    /// <summary>One ingredient slot. When it accepts several items (wildcards, tags) the cheapest valued one is used.</summary>
    public sealed class ConversionInput
    {
        public CollectibleObject[] Choices;
        public double Quantity;

        public override string ToString() => Choices.Length == 1 ? $"{Quantity}x {Choices[0].Code}" : $"{Quantity}x [{Choices.Length} options]";
    }
}

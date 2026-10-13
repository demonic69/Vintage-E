using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Client-only behaviour attached to every block and item at load: adds "EMC: x" to the tooltip.
    /// Liquids are shown per litre (the game stores them as 100 small portions per litre, which made a litre of
    /// water look like 0.01), and containers show what their contents are worth.
    /// </summary>
    public class CollectibleBehaviorEmcTooltip : CollectibleBehavior
    {
        readonly VintageEModSystem mod;

        public CollectibleBehaviorEmcTooltip(CollectibleObject collObj, VintageEModSystem mod) : base(collObj)
        {
            this.mod = mod;
        }

        public override bool ClientSideOptional => true;

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            ItemStack stack = inSlot?.Itemstack;
            if (stack == null) return;
            double value = mod.Emc.GetValue(stack);
            if (value > 0)
            {
                double perLitre = EmcRegistry.ItemsPerLitre(stack.Collectible);
                if (perLitre > 1)
                {
                    dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-litre", EmcFormat.Format(value * perLitre)));
                    if (stack.StackSize > 1)
                    {
                        dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-litres", EmcFormat.Format(stack.StackSize / perLitre), EmcFormat.Format(value * stack.StackSize)));
                    }
                }
                else
                {
                    dsc.AppendLine(Lang.Get("vintagee:tooltip-emc", EmcFormat.Format(value)));
                    if (stack.StackSize > 1)
                    {
                        dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-stack", EmcFormat.Format(value * stack.StackSize)));
                    }
                }
            }

            double contents = 0;
            foreach (ItemStack inner in StackContents.NestedStacks(stack))
            {
                if (inner.Collectible == null && !inner.ResolveBlockOrItem(world)) continue;
                contents += mod.Emc.GetValue(inner) * inner.StackSize;
            }
            if (contents > 0) dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-contents", EmcFormat.Format(contents)));
        }
    }
}

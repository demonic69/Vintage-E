using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>Client-only behaviour attached to every block and item at load: adds "EMC: x" to the tooltip.</summary>
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
            if (value <= 0) return;

            dsc.AppendLine(Lang.Get("vintagee:tooltip-emc", EmcFormat.Format(value)));
            if (stack.StackSize > 1)
            {
                dsc.AppendLine(Lang.Get("vintagee:tooltip-emc-stack", EmcFormat.Format(value * stack.StackSize)));
            }
        }
    }
}

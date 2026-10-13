using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>What the GUI hands to its packet callback when a display slot is clicked.</summary>
    public sealed class DisplaySlotClick
    {
        public DisplayInventory Inventory;
        public int SlotId;
        public EnumMouseButton Button;
        public bool Shift;
    }

    /// <summary>
    /// Client-only inventory that just shows stacks. Clicks never move anything locally; they are turned
    /// into <see cref="DisplaySlotClick"/> objects so the dialog can ask the server to do the real work.
    /// </summary>
    public class DisplayInventory : InventoryBase
    {
        readonly ItemSlot[] slots;

        public DisplayInventory(ICoreAPI api, string id, int count) : base("vintageedisplay", id, api)
        {
            slots = Enumerable.Range(0, count).Select(_ => (ItemSlot)new DisplaySlot(this)).ToArray();
        }

        public override int Count => slots.Length;

        public override ItemSlot this[int slotId]
        {
            get => slotId < 0 || slotId >= slots.Length ? null : slots[slotId];
            set { if (slotId >= 0 && slotId < slots.Length) slots[slotId] = value; }
        }

        public override object ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            return new DisplaySlotClick { Inventory = this, SlotId = slotId, Button = op.MouseButton, Shift = op.ShiftDown };
        }

        public override void FromTreeAttributes(ITreeAttribute tree) { }
        public override void ToTreeAttributes(ITreeAttribute tree) { }
    }

    public class DisplaySlot : ItemSlot
    {
        public DisplaySlot(InventoryBase inventory) : base(inventory) { }

        public override bool CanHold(ItemSlot sourceSlot) => false;
        public override bool CanTake() => false;
        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => false;
    }
}

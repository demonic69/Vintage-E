using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>
    /// Finds the block or item under the mouse, for the "Set EMC value" hotkey. In this order: an inventory slot
    /// (any window), an item picture in the handbook or any other rich text whose tooltip is showing, the handbook
    /// page's own item; with no window open, the block you're looking at, then the held item.
    /// </summary>
    public static class HoveredStack
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly FieldInfo InteractiveElements = typeof(GuiComposer).GetField("interactiveElements", Any);
        static readonly FieldInfo LastHover = typeof(ItemstackComponentBase).GetField("lastHoverslotInfoTextUpdateTotalMs", Any);
        static readonly FieldInfo TooltipSlot = typeof(ItemstackComponentBase).GetField("renderedTooltipSlot", Any);

        public static ItemStack Find(ICoreClientAPI capi)
        {
            IClientPlayer player = capi.World.Player;
            ItemStack stack = player?.InventoryManager?.CurrentHoveredSlot?.Itemstack;
            if (stack != null) return stack;

            bool windowOpen = false;
            var dialogs = new List<GuiDialog>(capi.Gui.OpenedGuis);
            foreach (GuiDialog dialog in dialogs)
            {
                if (dialog == null || !dialog.IsOpened() || dialog.DialogType == EnumDialogType.HUD) continue;
                windowOpen = true;
                stack = FromRichText(capi, dialog);
                if (stack != null) return stack;
            }
            foreach (GuiDialog dialog in dialogs)
            {
                if (dialog == null || !dialog.IsOpened()) continue;
                stack = FromHandbookPage(dialog);
                if (stack != null) return stack;
            }
            if (windowOpen) return null;

            BlockSelection sel = player?.CurrentBlockSelection;
            if (sel?.Position != null)
            {
                Block block = capi.World.BlockAccessor.GetBlock(sel.Position);
                stack = block?.Id > 0 ? block.OnPickBlock(capi.World, sel.Position) : null;
                if (stack != null) return stack;
            }
            return player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        }

        /// <summary>An item picture in a window's rich text (handbook pages) whose tooltip was drawn in the last few frames.</summary>
        static ItemStack FromRichText(ICoreClientAPI capi, GuiDialog dialog)
        {
            if (InteractiveElements == null || LastHover == null || TooltipSlot == null || dialog.Composers == null) return null;
            long now = capi.ElapsedMilliseconds;
            foreach (GuiComposer composer in dialog.Composers.Values)
            {
                if (composer == null || !(InteractiveElements.GetValue(composer) is IDictionary elements)) continue;
                foreach (object element in elements.Values)
                {
                    if (!(element is GuiElementRichtext rich) || rich.Components == null) continue;
                    foreach (RichTextComponentBase comp in rich.Components)
                    {
                        if (!(comp is ItemstackComponentBase isc)) continue;
                        if (!(LastHover.GetValue(isc) is long shown) || now - shown > 250) continue;
                        if (TooltipSlot.GetValue(isc) is ItemSlot slot && slot.Itemstack != null) return slot.Itemstack;
                    }
                }
            }
            return null;
        }

        /// <summary>The item a handbook window's open page is about (read from its browse history; null on the overview).</summary>
        static ItemStack FromHandbookPage(GuiDialog dialog)
        {
            FieldInfo history = null;
            for (System.Type t = dialog.GetType(); t != null && history == null; t = t.BaseType)
                history = t.GetField("browseHistory", Any | BindingFlags.DeclaredOnly);
            if (!(history?.GetValue(dialog) is IEnumerable entries)) return null;
            foreach (object top in entries) // a Stack enumerates from the top: the page on screen
            {
                object page = top?.GetType().GetField("Page", Any)?.GetValue(top);
                return page?.GetType().GetField("Stack", Any)?.GetValue(page) as ItemStack;
            }
            return null;
        }
    }
}

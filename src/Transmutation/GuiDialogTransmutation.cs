using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>
    /// The transmutation GUI shared by the table and the tablet.
    /// Top: personal EMC and the burn slot. Bottom: a searchable, paged grid of everything you have learned,
    /// most valuable first. All actions are requests to the server; nothing changes locally.
    /// </summary>
    public class GuiDialogTransmutation : GuiDialog
    {
        const int Cols = 9;
        const int Rows = 6;
        const int PerPage = Cols * Rows;

        readonly VintageEModSystem mod;
        readonly BlockPos tablePos;
        readonly DisplayInventory burnInv;
        readonly DisplayInventory knownInv;

        List<KeyValuePair<CollectibleObject, double>> entries = new List<KeyValuePair<CollectibleObject, double>>();
        string search = "";
        int page;
        double shownEmc = -1;
        int shownKnownCount = -1;
        long tickListener;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogTransmutation(ICoreClientAPI capi, VintageEModSystem mod, BlockPos tablePos) : base(capi)
        {
            this.mod = mod;
            this.tablePos = tablePos?.Copy();
            burnInv = new DisplayInventory(capi, "burn-" + capi.World.Player.PlayerUID, 1);
            knownInv = new DisplayInventory(capi, "known-" + capi.World.Player.PlayerUID, PerPage);
            Compose();
            mod.OnEmcTableUpdated += OnTableUpdated;
        }

        void Compose()
        {
            double slot = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
            double width = Cols * slot;
            double top = GuiStyle.TitleBarHeight;

            ElementBounds emcBounds = ElementBounds.Fixed(0, top + 4, width - 70, 28);
            ElementBounds burnHintBounds = ElementBounds.Fixed(0, top + 34, width - 70, 40);
            ElementBounds burnSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, width - slot, top + 8, 1, 1);
            ElementBounds burnLabelBounds = ElementBounds.Fixed(width - slot - 10, top + 8 + slot, slot + 20, 20);

            double row = top + 92;
            ElementBounds searchBounds = ElementBounds.Fixed(0, row, width - 160, 30);
            ElementBounds prevBounds = ElementBounds.Fixed(width - 150, row, 36, 30);
            ElementBounds pageBounds = ElementBounds.Fixed(width - 110, row + 5, 72, 24);
            ElementBounds nextBounds = ElementBounds.Fixed(width - 36, row, 36, 30);
            ElementBounds gridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, row + 40, Cols, Rows);
            ElementBounds helpBounds = ElementBounds.Fixed(0, row + 48 + Rows * slot, width, 36);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(emcBounds, burnHintBounds, burnSlotBounds, burnLabelBounds, searchBounds, prevBounds, pageBounds, nextBounds, gridBounds, helpBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            string title = Lang.Get(tablePos == null ? "vintagee:gui-tablet-title" : "vintagee:gui-table-title");

            SingleComposer = capi.Gui
                .CreateCompo("vintagee-transmutation", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddDynamicText("", CairoFont.WhiteSmallishText(), emcBounds, "emc")
                    .AddStaticText(Lang.Get("vintagee:gui-burn-hint"), CairoFont.WhiteDetailText(), burnHintBounds)
                    .AddItemSlotGrid(burnInv, OnSlotPacket, 1, burnSlotBounds, "burnslot")
                    .AddStaticText(Lang.Get("vintagee:gui-burn-label"), CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), burnLabelBounds)
                    .AddTextInput(searchBounds, OnSearchChanged, CairoFont.WhiteSmallText(), "search")
                    .AddSmallButton("<", OnPrevPage, prevBounds)
                    .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), pageBounds, "page")
                    .AddSmallButton(">", OnNextPage, nextBounds)
                    .AddItemSlotGrid(knownInv, OnSlotPacket, Cols, gridBounds, "knowngrid")
                    .AddStaticText(Lang.Get("vintagee:gui-create-hint"), CairoFont.WhiteDetailText(), helpBounds)
                .EndChildElements()
                .Compose();

            SingleComposer.GetTextInput("search").SetPlaceHolderText(Lang.Get("vintagee:gui-search"));
            Refresh(true);
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            tickListener = capi.Event.RegisterGameTickListener(OnTick, 250);
            Refresh(true);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            if (tickListener != 0) capi.Event.UnregisterGameTickListener(tickListener);
            tickListener = 0;
        }

        void OnTick(float dt)
        {
            IPlayer player = capi.World.Player;
            if (tablePos != null && player?.Entity != null && player.Entity.Pos.SquareDistanceTo(tablePos.X + 0.5, tablePos.Y + 0.5, tablePos.Z + 0.5) > 10 * 10)
            {
                TryClose(); // walked away from the table
                return;
            }
            double emc = PlayerEmc.GetEmc(player);
            int knownCount = PlayerEmc.GetKnown(player).Length;
            if (knownCount != shownKnownCount) Refresh(true);
            else if (Math.Abs(emc - shownEmc) > 1e-9) Refresh(false);
        }

        void OnTableUpdated() => capi.Event.EnqueueMainThreadTask(() => Refresh(true), "vintagee-refresh");

        public void OnServerResult(TransmuteResultPacket packet)
        {
            if (packet.LearnedKey != null)
            {
                CollectibleObject learned = PlayerEmc.Resolve(capi.World, packet.LearnedKey);
                if (learned != null) capi.ShowChatMessage(Lang.Get("vintagee:learned", learned.GetHeldItemName(new ItemStack(learned))));
            }
        }

        /// <param name="rebuildList">Re-read knowledge and re-filter; otherwise only the EMC text changes.</param>
        void Refresh(bool rebuildList)
        {
            if (SingleComposer == null) return;
            IPlayer player = capi.World.Player;
            double emc = PlayerEmc.GetEmc(player);
            string[] known = PlayerEmc.GetKnown(player);

            if (rebuildList)
            {
                string needle = search.Trim().ToLowerInvariant();
                entries = known
                    .Select(k => PlayerEmc.Resolve(capi.World, k))
                    .Where(c => c != null)
                    .Distinct()
                    .Select(c => new KeyValuePair<CollectibleObject, double>(c, mod.Emc.GetValue(c)))
                    .Where(kv => kv.Value > 0 && Matches(kv.Key, needle))
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key.Code.ToString(), StringComparer.Ordinal)
                    .ToList();
                shownKnownCount = known.Length;
            }

            int pages = Math.Max(1, (entries.Count + PerPage - 1) / PerPage);
            page = GameMath.Clamp(page, 0, pages - 1);
            for (int i = 0; i < PerPage; i++)
            {
                int idx = page * PerPage + i;
                knownInv[i].Itemstack = idx < entries.Count ? new ItemStack(entries[idx].Key, 1) : null;
            }

            shownEmc = emc;
            SingleComposer.GetDynamicText("emc").SetNewText(Lang.Get("vintagee:gui-emc", EmcFormat.Format(emc), known.Length));
            SingleComposer.GetDynamicText("page").SetNewText((page + 1) + " / " + pages);
        }

        bool Matches(CollectibleObject c, string needle)
        {
            if (needle.Length == 0) return true;
            if (c.Code.ToString().Contains(needle)) return true;
            try
            {
                return c.GetHeldItemName(new ItemStack(c)).ToLowerInvariant().Contains(needle);
            }
            catch
            {
                return false;
            }
        }

        void OnSearchChanged(string text)
        {
            search = text ?? "";
            page = 0;
            Refresh(true);
        }

        bool OnPrevPage()
        {
            page--;
            Refresh(false);
            return true;
        }

        bool OnNextPage()
        {
            page++;
            Refresh(false);
            return true;
        }

        void OnSlotPacket(object packet)
        {
            if (!(packet is DisplaySlotClick click) || click.Button == EnumMouseButton.Wheel) return;
            ItemSlot cursor = capi.World.Player.InventoryManager.MouseItemSlot;

            if (click.Inventory == burnInv)
            {
                if (cursor?.Itemstack == null) return;
                mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.Burn, Mode = click.Button == EnumMouseButton.Right ? 1 : 0 });
                return;
            }

            if (click.Inventory == knownInv)
            {
                ItemStack shown = knownInv[click.SlotId]?.Itemstack;
                if (shown == null) return;
                string key = PlayerEmc.KeyOf(shown.Collectible);
                int mode = click.Shift ? 2 : (click.Button == EnumMouseButton.Right ? 1 : 0);
                if (mode != 2 && cursor?.Itemstack != null && cursor.Itemstack.Collectible != shown.Collectible)
                {
                    // Holding something different: ignore, so dragging items across the grid never creates anything by accident.
                    return;
                }
                mod.SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.Create, Key = key, Mode = mode });
            }
        }

        public override void Dispose()
        {
            mod.OnEmcTableUpdated -= OnTableUpdated;
            if (tickListener != 0) capi.Event.UnregisterGameTickListener(tickListener);
            base.Dispose();
        }
    }
}

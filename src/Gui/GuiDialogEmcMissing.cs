using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Window opened by /emc missing (every block/item that still has no EMC value) and /emc blacklist (everything
    /// the blacklist keeps from having one): a scrollable list with icons, a search bar, a mod filter and a
    /// blocks/items filter. Each window can mix in the other list: "Show blacklisted" in the missing window,
    /// unticking "Hide missing" in the blacklist window; those entries are tagged and get their own button
    /// ("Remove from blacklist" / "Blacklist it"). Click an icon to open its handbook page; "Copy codes" puts the filtered list on the
    /// clipboard ready to paste into BaseValues.
    /// Admins click names to select them (click again to unselect; "Select shown" picks everything the filters show),
    /// then type a value and press "Set value" (saved like /emc set; it also takes blacklisted items off the
    /// blacklist), or press "Blacklist" / "Remove from blacklist". With a mod picked in the mod filter,
    /// "Blacklist whole mod" / "Unblacklist whole mod" does the same for everything from that mod. The list refreshes
    /// after each change and the selection is kept where it still applies.
    /// </summary>
    public class GuiDialogEmcMissing : GuiDialog
    {
        const double ListWidth = 560;
        const double ListHeight = 340;
        const int PerPage = 40; // keeps the rendered text small; page with the arrows for more

        sealed class Entry
        {
            public CollectibleObject Collectible;
            public string Key;
            public string Name;
            public string Code;
            public string Domain;
            public bool IsBlock;
            public string Why;
            /// <summary>From the other list: blacklisted (missing window) or missing (blacklist window).</summary>
            public bool Other;
        }

        /// <summary>"Show blacklisted" in the missing window; remembered while the game runs.</summary>
        static bool showBlacklisted;
        /// <summary>"Hide missing" in the blacklist window; ticked at first so the window opens with just the blacklist.</summary>
        static bool hideMissing = true;

        /// <summary>Whether the other list's entries are listed right now.</summary>
        bool ShowOther => IsBlacklist ? !hideMissing : showBlacklisted;

        readonly EmcMissingPacket packet;
        readonly Action<string[], double> onSetValue;
        readonly Action<string[], bool> onBlacklist;
        readonly Action<string, bool> onBlacklistMod;

        /// <summary>True for the /emc blacklist window.</summary>
        public bool IsBlacklist => packet.Blacklist;

        readonly HashSet<string> selected = new HashSet<string>();
        readonly List<Entry> all = new List<Entry>();
        List<Entry> shown = new List<Entry>();
        string search = "";
        string domainFilter = "*";
        string typeFilter = "*";
        int page;
        bool composing;
        ElementBounds clipBounds;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogEmcMissing(ICoreClientAPI capi, EmcMissingPacket packet, GuiDialogEmcMissing previous = null,
            Action<string[], double> onSetValue = null, Action<string[], bool> onBlacklist = null, Action<string, bool> onBlacklistMod = null) : base(capi)
        {
            this.packet = packet;
            this.onSetValue = onSetValue;
            this.onBlacklist = onBlacklist;
            this.onBlacklistMod = onBlacklistMod;
            string[] keysIn = packet.Keys ?? Array.Empty<string>();
            for (int ki = 0; ki < keysIn.Length; ki++)
            {
                string key = keysIn[ki];
                CollectibleObject c = PlayerEmc.Resolve(capi.World, key);
                if (c?.Code == null) continue;
                string name;
                try { name = c.GetHeldItemName(new ItemStack(c)); }
                catch { name = c.Code.ToShortString(); }
                all.Add(new Entry
                {
                    Collectible = c,
                    Key = key,
                    Name = name,
                    Code = c.Code.ToString(),
                    Domain = c.Code.Domain,
                    IsBlock = c.ItemClass == EnumItemClass.Block,
                    Why = packet.Why != null && ki < packet.Why.Length ? packet.Why[ki] : null
                });
            }
            string[] otherKeys = packet.OtherKeys ?? Array.Empty<string>();
            var seenKeys = new HashSet<string>(all.Select(e => e.Key));
            for (int ki = 0; ki < otherKeys.Length; ki++)
            {
                string key = otherKeys[ki];
                if (!seenKeys.Add(key)) continue;
                CollectibleObject c = PlayerEmc.Resolve(capi.World, key);
                if (c?.Code == null) continue;
                string name;
                try { name = c.GetHeldItemName(new ItemStack(c)); }
                catch { name = c.Code.ToShortString(); }
                all.Add(new Entry
                {
                    Collectible = c, Key = key, Name = name, Code = c.Code.ToString(), Domain = c.Code.Domain,
                    IsBlock = c.ItemClass == EnumItemClass.Block, Other = true,
                    Why = packet.OtherWhy != null && ki < packet.OtherWhy.Length ? packet.OtherWhy[ki] : null
                });
            }
            all.Sort((a, b) => string.Compare(a.Code, b.Code, StringComparison.Ordinal));
            if (previous != null)
            {
                search = previous.search;
                domainFilter = all.Any(e => e.Domain == previous.domainFilter) ? previous.domainFilter : "*";
                typeFilter = previous.typeFilter;
                page = previous.page;
                var keys = new HashSet<string>(all.Select(e => e.Key));
                foreach (string k in previous.selected) if (keys.Contains(k)) selected.Add(k);
            }
            Compose();
        }

        void Compose()
        {
            ClearComposers();
            double top = GuiStyle.TitleBarHeight + 4;

            bool canShowOther = all.Any(e => e.Other);
            ElementBounds summaryBounds = ElementBounds.Fixed(0, top, canShowOther ? ListWidth - 190 : ListWidth + 30, 40);
            ElementBounds showSwitchBounds = ElementBounds.Fixed(ListWidth - 180, top + 2, 30, 30);
            ElementBounds showLabelBounds = ElementBounds.Fixed(ListWidth - 144, top + 7, 180, 24);
            double row = top + 46;
            ElementBounds searchBounds = ElementBounds.Fixed(0, row, 230, 30);
            ElementBounds domainBounds = ElementBounds.Fixed(240, row, 170, 30);
            ElementBounds typeBounds = ElementBounds.Fixed(420, row, 170, 30);

            ElementBounds listBounds = ElementBounds.Fixed(0, row + 42, ListWidth, ListHeight);
            clipBounds = listBounds.ForkBoundingParent();
            ElementBounds insetBounds = listBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds scrollbarBounds = insetBounds.CopyOffsetedSibling(insetBounds.fixedWidth + 4).WithFixedWidth(20);

            double bottom = row + 42 + ListHeight + 14;
            ElementBounds prevBounds = ElementBounds.Fixed(0, bottom, 36, 30);
            ElementBounds countBounds = ElementBounds.Fixed(44, bottom + 6, 250, 24);
            ElementBounds nextBounds = ElementBounds.Fixed(300, bottom, 36, 30);
            ElementBounds copyBounds = ElementBounds.Fixed(ListWidth - 230, bottom, 130, 30);
            ElementBounds closeBounds = ElementBounds.Fixed(ListWidth - 90, bottom, 90, 30);

            double selRow = bottom + 40;
            ElementBounds selectedBounds = ElementBounds.Fixed(0, selRow + 6, 330, 24);
            ElementBounds selectAllBounds = ElementBounds.Fixed(ListWidth - 230, selRow, 130, 30);
            ElementBounds clearBounds = ElementBounds.Fixed(ListWidth - 90, selRow, 90, 30);

            double editRow = selRow + 38;
            ElementBounds valueBounds = ElementBounds.Fixed(0, editRow, 120, 30);
            ElementBounds setBounds = ElementBounds.Fixed(130, editRow, 100, 30);
            ElementBounds listButtonBounds = ElementBounds.Fixed(ListWidth - 230, editRow, 230, 30);

            bool unblacklistRow = canShowOther && ShowOther;
            double unblRow = editRow + 38;
            ElementBounds unblButtonBounds = ElementBounds.Fixed(ListWidth - 230, unblRow, 230, 30);
            ElementBounds unblHintBounds = ElementBounds.Fixed(0, unblRow + 6, ListWidth - 240, 24);
            double modRow = unblacklistRow ? unblRow + 38 : editRow + 38;
            ElementBounds modButtonBounds = ElementBounds.Fixed(ListWidth - 230, modRow, 230, 30);
            ElementBounds modHintBounds = ElementBounds.Fixed(0, modRow + 6, ListWidth - 240, 24);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(summaryBounds, searchBounds, domainBounds, typeBounds, clipBounds, prevBounds, countBounds, nextBounds, copyBounds, closeBounds,
                selectedBounds, selectAllBounds, clearBounds, valueBounds, setBounds, listButtonBounds, modButtonBounds, modHintBounds);
            if (canShowOther) bgBounds.WithChildren(showSwitchBounds, showLabelBounds);
            if (unblacklistRow) bgBounds.WithChildren(unblButtonBounds, unblHintBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            // Counts and filters cover what can be listed right now (the other list only while it's shown).
            List<Entry> listable = all.Where(e => !e.Other || ShowOther).ToList();
            int mainCount = all.Count(e => !e.Other), otherCount = all.Count - mainCount;
            string[] domains = listable.Select(e => e.Domain).Distinct().OrderBy(d => d == "game" ? "" : d, StringComparer.Ordinal).ToArray();
            string[] domainValues = new[] { "*" }.Concat(domains).ToArray();
            string[] domainNames = new[] { Lang.Get("vintagee:missing-allmods") }
                .Concat(domains.Select(d => d + " (" + listable.Count(e => e.Domain == d) + ")")).ToArray();
            string[] typeValues = { "*", "block", "item" };
            string[] typeNames =
            {
                Lang.Get("vintagee:missing-alltypes"),
                Lang.Get("vintagee:missing-blocks") + " (" + listable.Count(e => e.IsBlock) + ")",
                Lang.Get("vintagee:missing-items") + " (" + listable.Count(e => !e.IsBlock) + ")"
            };

            string summary = IsBlacklist
                ? Lang.Get(mainCount == 0 ? "vintagee:blacklist-summary-none" : "vintagee:blacklist-summary", mainCount, packet.ValueCount, packet.Version)
                : mainCount == 0
                    ? Lang.Get("vintagee:missing-summary-none", packet.CreativeTotal, packet.Version)
                    : Lang.Get("vintagee:missing-summary", mainCount, packet.CreativeTotal, packet.ValueCount, packet.Version);

            composing = true;
            GuiComposer composer = capi.Gui
                .CreateCompo(IsBlacklist ? "vintagee-blacklist" : "vintagee-missing", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get(IsBlacklist ? "vintagee:blacklist-title" : "vintagee:missing-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(summary, CairoFont.WhiteDetailText(), summaryBounds)
                    .AddTextInput(searchBounds, OnSearch, CairoFont.WhiteSmallText(), "search")
                    .AddDropDown(domainValues, domainNames, Math.Max(0, Array.IndexOf(domainValues, domainFilter)), OnDomain, domainBounds, "domain")
                    .AddDropDown(typeValues, typeNames, Math.Max(0, Array.IndexOf(typeValues, typeFilter)), OnType, typeBounds, "type")
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddRichtext(Array.Empty<RichTextComponentBase>(), listBounds, "list")
                    .EndClip()
                    .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                    .AddSmallButton("<", OnPrev, prevBounds)
                    .AddDynamicText("", CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), countBounds, "count")
                    .AddSmallButton(">", OnNext, nextBounds)
                    .AddSmallButton(Lang.Get("vintagee:missing-copy"), OnCopy, copyBounds)
                    .AddSmallButton(Lang.Get("vintagee:missing-close"), () => TryClose(), closeBounds)
                    .AddDynamicText("", CairoFont.WhiteDetailText(), selectedBounds, "selected")
                    .AddSmallButton(Lang.Get("vintagee:missing-selectshown"), OnSelectShown, selectAllBounds)
                    .AddSmallButton(Lang.Get("vintagee:missing-clearselection"), OnClearSelection, clearBounds)
                    .AddTextInput(valueBounds, null, CairoFont.WhiteSmallText(), "value")
                    .AddSmallButton(Lang.Get("vintagee:missing-set"), OnSet, setBounds)
                    .AddSmallButton(Lang.Get(IsBlacklist ? "vintagee:missing-unblacklist" : "vintagee:missing-blacklist"), OnBlacklistButton, listButtonBounds)
                    .AddDynamicText("", CairoFont.WhiteDetailText(), modHintBounds, "modhint")
                    .AddSmallButton(Lang.Get(IsBlacklist ? "vintagee:missing-unblacklistmod" : "vintagee:missing-blacklistmod"), OnModButton, modButtonBounds);
            if (canShowOther)
            {
                composer.AddSwitch(OnToggleOther, showSwitchBounds, "showother", 26)
                        .AddStaticText(Lang.Get(IsBlacklist ? "vintagee:blacklist-hidemissing" : "vintagee:missing-showblacklisted", otherCount), CairoFont.WhiteDetailText(), showLabelBounds);
            }
            if (unblacklistRow)
            {
                // Its own button for the mixed-in entries: take blacklisted ones off the blacklist, or blacklist missing ones.
                composer.AddStaticText(Lang.Get(IsBlacklist ? "vintagee:blacklist-blacklisthint" : "vintagee:missing-unblacklisthint"), CairoFont.WhiteDetailText(), unblHintBounds)
                        .AddSmallButton(Lang.Get(IsBlacklist ? "vintagee:missing-blacklist" : "vintagee:missing-unblacklist"), OnOtherButton, unblButtonBounds);
            }
            SingleComposer = composer.EndChildElements().Compose();
            SingleComposer.GetSwitch("showother")?.SetValue(IsBlacklist ? hideMissing : showBlacklisted);

            composing = false;
            SingleComposer.GetTextInput("search")?.SetPlaceHolderText(Lang.Get("vintagee:gui-search-missing"));
            if (search.Length > 0) SingleComposer.GetTextInput("search")?.SetValue(search, false);
            SingleComposer.GetTextInput("value")?.SetPlaceHolderText(Lang.Get("vintagee:missing-valuehint"));
            Refill();
        }

        void Refill()
        {
            if (composing || SingleComposer == null) return;
            try
            {
                string needle = search.Trim().ToLowerInvariant();
                shown = all.Where(e =>
                        (!e.Other || ShowOther) &&
                        (domainFilter == "*" || e.Domain == domainFilter) &&
                        (typeFilter == "*" || (typeFilter == "block") == e.IsBlock) &&
                        (needle.Length == 0 || e.Code.ToLowerInvariant().Contains(needle) || e.Name.ToLowerInvariant().Contains(needle)))
                    .ToList();
                int pages = Math.Max(1, (shown.Count + PerPage - 1) / PerPage);
                page = Math.Clamp(page, 0, pages - 1);

                var comps = new List<RichTextComponentBase>();
                CairoFont nameFont = CairoFont.WhiteSmallText();
                CairoFont pickedFont = CairoFont.WhiteSmallText().WithColor(new double[] { 0.55, 1, 0.55, 1 }).WithWeight(Cairo.FontWeight.Bold);
                CairoFont codeFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.7, 0.7, 0.7, 1 });
                CairoFont whyFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.85, 0.75, 0.5, 1 });
                // Mixed-in entries: red for blacklisted ones, amber for missing ones.
                CairoFont otherFont = CairoFont.WhiteSmallText().WithColor(IsBlacklist ? new double[] { 1, 0.8, 0.4, 1 } : new double[] { 1, 0.55, 0.5, 1 });
                string otherTag = Lang.Get(IsBlacklist ? "vintagee:blacklist-missingtag" : "vintagee:missing-blacklistedtag") + "  ";
                foreach (Entry e in shown.Skip(page * PerPage).Take(PerPage))
                {
                    ItemStack stack = null;
                    try { stack = new ItemStack(e.Collectible); }
                    catch { /* no icon for this one */ }
                    // Inline icons, one entry per line - the same layout the handbook uses for item lists.
                    if (stack != null) comps.Add(new ItemstackTextComponent(capi, stack, 32, 6, EnumFloat.Inline, OpenHandbook) { VerticalAlign = EnumVerticalAlign.Middle });
                    Entry entry = e;
                    bool picked = selected.Contains(e.Key);
                    comps.Add(new LinkTextComponent(capi, (picked ? "✔ " : "") + e.Name + "  ", picked ? pickedFont : e.Other ? otherFont : nameFont, _ => Toggle(entry)) { VerticalAlign = EnumVerticalAlign.Middle });
                    if (e.Other) comps.Add(new RichTextComponent(capi, otherTag, otherFont) { VerticalAlign = EnumVerticalAlign.Middle });
                    comps.Add(new RichTextComponent(capi, (e.IsBlock ? "block:" : "item:") + e.Code + "\n", codeFont) { VerticalAlign = EnumVerticalAlign.Middle });
                    if (!string.IsNullOrEmpty(e.Why)) comps.Add(new RichTextComponent(capi, "      " + e.Why + "\n", whyFont));
                }
                int listable = all.Count(e => !e.Other || ShowOther);
                if (shown.Count == 0)
                {
                    comps.Add(new RichTextComponent(capi, Lang.Get(listable == 0 ? (IsBlacklist ? "vintagee:blacklist-empty" : "vintagee:missing-empty") : "vintagee:missing-nomatch"), CairoFont.WhiteSmallText()));
                }

                GuiElementRichtext list = SingleComposer.GetRichtext("list");
                if (list == null) return;
                float scroll = (float)-list.Bounds.fixedY;
                list.SetNewText(comps.ToArray());
                list.Bounds.CalcWorldBounds();

                GuiElementScrollbar bar = SingleComposer.GetScrollbar("scrollbar");
                if (bar != null)
                {
                    bar.Bounds.CalcWorldBounds();
                    bar.SetHeights((float)ListHeight, (float)Math.Max(ListHeight, list.Bounds.fixedHeight));
                    float max = (float)Math.Max(0, list.Bounds.fixedHeight - ListHeight);
                    scroll = Math.Clamp(scroll, 0, max);
                    bar.CurrentYPosition = scroll;
                }
                list.Bounds.fixedY = -scroll;
                list.Bounds.CalcWorldBounds();

                SingleComposer.GetDynamicText("count")?.SetNewText(Lang.Get("vintagee:missing-page", page + 1, pages, shown.Count, listable));
                UpdateStatus();
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Vintage-E: could not fill the EMC list: {0}", ex);
            }
        }

        void UpdateStatus(string text = null)
        {
            if (SingleComposer == null) return;
            if (text == null)
            {
                if (selected.Count == 0) text = Lang.Get("vintagee:missing-select");
                else if (selected.Count == 1)
                {
                    Entry e = all.FirstOrDefault(x => selected.Contains(x.Key));
                    text = Lang.Get("vintagee:missing-selected", e?.Name ?? selected.First());
                }
                else text = Lang.Get("vintagee:missing-selectedmany", selected.Count);
            }
            SingleComposer.GetDynamicText("selected")?.SetNewText(text);
            SingleComposer.GetDynamicText("modhint")?.SetNewText(domainFilter == "*"
                ? Lang.Get("vintagee:missing-modhint-pick")
                : Lang.Get("vintagee:missing-modhint", domainFilter));
        }

        void Toggle(Entry e)
        {
            if (!selected.Remove(e.Key)) selected.Add(e.Key);
            Refill();
        }

        bool OnSelectShown()
        {
            foreach (Entry e in shown) selected.Add(e.Key);
            Refill();
            return true;
        }

        bool OnClearSelection()
        {
            selected.Clear();
            Refill();
            return true;
        }

        string[] SelectedKeys => all.Where(e => selected.Contains(e.Key)).Select(e => e.Key).ToArray();

        bool OnSet()
        {
            if (SingleComposer == null) return true;
            string[] keys = SelectedKeys;
            if (keys.Length == 0) { UpdateStatus(Lang.Get("vintagee:missing-select")); return true; }
            string text = (SingleComposer.GetTextInput("value")?.GetText() ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || v < 0 || double.IsInfinity(v))
            {
                UpdateStatus(Lang.Get("vintagee:missing-badvalue"));
                return true;
            }
            if (onSetValue == null) return true;
            onSetValue(keys, v);
            UpdateStatus(Lang.Get("vintagee:missing-sendingmany", keys.Length, text));
            return true;
        }

        /// <summary>"Show blacklisted" (missing window) / "Hide missing" (blacklist window).</summary>
        void OnToggleOther(bool on)
        {
            if (composing) return;
            if (IsBlacklist) hideMissing = on;
            else showBlacklisted = on;
            page = 0;
            // Recompose after the click (the switch belongs to the current layout): adds or removes the extra button row.
            capi.Event.EnqueueMainThreadTask(() => { if (IsOpened()) Compose(); }, "vintagee-missing");
        }

        /// <summary>The mixed-in entries' button: takes blacklisted ones off the blacklist, or blacklists missing ones.</summary>
        bool OnOtherButton()
        {
            string[] keys = all.Where(e => selected.Contains(e.Key) && e.Other).Select(e => e.Key).ToArray();
            if (keys.Length == 0) { UpdateStatus(Lang.Get(IsBlacklist ? "vintagee:blacklist-selectmissing" : "vintagee:missing-selectblacklisted")); return true; }
            if (onBlacklist == null) return true;
            onBlacklist(keys, IsBlacklist);
            UpdateStatus(Lang.Get(IsBlacklist ? "vintagee:missing-blacklistingmany" : "vintagee:missing-unblacklistingmany", keys.Length));
            return true;
        }

        bool OnBlacklistButton()
        {
            // The window's own button only acts on its own list's entries (not the mixed-in ones).
            string[] keys = all.Where(e => selected.Contains(e.Key) && !e.Other).Select(e => e.Key).ToArray();
            if (keys.Length == 0) { UpdateStatus(Lang.Get("vintagee:missing-select")); return true; }
            if (onBlacklist == null) return true;
            onBlacklist(keys, !IsBlacklist);
            UpdateStatus(Lang.Get(IsBlacklist ? "vintagee:missing-unblacklistingmany" : "vintagee:missing-blacklistingmany", keys.Length));
            return true;
        }

        bool OnModButton()
        {
            if (domainFilter == "*") { UpdateStatus(Lang.Get("vintagee:missing-modhint-pick")); return true; }
            if (domainFilter == "game") { UpdateStatus(Lang.Get("vintagee:missing-nogame")); return true; }
            if (onBlacklistMod == null) return true;
            onBlacklistMod(domainFilter, !IsBlacklist);
            UpdateStatus(Lang.Get(IsBlacklist ? "vintagee:missing-unblacklistingmod" : "vintagee:missing-blacklistingmod", domainFilter));
            return true;
        }

        void OnScroll(float value)
        {
            GuiElementRichtext list = SingleComposer?.GetRichtext("list");
            if (list == null) return;
            list.Bounds.fixedY = -value;
            list.Bounds.CalcWorldBounds();
        }

        bool OnPrev()
        {
            page--;
            ResetScroll();
            Refill();
            return true;
        }

        bool OnNext()
        {
            page++;
            ResetScroll();
            Refill();
            return true;
        }

        void ResetScroll()
        {
            GuiElementRichtext list = SingleComposer?.GetRichtext("list");
            if (list != null) list.Bounds.fixedY = 0;
        }

        void OnSearch(string text)
        {
            if ((text ?? "") == search) return;
            search = text ?? "";
            page = 0;
            ResetScroll();
            Refill();
        }

        void OnDomain(string code, bool sel)
        {
            domainFilter = code ?? "*";
            page = 0;
            ResetScroll();
            Refill();
        }

        void OnType(string code, bool sel)
        {
            typeFilter = code ?? "*";
            page = 0;
            ResetScroll();
            Refill();
        }

        bool OnCopy()
        {
            var sb = new StringBuilder();
            foreach (Entry e in shown) sb.Append('"').Append(e.IsBlock ? "block:" : "item:").Append(e.Code).Append("\": 0,\n");
            try { capi.Forms.SetClipboardText(sb.ToString()); }
            catch (Exception ex) { capi.Logger.Warning("Vintage-E: clipboard not available: {0}", ex.Message); return true; }
            capi.ShowChatMessage(Lang.Get("vintagee:missing-copied", shown.Count));
            return true;
        }

        void OpenHandbook(ItemStack stack)
        {
            // Same page code the survival mod's handbook uses for item stacks.
            string pageCode = stack.Class.Name() + "-" + stack.Collectible.Code.ToShortString();
            if (capi.LinkProtocols != null && capi.LinkProtocols.TryGetValue("handbook", out Action<LinkTextComponent> open))
            {
                try { open?.Invoke(new LinkTextComponent("handbook://" + pageCode)); }
                catch (Exception ex) { capi.Logger.Warning("Vintage-E: could not open handbook page {0}: {1}", pageCode, ex.Message); }
            }
        }
    }
}

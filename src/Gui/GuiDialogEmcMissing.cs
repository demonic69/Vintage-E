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
    /// blocks/items filter. Click an icon to open its handbook page; "Copy codes" puts the filtered list on the
    /// clipboard ready to paste into BaseValues. Admins can click a name and then type a value and press "Set value"
    /// (saved like /emc set; it also takes a blacklisted item off the blacklist), or press "Blacklist" (missing
    /// window) / "Remove from blacklist" (blacklist window). The list refreshes after each change.
    /// </summary>
    public class GuiDialogEmcMissing : GuiDialog
    {
        const double ListWidth = 560;
        const double ListHeight = 360;
        const int PerPage = 40; // keeps the rendered text small; page with the arrows for more

        sealed class Entry
        {
            public CollectibleObject Collectible;
            public string Key;
            public string Name;
            public string Code;
            public string Domain;
            public bool IsBlock;
        }

        readonly EmcMissingPacket packet;
        readonly Action<string, double> onSetValue;
        readonly Action<string, bool> onBlacklist;

        /// <summary>True for the /emc blacklist window.</summary>
        public bool IsBlacklist => packet.Blacklist;
        Entry selected;
        readonly List<Entry> all = new List<Entry>();
        List<Entry> shown = new List<Entry>();
        string search = "";
        string domainFilter = "*";
        string typeFilter = "*";
        int page;
        bool composing;
        ElementBounds clipBounds;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogEmcMissing(ICoreClientAPI capi, EmcMissingPacket packet, GuiDialogEmcMissing previous = null, Action<string, double> onSetValue = null, Action<string, bool> onBlacklist = null) : base(capi)
        {
            this.packet = packet;
            this.onSetValue = onSetValue;
            this.onBlacklist = onBlacklist;
            if (previous != null)
            {
                search = previous.search;
                domainFilter = previous.domainFilter;
                typeFilter = previous.typeFilter;
                page = previous.page;
            }
            foreach (string key in packet.Keys ?? Array.Empty<string>())
            {
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
                    IsBlock = c.ItemClass == EnumItemClass.Block
                });
            }
            all.Sort((a, b) => string.Compare(a.Code, b.Code, StringComparison.Ordinal));
            Compose();
        }

        void Compose()
        {
            ClearComposers();
            double top = GuiStyle.TitleBarHeight + 4;

            ElementBounds summaryBounds = ElementBounds.Fixed(0, top, ListWidth + 30, 40);
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
            double editRow = bottom + 40;
            ElementBounds selectedBounds = ElementBounds.Fixed(0, editRow + 6, 330, 24);
            ElementBounds valueBounds = ElementBounds.Fixed(ListWidth - 230, editRow, 120, 30);
            ElementBounds setBounds = ElementBounds.Fixed(ListWidth - 100, editRow, 100, 30);
            ElementBounds listButtonBounds = ElementBounds.Fixed(ListWidth - 230, editRow + 38, 230, 30);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(summaryBounds, searchBounds, domainBounds, typeBounds, clipBounds, prevBounds, countBounds, nextBounds, copyBounds, closeBounds,
                selectedBounds, valueBounds, setBounds, listButtonBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            string[] domains = all.Select(e => e.Domain).Distinct().OrderBy(d => d == "game" ? "" : d, StringComparer.Ordinal).ToArray();
            string[] domainValues = new[] { "*" }.Concat(domains).ToArray();
            string[] domainNames = new[] { Lang.Get("vintagee:missing-allmods") }
                .Concat(domains.Select(d => d + " (" + all.Count(e => e.Domain == d) + ")")).ToArray();
            string[] typeValues = { "*", "block", "item" };
            string[] typeNames =
            {
                Lang.Get("vintagee:missing-alltypes"),
                Lang.Get("vintagee:missing-blocks") + " (" + all.Count(e => e.IsBlock) + ")",
                Lang.Get("vintagee:missing-items") + " (" + all.Count(e => !e.IsBlock) + ")"
            };

            string summary = IsBlacklist
                ? Lang.Get(all.Count == 0 ? "vintagee:blacklist-summary-none" : "vintagee:blacklist-summary", all.Count, packet.ValueCount, packet.Version)
                : all.Count == 0
                    ? Lang.Get("vintagee:missing-summary-none", packet.CreativeTotal, packet.Version)
                    : Lang.Get("vintagee:missing-summary", all.Count, packet.CreativeTotal, packet.ValueCount, packet.Version);

            composing = true;
            SingleComposer = capi.Gui
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
                    .AddDynamicText(Lang.Get("vintagee:missing-select"), CairoFont.WhiteDetailText(), selectedBounds, "selected")
                    .AddTextInput(valueBounds, null, CairoFont.WhiteSmallText(), "value")
                    .AddSmallButton(Lang.Get("vintagee:missing-set"), OnSet, setBounds)
                    .AddSmallButton(Lang.Get(IsBlacklist ? "vintagee:missing-unblacklist" : "vintagee:missing-blacklist"), OnBlacklistButton, listButtonBounds)
                .EndChildElements()
                .Compose();

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
                        (domainFilter == "*" || e.Domain == domainFilter) &&
                        (typeFilter == "*" || (typeFilter == "block") == e.IsBlock) &&
                        (needle.Length == 0 || e.Code.ToLowerInvariant().Contains(needle) || e.Name.ToLowerInvariant().Contains(needle)))
                    .ToList();
                int pages = Math.Max(1, (shown.Count + PerPage - 1) / PerPage);
                page = Math.Clamp(page, 0, pages - 1);

                var comps = new List<RichTextComponentBase>();
                CairoFont nameFont = CairoFont.WhiteSmallText();
                CairoFont codeFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.7, 0.7, 0.7, 1 });
                foreach (Entry e in shown.Skip(page * PerPage).Take(PerPage))
                {
                    ItemStack stack = null;
                    try { stack = new ItemStack(e.Collectible); }
                    catch { /* no icon for this one */ }
                    // Inline icons, one entry per line - the same layout the handbook uses for item lists.
                    if (stack != null) comps.Add(new ItemstackTextComponent(capi, stack, 32, 6, EnumFloat.Inline, OpenHandbook) { VerticalAlign = EnumVerticalAlign.Middle });
                    Entry entry = e;
                    comps.Add(new LinkTextComponent(capi, e.Name + "  ", nameFont, _ => Select(entry)) { VerticalAlign = EnumVerticalAlign.Middle });
                    comps.Add(new RichTextComponent(capi, (e.IsBlock ? "block:" : "item:") + e.Code + "\n", codeFont) { VerticalAlign = EnumVerticalAlign.Middle });
                }
                if (shown.Count == 0)
                {
                    comps.Add(new RichTextComponent(capi, Lang.Get(all.Count == 0 ? (IsBlacklist ? "vintagee:blacklist-empty" : "vintagee:missing-empty") : "vintagee:missing-nomatch"), CairoFont.WhiteSmallText()));
                }

                GuiElementRichtext list = SingleComposer.GetRichtext("list");
                if (list == null) return;
                list.SetNewText(comps.ToArray());
                list.Bounds.fixedY = 0;
                list.Bounds.CalcWorldBounds();

                GuiElementScrollbar bar = SingleComposer.GetScrollbar("scrollbar");
                if (bar != null)
                {
                    bar.Bounds.CalcWorldBounds();
                    bar.SetHeights((float)ListHeight, (float)Math.Max(ListHeight, list.Bounds.fixedHeight));
                }

                SingleComposer.GetDynamicText("count")?.SetNewText(Lang.Get("vintagee:missing-page", page + 1, pages, shown.Count, all.Count));
            }
            catch (Exception ex)
            {
                capi.Logger.Error("Vintage-E: could not fill the missing-EMC list: {0}", ex);
            }
        }

        void Select(Entry e)
        {
            selected = e;
            SingleComposer?.GetDynamicText("selected")?.SetNewText(Lang.Get("vintagee:missing-selected", e.Name));
        }

        bool OnSet()
        {
            if (SingleComposer == null) return true;
            var status = SingleComposer.GetDynamicText("selected");
            if (selected == null) { status?.SetNewText(Lang.Get("vintagee:missing-select")); return true; }
            string text = (SingleComposer.GetTextInput("value")?.GetText() ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || v < 0 || double.IsInfinity(v))
            {
                status?.SetNewText(Lang.Get("vintagee:missing-badvalue"));
                return true;
            }
            if (onSetValue == null) return true;
            onSetValue(selected.Key, v);
            status?.SetNewText(Lang.Get("vintagee:missing-sending", selected.Name, text));
            return true;
        }

        bool OnBlacklistButton()
        {
            var status = SingleComposer?.GetDynamicText("selected");
            if (selected == null) { status?.SetNewText(Lang.Get("vintagee:missing-select")); return true; }
            if (onBlacklist == null) return true;
            onBlacklist(selected.Key, !IsBlacklist);
            status?.SetNewText(Lang.Get(IsBlacklist ? "vintagee:missing-unblacklisting" : "vintagee:missing-blacklisting", selected.Name));
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
            Refill();
            return true;
        }

        bool OnNext()
        {
            page++;
            Refill();
            return true;
        }

        void OnSearch(string text)
        {
            if ((text ?? "") == search) return;
            search = text ?? "";
            page = 0;
            Refill();
        }

        void OnDomain(string code, bool selected)
        {
            domainFilter = code ?? "*";
            page = 0;
            Refill();
        }

        void OnType(string code, bool selected)
        {
            typeFilter = code ?? "*";
            page = 0;
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

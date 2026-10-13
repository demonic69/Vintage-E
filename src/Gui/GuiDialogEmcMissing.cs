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
    /// Window opened by /emc missing: every block/item that still has no EMC value, as a scrollable list with
    /// icons, a search bar, a mod filter and a blocks/items filter. Click an icon to open its handbook page;
    /// "Copy codes" puts the filtered list on the clipboard ready to paste into BaseValues.
    /// </summary>
    public class GuiDialogEmcMissing : GuiDialog
    {
        const double ListWidth = 560;
        const double ListHeight = 420;
        const int MaxRows = 1500; // plenty for any real modpack; more would only slow the window down

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
        readonly List<Entry> all = new List<Entry>();
        List<Entry> shown = new List<Entry>();
        string search = "";
        string domainFilter = "*";
        string typeFilter = "*";
        ElementBounds clipBounds;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogEmcMissing(ICoreClientAPI capi, EmcMissingPacket packet) : base(capi)
        {
            this.packet = packet;
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
            ElementBounds countBounds = ElementBounds.Fixed(0, bottom + 6, 330, 24);
            ElementBounds copyBounds = ElementBounds.Fixed(ListWidth - 230, bottom, 130, 30);
            ElementBounds closeBounds = ElementBounds.Fixed(ListWidth - 90, bottom, 90, 30);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(summaryBounds, searchBounds, domainBounds, typeBounds, insetBounds, scrollbarBounds, countBounds, copyBounds, closeBounds);

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

            string summary = all.Count == 0
                ? Lang.Get("vintagee:missing-summary-none", packet.CreativeTotal, packet.Version)
                : Lang.Get("vintagee:missing-summary", all.Count, packet.CreativeTotal, packet.ValueCount, packet.Version);

            SingleComposer = capi.Gui
                .CreateCompo("vintagee-missing", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("vintagee:missing-title"), () => TryClose())
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
                    .AddDynamicText("", CairoFont.WhiteDetailText(), countBounds, "count")
                    .AddSmallButton(Lang.Get("vintagee:missing-copy"), OnCopy, copyBounds)
                    .AddSmallButton(Lang.Get("vintagee:missing-close"), () => TryClose(), closeBounds)
                .EndChildElements()
                .Compose();

            SingleComposer.GetTextInput("search").SetPlaceHolderText(Lang.Get("vintagee:gui-search-missing"));
            Refill();
        }

        void Refill()
        {
            string needle = search.Trim().ToLowerInvariant();
            shown = all.Where(e =>
                    (domainFilter == "*" || e.Domain == domainFilter) &&
                    (typeFilter == "*" || (typeFilter == "block") == e.IsBlock) &&
                    (needle.Length == 0 || e.Code.ToLowerInvariant().Contains(needle) || e.Name.ToLowerInvariant().Contains(needle)))
                .ToList();

            var comps = new List<RichTextComponentBase>();
            CairoFont nameFont = CairoFont.WhiteSmallText();
            CairoFont codeFont = CairoFont.WhiteDetailText().WithColor(new double[] { 0.75, 0.75, 0.75, 1 });
            foreach (Entry e in shown.Take(MaxRows))
            {
                ItemStack stack;
                try { stack = new ItemStack(e.Collectible); }
                catch { continue; }
                comps.Add(new ItemstackTextComponent(capi, stack, 40, 10, EnumFloat.Left, OpenHandbook));
                comps.Add(new RichTextComponent(capi, e.Name + "\n", nameFont));
                comps.Add(new RichTextComponent(capi, (e.IsBlock ? "block:" : "item:") + e.Code + "\n", codeFont));
                comps.Add(new ClearFloatTextComponent(capi, 6));
            }
            if (shown.Count == 0)
            {
                comps.Add(new RichTextComponent(capi, Lang.Get(all.Count == 0 ? "vintagee:missing-empty" : "vintagee:missing-nomatch"), CairoFont.WhiteSmallText()));
            }

            GuiElementRichtext list = SingleComposer.GetRichtext("list");
            list.SetNewText(comps.ToArray());
            list.Bounds.fixedY = 0;
            list.Bounds.CalcWorldBounds();

            GuiElementScrollbar bar = SingleComposer.GetScrollbar("scrollbar");
            bar.Bounds.CalcWorldBounds();
            bar.SetHeights((float)ListHeight, (float)Math.Max(ListHeight, list.Bounds.fixedHeight));
            bar.CurrentYPosition = 0;

            string count = Lang.Get("vintagee:missing-count", shown.Count, all.Count);
            if (shown.Count > MaxRows) count += " " + Lang.Get("vintagee:missing-capped", MaxRows);
            SingleComposer.GetDynamicText("count").SetNewText(count);
        }

        void OnScroll(float value)
        {
            GuiElementRichtext list = SingleComposer.GetRichtext("list");
            list.Bounds.fixedY = -value;
            list.Bounds.CalcWorldBounds();
        }

        void OnSearch(string text)
        {
            if ((text ?? "") == search) return;
            search = text ?? "";
            Refill();
        }

        void OnDomain(string code, bool selected)
        {
            domainFilter = code ?? "*";
            Refill();
        }

        void OnType(string code, bool selected)
        {
            typeFilter = code ?? "*";
            Refill();
        }

        bool OnCopy()
        {
            var sb = new StringBuilder();
            foreach (Entry e in shown) sb.Append('"').Append(e.IsBlock ? "block:" : "item:").Append(e.Code).Append("\": 0,\n");
            capi.Forms.SetClipboardText(sb.ToString());
            capi.ShowChatMessage(Lang.Get("vintagee:missing-copied", shown.Count));
            return true;
        }

        void OpenHandbook(ItemStack stack)
        {
            // Same page code the survival mod's handbook uses for item stacks.
            string pageCode = stack.Class.Name() + "-" + stack.Collectible.Code.ToShortString();
            capi.LinkProtocols.TryGetValue("handbook", out Action<LinkTextComponent> open);
            open?.Invoke(new LinkTextComponent("handbook://" + pageCode));
        }
    }
}

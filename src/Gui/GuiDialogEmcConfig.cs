using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Window opened by /emc configure: every server setting on the left (searchable), the picked one on the right with
    /// what it does and an editor that fits it - a switch for on/off settings, a box for numbers, and a text area for
    /// lists (one entry per line) and tables ("key = value" per line). Save sends it to the server, which checks it,
    /// saves ModConfig/vintagee.json and applies it straight away, then sends the settings back.
    /// </summary>
    public class GuiDialogEmcConfig : GuiDialog
    {
        const double ListWidth = 270;
        const double ListHeight = 440;
        const double PaneWidth = 470;
        const double AreaHeight = 220;

        readonly Action<string, string> onSave;
        string[] names = Array.Empty<string>();
        Dictionary<string, string> values = new Dictionary<string, string>();
        string selected;
        string search = "";
        string status = "";
        bool statusIsError;
        /// <summary>Unsaved text in the editor, kept across redraws.</summary>
        string draft;
        bool composing;
        ElementBounds listClip, areaClip;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogEmcConfig(ICoreClientAPI capi, EmcConfigPacket packet, Action<string, string> onSave) : base(capi)
        {
            this.onSave = onSave;
            Load(packet);
            selected = packet.Selected ?? names.FirstOrDefault();
            Compose();
        }

        void Load(EmcConfigPacket packet)
        {
            names = packet.Names ?? Array.Empty<string>();
            values = new Dictionary<string, string>();
            for (int i = 0; i < names.Length; i++) values[names[i]] = packet.Values != null && i < packet.Values.Length ? packet.Values[i] : "";
        }

        /// <summary>The server answered a change: new values and what happened.</summary>
        public void Update(EmcConfigPacket packet)
        {
            Load(packet);
            if (!string.IsNullOrEmpty(packet.Selected)) selected = packet.Selected;
            status = packet.Message ?? "";
            statusIsError = packet.Error;
            if (!packet.Error) draft = null; // saved: show what the server has now
            Compose();
        }

        static string KindOf(string name)
        {
            FieldInfo f = ConfigureCommand.Field(name);
            return f == null ? "text" : ConfigureCommand.KindOf(f);
        }

        static string About(string name) => ConfigureCommand.Field(name)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";

        static string Short(string kind, string value)
        {
            if (kind == "list" || kind == "table")
            {
                int n = string.IsNullOrWhiteSpace(value) ? 0 : value.Split('\n').Count(l => l.Trim().Length > 0);
                return n == 0 ? "(empty)" : "(" + n + ")";
            }
            return value.Length > 18 ? value.Substring(0, 18) + "…" : value;
        }

        void Compose()
        {
            ClearComposers();
            double top = GuiStyle.TitleBarHeight + 4;
            ElementBounds searchBounds = ElementBounds.Fixed(0, top, ListWidth, 30);
            ElementBounds listBounds = ElementBounds.Fixed(0, top + 40, ListWidth, ListHeight);
            listClip = listBounds.ForkBoundingParent();
            ElementBounds listInset = listBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
            ElementBounds listBar = listInset.CopyOffsetedSibling(listInset.fixedWidth + 4).WithFixedWidth(20);

            double x = ListWidth + 40;
            ElementBounds nameBounds = ElementBounds.Fixed(x, top, PaneWidth, 30);
            ElementBounds aboutBounds = ElementBounds.Fixed(x, top + 34, PaneWidth, 120);
            double editTop = top + 160;
            ElementBounds hintBounds = ElementBounds.Fixed(x, editTop, PaneWidth, 22);
            ElementBounds statusBounds = ElementBounds.Fixed(x, top + 40 + ListHeight - 64, PaneWidth, 40);
            ElementBounds saveBounds = ElementBounds.Fixed(x + PaneWidth - 100, top + 40 + ListHeight - 20, 100, 30);
            ElementBounds revertBounds = ElementBounds.Fixed(x + PaneWidth - 210, top + 40 + ListHeight - 20, 100, 30);
            ElementBounds closeBounds = ElementBounds.Fixed(x, top + 40 + ListHeight - 20, 90, 30);

            string kind = selected == null ? "text" : KindOf(selected);
            string current = selected != null && values.TryGetValue(selected, out string v) ? v : "";
            string shownText = draft ?? current;

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(searchBounds, listClip, listBar, nameBounds, aboutBounds, hintBounds, statusBounds, saveBounds, revertBounds, closeBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            composing = true;
            GuiComposer c = capi.Gui
                .CreateCompo("vintagee-config", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("vintagee:cfg-window-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddTextInput(searchBounds, OnSearch, CairoFont.WhiteSmallText(), "search")
                    .BeginClip(listClip)
                        .AddInset(listInset, 3)
                        .AddRichtext(Array.Empty<RichTextComponentBase>(), listBounds, "list")
                    .EndClip()
                    .AddVerticalScrollbar(v2 => Scroll("list", v2), listBar, "listbar")
                    .AddStaticText(selected ?? "", CairoFont.WhiteSmallishText().WithWeight(Cairo.FontWeight.Bold), nameBounds)
                    .AddStaticText(selected == null ? "" : About(selected), CairoFont.WhiteDetailText(), aboutBounds)
                    .AddStaticText(Lang.Get("vintagee:cfg-hint-" + kind), CairoFont.WhiteDetailText().WithColor(new double[] { 0.75, 0.75, 0.75, 1 }), hintBounds);

            double editorTop = editTop + 26;
            if (kind == "bool")
            {
                ElementBounds switchBounds = ElementBounds.Fixed(x, editorTop, 40, 40);
                ElementBounds switchLabel = ElementBounds.Fixed(x + 50, editorTop + 6, 200, 30);
                c.AddSwitch(on => { draft = on ? "true" : "false"; }, switchBounds, "switch")
                 .AddStaticText(Lang.Get("vintagee:cfg-on"), CairoFont.WhiteSmallText(), switchLabel);
                bgBounds.WithChildren(switchBounds, switchLabel);
            }
            else if (kind == "list" || kind == "table")
            {
                ElementBounds areaBounds = ElementBounds.Fixed(0, 0, PaneWidth - 30, AreaHeight);
                areaClip = ElementBounds.Fixed(x, editorTop, PaneWidth - 30, AreaHeight);
                areaClip.WithChild(areaBounds);
                ElementBounds areaInset = areaClip.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
                ElementBounds areaBar = areaInset.CopyOffsetedSibling(areaInset.fixedWidth + 4).WithFixedWidth(20);
                c.AddInset(areaInset, 3)
                 .BeginClip(areaClip)
                    .AddTextArea(areaBounds, t => { if (!composing) { draft = t; UpdateAreaScroll(); } }, CairoFont.WhiteSmallText(), "area")
                 .EndClip()
                 .AddVerticalScrollbar(v2 => Scroll("area", v2), areaBar, "areabar");
                bgBounds.WithChildren(areaInset, areaClip, areaBar);
            }
            else
            {
                ElementBounds inputBounds = ElementBounds.Fixed(x, editorTop, 220, 30);
                c.AddTextInput(inputBounds, t => { if (!composing) draft = t; }, CairoFont.WhiteSmallText(), "input");
                bgBounds.WithChildren(inputBounds);
            }

            CairoFont statusFont = statusIsError ? CairoFont.WhiteDetailText().WithColor(new double[] { 1, 0.5, 0.45, 1 }) : CairoFont.WhiteDetailText().WithColor(new double[] { 0.55, 1, 0.55, 1 });
            c.AddStaticText(status, statusFont, statusBounds)
             .AddSmallButton(Lang.Get("vintagee:missing-close"), () => TryClose(), closeBounds)
             .AddSmallButton(Lang.Get("vintagee:cfg-revert"), OnRevert, revertBounds)
             .AddSmallButton(Lang.Get("vintagee:cfg-save"), OnSave, saveBounds)
             .EndChildElements();
            SingleComposer = c.Compose();

            SingleComposer.GetTextInput("search")?.SetPlaceHolderText(Lang.Get("vintagee:cfg-search"));
            if (search.Length > 0) SingleComposer.GetTextInput("search")?.SetValue(search, false);
            if (kind == "bool") SingleComposer.GetSwitch("switch")?.SetValue(shownText == "true");
            else if (kind == "list" || kind == "table") SingleComposer.GetTextArea("area")?.SetValue(shownText, false);
            else SingleComposer.GetTextInput("input")?.SetValue(shownText, false);
            composing = false;
            if (kind == "list" || kind == "table") UpdateAreaScroll();
            FillList();
        }

        void FillList()
        {
            if (SingleComposer == null) return;
            string needle = search.Trim().ToLowerInvariant();
            var comps = new List<RichTextComponentBase>();
            CairoFont normal = CairoFont.WhiteSmallText();
            CairoFont picked = CairoFont.WhiteSmallText().WithColor(new double[] { 0.55, 1, 0.55, 1 }).WithWeight(Cairo.FontWeight.Bold);
            CairoFont detail = CairoFont.WhiteDetailText().WithColor(new double[] { 0.7, 0.7, 0.7, 1 });
            foreach (string name in names)
            {
                if (needle.Length > 0 && !name.ToLowerInvariant().Contains(needle) && !About(name).ToLowerInvariant().Contains(needle)) continue;
                string n = name;
                comps.Add(new LinkTextComponent(capi, name, name == selected ? picked : normal, _ => Pick(n)));
                comps.Add(new RichTextComponent(capi, "  " + Short(KindOf(name), values.TryGetValue(name, out string v) ? v : "") + "\n", detail));
            }
            if (comps.Count == 0) comps.Add(new RichTextComponent(capi, Lang.Get("vintagee:missing-nomatch"), normal));
            GuiElementRichtext list = SingleComposer.GetRichtext("list");
            if (list == null) return;
            float scroll = (float)-list.Bounds.fixedY;
            list.SetNewText(comps.ToArray());
            list.Bounds.CalcWorldBounds();
            GuiElementScrollbar bar = SingleComposer.GetScrollbar("listbar");
            if (bar != null)
            {
                bar.Bounds.CalcWorldBounds();
                bar.SetHeights((float)ListHeight, (float)Math.Max(ListHeight, list.Bounds.fixedHeight));
                scroll = Math.Clamp(scroll, 0, (float)Math.Max(0, list.Bounds.fixedHeight - ListHeight));
                bar.CurrentYPosition = scroll;
            }
            list.Bounds.fixedY = -scroll;
            list.Bounds.CalcWorldBounds();
        }

        void UpdateAreaScroll()
        {
            GuiElementTextArea area = SingleComposer?.GetTextArea("area");
            GuiElementScrollbar bar = SingleComposer?.GetScrollbar("areabar");
            if (area == null || bar == null) return;
            bar.SetHeights((float)AreaHeight, (float)Math.Max(AreaHeight, area.Bounds.fixedHeight));
        }

        void Scroll(string key, float value)
        {
            if (SingleComposer == null) return;
            ElementBounds b = key == "list" ? SingleComposer.GetRichtext("list")?.Bounds : SingleComposer.GetTextArea("area")?.Bounds;
            if (b == null) return;
            b.fixedY = -value;
            b.CalcWorldBounds();
        }

        void Pick(string name)
        {
            if (name == selected) return;
            selected = name;
            draft = null;
            status = "";
            Recompose();
        }

        /// <summary>Redraws after the current click has been handled (the clicked element belongs to the old layout).</summary>
        void Recompose() => capi.Event.EnqueueMainThreadTask(() => { if (IsOpened()) Compose(); }, "vintagee-config");

        void OnSearch(string text)
        {
            if (composing || (text ?? "") == search) return;
            search = text ?? "";
            GuiElementRichtext list = SingleComposer?.GetRichtext("list");
            if (list != null) list.Bounds.fixedY = 0;
            FillList();
        }

        bool OnSave()
        {
            if (selected == null) return true;
            string text = draft ?? (values.TryGetValue(selected, out string v) ? v : "");
            onSave?.Invoke(selected, text);
            status = Lang.Get("vintagee:cfg-saving");
            statusIsError = false;
            return true;
        }

        bool OnRevert()
        {
            draft = null;
            status = "";
            Recompose();
            return true;
        }
    }
}

using System;
using System.Globalization;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// The small box the "Set EMC value" hotkey opens for the block or item under the mouse (see <see cref="HoveredStack"/>):
    /// type a value and press Set or Enter. Unpin takes a value set this way (or with /emc set) off again, so the item
    /// goes back to what recipes give it. Admins only; the server checks too.
    /// </summary>
    public class GuiDialogEmcSetValue : GuiDialog
    {
        const double Width = 340;

        readonly ItemStack stack;
        readonly Action<double?> onSet;
        readonly DisplayInventory iconInventory;

        public override string ToggleKeyCombinationCode => null;
        /// <summary>Above the handbook (0.2) and container windows.</summary>
        public override double DrawOrder => 0.3;
        public override double InputOrder => 0.3;
        public override bool UnregisterOnClose => true;

        /// <param name="onSet">Called with the typed value, or null for Unpin.</param>
        public GuiDialogEmcSetValue(ICoreClientAPI capi, ItemStack stack, double current, Action<double?> onSet) : base(capi)
        {
            this.stack = stack.Clone();
            this.stack.StackSize = 1;
            this.onSet = onSet;
            iconInventory = new DisplayInventory(capi, "setvalue-" + Guid.NewGuid().ToString("N"), 1);
            iconInventory[0].Itemstack = this.stack;
            Compose(current);
        }

        void Compose(double current)
        {
            CairoFont detail = CairoFont.WhiteDetailText();
            string name = stack.GetName();
            string code = (stack.Class == EnumItemClass.Block ? "block:" : "item:") + stack.Collectible.Code;
            string now = current > 0 ? Lang.Get("vintagee:setvalue-now", EmcFormat.Format(current)) : Lang.Get("vintagee:setvalue-none");
            string info = name + "\n" + code + "\n" + now;
            double textX = 60;
            double infoHeight = Math.Max(52, GuiLayout.TextHeight(capi, detail, info, Width - textX));

            ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 30, 1, 1);
            ElementBounds infoBounds = ElementBounds.Fixed(textX, 30, Width - textX, infoHeight);
            double y = 30 + infoHeight + 10;
            ElementBounds labelBounds = ElementBounds.Fixed(0, y + 6, 70, 20);
            ElementBounds valueBounds = ElementBounds.Fixed(75, y, Width - 75, 30);
            y += 40;
            ElementBounds hintBounds = ElementBounds.Fixed(0, y, Width, GuiLayout.TextHeight(capi, detail, Lang.Get("vintagee:setvalue-hint"), Width));
            y += hintBounds.fixedHeight + 6;
            double buttonWidth = (Width - 20) / 3;
            ElementBounds setBounds = ElementBounds.Fixed(0, y, buttonWidth, 30);
            ElementBounds unpinBounds = ElementBounds.Fixed(buttonWidth + 10, y, buttonWidth, 30);
            ElementBounds cancelBounds = ElementBounds.Fixed(2 * (buttonWidth + 10), y, buttonWidth, 30);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(slotBounds, infoBounds, labelBounds, valueBounds, hintBounds, setBounds, unpinBounds, cancelBounds);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            SingleComposer = capi.Gui
                .CreateCompo("vintagee-setvalue", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("vintagee:setvalue-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddItemSlotGrid(iconInventory, _ => { }, 1, slotBounds, "icon")
                    .AddStaticText(info, detail, infoBounds)
                    .AddStaticText(Lang.Get("vintagee:setvalue-label"), detail, labelBounds)
                    .AddTextInput(valueBounds, null, CairoFont.WhiteSmallText(), "value")
                    .AddStaticText(Lang.Get("vintagee:setvalue-hint"), detail, hintBounds)
                    .AddSmallButton(Lang.Get("vintagee:missing-set"), OnSet, setBounds)
                    .AddSmallButton(Lang.Get("vintagee:setvalue-unpin"), OnUnpin, unpinBounds)
                    .AddSmallButton(Lang.Get("vintagee:setvalue-cancel"), () => TryClose(), cancelBounds)
                .EndChildElements()
                .Compose();

            GuiElementTextInput input = SingleComposer.GetTextInput("value");
            input.SetValue(current > 0 ? current.ToString("0.####", CultureInfo.InvariantCulture) : "");
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            ignoreNextKeyPress = true; // the hotkey's own key press
            GuiElementTextInput input = SingleComposer?.GetTextInput("value");
            if (input != null) SingleComposer.FocusElement(input.TabIndex);
        }

        public override void OnKeyDown(KeyEvent args)
        {
            if (args.KeyCode == (int)GlKeys.Enter || args.KeyCode == (int)GlKeys.KeypadEnter)
            {
                OnSet();
                args.Handled = true;
                return;
            }
            base.OnKeyDown(args);
        }

        bool OnSet()
        {
            string text = SingleComposer.GetTextInput("value").GetText();
            if (!TryParse(text, out double value))
            {
                capi.TriggerIngameError(this, "badvalue", Lang.Get("vintagee:setvalue-bad"));
                return true;
            }
            onSet(value);
            TryClose();
            return true;
        }

        bool OnUnpin()
        {
            onSet(null);
            TryClose();
            return true;
        }

        /// <summary>"1234", "1,234.5", "1e6", "12k", "3m"; never negative.</summary>
        public static bool TryParse(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().Replace(",", "").Replace(" ", "").Replace("_", "");
            double scale = 1;
            char last = char.ToLowerInvariant(t[t.Length - 1]);
            if (last == 'k' || last == 'm' || last == 'b')
            {
                scale = last == 'k' ? 1e3 : last == 'm' ? 1e6 : 1e9;
                t = t.Substring(0, t.Length - 1);
            }
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            value *= scale;
            return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            iconInventory[0].Itemstack = null;
        }
    }
}

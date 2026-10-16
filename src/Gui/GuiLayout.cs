using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>Layout helpers so windows can place each row below the text above it, however long it wraps.</summary>
    public static class GuiLayout
    {
        /// <summary>Height (unscaled GUI units, like ElementBounds.Fixed) that <paramref name="text"/> needs when wrapped to <paramref name="width"/>.</summary>
        public static double TextHeight(ICoreClientAPI capi, CairoFont font, string text, double width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double scale = RuntimeEnv.GUIScale <= 0 ? 1 : RuntimeEnv.GUIScale;
            return capi.Gui.Text.GetMultilineTextHeight(font, text, GuiElement.scaled(width)) / scale + 4;
        }
    }
}

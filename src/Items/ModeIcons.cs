using Cairo;
using Vintagestory.API.Client;

namespace VintageE
{
    /// <summary>Icons for the tool-mode menu that can show an on/off state.</summary>
    public static class ModeIcons
    {
        /// <summary>A letter; switched-off entries are drawn faded and struck through.</summary>
        public static void Letter(ICoreClientAPI capi, Context ctx, int x, int y, float w, float h, string letter, bool on)
        {
            double alpha = on ? 1 : 0.35;
            CairoFont font = CairoFont.WhiteMediumText().WithColor(new double[] { 1, 1, 1, alpha });
            font.SetupContext(ctx);
            TextExtents size = font.GetTextExtents(letter);
            double asc = font.GetFontExtents().Ascent;
            capi.Gui.Text.DrawTextLine(ctx, font, letter, x + (w - size.Width) / 2, y + (h - asc) / 2);
            if (on) return;
            ctx.SetSourceRGBA(1, 0.35, 0.3, 0.9);
            ctx.LineWidth = 3;
            ctx.MoveTo(x + w * 0.15, y + h * 0.85);
            ctx.LineTo(x + w * 0.85, y + h * 0.15);
            ctx.Stroke();
        }
    }
}

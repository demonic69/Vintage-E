using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>Shared "1x1 / 3x3 / 5x5" tool modes (picked with the tool-mode key, F by default), stored on the itemstack.</summary>
    public static class AreaToolModes
    {
        public const string AttributeKey = "vintageeArea";

        public static SkillItem[] Create(ICoreClientAPI capi, int maxRadius)
        {
            var modes = new SkillItem[maxRadius + 1];
            for (int r = 0; r <= maxRadius; r++)
            {
                int size = r * 2 + 1;
                int cells = size;
                modes[r] = new SkillItem
                {
                    Code = new AssetLocation("vintagee", "area" + size),
                    Name = Lang.Get("vintagee:toolmode-area", size, size)
                }.WithIcon(capi, (ctx, x, y, w, h, rgba) => DrawGrid(ctx, x, y, w, h, rgba, cells));
            }
            return modes;
        }

        static void DrawGrid(Context ctx, int x, int y, float w, float h, double[] rgba, int cells)
        {
            double size = Math.Min(w, h);
            double gap = size * 0.06;
            double cell = (size - gap * (cells + 1)) / cells;
            ctx.SetSourceRGBA(rgba[0], rgba[1], rgba[2], rgba[3]);
            for (int i = 0; i < cells; i++)
            {
                for (int j = 0; j < cells; j++)
                {
                    ctx.Rectangle(x + gap + i * (cell + gap), y + gap + j * (cell + gap), cell, cell);
                }
            }
            ctx.Fill();
        }

        public static int GetRadius(ItemSlot slot) => slot?.Itemstack?.Attributes?.GetInt(AttributeKey, 0) ?? 0;

        public static void SetRadius(ItemSlot slot, int radius)
        {
            if (slot?.Itemstack == null) return;
            slot.Itemstack.Attributes.SetInt(AttributeKey, Math.Max(0, radius));
            slot.MarkDirty();
        }

        /// <summary>Positions in the square of the given radius around <paramref name="center"/>, in the plane facing <paramref name="face"/>.</summary>
        public static System.Collections.Generic.IEnumerable<BlockPos> Plane(BlockPos center, BlockFacing face, int radius)
        {
            for (int a = -radius; a <= radius; a++)
            {
                for (int b = -radius; b <= radius; b++)
                {
                    BlockPos p = center.Copy();
                    switch (face?.Axis ?? EnumAxis.Y)
                    {
                        case EnumAxis.X: p.Add(0, a, b); break;
                        case EnumAxis.Z: p.Add(a, b, 0); break;
                        default: p.Add(a, 0, b); break;
                    }
                    yield return p;
                }
            }
        }
    }
}

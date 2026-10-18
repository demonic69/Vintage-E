using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>
    /// Client: draws what sits on a pedestal floating above it, upright, facing the viewer and bobbing (like ProjectE's
    /// pedestals). Uses the item's own inventory model (its shape, the ProjectE look when that's on) at a fixed size.
    /// </summary>
    public sealed class PedestalItemRenderer : IRenderer
    {
        const float Size = 0.42f;

        readonly ICoreClientAPI capi;
        readonly BlockPos pos;
        readonly float height;
        readonly DummySlot slot = new DummySlot();
        readonly Matrixf model = new Matrixf();
        readonly float phase;

        public double RenderOrder => 0.5;
        public int RenderRange => 32;

        public ItemStack Stack
        {
            get => slot.Itemstack;
            set => slot.Itemstack = value?.Clone();
        }

        /// <param name="height">Where the item's middle floats, in blocks above the pedestal's base.</param>
        public PedestalItemRenderer(ICoreClientAPI capi, BlockPos pos, float height)
        {
            this.capi = capi;
            this.pos = pos.Copy();
            this.height = height;
            phase = (pos.X * 31 + pos.Z * 17) % 100 / 10f; // pedestals side by side don't turn in step
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (slot.Itemstack?.Collectible == null) return;
            ItemRenderInfo info;
            try { info = capi.Render.GetItemStackRenderInfo(slot, EnumItemRenderTarget.Gui, deltaTime); }
            catch { return; }
            if (info?.ModelRef == null || info.Transform == null) return;

            IRenderAPI rpi = capi.Render;
            Vec3d cam = capi.World.Player.Entity.CameraPos;
            float t = capi.World.ElapsedMilliseconds / 1000f + phase;
            ModelTransform tf = info.Transform;
            float scale = Size * Math.Max(0.2f, tf.ScaleXYZ.X);

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);
            IStandardShaderProgram prog = rpi.StandardShader;
            prog.Use();
            prog.DontWarpVertices = 0;
            prog.AddRenderFlags = 0;
            prog.RgbaAmbientIn = rpi.AmbientColor;
            prog.RgbaFogIn = rpi.FogColor;
            prog.FogMinIn = rpi.FogMin;
            prog.FogDensityIn = rpi.FogDensity;
            prog.RgbaTint = ColorUtil.WhiteArgbVec;
            prog.NormalShaded = info.NormalShaded ? 1 : 0;
            prog.ExtraGodray = 0;
            prog.SsaoAttn = 0;
            prog.AlphaTest = info.AlphaTest;
            prog.OverlayOpacity = 0;
            prog.DamageEffect = 0;
            prog.ExtraGlow = 0;
            Vec4f light = capi.World.BlockAccessor.GetLightRGBs(pos.X, pos.Y + 1, pos.Z);
            prog.RgbaLightIn = light;

            // Upright and turned to face whoever looks at it, swaying a little: the inventory look's own tilt isn't
            // used (it's made for the inventory view), and a spinning flat ring kept showing its edge.
            double dx = cam.X - (pos.X + 0.5), dz = cam.Z - (pos.Z + 0.5);
            float facing = (float)Math.Atan2(dx, dz) + 0.25f * (float)Math.Sin(t * 0.8);
            prog.ModelMatrix = model
                .Identity()
                .Translate(pos.X - cam.X + 0.5, pos.Y - cam.Y + height + 0.04 * Math.Sin(t * 2), pos.Z - cam.Z + 0.5)
                .RotateY(facing)
                .Scale(scale, scale, scale)
                .Translate(-0.5f, -0.5f, -0.5f)
                .Values;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

            rpi.RenderMultiTextureMesh(info.ModelRef, "tex");
            prog.Stop();
            rpi.GlEnableCullFace();
        }

        public void Dispose() => slot.Itemstack = null;
    }
}

using System;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// Dark/Red Matter Furnace: the game's own firepit (same window, fuel, cooking pots, crucibles, spits and
    /// ignition) built from matter, so it burns hotter, cooks faster and makes fuel last longer. Block attributes:
    /// <c>heatModifier</c> (× the fuel's burn temperature), <c>burnDurationModifier</c> (× how long fuel lasts),
    /// <c>cookingSpeed</c> (÷ cooking/smelting time once hot), <c>emcPerSecond</c> and <c>emcTemperature</c>.
    /// It can also run on EMC pushed in by collectors and relays: when it has something to heat and no fuel burning,
    /// it lights itself and burns EMC instead, at <c>emcPerSecond</c> and <c>emcTemperature</c> °C (× heatModifier).
    /// </summary>
    public class BEMatterFurnace : BlockEntityFirepit, IEmcAcceptor
    {
        public const double EmcCapacity = 1000;
        const float EmcBurnSeconds = 10;

        double emcBuffer;
        float heatModifier = 1.4f, burnDurationModifier = 3f, cookingSpeed = 4f, emcTemperature = 1300;
        double emcPerSecond = 2;

        static readonly MethodInfo ContentMesh = typeof(BlockEntityFirepit).GetMethod("getContentMesh", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo ContentsRendererField = typeof(BlockEntityFirepit).GetField("renderer", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// The firepit draws items cooked on a spit (meat...) 0.6 blocks up, where its spit is. The furnace has no spit,
        /// so they'd float: they're lowered by this much to lie on the furnace instead.
        /// </summary>
        const float SpitDrop = 0.55f;
        ModelTransform loweredTransform;

        public override float HeatModifier => heatModifier;
        public override float BurnDurationModifier => burnDurationModifier;
        public override float maxCookingTime() => base.maxCookingTime() / Math.Max(0.1f, cookingSpeed);

        public override string DialogTitle
        {
            get
            {
                try { return Block?.GetPlacedBlockName(Api.World, Pos) ?? base.DialogTitle; }
                catch { return base.DialogTitle; }
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            JsonObject attrs = Block?.Attributes;
            heatModifier = attrs?["heatModifier"].AsFloat(1.4f) ?? 1.4f;
            burnDurationModifier = attrs?["burnDurationModifier"].AsFloat(3f) ?? 3f;
            cookingSpeed = attrs?["cookingSpeed"].AsFloat(4f) ?? 4f;
            emcPerSecond = attrs?["emcPerSecond"].AsDouble(2) ?? 2;
            emcTemperature = attrs?["emcTemperature"].AsFloat(1300) ?? 1300;
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnEmcTick, 1000);
            else
            {
                LowerSpitItem();
                RegisterGameTickListener(_ => LowerSpitItem(), 250);
            }
        }

        /// <summary>Client: whenever the firepit renderer shows a spit item, lowers it onto the furnace (a lowered copy of its transform).</summary>
        void LowerSpitItem()
        {
            // The renderer's fields are public in the game's FirepitContentsRenderer; read them by name.
            object r = ContentsRendererField?.GetValue(this);
            if (r == null) return;
            Type type = r.GetType();
            FieldInfo stackField = type.GetField("ContentStack"), childField = type.GetField("contentStackRenderer"), transformField = type.GetField("Transform");
            if (stackField == null || childField == null || transformField == null) return;
            if (!(stackField.GetValue(r) is ItemStack stack) || stack.Class != EnumItemClass.Item || childField.GetValue(r) != null) return;
            if (!(transformField.GetValue(r) is ModelTransform current) || ReferenceEquals(current, loweredTransform)) return;
            ModelTransform t = current.Clone();
            t.Translation.Y -= SpitDrop;
            loweredTransform = t;
            transformField.SetValue(r, t);
        }

        public double AcceptEmc(double amount)
        {
            double take = Math.Min(amount, Math.Max(0, EmcCapacity - emcBuffer));
            emcBuffer += take;
            return take;
        }

        /// <summary>Keeps the fire going on EMC while there is something to heat and no fuel is burning.</summary>
        void OnEmcTick(float dt)
        {
            double cost = emcPerSecond * EmcBurnSeconds;
            if (emcBuffer < cost || inputStack == null) return;
            bool wantsHeat;
            try { wantsHeat = canHeatInput(); }
            catch { wantsHeat = false; }
            if (!wantsHeat) return;

            if (!IsBurning)
            {
                if (fuelSlot.Itemstack?.Collectible.GetCombustibleProperties(Api.World, fuelSlot.Itemstack, null)?.BurnTemperature > 0) return; // real fuel first
                emcBuffer -= cost;
                maxFuelBurnTime = fuelBurnTime = EmcBurnSeconds;
                maxTemperature = (int)(emcTemperature * HeatModifier);
                setBlockState("lit");
                MarkDirty(true);
            }
            else if (fuelBurnTime < 2 && fuelSlot.Empty)
            {
                emcBuffer -= cost;
                fuelBurnTime += EmcBurnSeconds;
                maxFuelBurnTime = Math.Max(maxFuelBurnTime, fuelBurnTime);
                maxTemperature = Math.Max(maxTemperature, (int)(emcTemperature * HeatModifier));
                MarkDirty(false);
            }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            // Draw whatever sits on the fire the way the firepit does, but our own furnace instead of the logs.
            try
            {
                ItemStack content = inputStack ?? outputStack;
                if (ContentMesh?.Invoke(this, new object[] { content, tesselator }) is MeshData mesh) mesher.AddMeshData(mesh);
            }
            catch (Exception e)
            {
                Api?.Logger.VerboseDebug("Vintage-E furnace content mesh: {0}", e.Message);
            }
            return false;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("vintageeEmc", emcBuffer);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            emcBuffer = tree.GetDouble("vintageeEmc");
            if (Api?.Side == EnumAppSide.Client) LowerSpitItem();
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vintagee:furnace-info", (int)furnaceTemperature, EmcFormat.Format(emcBuffer), EmcFormat.Format(EmcCapacity)));
        }
    }

    /// <summary>
    /// Matter furnaces placed with Vintage-E 1.4.0 (before they became firepits): turns itself into the new furnace
    /// as soon as it loads, dropping whatever was inside on top of it.
    /// </summary>
    public class BEMatterFurnaceLegacy : BEInventoryBase
    {
        public override string InventoryClassName => "vintageefurnace";
        protected override int Columns => 3;
        protected override InventoryBase CreateInventory() => new InventoryGeneric(3, null, null);

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server) RegisterDelayedCallback(_ => Convert(), 500);
        }

        void Convert()
        {
            Block current = Api.World.BlockAccessor.GetBlock(Pos);
            if (current?.Code == null || current.Code.Domain != VintageEModSystem.ModId) return;
            string type = current.Variant?["type"] ?? "dark";
            Block replacement = Api.World.GetBlock(new AssetLocation(VintageEModSystem.ModId, "furnace-" + type + "-cold"));
            if (replacement == null || replacement.Id == 0) return;
            var contents = new System.Collections.Generic.List<ItemStack>();
            foreach (ItemSlot slot in Inventory)
            {
                if (slot?.Itemstack != null) contents.Add(slot.Itemstack.Clone());
                if (slot != null) slot.Itemstack = null;
            }
            Api.World.BlockAccessor.SetBlock(replacement.Id, Pos);
            foreach (ItemStack stack in contents) Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.1, 0.5));
        }
    }
}

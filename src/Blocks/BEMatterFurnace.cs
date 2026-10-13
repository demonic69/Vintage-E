using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>
    /// Dark/Red Matter Furnace: a fast, fuss-free smelter. Slot 1 = input, 2 = fuel, 3 = output. Anything with
    /// smelting/firing/cooking data whose melting point the furnace reaches is processed directly — ore nuggets
    /// straight to ingots (no crucible), raw clay to fired, raw food to cooked. Runs on any fuel (alchemical fuels
    /// burn longest) or on EMC fed in by adjacent collectors and relays (1 EMC per second of heat).
    /// Speeds and temperature come from block attributes: <c>secondsPerItem</c>, <c>maxTemperature</c>.
    /// Hoppers can feed the input/fuel; chutes take from the output.
    /// </summary>
    public class BEMatterFurnace : BEInventoryBase, IEmcAcceptor
    {
        public const int InputSlot = 0, FuelSlot = 1, OutputSlot = 2;

        double heatSeconds;   // remaining burn time
        double emcBuffer;     // EMC pushed in by collectors, spent as heat
        double progress;      // seconds spent on the current item
        double secondsPerItem = 1;
        double maxTemperature = 1600;

        public override string InventoryClassName => "vintageefurnace";
        protected override int Columns => 3;

        protected override InventoryBase CreateInventory() => new FurnaceInventory();

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            secondsPerItem = Block?.Attributes?["secondsPerItem"].AsDouble(1) ?? 1;
            maxTemperature = Block?.Attributes?["maxTemperature"].AsDouble(1600) ?? 1600;
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnServerTick, 250);
        }

        VintageEModSystem Mod => Api.ModLoader.GetModSystem<VintageEModSystem>();

        public double AcceptEmc(double amount)
        {
            double room = Math.Max(0, 10000 - emcBuffer);
            double take = Math.Min(room, amount);
            emcBuffer += take;
            return take;
        }

        /// <summary>What the input slot turns into, and how many inputs that takes; null if the furnace can't process it.</summary>
        (ItemStack output, int inputsNeeded)? Recipe(ItemStack input)
        {
            if (input == null) return null;
            CombustibleProperties props = input.Collectible.CombustibleProps;
            if (props == null)
            {
                try { props = input.Collectible.GetCombustibleProperties(Api.World, input, null); }
                catch { props = null; }
            }
            ItemStack smelted = props?.SmeltedStack?.ResolvedItemstack;
            if (smelted == null) return null;
            if (props.MeltingPoint > maxTemperature) return null;
            return (smelted.Clone(), Math.Max(1, props.SmeltedRatio));
        }

        void OnServerTick(float dt)
        {
            double speed = Mod?.Config.MachineSpeedMultiplier ?? 1;
            ItemSlot input = inventory[InputSlot], fuel = inventory[FuelSlot], output = inventory[OutputSlot];
            var recipe = Recipe(input.Itemstack);
            bool canWork = recipe != null && input.StackSize >= recipe.Value.inputsNeeded && FitsOutput(Api.World, output, recipe.Value.output);
            bool changed = false;

            if (canWork && heatSeconds <= 0)
            {
                // Refuel: EMC first (cheap and automatic), then the fuel slot.
                if (emcBuffer >= 1)
                {
                    double use = Math.Min(emcBuffer, 5);
                    emcBuffer -= use;
                    heatSeconds += use;
                    changed = true;
                }
                else if (fuel.Itemstack?.Collectible.CombustibleProps is CombustibleProperties fp && fp.BurnDuration > 0 && fp.BurnTemperature > 0)
                {
                    heatSeconds += fp.BurnDuration;
                    fuel.TakeOut(1);
                    fuel.MarkDirty();
                    changed = true;
                }
            }

            if (canWork && heatSeconds > 0)
            {
                heatSeconds -= dt;
                progress += dt * speed;
                if (progress >= secondsPerItem)
                {
                    progress = 0;
                    input.TakeOut(recipe.Value.inputsNeeded);
                    input.MarkDirty();
                    if (output.Itemstack == null) output.Itemstack = recipe.Value.output;
                    else output.Itemstack.StackSize += recipe.Value.output.StackSize;
                    output.MarkDirty();
                }
                changed = true;
            }
            else if (!canWork && progress > 0)
            {
                progress = 0;
                changed = true;
            }
            if (changed) MarkDirty(false);
        }

        static bool FitsOutput(IWorldAccessor world, ItemSlot output, ItemStack result)
        {
            if (output.Itemstack == null) return true;
            if (!output.Itemstack.Equals(world, result, GlobalConstants.IgnoredStackAttributes)) return false;
            return output.Itemstack.StackSize + result.StackSize <= output.Itemstack.Collectible.MaxStackSize;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("heat", heatSeconds);
            tree.SetDouble("emc", emcBuffer);
            tree.SetDouble("progress", progress);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            heatSeconds = tree.GetDouble("heat");
            emcBuffer = tree.GetDouble("emc");
            progress = tree.GetDouble("progress");
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vintagee:furnace-slots"));
            dsc.AppendLine(Lang.Get(heatSeconds > 0 ? "vintagee:furnace-burning" : "vintagee:furnace-cold", (int)Math.Ceiling(heatSeconds), EmcFormat.Format(emcBuffer)));
            if (progress > 0) dsc.AppendLine(Lang.Get("vintagee:furnace-progress", (int)(progress / Math.Max(0.01, secondsPerItem) * 100)));
        }
    }

    /// <summary>Input, fuel and a take-only output. Hoppers fill input (or fuel, for fuels); chutes pull the output.</summary>
    public class FurnaceInventory : InventoryGeneric
    {
        public FurnaceInventory() : base(3, null, null, null, (id, inv) => id == BEMatterFurnace.OutputSlot ? new ItemSlotOutput(inv) : new ItemSlotSurvival(inv)) { }

        public override ItemSlot GetAutoPushIntoSlot(Vintagestory.API.MathTools.BlockFacing atBlockFace, ItemSlot fromSlot)
        {
            CombustibleProperties props = fromSlot?.Itemstack?.Collectible.CombustibleProps;
            bool isFuel = props != null && props.BurnDuration > 0 && props.SmeltedStack == null;
            return isFuel ? this[BEMatterFurnace.FuelSlot] : this[BEMatterFurnace.InputSlot];
        }

        public override ItemSlot GetAutoPullFromSlot(Vintagestory.API.MathTools.BlockFacing atBlockFace) => this[BEMatterFurnace.OutputSlot];
    }
}

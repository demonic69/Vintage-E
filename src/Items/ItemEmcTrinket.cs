using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageE
{
    /// <summary>
    /// Rings, stones, amulets, the repair talisman and the watch. Vintage Story has no ring slots, so these work
    /// from anywhere in your hotbar or backpack. Right-click toggles one on or off (shown in its tooltip); the
    /// Ring of Arcana and the Gem of Eternal Density pick their mode with the tool-mode key. The Evertide and
    /// Volcanite amulets instead place water/lava where you right-click. Effects run in <see cref="TrinketEffects"/>
    /// and are paid for from personal EMC, then carried Klein stars.
    /// </summary>
    public class ItemEmcTrinket : Item
    {
        public const string ActiveKey = "vintageeActive";
        public const string ModeKey = "vintageeMode";

        public static readonly string[] ArcanaModes = { "zero", "ignition", "harvest", "swiftwolf" };
        public static readonly string[] DensityTargets = { "game:ingot-iron", "game:ingot-gold", "game:gem-diamond-rough", "vintagee:matter-dark", "vintagee:matter-red" };

        SkillItem[] modes;

        public string Kind => Variant?["type"] ?? "";

        public bool IsToggleable => Kind != "evertideamulet" && Kind != "volcaniteamulet";

        public static bool IsActive(ItemStack stack) => stack?.Attributes?.GetBool(ActiveKey) == true;

        public static int GetMode(ItemStack stack) => stack?.Attributes?.GetInt(ModeKey) ?? 0;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (api is ICoreClientAPI capi)
            {
                if (Kind == "ringofarcana")
                {
                    modes = new SkillItem[ArcanaModes.Length];
                    for (int i = 0; i < modes.Length; i++)
                    {
                        modes[i] = new SkillItem { Code = new AssetLocation("vintagee", "arcana-" + ArcanaModes[i]), Name = Lang.Get("vintagee:arcana-" + ArcanaModes[i]) }
                            .WithLetterIcon(capi, Lang.Get("vintagee:arcana-" + ArcanaModes[i]).Substring(0, 1));
                    }
                }
                else if (Kind == "gemofeternaldensity")
                {
                    modes = new SkillItem[DensityTargets.Length];
                    for (int i = 0; i < modes.Length; i++)
                    {
                        CollectibleObject target = Resolve(capi.World, DensityTargets[i]);
                        string name = target == null ? DensityTargets[i] : target.GetHeldItemName(new ItemStack(target));
                        modes[i] = new SkillItem { Code = new AssetLocation("vintagee", "density" + i), Name = name }.WithLetterIcon(capi, name.Substring(0, 1));
                    }
                }
            }
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            if (modes != null) foreach (SkillItem m in modes) m?.Dispose();
            base.OnUnloaded(api);
        }

        public static CollectibleObject Resolve(IWorldAccessor world, string code)
        {
            var loc = new AssetLocation(code);
            return (CollectibleObject)world.GetItem(loc) ?? world.GetBlock(loc);
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel) => modes;

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection) => GetMode(slot?.Itemstack);

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
        {
            if (slot?.Itemstack == null) return;
            slot.Itemstack.Attributes.SetInt(ModeKey, toolMode);
            slot.MarkDirty();
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            // Placing a trinket on a pedestal is the pedestal's job.
            if (blockSel != null && byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) is BEPedestal)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }
            handling = EnumHandHandling.PreventDefault;
            if (!firstEvent || byEntity.World.Side != EnumAppSide.Server) return;
            IPlayer player = (byEntity as EntityPlayer)?.Player;
            if (player == null || slot.Itemstack == null) return;

            TrinketEffects effects = byEntity.World.Api.ModLoader.GetModSystem<VintageEModSystem>()?.Trinkets;
            if (!IsToggleable)
            {
                if (blockSel != null) effects?.PlaceLiquid(this, player, blockSel);
                return;
            }
            bool on = !IsActive(slot.Itemstack);
            slot.Itemstack.Attributes.SetBool(ActiveKey, on);
            slot.MarkDirty();
            byEntity.World.PlaySoundAt(new AssetLocation(on ? "game:sounds/effect/latch" : "game:sounds/effect/woodswitch"), byEntity, null, true, 8, 0.7f);
            if (!on) effects?.OnDeactivated(player as Vintagestory.API.Server.IServerPlayer);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            ItemStack stack = inSlot?.Itemstack;
            if (stack == null) return;
            if (IsToggleable) dsc.AppendLine(Lang.Get(IsActive(stack) ? "vintagee:trinket-on" : "vintagee:trinket-off"));
            if (Kind == "ringofarcana")
            {
                dsc.AppendLine(Lang.Get("vintagee:trinket-mode", Lang.Get("vintagee:arcana-" + ArcanaModes[GameMath.Clamp(GetMode(stack), 0, ArcanaModes.Length - 1)])));
            }
            else if (Kind == "gemofeternaldensity")
            {
                CollectibleObject target = Resolve(world, DensityTargets[GameMath.Clamp(GetMode(stack), 0, DensityTargets.Length - 1)]);
                if (target != null) dsc.AppendLine(Lang.Get("vintagee:density-target", target.GetHeldItemName(new ItemStack(target)), EmcFormat.Format(StoredEmc.Get(stack))));
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            var list = new System.Collections.Generic.List<WorldInteraction>
            {
                new WorldInteraction { ActionLangCode = IsToggleable ? "vintagee:heldhelp-trinket-toggle" : "vintagee:heldhelp-trinket-place", MouseButton = EnumMouseButton.Right }
            };
            if (modes != null) list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-trinket-mode", HotKeyCodes = new[] { "toolmodeselect" }, MouseButton = EnumMouseButton.None });
            return list.ToArray();
        }
    }
}

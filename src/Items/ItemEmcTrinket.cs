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
    /// watch and the Gem of Eternal Density pick their mode with the tool-mode key, and on the Ring of Arcana the
    /// tool-mode key switches each of its four effects on or off independently (all four are on by default). The Evertide and
    /// Volcanite amulets instead place water/lava where you right-click. Effects run in <see cref="TrinketEffects"/>
    /// and are paid for from personal EMC, then carried Klein stars.
    /// </summary>
    public class ItemEmcTrinket : Item
    {
        public const string ActiveKey = "vintageeActive";
        public const string ModeKey = "vintageeMode";
        /// <summary>Ring of Arcana: bit i set = effect <see cref="ArcanaModes"/>[i] switched off.</summary>
        public const string ArcanaOffKey = "vintageeArcanaOff";

        public static readonly string[] ArcanaModes = { "zero", "ignition", "harvest", "swiftwolf" };
        public static readonly string[] WatchModes = { "faster", "slower" };
        public static readonly string[] DensityTargets = { "game:ingot-iron", "game:ingot-gold", "game:gem-diamond-rough", "vintagee:matter-dark", "vintagee:matter-red" };

        SkillItem[] modes;

        public string Kind => Variant?["type"] ?? "";

        public bool IsToggleable => Kind != "evertideamulet" && Kind != "volcaniteamulet";

        public static bool IsActive(ItemStack stack) => stack?.Attributes?.GetBool(ActiveKey) == true;

        public static int GetMode(ItemStack stack) => stack?.Attributes?.GetInt(ModeKey) ?? 0;

        /// <summary>Whether the Ring of Arcana's effect <paramref name="index"/> is switched on (all are by default).</summary>
        public static bool ArcanaEnabled(ItemStack stack, int index) => ((stack?.Attributes?.GetInt(ArcanaOffKey) ?? 0) & (1 << index)) == 0;

        SkillItem[] arcanaOn, arcanaOff;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (api is ICoreClientAPI capi)
            {
                if (Kind == "ringofarcana")
                {
                    arcanaOn = new SkillItem[ArcanaModes.Length];
                    arcanaOff = new SkillItem[ArcanaModes.Length];
                    for (int i = 0; i < ArcanaModes.Length; i++)
                    {
                        string name = Lang.Get("vintagee:arcana-" + ArcanaModes[i]);
                        string letter = name.Substring(0, 1);
                        arcanaOn[i] = new SkillItem { Code = new AssetLocation("vintagee", "arcana-" + ArcanaModes[i] + "-on"), Name = Lang.Get("vintagee:arcana-toggle-on", name) }
                            .WithIcon(capi, (ctx, x, y, w, h, rgba) => ModeIcons.Letter(capi, ctx, x, y, w, h, letter, true));
                        arcanaOff[i] = new SkillItem { Code = new AssetLocation("vintagee", "arcana-" + ArcanaModes[i] + "-off"), Name = Lang.Get("vintagee:arcana-toggle-off", name) }
                            .WithIcon(capi, (ctx, x, y, w, h, rgba) => ModeIcons.Letter(capi, ctx, x, y, w, h, letter, false));
                    }
                }
                else if (Kind == "watchofflowingtime")
                {
                    modes = new SkillItem[WatchModes.Length];
                    for (int i = 0; i < modes.Length; i++)
                    {
                        modes[i] = new SkillItem { Code = new AssetLocation("vintagee", "watch-" + WatchModes[i]), Name = Lang.Get("vintagee:watch-" + WatchModes[i]) }
                            .WithLetterIcon(capi, i == 0 ? "+" : "-");
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
            if (arcanaOn != null) foreach (SkillItem m in arcanaOn) m?.Dispose();
            if (arcanaOff != null) foreach (SkillItem m in arcanaOff) m?.Dispose();
            base.OnUnloaded(api);
        }

        public static CollectibleObject Resolve(IWorldAccessor world, string code)
        {
            var loc = new AssetLocation(code);
            return (CollectibleObject)world.GetItem(loc) ?? world.GetBlock(loc);
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
        {
            if (arcanaOn == null) return modes;
            // One entry per effect, showing whether it is on; picking one switches it.
            var list = new SkillItem[ArcanaModes.Length];
            for (int i = 0; i < list.Length; i++) list[i] = ArcanaEnabled(slot?.Itemstack, i) ? arcanaOn[i] : arcanaOff[i];
            return list;
        }

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection) => GetMode(slot?.Itemstack);

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
        {
            if (slot?.Itemstack == null) return;
            if (Kind == "ringofarcana" && toolMode >= 0 && toolMode < ArcanaModes.Length)
            {
                int off = slot.Itemstack.Attributes.GetInt(ArcanaOffKey) ^ (1 << toolMode);
                if (off == 0) slot.Itemstack.Attributes.RemoveAttribute(ArcanaOffKey);
                else slot.Itemstack.Attributes.SetInt(ArcanaOffKey, off);
                if (byPlayer is Vintagestory.API.Server.IServerPlayer sp && !ArcanaEnabled(slot.Itemstack, 3))
                {
                    byPlayer.Entity.World.Api.ModLoader.GetModSystem<VintageEModSystem>()?.Trinkets?.OnDeactivated(sp);
                }
            }
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
                var on = new System.Collections.Generic.List<string>();
                for (int i = 0; i < ArcanaModes.Length; i++) if (ArcanaEnabled(stack, i)) on.Add(Lang.Get("vintagee:arcana-" + ArcanaModes[i]));
                dsc.AppendLine(on.Count == 0 ? Lang.Get("vintagee:arcana-none") : Lang.Get("vintagee:arcana-effects", string.Join(", ", on)));
            }
            else if (Kind == "watchofflowingtime")
            {
                dsc.AppendLine(Lang.Get("vintagee:trinket-mode", Lang.Get("vintagee:watch-" + WatchModes[GameMath.Clamp(GetMode(stack), 0, WatchModes.Length - 1)])));
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
            if (modes != null || arcanaOn != null) list.Add(new WorldInteraction { ActionLangCode = arcanaOn != null ? "vintagee:heldhelp-arcana-toggle" : "vintagee:heldhelp-trinket-mode", HotKeyCodes = new[] { "toolmodeselect" }, MouseButton = EnumMouseButton.None });
            return list.ToArray();
        }
    }
}

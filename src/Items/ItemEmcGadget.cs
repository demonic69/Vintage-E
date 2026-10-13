using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// EMC-powered gadgets, used with right-click and paid for from personal EMC, then carried Klein stars.
    /// Lenses and the catalyst follow ProjectE: their charge level is picked with the tool-mode key.
    /// <list type="bullet">
    /// <item>Destruction Catalyst: breaks a 3×3 tunnel into the block you click, 1/4/8/16 deep (charge 0–3), 8 EMC a block.
    ///   Everything it breaks drops where you clicked.</item>
    /// <item>Hyperkinetic Lens: right-click (or the fire-projectile key, R) shoots an explosive charge that flies
    ///   straight and explodes where it hits: radius 4/6/8/10 for 384/768/1,536/2,304 EMC (charge 0–3).</item>
    /// <item>Catalytic Lens: right-click a block for the catalyst's tunnel, 1 then 16–64 deep (charge 0–7);
    ///   the fire-projectile key shoots the lens charge (charge 3 and up = the biggest shot).</item>
    /// <item>Mercurial Eye: sneak + right-click a block to remember it, then right-click blocks to turn them into it
    ///   (1×1 or 3×3); you pay the difference in EMC (or get it back if the old block was worth more).</item>
    /// <item>Archangel's Smite: strikes the creature you look at, up to 48 blocks away.</item>
    /// <item>Tome of Knowledge: learn every item that has an EMC value.</item>
    /// <item>Divining Rods: report the EMC of the ground below the block you click (3, 16 or 64 deep).</item>
    /// </list>
    /// </summary>
    public class ItemEmcGadget : Item
    {
        public const string EyeTargetKey = "vintageeEyeTarget";
        SkillItem[] modes;

        public string Kind => Variant?["type"] ?? "";

        VintageEModSystem Mod(IWorldAccessor world) => world.Api.ModLoader.GetModSystem<VintageEModSystem>();

        /// <summary>Lens shot explosion radius and EMC cost by charge level (ProjectE: power 4/8/12/16, 384–2,304 EMC).</summary>
        public static readonly float[] LensRadius = { 4, 6, 8, 10 };
        public static readonly double[] LensCost = { 384, 768, 1536, 2304 };
        /// <summary>Destruction Catalyst tunnel depth by charge (ProjectE: 1, then 2^(1+charge)).</summary>
        public static readonly int[] CatalystDepth = { 1, 4, 8, 16 };
        public const int CatalyticCharges = 8;
        public const double DestroyCostPerBlock = 8;
        /// <summary>Catalytic Lens tunnel depth by charge (ProjectE: 1, then 8 + 8 × charge, up to 64).</summary>
        public static int CatalyticDepth(int charge) => charge <= 0 ? 1 : 8 + 8 * Math.Min(charge, CatalyticCharges - 1);
        public static int LensCharge(int charge) => GameMath.Clamp(charge, 0, LensRadius.Length - 1);

        public bool IsLens => Kind == "hyperkineticlens" || Kind == "catalyticlens";

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (!(api is ICoreClientAPI capi)) return;
            if (Kind == "destructioncatalyst")
            {
                modes = Enumerable.Range(0, CatalystDepth.Length).Select(c => new SkillItem
                {
                    Code = new AssetLocation("vintagee", "charge" + c),
                    Name = Lang.Get("vintagee:gadget-charge-catalyst", c, CatalystDepth[c])
                }.WithLetterIcon(capi, c.ToString())).ToArray();
            }
            else if (Kind == "hyperkineticlens")
            {
                modes = Enumerable.Range(0, LensRadius.Length).Select(c => new SkillItem
                {
                    Code = new AssetLocation("vintagee", "charge" + c),
                    Name = Lang.Get("vintagee:gadget-charge-lens", c, LensRadius[c], EmcFormat.Format(LensCost[c]))
                }.WithLetterIcon(capi, c.ToString())).ToArray();
            }
            else if (Kind == "catalyticlens")
            {
                modes = Enumerable.Range(0, CatalyticCharges).Select(c => new SkillItem
                {
                    Code = new AssetLocation("vintagee", "charge" + c),
                    Name = Lang.Get("vintagee:gadget-charge-catalytic", c, CatalyticDepth(c), LensRadius[LensCharge(c)], EmcFormat.Format(LensCost[LensCharge(c)]))
                }.WithLetterIcon(capi, c.ToString())).ToArray();
            }
            else if (Kind == "mercurialeye")
            {
                modes = AreaToolModes.Create(capi, 1);
            }
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            if (modes != null) foreach (SkillItem m in modes) m?.Dispose();
            base.OnUnloaded(api);
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel) => modes;

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection) => slot?.Itemstack?.Attributes?.GetInt(ItemEmcTrinket.ModeKey) ?? 0;

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
        {
            if (slot?.Itemstack == null) return;
            slot.Itemstack.Attributes.SetInt(ItemEmcTrinket.ModeKey, toolMode);
            slot.MarkDirty();
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;
            if (!firstEvent || byEntity.World.Side != EnumAppSide.Server) return;
            if (!((byEntity as EntityPlayer)?.Player is IServerPlayer player) || slot.Itemstack == null) return;
            var mod = Mod(byEntity.World);
            if (mod == null) return;
            double mult = mod.Config.TrinketCostMultiplier;
            int mode = GetToolMode(slot, player, blockSel);

            switch (Kind)
            {
                case "destructioncatalyst":
                    if (blockSel != null) Destroy(player, blockSel, CatalystDepth[GameMath.Clamp(mode, 0, CatalystDepth.Length - 1)], DestroyCostPerBlock * mult);
                    break;
                case "hyperkineticlens":
                    FireLens(player, slot);
                    break;
                case "catalyticlens":
                    if (blockSel != null) Destroy(player, blockSel, CatalyticDepth(mode), DestroyCostPerBlock * mult);
                    break;
                case "mercurialeye":
                    if (blockSel != null) Eye(mod, player, slot, blockSel, mode);
                    break;
                case "archangelssmite":
                    Smite(mod, player, 64 * mult);
                    break;
                case "tomeofknowledge":
                    int n = PlayerEmc.LearnMany(player, mod.Emc.All.Keys.Select(PlayerEmc.KeyOf));
                    Tell(player, Lang.GetL(player.LanguageCode, "vintagee:tome-learned", n));
                    break;
                case "diviningrodlow":
                case "diviningrodmedium":
                case "diviningrodhigh":
                    if (blockSel != null) Divine(mod, player, blockSel, Kind == "diviningrodlow" ? 3 : Kind == "diviningrodmedium" ? 16 : 64);
                    break;
            }
        }

        public static void Tell(IServerPlayer player, string msg) => player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.Notification);

        static bool Breakable(IWorldAccessor world, IServerPlayer player, BlockPos pos, Block block)
        {
            if (block == null || block.Id == 0 || block.Replaceable >= 6000) return false;
            if (block.RequiredMiningTier > 6 || block.Code.Path.StartsWith("mantle")) return false;
            return world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak);
        }

        /// <summary>
        /// ProjectE's destruction: a 3×3 area <paramref name="depth"/> blocks deep into the clicked face, paying
        /// <paramref name="costEach"/> per block broken. The drops are gathered where you clicked.
        /// </summary>
        void Destroy(IServerPlayer player, BlockSelection sel, int depth, double costEach)
        {
            IWorldAccessor world = player.Entity.World;
            IBlockAccessor ba = world.BlockAccessor;
            BlockFacing into = sel.Face.Opposite;
            int broken = 0;
            bool stop = false;
            for (int d = 0; d < depth && !stop; d++)
            {
                BlockPos layer = sel.Position.AddCopy(into, d);
                foreach (BlockPos pos in AreaToolModes.Plane(layer, sel.Face, 1))
                {
                    Block block = ba.GetBlock(pos);
                    if (!Breakable(world, player, pos, block)) continue;
                    if (!EmcWallet.TryPay(player, costEach))
                    {
                        Tell(player, Lang.GetL(player.LanguageCode, "vintagee:error-notenoughemc", EmcFormat.Format(costEach), EmcFormat.Format(EmcWallet.Balance(player))));
                        stop = true;
                        break;
                    }
                    ba.BreakBlock(pos, player);
                    broken++;
                }
            }
            if (broken == 0) return;
            world.PlaySoundAt(new AssetLocation("game:sounds/effect/rockslide"), sel.Position.X + 0.5, sel.Position.Y + 0.5, sel.Position.Z + 0.5, null, true, 16, 0.6f);

            // Pull what dropped along the tunnel back to where the player clicked.
            Vec3f n = sel.Face.Normalf;
            Vec3d target = sel.Position.ToVec3d().Add(0.5 + n.X * 0.7, 0.5 + n.Y * 0.7, 0.5 + n.Z * 0.7);
            BlockPos far = sel.Position.AddCopy(into, depth - 1);
            Vec3d mid = new Vec3d((sel.Position.X + far.X) / 2.0 + 0.5, (sel.Position.Y + far.Y) / 2.0 + 0.5, (sel.Position.Z + far.Z) / 2.0 + 0.5);
            float reach = depth / 2f + 2;
            world.Api.Event.RegisterCallback(_ =>
            {
                foreach (Entity e in world.GetEntitiesAround(mid, reach, reach, x => x is EntityItem && x.Alive))
                {
                    e.Pos.SetPos(target);
                    e.Pos.Motion.Set(0, 0, 0);
                }
            }, 100);
        }

        /// <summary>Hyperkinetic/Catalytic Lens: pays for and fires an explosive lens charge where the player looks.</summary>
        public static bool FireLens(IServerPlayer player, ItemSlot slot)
        {
            if (!(slot?.Itemstack?.Collectible is ItemEmcGadget gadget) || !gadget.IsLens) return false;
            IWorldAccessor world = player.Entity.World;
            var mod = world.Api.ModLoader.GetModSystem<VintageEModSystem>();
            int charge = LensCharge(slot.Itemstack.Attributes.GetInt(ItemEmcTrinket.ModeKey));
            double cost = LensCost[charge] * (mod?.Config.TrinketCostMultiplier ?? 1);
            if (!EmcWallet.TryPay(player, cost))
            {
                Tell(player, Lang.GetL(player.LanguageCode, "vintagee:error-notenoughemc", EmcFormat.Format(cost), EmcFormat.Format(EmcWallet.Balance(player))));
                return false;
            }
            if (EntityVintageEProjectile.Shoot(player, "lens", 36, shot => shot.Radius = LensRadius[charge]) == null) return false;
            world.PlaySoundAt(new AssetLocation("game:sounds/effect/swoosh"), player.Entity, null, true, 24, 1f);
            return true;
        }

        void Eye(VintageEModSystem mod, IServerPlayer player, ItemSlot slot, BlockSelection sel, int mode)
        {
            IWorldAccessor world = player.Entity.World;
            IBlockAccessor ba = world.BlockAccessor;
            Block clicked = ba.GetBlock(sel.Position);
            if (player.Entity.Controls.Sneak)
            {
                if (clicked == null || clicked.Id == 0 || clicked.EntityClass != null || mod.Emc.GetValue(clicked) <= 0) return;
                slot.Itemstack.Attributes.SetString(EyeTargetKey, clicked.Code.ToString());
                slot.MarkDirty();
                Tell(player, Lang.GetL(player.LanguageCode, "vintagee:eye-target", clicked.GetPlacedBlockName(world, sel.Position)));
                return;
            }
            string code = slot.Itemstack.Attributes.GetString(EyeTargetKey);
            Block target = code == null ? null : world.GetBlock(new AssetLocation(code));
            if (target == null || target.Id == 0) { Tell(player, Lang.GetL(player.LanguageCode, "vintagee:eye-notarget")); return; }
            double targetValue = mod.Emc.GetValue(target);

            foreach (BlockPos pos in AreaToolModes.Plane(sel.Position, sel.Face, GameMath.Clamp(mode, 0, 1)))
            {
                Block old = ba.GetBlock(pos);
                if (old.Id == target.Id || old.EntityClass != null || !Breakable(world, player, pos, old)) continue;
                if (mode == 1 && old.Id != clicked.Id) continue; // 3x3 only swaps blocks like the one clicked
                double diff = targetValue - mod.Emc.GetValue(old);
                if (diff > 0 && !EmcWallet.TryPay(player, diff)) break;
                if (diff < 0) PlayerEmc.AddEmc(player, -diff);
                ba.SetBlock(target.BlockId, pos);
                ba.TriggerNeighbourBlockUpdate(pos);
            }
            world.PlaySoundAt(new AssetLocation("game:sounds/effect/swoosh"), sel.Position.X + 0.5, sel.Position.Y + 0.5, sel.Position.Z + 0.5, null, true, 12, 0.5f);
        }

        void Smite(VintageEModSystem mod, IServerPlayer player, double cost)
        {
            IWorldAccessor world = player.Entity.World;
            BlockSelection blockSel = null;
            EntitySelection entitySel = null;
            Vec3d eye = player.Entity.ServerPos.XYZ.Add(player.Entity.LocalEyePos);
            world.RayTraceForSelection(eye, player.Entity.ServerPos.Pitch, player.Entity.ServerPos.Yaw, 48, ref blockSel, ref entitySel,
                null, e => e != player.Entity && e is EntityAgent && e.Alive && !(e is EntityPlayer));
            Entity target = entitySel?.Entity;
            if (target == null) return;
            if (!EmcWallet.TryPay(player, cost))
            {
                Tell(player, Lang.GetL(player.LanguageCode, "vintagee:error-notenoughemc", EmcFormat.Format(cost), EmcFormat.Format(EmcWallet.Balance(player))));
                return;
            }
            target.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Player,
                SourceEntity = player.Entity,
                CauseEntity = player.Entity,
                Type = EnumDamageType.PiercingAttack
            }, 12f);
            world.PlaySoundAt(new AssetLocation("game:sounds/weather/lightning-verynear"), target, null, true, 32, 0.4f);
        }

        void Divine(VintageEModSystem mod, IServerPlayer player, BlockSelection sel, int depth)
        {
            IBlockAccessor ba = player.Entity.World.BlockAccessor;
            double total = 0, best = 0;
            int count = 0;
            Block bestBlock = null;
            for (int dy = 0; dy < depth; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        BlockPos pos = sel.Position.AddCopy(dx, -dy, dz);
                        if (pos.Y < 1) continue;
                        Block block = ba.GetBlock(pos);
                        if (block == null || block.Id == 0) continue;
                        double v = mod.Emc.GetValue(block);
                        if (v <= 0) continue;
                        total += v;
                        count++;
                        if (v > best) { best = v; bestBlock = block; }
                    }
                }
            }
            string bestName = bestBlock == null ? "-" : bestBlock.GetHeldItemName(new ItemStack(bestBlock));
            Tell(player, Lang.GetL(player.LanguageCode, "vintagee:divining-result", depth, EmcFormat.Format(total), EmcFormat.Format(count == 0 ? 0 : total / count), bestName, EmcFormat.Format(best)));
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            if (Kind == "mercurialeye")
            {
                string code = inSlot?.Itemstack?.Attributes?.GetString(EyeTargetKey);
                Block target = code == null ? null : world.GetBlock(new AssetLocation(code));
                dsc.AppendLine(target == null ? Lang.Get("vintagee:eye-notarget") : Lang.Get("vintagee:eye-current", target.GetHeldItemName(new ItemStack(target))));
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            var list = new System.Collections.Generic.List<WorldInteraction>
            {
                new WorldInteraction { ActionLangCode = "vintagee:heldhelp-gadget-" + Kind, MouseButton = EnumMouseButton.Right }
            };
            if (Kind == "mercurialeye") list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-gadget-eyepick", HotKeyCode = "sneak", MouseButton = EnumMouseButton.Right });
            if (IsLens) list.Add(new WorldInteraction { ActionLangCode = "vintagee:heldhelp-gadget-fire", HotKeyCode = VintageEModSystem.FireHotkey, MouseButton = EnumMouseButton.None });
            if (modes != null) list.Add(new WorldInteraction { ActionLangCode = IsLens || Kind == "destructioncatalyst" ? "vintagee:heldhelp-gadget-charge" : "vintagee:heldhelp-trinket-mode", HotKeyCodes = new[] { "toolmodeselect" }, MouseButton = EnumMouseButton.None });
            return list.ToArray();
        }
    }
}

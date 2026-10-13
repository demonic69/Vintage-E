using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Anti-Matter Relay MK1–3: an EMC battery and booster between collectors and the machines they power.
    /// Accepts EMC from collectors (or other relays), adds a small bonus while it holds EMC, and pushes it on to
    /// adjacent condensers, furnaces and emptier relays. Sneak + right-click with an empty hand to take the EMC
    /// yourself, or with a Klein star to charge it. Attributes: <c>capacity</c>, <c>bonusPerSecond</c>, <c>transferPerSecond</c>.
    /// </summary>
    public class BEAntimatterRelay : BlockEntity, IEmcAcceptor, IEmcInteractable
    {
        public double Stored;
        double capacity = 100000, bonusPerSecond = 1, transferPerSecond = 64;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            capacity = Block?.Attributes?["capacity"].AsDouble(100000) ?? 100000;
            bonusPerSecond = Block?.Attributes?["bonusPerSecond"].AsDouble(1) ?? 1;
            transferPerSecond = Block?.Attributes?["transferPerSecond"].AsDouble(64) ?? 64;
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnServerTick, 1000);
        }

        double Fill => capacity <= 0 ? 1 : Stored / capacity;

        public double AcceptEmc(double amount)
        {
            double take = Math.Min(amount, Math.Max(0, capacity - Stored));
            Stored += take;
            if (take > 0) MarkDirty(false);
            return take;
        }

        void OnServerTick(float dt)
        {
            double before = Stored;
            double speed = Api.ModLoader.GetModSystem<VintageEModSystem>()?.Config.MachineSpeedMultiplier ?? 1;
            if (Stored > 0) Stored = Math.Min(capacity, Stored + bonusPerSecond * speed * dt);

            double budget = Math.Min(Stored, transferPerSecond * speed * dt);
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (budget <= 0) break;
                BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(face));
                if (!(be is IEmcAcceptor acceptor) || be is BEEnergyCollector) continue;
                if (be is BEAntimatterRelay other && other.Fill >= Fill) continue; // flow downhill only
                double moved = acceptor.AcceptEmc(budget);
                budget -= moved;
                Stored -= moved;
            }
            if (Math.Abs(Stored - before) > 1e-6) MarkDirty(false);
        }

        public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!byPlayer.Entity.Controls.Sneak) return false;
            if (Api.Side == EnumAppSide.Client) return true;
            if (!Api.World.Claims.TryAccess(byPlayer, Pos, EnumBlockAccessFlags.Use) || Stored < 1) return true;
            Stored -= EmcTransfer.ToPlayerOrStar(byPlayer, Math.Floor(Stored));
            MarkDirty(false);
            return true;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("emc", Stored);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            Stored = tree.GetDouble("emc");
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vintagee:collector-stored", EmcFormat.Format(Stored), EmcFormat.Format(capacity)));
            dsc.AppendLine(Lang.Get("vintagee:relay-bonus", EmcFormat.Format(bonusPerSecond), EmcFormat.Format(transferPerSecond)));
        }
    }

    /// <summary>Moving EMC out of machines: into a Klein star in the player's hand if there is one, otherwise their personal EMC.</summary>
    public static class EmcTransfer
    {
        /// <summary>Returns how much was moved.</summary>
        public static double ToPlayerOrStar(IPlayer player, double amount)
        {
            if (amount <= 0) return 0;
            ItemSlot held = player.InventoryManager?.ActiveHotbarSlot;
            if (held?.Itemstack?.Collectible is IEmcStorageItem star)
            {
                double stored = StoredEmc.Get(held.Itemstack);
                double move = Math.Min(amount, star.GetCapacity(held.Itemstack) - stored);
                if (move <= 0) return 0;
                StoredEmc.Set(held.Itemstack, stored + move);
                held.MarkDirty();
                return move;
            }
            if (held?.Empty == false) return 0;
            PlayerEmc.AddEmc(player, amount);
            return amount;
        }
    }

    /// <summary>Interdiction Torch: keeps hostile creatures away from it (radius from the <c>radius</c> attribute).</summary>
    public class BEInterdictionTorch : BlockEntity
    {
        float radius = 8;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            radius = Block?.Attributes?["radius"].AsFloat(8) ?? 8;
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(_ => Api.ModLoader.GetModSystem<VintageEModSystem>()?.Trinkets?.Repel(Pos.ToVec3d().Add(0.5, 0.5, 0.5), radius), 250);
        }
    }

    /// <summary>
    /// Nova Catalyst / Nova Cataclysm: sneak + right-click with an empty hand to light the fuse; a few seconds later
    /// it explodes, dropping what it breaks. Attributes: <c>radius</c>, <c>fuseSeconds</c>.
    /// </summary>
    public class BENovaExplosive : BlockEntity, IEmcInteractable
    {
        bool lit;
        double fuseLeft;
        string litBy;
        long listener;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server && lit) StartTicking();
        }

        void StartTicking()
        {
            if (listener == 0) listener = RegisterGameTickListener(OnTick, 100);
        }

        public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!byPlayer.Entity.Controls.Sneak || byPlayer.InventoryManager.ActiveHotbarSlot?.Empty == false) return false;
            if (Api.Side == EnumAppSide.Client || lit) return true;
            if (!Api.World.Claims.TryAccess(byPlayer, Pos, EnumBlockAccessFlags.BuildOrBreak)) return true;
            lit = true;
            litBy = byPlayer.PlayerUID;
            fuseLeft = Block?.Attributes?["fuseSeconds"].AsDouble(4) ?? 4;
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/fuse"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, false, 24, 1f);
            StartTicking();
            MarkDirty(false);
            return true;
        }

        void OnTick(float dt)
        {
            if (!lit) return;
            fuseLeft -= dt;
            Api.World.SpawnParticles(4, ColorUtil.ToRgba(255, 255, 160, 40), Pos.ToVec3d().Add(0.3, 1, 0.3), Pos.ToVec3d().Add(0.7, 1.2, 0.7),
                new Vec3f(-0.3f, 0.5f, -0.3f), new Vec3f(0.3f, 1.2f, 0.3f), 0.6f, -0.1f, 0.4f);
            if (fuseLeft > 0) return;
            lit = false;
            float radius = Block?.Attributes?["radius"].AsFloat(6) ?? 6;
            BlockPos at = Pos.Copy();
            Api.World.BlockAccessor.SetBlock(0, at);
            ((IServerWorldAccessor)Api.World).CreateExplosion(at, EnumBlastType.RockBlast, radius, radius + 2, 1f, litBy);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBool("lit", lit);
            tree.SetDouble("fuse", fuseLeft);
            if (litBy != null) tree.SetString("litBy", litBy);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            lit = tree.GetBool("lit");
            fuseLeft = tree.GetDouble("fuse");
            litBy = tree.GetString("litBy");
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            if (lit) dsc.AppendLine(Lang.Get("vintagee:nova-lit", Math.Max(0, Math.Ceiling(fuseLeft))));
        }
    }

    /// <summary>
    /// Dark Matter Pedestal: right-click with a ring, stone, amulet or talisman to set it on the pedestal (empty hand
    /// takes it back). It then works on the area around the pedestal for free: the Harvest Goddess Band and Watch
    /// grow crops, the Ignition Ring burns hostile creatures, the Zero Ring freezes water, the Black Hole Band pulls
    /// in loose items and drops them into a container under the pedestal, Swiftwolf's Rending Gale strikes hostile
    /// creatures, the Soul/Body/Life Stones heal and feed nearby players and the Repair Talisman mends their gear.
    /// </summary>
    public class BEPedestal : BlockEntity, IEmcInteractable
    {
        ItemStack held;
        float acc;

        public ItemStack Held => held;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            held?.ResolveBlockOrItem(api.World);
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnServerTick, 500);
        }

        public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (Api.Side == EnumAppSide.Client) return true;
            if (!Api.World.Claims.TryAccess(byPlayer, Pos, EnumBlockAccessFlags.Use)) return true;
            if (held == null && hand?.Itemstack?.Collectible is ItemEmcTrinket t && t.IsToggleable)
            {
                held = hand.TakeOut(1);
                hand.MarkDirty();
            }
            else if (held != null && (hand == null || hand.Empty))
            {
                if (!byPlayer.InventoryManager.TryGiveItemstack(held, true)) Api.World.SpawnItemEntity(held, Pos.ToVec3d().Add(0.5, 1.1, 0.5));
                held = null;
            }
            else return true;
            UpdateBlockState();
            MarkDirty(true);
            return true;
        }

        void UpdateBlockState()
        {
            Block want = Api.World.GetBlock(Block.CodeWithVariant("state", held == null ? "empty" : "active"));
            if (want != null && want.Id != Block.Id) Api.World.BlockAccessor.ExchangeBlock(want.Id, Pos);
        }

        void OnServerTick(float dt)
        {
            if (!(held?.Collectible is ItemEmcTrinket t)) return;
            TrinketEffects fx = Api.ModLoader.GetModSystem<VintageEModSystem>()?.Trinkets;
            if (fx == null) return;
            acc += dt;
            bool everySecond = acc >= 1;
            if (everySecond) acc = 0;
            Vec3d center = Pos.ToVec3d().Add(0.5, 0.5, 0.5);
            string effect = TrinketEffects.EffectOf(t, held);

            switch (effect)
            {
                case "harvestgoddessband": if (everySecond) fx.Grow(Pos, 8, 10, null, 0); break;
                case "watchofflowingtime": if (everySecond) fx.Grow(Pos, 12, 24, null, 0); break;
                case "ignitionring": if (everySecond) fx.Burn(center, 8, null, null, 0); break;
                case "zeroring": fx.Freeze(Pos.UpCopy(), 6, null); break;
                case "blackholeband":
                    fx.Attract(center.AddCopy(0, 0.6, 0), 10);
                    Collect(center);
                    break;
                case "swiftwolfsrendinggale":
                    if (everySecond)
                    {
                        foreach (Entity e in Api.World.GetEntitiesAround(center, 10, 6, fx.IsHostile))
                        {
                            e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Block, Type = EnumDamageType.PiercingAttack }, 4f);
                        }
                    }
                    break;
                case "soulstone":
                case "bodystone":
                case "lifestone":
                case "repairtalisman":
                    if (everySecond) HelpPlayers(effect, center);
                    break;
            }
        }

        void Collect(Vec3d center)
        {
            if (!(Api.World.BlockAccessor.GetBlockEntity(Pos.DownCopy()) is IBlockEntityContainer container)) return;
            foreach (Entity e in Api.World.GetEntitiesAround(center.AddCopy(0, 0.6, 0), 1.5f, 1.5f, x => x is EntityItem && x.Alive))
            {
                var ei = (EntityItem)e;
                if (ei.Itemstack == null) continue;
                var source = new DummySlot(ei.Itemstack);
                foreach (ItemSlot slot in container.Inventory)
                {
                    if (source.Empty) break;
                    source.TryPutInto(Api.World, slot, source.StackSize);
                }
                if (source.Empty) e.Die(EnumDespawnReason.PickedUp);
                else ei.Itemstack = source.Itemstack;
            }
        }

        void HelpPlayers(string effect, Vec3d center)
        {
            foreach (IPlayer p in Api.World.GetPlayersAround(center, 6, 4))
            {
                if (!(p.Entity is EntityPlayer ep) || !ep.Alive) continue;
                if (effect == "soulstone" || effect == "lifestone")
                {
                    ITreeAttribute health = ep.WatchedAttributes.GetTreeAttribute("health");
                    if (health != null && health.GetFloat("currenthealth") < health.GetFloat("maxhealth"))
                    {
                        ep.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Heal }, 0.5f);
                    }
                }
                if (effect == "bodystone" || effect == "lifestone")
                {
                    ITreeAttribute hunger = ep.WatchedAttributes.GetTreeAttribute("hunger");
                    if (hunger != null)
                    {
                        float cur = hunger.GetFloat("currentsaturation"), max = hunger.GetFloat("maxsaturation");
                        if (max > 0 && cur < max)
                        {
                            hunger.SetFloat("currentsaturation", Math.Min(max, cur + 25));
                            ep.WatchedAttributes.MarkPathDirty("hunger");
                        }
                    }
                }
                if (effect == "repairtalisman")
                {
                    foreach (ItemSlot slot in EmcWallet.CarriedSlots(p))
                    {
                        CollectibleObject c = slot.Itemstack.Collectible;
                        int max = c.GetMaxDurability(slot.Itemstack);
                        int rem = c.GetRemainingDurability(slot.Itemstack);
                        if (max > 0 && rem > 0 && rem < max) { c.SetDurability(slot.Itemstack, rem + 1); slot.MarkDirty(); }
                    }
                }
            }
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            if (held != null && Api.Side == EnumAppSide.Server) Api.World.SpawnItemEntity(held, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            held = null;
            base.OnBlockBroken(byPlayer);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (held != null) tree.SetItemstack("held", held);
            else tree.RemoveAttribute("held");
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            held = tree.GetItemstack("held");
            held?.ResolveBlockOrItem(worldAccessForResolve);
        }

        public override void OnStoreCollectibleMappings(System.Collections.Generic.Dictionary<int, AssetLocation> blockIdMapping, System.Collections.Generic.Dictionary<int, AssetLocation> itemIdMapping)
        {
            held?.Collectible?.OnStoreCollectibleMappings(Api.World, new DummySlot(held), blockIdMapping, itemIdMapping);
        }

        public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, System.Collections.Generic.Dictionary<int, AssetLocation> oldBlockIdMapping, System.Collections.Generic.Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
        {
            if (held != null && !held.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve)) held = null;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(held == null ? Lang.Get("vintagee:pedestal-empty") : Lang.Get("vintagee:pedestal-holding", held.GetName()));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace VintageE
{
    /// <summary>
    /// What the rings, stones, amulets, talisman and watch do, run on the server twice a second for every player
    /// carrying an active one (and by pedestals for a fixed spot). Costs are paid with <see cref="EmcWallet"/>.
    /// </summary>
    public class TrinketEffects
    {
        public const string GaleStatCode = "vintageegale";
        const float Interval = 0.5f;

        readonly VintageEModSystem mod;
        readonly ICoreServerAPI sapi;
        readonly Dictionary<string, Dictionary<string, float>> timers = new Dictionary<string, Dictionary<string, float>>();
        readonly HashSet<string> galeOn = new HashSet<string>();
        readonly Random rand = new Random();
        AssetLocation[] hostile;

        /// <summary>Name of this mod's time speed modifier on the world calendar (the Watch of Flowing Time).</summary>
        public const string TimeModifierName = "vintageewatch";
        /// <summary>The calendar modifier currently applied (0 = none). Mirrored to clients.</summary>
        public float WatchTimeModifier { get; private set; }
        int watchFaster, watchSlower; // players with an active watch this tick, by mode

        public TrinketEffects(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            this.mod = mod;
            this.sapi = sapi;
            sapi.Event.RegisterGameTickListener(OnTick, (int)(Interval * 1000));
            sapi.Event.PlayerDisconnect += p =>
            {
                galeOn.Remove(p.PlayerUID);
                timers.Remove(p.PlayerUID);
            };
        }

        double Cost(double baseCost) => baseCost * mod.Config.TrinketCostMultiplier;

        public bool IsHostile(Entity e)
        {
            if (e == null || !e.Alive || e is EntityPlayer || !(e is EntityAgent) || e.Code == null) return false;
            hostile ??= mod.Config.HostileCreatures.Select(p => new AssetLocation(p)).ToArray();
            foreach (AssetLocation pattern in hostile)
            {
                if (WildcardUtil.Match(pattern, e.Code)) return true;
            }
            return false;
        }

        /// <summary>True once per <paramref name="seconds"/> for this player and key.</summary>
        bool Every(string uid, string key, float seconds)
        {
            if (!timers.TryGetValue(uid, out var t)) timers[uid] = t = new Dictionary<string, float>();
            t.TryGetValue(key, out float acc);
            acc += Interval;
            if (acc + 1e-4 < seconds) { t[key] = acc; return false; }
            t[key] = 0;
            return true;
        }

        void OnTick(float dt)
        {
            watchFaster = watchSlower = 0;
            foreach (IPlayer p in sapi.World.AllOnlinePlayers)
            {
                if (!(p is IServerPlayer player) || player.ConnectionState != EnumClientState.Playing || player.Entity == null || !player.Entity.Alive) continue;
                try { TickPlayer(player); }
                catch (Exception e) { sapi.Logger.Warning("Vintage-E trinkets for {0}: {1}", player.PlayerName, e.Message); }
            }
            UpdateWorldTime();
        }

        /// <summary>
        /// The Watch of Flowing Time changes how fast time passes for the whole world (like sleeping does), as long as
        /// someone has one switched on and can pay for it. Speeding up wins over slowing down.
        /// </summary>
        void UpdateWorldTime()
        {
            float want = 0;
            if (mod.Config.WatchChangesWorldTime)
            {
                // The game's base is 60 "time speed"; the modifier is added to it.
                if (watchFaster > 0) want = 60f * Math.Max(0, (float)mod.Config.WatchSpeedUp - 1);
                else if (watchSlower > 0) want = -60f * (1 - 1 / Math.Max(1, (float)mod.Config.WatchSlowDown));
            }
            if (Math.Abs(want - WatchTimeModifier) < 0.01f) return;
            WatchTimeModifier = want;
            ApplyTimeModifier(sapi.World.Calendar, want);
            mod.BroadcastWatchSpeed(want);
        }

        public static void ApplyTimeModifier(IGameCalendar calendar, float value)
        {
            if (calendar == null) return;
            if (Math.Abs(value) < 0.01f) calendar.RemoveTimeSpeedModifier(TimeModifierName);
            else calendar.SetTimeSpeedModifier(TimeModifierName, value);
        }

        public static IEnumerable<(ItemSlot slot, ItemEmcTrinket trinket)> ActiveTrinkets(IPlayer player)
        {
            foreach (ItemSlot slot in EmcWallet.CarriedSlots(player))
            {
                if (slot.Itemstack.Collectible is ItemEmcTrinket t && t.IsToggleable && ItemEmcTrinket.IsActive(slot.Itemstack)) yield return (slot, t);
            }
        }

        /// <summary>The ring each Ring of Arcana effect copies, in <see cref="ItemEmcTrinket.ArcanaModes"/> order.</summary>
        static readonly string[] ArcanaRings = { "zeroring", "ignitionring", "harvestgoddessband", "swiftwolfsrendinggale" };

        /// <summary>
        /// The effects of an active trinket: its own kind, or for the Ring of Arcana every one of its four ring
        /// effects that is switched on.
        /// </summary>
        public static IEnumerable<string> EffectsOf(ItemEmcTrinket t, ItemStack stack)
        {
            if (t.Kind != "ringofarcana")
            {
                yield return t.Kind;
                yield break;
            }
            for (int i = 0; i < ArcanaRings.Length; i++)
            {
                if (ItemEmcTrinket.ArcanaEnabled(stack, i)) yield return ArcanaRings[i];
            }
        }

        public static bool HasActiveEffect(IPlayer player, string effect)
        {
            foreach (var (slot, t) in ActiveTrinkets(player))
            {
                if (EffectsOf(t, slot.Itemstack).Contains(effect)) return true;
            }
            return false;
        }

        void TickPlayer(IServerPlayer player)
        {
            string uid = player.PlayerUID;
            EntityPlayer entity = player.Entity;
            bool gale = false;
            var done = new HashSet<string>(); // the same effect twice doesn't stack

            foreach (var (slot, effect) in ActiveTrinkets(player).SelectMany(a => EffectsOf(a.trinket, a.slot.Itemstack).Select(e => (a.slot, e))).ToList())
            {
                if (!done.Add(effect + (effect == "gemofeternaldensity" ? slot.GetHashCode().ToString() : ""))) continue;
                switch (effect)
                {
                    case "swiftwolfsrendinggale":
                        gale = EmcWallet.TryPay(player, Cost(0.5 * Interval));
                        break;
                    case "blackholeband":
                        Attract(entity.ServerPos.XYZ.Add(0, 0.5, 0), 8);
                        break;
                    case "harvestgoddessband":
                        if (Every(uid, effect, 1)) Grow(entity.ServerPos.AsBlockPos, 5, 6, player, Cost(4));
                        break;
                    case "watchofflowingtime":
                        bool faster = ItemEmcTrinket.GetMode(slot.Itemstack) == 0;
                        if (!EmcWallet.TryPay(player, Cost((faster ? 8 : 4) * Interval))) break;
                        if (faster)
                        {
                            watchFaster++;
                            if (Every(uid, effect, 1)) Grow(entity.ServerPos.AsBlockPos, 6, 4, null, 0);
                        }
                        else
                        {
                            watchSlower++;
                            Slow(entity.ServerPos.XYZ, 10);
                        }
                        break;
                    case "ignitionring":
                        if (Every(uid, effect, 1)) Burn(entity.ServerPos.XYZ, 5, entity, player, Cost(4));
                        break;
                    case "zeroring":
                        Freeze(entity.ServerPos.AsBlockPos, 3, player);
                        break;
                    case "bodystone":
                        if (Every(uid, effect, 2)) Feed(player);
                        break;
                    case "soulstone":
                        if (Every(uid, effect, 1)) Heal(player);
                        break;
                    case "lifestone":
                        if (Every(uid, "life-feed", 2)) Feed(player);
                        if (Every(uid, "life-heal", 1)) Heal(player);
                        break;
                    case "repairtalisman":
                        if (Every(uid, effect, 1)) Repair(player);
                        break;
                    case "gemofeternaldensity":
                        if (Every(uid, effect, 2)) Condense(player, slot);
                        break;
                }
            }
            SetGale(player, gale);
        }

        public void OnDeactivated(IServerPlayer player)
        {
            if (player != null && !HasActiveEffect(player, "swiftwolfsrendinggale")) SetGale(player, false);
        }

        void SetGale(IPlayer player, bool on)
        {
            EntityPlayer e = player.Entity;
            if (e?.Stats == null) return;
            if (on && galeOn.Add(player.PlayerUID)) e.Stats.Set("walkspeed", GaleStatCode, 0.4f, false);
            else if (!on && galeOn.Remove(player.PlayerUID)) e.Stats.Remove("walkspeed", GaleStatCode);
        }

        // ------------------------------------------------------------------ effects (also used by pedestals)

        /// <summary>Pulls loose items towards <paramref name="center"/>.</summary>
        public void Attract(Vec3d center, float radius)
        {
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius, x => x is EntityItem && x.Alive))
            {
                Vec3d dir = center.SubCopy(e.ServerPos.XYZ);
                double dist = dir.Length();
                if (dist < 0.75) continue;
                dir.Mul(Math.Min(0.35, 0.08 + dist * 0.04) / dist);
                e.ServerPos.Motion.Set(dir.X, dir.Y + 0.04, dir.Z);
            }
        }

        /// <summary>Advances random crops around <paramref name="center"/> by one growth stage.</summary>
        public int Grow(BlockPos center, int radius, int attempts, IServerPlayer payer, double costEach)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            int grown = 0;
            for (int i = 0; i < attempts; i++)
            {
                BlockPos pos = center.AddCopy(rand.Next(-radius, radius + 1), rand.Next(-2, 3), rand.Next(-radius, radius + 1));
                Block block = ba.GetBlock(pos);
                Block next = NextCropStage(block);
                if (next == null) continue;
                if (payer != null && !EmcWallet.TryPay(payer, costEach)) break;
                ba.ExchangeBlock(next.BlockId, pos);
                grown++;
            }
            return grown;
        }

        Block NextCropStage(Block block)
        {
            if (block?.Code == null || !block.Code.Path.StartsWith("crop-", StringComparison.Ordinal)) return null;
            string path = block.Code.Path;
            int dash = path.LastIndexOf('-');
            if (dash < 0 || !int.TryParse(path.Substring(dash + 1), out int stage)) return null;
            Block next = sapi.World.GetBlock(new AssetLocation(block.Code.Domain, path.Substring(0, dash + 1) + (stage + 1)));
            return next == null || next.Id == 0 ? null : next;
        }

        /// <summary>Sets nearby hostile creatures on fire. <paramref name="source"/> may be null (pedestal).</summary>
        public int Burn(Vec3d center, float radius, Entity source, IServerPlayer payer, double costEach)
        {
            int hit = 0;
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius / 2, IsHostile))
            {
                if (payer != null && !EmcWallet.TryPay(payer, costEach)) break;
                e.ReceiveDamage(new DamageSource
                {
                    Source = source != null ? EnumDamageSource.Player : EnumDamageSource.Block,
                    SourceEntity = source,
                    CauseEntity = source,
                    Type = EnumDamageType.Fire
                }, 3f);
                e.Ignite();
                hit++;
            }
            return hit;
        }

        /// <summary>Turns still water around and below <paramref name="center"/> into ice.</summary>
        public int Freeze(BlockPos center, int radius, IServerPlayer byPlayer)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            Block ice = sapi.World.GetBlock(new AssetLocation("game:lakeice"));
            if (ice == null || ice.Id == 0) return 0;
            int frozen = 0;
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (dx * dx + dz * dz > radius * radius) continue;
                    for (int dy = -2; dy <= 0; dy++)
                    {
                        BlockPos pos = center.AddCopy(dx, dy, dz);
                        Block fluid = ba.GetBlock(pos, BlockLayersAccess.Fluid);
                        if (fluid?.Code == null || !fluid.Code.Path.StartsWith("water-still", StringComparison.Ordinal)) continue;
                        if (ba.GetBlock(pos, BlockLayersAccess.Solid)?.Id > 0) continue;
                        if (byPlayer != null && !sapi.World.Claims.TryAccess(byPlayer, pos, EnumBlockAccessFlags.BuildOrBreak)) continue;
                        if (!ice.ForFluidsLayer) ba.SetBlock(0, pos, BlockLayersAccess.Fluid);
                        ba.SetBlock(ice.BlockId, pos);
                        frozen++;
                    }
                }
            }
            return frozen;
        }

        /// <summary>Damps the movement of hostile creatures around <paramref name="center"/> (watch in slow mode).</summary>
        public void Slow(Vec3d center, float radius)
        {
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius / 2, IsHostile))
            {
                e.ServerPos.Motion.Mul(0.4, 1, 0.4);
            }
        }

        /// <summary>Pushes hostile creatures away from <paramref name="center"/> (interdiction torch).</summary>
        public void Repel(Vec3d center, float radius)
        {
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius, IsHostile))
            {
                Vec3d dir = e.ServerPos.XYZ.SubCopy(center);
                dir.Y = 0;
                double dist = Math.Max(0.2, dir.Length());
                dir.Mul(0.25 / dist);
                e.ServerPos.Motion.Add(dir.X, 0.05, dir.Z);
            }
        }

        void Feed(IServerPlayer player)
        {
            ITreeAttribute hunger = player.Entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (hunger == null) return;
            float cur = hunger.GetFloat("currentsaturation"), max = hunger.GetFloat("maxsaturation");
            if (max <= 0 || cur > max - 100) return;
            float add = Math.Min(200, max - cur);
            if (!EmcWallet.TryPay(player, Cost(add * 0.5))) return;
            hunger.SetFloat("currentsaturation", cur + add);
            player.Entity.WatchedAttributes.MarkPathDirty("hunger");
        }

        void Heal(IServerPlayer player)
        {
            ITreeAttribute health = player.Entity.WatchedAttributes.GetTreeAttribute("health");
            if (health == null) return;
            float cur = health.GetFloat("currenthealth"), max = health.GetFloat("maxhealth");
            if (max <= 0 || cur >= max) return;
            if (!EmcWallet.TryPay(player, Cost(64))) return;
            player.Entity.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Heal }, 1f);
        }

        void Repair(IServerPlayer player)
        {
            foreach (ItemSlot slot in EmcWallet.CarriedSlots(player))
            {
                ItemStack stack = slot.Itemstack;
                CollectibleObject c = stack.Collectible;
                int max = c.GetMaxDurability(stack);
                if (max <= 0) continue;
                int remaining = c.GetRemainingDurability(stack);
                if (remaining <= 0 || remaining >= max) continue;
                c.SetDurability(stack, remaining + 1);
                slot.MarkDirty();
            }
        }

        /// <summary>
        /// Gem of Eternal Density: burns cheap junk you carry (anything worth 8 EMC or less — stone, soil, sand,
        /// gravel, sticks...) and turns it into the selected target (iron, gold, diamond, dark or red matter).
        /// </summary>
        void Condense(IServerPlayer player, ItemSlot gemSlot)
        {
            int mode = GameMath.Clamp(ItemEmcTrinket.GetMode(gemSlot.Itemstack), 0, ItemEmcTrinket.DensityTargets.Length - 1);
            CollectibleObject target = ItemEmcTrinket.Resolve(sapi.World, ItemEmcTrinket.DensityTargets[mode]);
            double targetValue = target == null ? 0 : mod.Emc.GetValue(target);
            if (targetValue <= 0) return;
            double maxInput = gemSlot.Itemstack.Collectible.Attributes?["maxInputValue"].AsDouble(8) ?? 8;

            double stored = StoredEmc.Get(gemSlot.Itemstack);
            var inv = player.InventoryManager;
            foreach (string name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            {
                IInventory own = inv.GetOwnInventory(name);
                if (own == null) continue;
                foreach (ItemSlot slot in own)
                {
                    ItemStack stack = slot?.Itemstack;
                    if (stack == null || slot == gemSlot || slot is ItemSlotBackpack) continue;
                    CollectibleObject c = stack.Collectible;
                    if (c == target || c is ItemEmcTrinket || c is IEmcStorageItem || c.GetMaxDurability(stack) > 0) continue;
                    if (StackContents.HasNestedStacks(stack)) continue;
                    double value = mod.Emc.GetValue(stack);
                    if (value <= 0 || value > maxInput) continue;
                    stored += value * stack.StackSize;
                    slot.Itemstack = null;
                    slot.MarkDirty();
                }
            }

            int made = 0;
            while (stored >= targetValue && made < 64)
            {
                stored -= targetValue;
                var stack = new ItemStack(target);
                if (!inv.TryGiveItemstack(stack, true)) sapi.World.SpawnItemEntity(stack, player.Entity.ServerPos.XYZ);
                made++;
            }
            StoredEmc.Set(gemSlot.Itemstack, stored);
            gemSlot.MarkDirty();
        }

        /// <summary>Places a water or lava source block at <paramref name="pos"/> if nothing solid is in the way. Returns true if placed.</summary>
        public static bool PlaceLiquidAt(IWorldAccessor world, BlockPos pos, bool lava)
        {
            IBlockAccessor ba = world.BlockAccessor;
            Block solid = ba.GetBlock(pos, BlockLayersAccess.Solid);
            if (solid != null && solid.Id != 0 && solid.Replaceable < 6000) return false;
            Block liquid = world.GetBlock(new AssetLocation(lava ? "game:lava-still-7" : "game:water-still-7"));
            if (liquid == null || liquid.Id == 0) return false;
            if (solid != null && solid.Id != 0) ba.SetBlock(0, pos, BlockLayersAccess.Solid);
            ba.SetBlock(liquid.BlockId, pos);
            ba.TriggerNeighbourBlockUpdate(pos);
            world.PlaySoundAt(new AssetLocation(lava ? "game:sounds/effect/extinguish" : "game:sounds/environment/waterfall"), pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 12, 0.6f);
            return true;
        }

        /// <summary>Evertide (water, free) and Volcanite (lava, costs EMC) amulets: places a source block where you click.</summary>
        public void PlaceLiquid(ItemEmcTrinket amulet, IPlayer player, BlockSelection blockSel)
        {
            bool lava = amulet.Kind == "volcaniteamulet";
            IBlockAccessor ba = sapi.World.BlockAccessor;
            BlockPos pos = blockSel.Position.AddCopy(blockSel.Face);
            if (!sapi.World.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) return;
            Block solid = ba.GetBlock(pos, BlockLayersAccess.Solid);
            if (solid != null && solid.Id != 0 && solid.Replaceable < 6000) return;
            Block liquid = sapi.World.GetBlock(new AssetLocation(lava ? "game:lava-still-7" : "game:water-still-7"));
            if (liquid == null || liquid.Id == 0) return;
            if (lava && !EmcWallet.TryPay(player, Cost(32))) return;
            if (solid != null && solid.Id != 0) ba.SetBlock(0, pos, BlockLayersAccess.Solid);
            ba.SetBlock(liquid.BlockId, pos);
            ba.TriggerNeighbourBlockUpdate(pos);
            sapi.World.PlaySoundAt(new AssetLocation(lava ? "game:sounds/effect/extinguish" : "game:sounds/environment/waterfall"), pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 12, 0.6f);
        }
    }
}

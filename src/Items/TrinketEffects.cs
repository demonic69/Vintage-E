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
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// What the rings, stones, amulets, talisman and watch do, run on the server twice a second for every player
    /// carrying an active one (and by pedestals for a fixed spot). Costs are paid with <see cref="EmcWallet"/>;
    /// the Ring of Arcana is free, as in ProjectE.
    /// </summary>
    public class TrinketEffects
    {
        /// <summary>The walk speed bonus older versions gave Swiftwolf's Rending Gale; removed from players on sight.</summary>
        public const string GaleStatCode = "vintageegale";
        /// <summary>On the player entity: flight was switched on by a ring (so only we switch it off again).</summary>
        public const string FlightKey = "vintageeFlight";
        const float Interval = 0.5f;

        // ProjectE: 0.32 EMC a tick each for flying and for the shield.
        public const double FlightCostPerSecond = 6.4, ShieldCostPerSecond = 6.4;
        public const float ShieldRadius = 5;
        /// <summary>Seconds after ring flight ends during which falling doesn't hurt.</summary>
        public const double FlightGraceSeconds = 15;

        readonly VintageEModSystem mod;
        readonly ICoreServerAPI sapi;
        readonly Dictionary<string, Dictionary<string, float>> timers = new Dictionary<string, Dictionary<string, float>>();
        readonly Dictionary<string, long> flightEndedMs = new Dictionary<string, long>();
        readonly Dictionary<string, long> lastTeleportMs = new Dictionary<string, long>();
        readonly HashSet<string> statsCleaned = new HashSet<string>();
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
                timers.Remove(p.PlayerUID);
                statsCleaned.Remove(p.PlayerUID);
            };
        }

        double Cost(double baseCost) => baseCost * mod.Config.TrinketCostMultiplier;

        /// <summary>Pays <paramref name="baseCost"/> × the cost multiplier, or tells the player they can't.</summary>
        public bool Pay(IServerPlayer player, double baseCost)
        {
            double cost = Cost(baseCost);
            if (EmcWallet.TryPay(player, cost)) return true;
            ItemEmcGadget.Tell(player, Lang.GetL(player.LanguageCode, "vintagee:error-notenoughemc", EmcFormat.Format(cost), EmcFormat.Format(EmcWallet.Balance(player))));
            return false;
        }

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

        public static IEnumerable<(ItemSlot slot, ItemEmcTrinket trinket)> CarriedTrinkets(IPlayer player)
        {
            foreach (ItemSlot slot in EmcWallet.CarriedSlots(player))
            {
                if (slot.Itemstack.Collectible is ItemEmcTrinket t) yield return (slot, t);
            }
        }

        /// <summary>Trinkets that are working: switched-on ones and the always-on talisman.</summary>
        public static IEnumerable<(ItemSlot slot, ItemEmcTrinket trinket)> ActiveTrinkets(IPlayer player)
        {
            foreach (var (slot, t) in CarriedTrinkets(player))
            {
                if (t.IsAlwaysOn || (t.IsToggleable && ItemEmcTrinket.IsActive(slot.Itemstack))) yield return (slot, t);
            }
        }

        /// <summary>The ring each Ring of Arcana effect copies, in <see cref="ItemEmcTrinket.ArcanaModes"/> order.</summary>
        static readonly string[] ArcanaRings = { "zeroring", "ignitionring", "harvestgoddessband", "swiftwolfsrendinggale" };

        /// <summary>
        /// The effects of an active trinket: its own kind; for the Ring of Arcana every one of its four ring effects
        /// that is switched on; for the Void Ring the Gem of Eternal Density and the Black Hole Band.
        /// </summary>
        public static IEnumerable<string> EffectsOf(ItemEmcTrinket t, ItemStack stack)
        {
            if (t.Kind == "ringofarcana")
            {
                for (int i = 0; i < ArcanaRings.Length; i++)
                {
                    if (ItemEmcTrinket.ArcanaEnabled(stack, i)) yield return ArcanaRings[i];
                }
                yield break;
            }
            if (t.Kind == "voidring")
            {
                yield return "gemofeternaldensity";
                yield return "blackholeband";
                yield break;
            }
            yield return t.Kind;
        }

        public static bool HasActiveEffect(IPlayer player, string effect)
        {
            foreach (var (slot, t) in ActiveTrinkets(player))
            {
                if (EffectsOf(t, slot.Itemstack).Contains(effect)) return true;
            }
            return false;
        }

        /// <summary>ProjectE's fire protection: carrying an Ignition Ring, Volcanite Amulet or Ring of Arcana (on or off).</summary>
        public static bool IsFireProof(IPlayer player)
        {
            foreach (var (_, t) in CarriedTrinkets(player))
            {
                if (t.Kind == "ignitionring" || t.Kind == "volcaniteamulet" || t.Kind == "ringofarcana") return true;
            }
            return false;
        }

        /// <summary>Flying on a ring now, or stopped within the last <see cref="FlightGraceSeconds"/> (so the landing doesn't hurt).</summary>
        public bool FlightProtects(IPlayer player)
        {
            if (player?.Entity?.WatchedAttributes.GetBool(FlightKey) == true) return true;
            return flightEndedMs.TryGetValue(player?.PlayerUID ?? "", out long ms) && sapi.World.ElapsedMilliseconds - ms < FlightGraceSeconds * 1000;
        }

        void TickPlayer(IServerPlayer player)
        {
            string uid = player.PlayerUID;
            EntityPlayer entity = player.Entity;
            if (statsCleaned.Add(uid)) entity.Stats?.Remove("walkspeed", GaleStatCode);

            bool wantFlight = false, wantShield = false, flightFree = false, shieldFree = false;
            var paidGaleSlots = new List<ItemSlot>();
            var done = new HashSet<string>(); // the same effect twice doesn't stack

            foreach (var (slot, t) in ActiveTrinkets(player).ToList())
            {
                bool free = t.Kind == "ringofarcana"; // ProjectE's Arcana costs nothing
                foreach (string effect in EffectsOf(t, slot.Itemstack))
                {
                    if (effect == "swiftwolfsrendinggale")
                    {
                        int mode = free ? 0 : GameMath.Clamp(ItemEmcTrinket.GetMode(slot.Itemstack), 0, ItemEmcTrinket.GaleModes.Length - 1);
                        bool flight = mode != 2, shield = mode != 1;
                        wantFlight |= flight;
                        wantShield |= shield;
                        if (free) { flightFree |= flight; shieldFree |= shield; }
                        else paidGaleSlots.Add(slot);
                        continue;
                    }
                    string key = effect + (effect == "gemofeternaldensity" ? slot.GetHashCode().ToString() : "");
                    if (!done.Add(key)) continue;
                    IServerPlayer payer = free ? null : player;
                    switch (effect)
                    {
                        case "blackholeband":
                            Attract(entity.Pos.XYZ.Add(0, 0.5, 0), 7);
                            break;
                        case "harvestgoddessband":
                            if (Every(uid, effect, 1))
                            {
                                Grow(entity.Pos.AsBlockPos, 5, 6, payer, payer == null ? 0 : Cost(4));
                                Harvest(entity.Pos.AsBlockPos, 5, player);
                            }
                            break;
                        case "watchofflowingtime":
                            bool faster = ItemEmcTrinket.GetMode(slot.Itemstack) == 0;
                            if (!EmcWallet.TryPay(player, Cost((faster ? 8 : 4) * Interval))) break;
                            if (faster)
                            {
                                watchFaster++;
                                if (Every(uid, effect, 1)) Grow(entity.Pos.AsBlockPos, 6, 4, null, 0);
                            }
                            else
                            {
                                watchSlower++;
                                Slow(entity.Pos.XYZ, 10);
                            }
                            break;
                        case "ignitionring":
                            if (Every(uid, effect, 1)) Burn(entity.Pos.XYZ, 5, entity, payer, payer == null ? 0 : Cost(4));
                            break;
                        case "zeroring":
                            Freeze(entity.Pos.AsBlockPos, 3, player);
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
            }

            // ProjectE: a Harvest Goddess Band that is switched off still makes nearby crops grow, slowly and for free.
            if (!done.Contains("harvestgoddessband") && CarriedTrinkets(player).Any(c => c.trinket.Kind == "harvestgoddessband") && Every(uid, "harvest-passive", 2))
            {
                Grow(entity.Pos.AsBlockPos, 5, 3, null, 0);
            }

            // Swiftwolf's Rending Gale: shield and flight, paid for per second (the ring switches off when you can't pay).
            bool outOfEmc = false;
            if (wantShield)
            {
                if (shieldFree || EmcWallet.TryPay(player, Cost(ShieldCostPerSecond * Interval)))
                {
                    Repel(entity.Pos.XYZ, ShieldRadius);
                    RepelProjectiles(entity, ShieldRadius);
                }
                else outOfEmc = true;
            }
            bool airborne = !entity.OnGround && !entity.Swimming;
            if (wantFlight && !flightFree && airborne && player.WorldData.FreeMove && !EmcWallet.TryPay(player, Cost(FlightCostPerSecond * Interval)))
            {
                outOfEmc = true;
            }
            if (outOfEmc && !flightFree && !shieldFree)
            {
                foreach (ItemSlot s in paidGaleSlots)
                {
                    s.Itemstack.Attributes.SetBool(ItemEmcTrinket.ActiveKey, false);
                    s.MarkDirty();
                }
                ItemEmcGadget.Tell(player, Lang.GetL(player.LanguageCode, "vintagee:gale-outofemc"));
                wantFlight = false;
            }
            SetFlight(player, wantFlight);
        }

        /// <summary>Switches flying (the game's free move) on or off for a survival player. Only undoes flight it switched on.</summary>
        void SetFlight(IServerPlayer player, bool on)
        {
            IWorldPlayerData wd = player.WorldData;
            if (wd == null || player.Entity == null) return;
            if (wd.CurrentGameMode == EnumGameMode.Creative || wd.CurrentGameMode == EnumGameMode.Spectator) return;
            SyncedTreeAttribute attrs = player.Entity.WatchedAttributes;
            bool ours = attrs.GetBool(FlightKey);
            if (on)
            {
                if (!wd.FreeMove)
                {
                    wd.FreeMove = true;
                    attrs.SetBool(FlightKey, true);
                    player.BroadcastPlayerData();
                }
            }
            else if (ours)
            {
                attrs.RemoveAttribute(FlightKey);
                if (wd.FreeMove)
                {
                    wd.FreeMove = false;
                    player.BroadcastPlayerData();
                }
                flightEndedMs[player.PlayerUID] = sapi.World.ElapsedMilliseconds;
            }
        }

        // ------------------------------------------------------------------ effects (also used by pedestals)

        /// <summary>Pulls loose items towards <paramref name="center"/>.</summary>
        public void Attract(Vec3d center, float radius)
        {
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius, x => x is EntityItem && x.Alive))
            {
                Vec3d dir = center.SubCopy(e.Pos.XYZ);
                double dist = dir.Length();
                if (dist < 0.75) continue;
                dir.Mul(Math.Min(0.35, 0.08 + dist * 0.04) / dist);
                e.Pos.Motion.Set(dir.X, dir.Y + 0.04, dir.Z);
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

        static bool IsRipeCrop(Block block)
        {
            if (block?.CropProps == null || block.Code == null) return false;
            string path = block.Code.Path;
            int dash = path.LastIndexOf('-');
            return dash >= 0 && int.TryParse(path.Substring(dash + 1), out int stage) && stage >= block.CropProps.GrowthStages;
        }

        /// <summary>ProjectE's Harvest Goddess Band also harvests fully grown crops around you.</summary>
        public int Harvest(BlockPos center, int radius, IServerPlayer byPlayer)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            int harvested = 0;
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        BlockPos pos = center.AddCopy(dx, dy, dz);
                        if (!IsRipeCrop(ba.GetBlock(pos))) continue;
                        if (byPlayer != null && !sapi.World.Claims.TryAccess(byPlayer, pos, EnumBlockAccessFlags.BuildOrBreak)) continue;
                        ba.BreakBlock(pos, byPlayer);
                        harvested++;
                    }
                }
            }
            return harvested;
        }

        /// <summary>
        /// Harvest Goddess Band right-click: plants the seeds the player carries on empty farmland within
        /// <paramref name="radius"/> blocks of the clicked farmland. Returns how many were planted (0 = not farmland).
        /// </summary>
        public int PlantSeeds(IServerPlayer player, BlockSelection sel, int radius)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            if (!(ba.GetBlockEntity(sel.Position) is BlockEntityFarmland)) return 0;
            var seeds = new List<(ItemSlot slot, Block crop)>();
            foreach (ItemSlot slot in EmcWallet.CarriedSlots(player))
            {
                ItemStack stack = slot.Itemstack;
                if (stack?.Class != EnumItemClass.Item || !stack.Collectible.Code.Path.StartsWith("seeds-", StringComparison.Ordinal)) continue;
                Block crop = sapi.World.GetBlock(stack.Collectible.CodeWithPath("crop-" + stack.Collectible.LastCodePart() + "-1"));
                if (crop?.CropProps != null) seeds.Add((slot, crop));
            }
            if (seeds.Count == 0) return 0;

            int planted = 0;
            for (int dx = -radius; dx <= radius && seeds.Count > 0; dx++)
            {
                for (int dz = -radius; dz <= radius && seeds.Count > 0; dz++)
                {
                    BlockPos pos = sel.Position.AddCopy(dx, 0, dz);
                    if (!(ba.GetBlockEntity(pos) is BlockEntityFarmland farmland) || !farmland.CanPlant()) continue;
                    if (!sapi.World.Claims.TryAccess(player, pos.UpCopy(), EnumBlockAccessFlags.BuildOrBreak)) continue;
                    var (slot, crop) = seeds[0];
                    if (!farmland.TryPlant(crop, slot, player.Entity, new BlockSelection { Position = pos, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) })) continue;
                    slot.TakeOut(1);
                    slot.MarkDirty();
                    planted++;
                    if (slot.Empty) seeds.RemoveAt(0);
                }
            }
            if (planted > 0) sapi.World.PlaySoundAt(new AssetLocation("game:sounds/block/plant"), sel.Position.X + 0.5, sel.Position.Y + 1, sel.Position.Z + 0.5, null, true, 12, 0.8f);
            return planted;
        }

        /// <summary>Sets nearby hostile creatures on fire. <paramref name="payer"/> null = free (pedestal, Ring of Arcana).</summary>
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

        /// <summary>Puts out burning creatures around <paramref name="center"/> (Zero Ring on a pedestal).</summary>
        public void Extinguish(Vec3d center, float radius)
        {
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius, x => x.IsOnFire))
            {
                e.IsOnFire = false;
            }
        }

        /// <summary>Damps the movement of hostile creatures around <paramref name="center"/> (watch in slow mode).</summary>
        public void Slow(Vec3d center, float radius)
        {
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius / 2, IsHostile))
            {
                e.Pos.Motion.Mul(0.4, 1, 0.4);
            }
        }

        /// <summary>Pushes hostile creatures away from <paramref name="center"/> (interdiction torch, Swiftwolf's shield).</summary>
        public void Repel(Vec3d center, float radius)
        {
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius, IsHostile))
            {
                Vec3d dir = e.Pos.XYZ.SubCopy(center);
                dir.Y = 0;
                double dist = Math.Max(0.2, dir.Length());
                dir.Mul(0.25 / dist);
                e.Pos.Motion.Add(dir.X, 0.05, dir.Z);
            }
        }

        /// <summary>Swiftwolf's shield: turns arrows and other projectiles that someone else fired away from <paramref name="owner"/>.</summary>
        public void RepelProjectiles(Entity owner, float radius)
        {
            Vec3d center = owner.Pos.XYZ.Add(0, 1, 0);
            foreach (Entity e in sapi.World.GetEntitiesAround(center, radius, radius, x => x is IProjectile))
            {
                if (((IProjectile)e).FiredBy?.EntityId == owner.EntityId) continue;
                Vec3d away = e.Pos.XYZ.SubCopy(center);
                double dist = away.Length();
                double speed = Math.Max(0.2, e.Pos.Motion.Length());
                if (dist < 0.01) continue;
                away.Mul(speed / dist);
                e.Pos.Motion.Set(away.X, away.Y, away.Z);
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
        /// Gem of Eternal Density / Void Ring: burns cheap junk you carry (anything worth 8 EMC or less — stone,
        /// soil, sand, gravel, sticks...) and turns it into the selected target (iron, gold, diamond, dark or red matter).
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
                if (!inv.TryGiveItemstack(stack, true)) sapi.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
                made++;
            }
            StoredEmc.Set(gemSlot.Itemstack, stored);
            gemSlot.MarkDirty();
        }

        /// <summary>Black Hole Band / Void Ring right-click: removes a water or lava source block. Returns false if there is none.</summary>
        public bool RemoveLiquidSource(IServerPlayer player, BlockPos pos)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            Block fluid = ba.GetBlock(pos, BlockLayersAccess.Fluid);
            if (fluid == null || !fluid.IsLiquid() || fluid.LiquidLevel < 7) return false;
            if (!sapi.World.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) return true;
            ba.SetBlock(0, pos, BlockLayersAccess.Fluid);
            ba.TriggerNeighbourBlockUpdate(pos);
            sapi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/water-pour"), pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 12, 0.6f);
            return true;
        }

        /// <summary>Void Ring: teleports the player to the block they look at (or <paramref name="range"/>/2 ahead), at most twice a second.</summary>
        public void Teleport(IServerPlayer player, float range)
        {
            long now = sapi.World.ElapsedMilliseconds;
            if (lastTeleportMs.TryGetValue(player.PlayerUID, out long last) && now - last < 500) return;
            EntityPlayer e = player.Entity;
            Vec3d eye = e.Pos.XYZ.Add(0, e.LocalEyePos.Y, 0);
            BlockSelection blockSel = null;
            EntitySelection entitySel = null;
            sapi.World.RayTraceForSelection(eye, e.Pos.Pitch, e.Pos.Yaw, range, ref blockSel, ref entitySel);
            Vec3d target;
            if (blockSel != null)
            {
                BlockPos stand = blockSel.Position.AddCopy(blockSel.Face);
                target = new Vec3d(stand.X + 0.5, stand.Y, stand.Z + 0.5);
            }
            else
            {
                target = eye.AheadCopy(range / 2, e.Pos.Pitch, e.Pos.Yaw);
            }
            if (!sapi.World.Claims.TryAccess(player, target.AsBlockPos, EnumBlockAccessFlags.Use)) return;
            lastTeleportMs[player.PlayerUID] = now;
            e.TeleportToDouble(target.X, target.Y, target.Z);
            sapi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-breakdimension"), e, null, true, 16, 0.6f);
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

        /// <summary>Evertide (water, free) and Volcanite (lava, 32 EMC) amulets: places a source block where you click.</summary>
        public void PlaceLiquid(ItemEmcTrinket amulet, IServerPlayer player, BlockSelection blockSel)
        {
            bool lava = amulet.Kind == "volcaniteamulet";
            BlockPos pos = blockSel.Position.AddCopy(blockSel.Face);
            if (!sapi.World.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) return;
            Block solid = sapi.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid);
            if (solid != null && solid.Id != 0 && solid.Replaceable < 6000) return;
            if (lava && !Pay(player, ItemEmcTrinket.LavaCost)) return;
            PlaceLiquidAt(sapi.World, pos, lava);
        }
    }
}

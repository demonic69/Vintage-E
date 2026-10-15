using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Entry point. Registers every class, owns the EMC table for its side, and wires up networking.
    /// One instance exists per side (server and client each get their own), so nothing here is static.
    /// </summary>
    public class VintageEModSystem : ModSystem
    {
        public const string ModId = "vintagee";
        public const string ChannelName = "vintagee";
        public const string ConfigFile = "vintagee.json";

        public ICoreAPI Api { get; private set; }
        public ICoreServerAPI Sapi { get; private set; }
        public ICoreClientAPI Capi { get; private set; }

        /// <summary>EMC value of every collectible on this side. Server computes it, client receives it.</summary>
        public EmcRegistry Emc { get; } = new EmcRegistry();

        /// <summary>Server-side settings from ModConfig/vintagee.json.</summary>
        public VintageEConfig Config { get; private set; } = new VintageEConfig();

        /// <summary>Base values, blacklist and world-transmutation rules merged from every mod's config/vintagee-emc.json asset.</summary>
        public EmcRuleSet Rules { get; private set; } = new EmcRuleSet();

        public TransmutationService Transmutation { get; private set; }

        /// <summary>Runs ring/stone/amulet effects (server only).</summary>
        public TrinketEffects Trinkets { get; private set; }

        /// <summary>Result of the most recent mapping run (server only).</summary>
        public EmcMappingReport LastReport { get; private set; }
        WorldTransmutation worldTransmutation;
        /// <summary>Server-side philosopher's stone rules, built on first use from the merged rule files.</summary>
        public WorldTransmutation WorldTransmutation => worldTransmutation ??= new WorldTransmutation(Api, Rules.WorldTransmutation);

        /// <summary>Raised on either side whenever a fresh EMC table is available.</summary>
        public event Action OnEmcTableUpdated;

        IServerNetworkChannel serverChannel;
        IClientNetworkChannel clientChannel;
        GuiDialogTransmutation transmutationDialog;

        /// <summary>Hotkey that fires the Hyperkinetic/Catalytic Lens charge (R by default, like ProjectE).</summary>
        public const string FireHotkey = "vintageefireprojectile";

        public override double ExecuteOrder() => 1.0; // after the survival mod has registered its recipe registries

        public override void Start(ICoreAPI api)
        {
            Api = api;

            api.RegisterItemClass("ItemPhilosophersStone", typeof(ItemPhilosophersStone));
            api.RegisterItemClass("ItemTransmutationTablet", typeof(ItemTransmutationTablet));
            api.RegisterBlockClass("BlockTransmutationTable", typeof(BlockTransmutationTable));
            api.RegisterBlockClass("BlockEmcMachine", typeof(BlockEmcMachine));
            api.RegisterBlockEntityClass("VintageEEnergyCollector", typeof(BEEnergyCollector));
            api.RegisterBlockEntityClass("VintageEEnergyCondenser", typeof(BEEnergyCondenser));
            api.RegisterCollectibleBehaviorClass("VintageEMatterTool", typeof(CollectibleBehaviorMatterTool));
            api.RegisterItemClass("ItemKleinStar", typeof(ItemKleinStar));
            api.RegisterItemClass("ItemEmcTrinket", typeof(ItemEmcTrinket));
            api.RegisterItemClass("ItemEmcGadget", typeof(ItemEmcGadget));
            api.RegisterBlockEntityClass("VintageEAlchemicalChest", typeof(BEAlchemicalChest));
            api.RegisterBlockEntityClass("VintageEMatterFurnace", typeof(BEMatterFurnaceLegacy)); // 1.4.0 furnaces convert themselves
            api.RegisterBlockEntityClass("VintageEFurnace", typeof(BEMatterFurnace));
            api.RegisterBlockEntityClass("VintageEAntimatterRelay", typeof(BEAntimatterRelay));
            api.RegisterBlockEntityClass("VintageEInterdictionTorch", typeof(BEInterdictionTorch));
            api.RegisterBlockEntityClass("VintageENovaExplosive", typeof(BENovaExplosive));
            api.RegisterBlockEntityClass("VintageEPedestal", typeof(BEPedestal));
            api.RegisterEntityBehaviorClass(EntityBehaviorVintageE.Code, typeof(EntityBehaviorVintageE));
            api.RegisterEntity("VintageEProjectile", typeof(EntityVintageEProjectile));

            api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<EmcTablePacket>()
                .RegisterMessageType<TransmuteRequestPacket>()
                .RegisterMessageType<TransmuteResultPacket>()
                .RegisterMessageType<EmcMissingPacket>()
                .RegisterMessageType<EmcSetValuePacket>()
                .RegisterMessageType<WatchSpeedPacket>()
                .RegisterMessageType<FireProjectilePacket>()
                .RegisterMessageType<EmcBlacklistPacket>()
                .RegisterMessageType<EmcContentsPacket>()
                .RegisterMessageType<EmcContentsSetPacket>();
        }

        #region Server

        public override void AssetsFinalize(ICoreAPI api)
        {
            if (api.Side != EnumAppSide.Server) return;
            // Read rule files now, while patched asset data is guaranteed to be in memory.
            Rules = EmcRuleSet.Load(api, Mod.Logger);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            Sapi = sapi;
            Config = LoadConfig(sapi);
            Transmutation = new TransmutationService(this, sapi);
            Trinkets = new TrinketEffects(this, sapi);

            serverChannel = sapi.Network.GetChannel(ChannelName);
            serverChannel.SetMessageHandler<TransmuteRequestPacket>(Transmutation.OnRequest);
            serverChannel.SetMessageHandler<EmcSetValuePacket>(OnSetValueRequest);
            serverChannel.SetMessageHandler<EmcBlacklistPacket>(OnBlacklistRequest);
            serverChannel.SetMessageHandler<FireProjectilePacket>(OnFireProjectile);
            serverChannel.SetMessageHandler<EmcContentsSetPacket>(OnContentsSetRequest);

            // Every mod has registered its recipes by the time the game starts running, so map then.
            sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
            {
                RecalculateEmc("server start", allowCache: true);
                if (Config.SelfTestOnStart) sapi.Event.RegisterCallback(_ => new SelfTest(this, sapi).Start(), 5000);
            });
            sapi.Event.PlayerNowPlaying += player =>
            {
                if (player.Entity != null && player.Entity.GetBehavior<EntityBehaviorVintageE>() == null)
                {
                    player.Entity.AddBehavior(new EntityBehaviorVintageE(player.Entity));
                }
                SendEmcTable(player);
                if (Trinkets != null && Trinkets.WatchTimeModifier != 0) serverChannel.SendPacket(new WatchSpeedPacket { Modifier = Trinkets.WatchTimeModifier }, player);
                Announce(player, LastReport);
            };

            EmcCommands.Register(this, sapi);
        }

        readonly Dictionary<string, long> lastShot = new Dictionary<string, long>();

        /// <summary>The fire-projectile key: the held lens, amulet or ring shoots (at most 4 times a second).</summary>
        void OnFireProjectile(IServerPlayer player, FireProjectilePacket packet)
        {
            ItemSlot slot = player?.InventoryManager?.ActiveHotbarSlot;
            if (!(slot?.Itemstack?.Collectible is IVintageEShooter shooter) || !shooter.CanShoot(slot)) return;
            long now = Sapi.World.ElapsedMilliseconds;
            if (lastShot.TryGetValue(player.PlayerUID, out long last) && now - last < 250) return;
            lastShot[player.PlayerUID] = now;
            shooter.Shoot(player, slot);
        }

        VintageEConfig LoadConfig(ICoreServerAPI sapi)
        {
            VintageEConfig cfg = null;
            bool broken = false;
            try
            {
                cfg = sapi.LoadModConfig<VintageEConfig>(ConfigFile);
            }
            catch (Exception e)
            {
                broken = true;
                Mod.Logger.Error("Could not read ModConfig/{0}, using defaults for this session (the file is left untouched so you can fix it): {1}", ConfigFile, e.Message);
            }
            cfg ??= new VintageEConfig();
            cfg.Normalize();
            if (!broken) SaveConfig(cfg); // writes the file on first run and adds any new settings
            return cfg;
        }

        public void SaveConfig(VintageEConfig cfg = null)
        {
            try { Sapi?.StoreModConfig(cfg ?? Config, ConfigFile); }
            catch (Exception e) { Mod.Logger.Warning("Could not write ModConfig/{0}: {1}", ConfigFile, e.Message); }
        }

        /// <summary>
        /// Runs the recipe-graph mapper (or, with <paramref name="allowCache"/>, reuses the saved values if nothing that
        /// affects them has changed) and pushes the result to every connected player.
        /// </summary>
        public EmcMappingReport RecalculateEmc(string reason, bool allowCache = false)
        {
            var sw = Stopwatch.StartNew();
            if (Rules.LoadedFiles.Count == 0)
            {
                Rules = EmcRuleSet.Load(Sapi, Mod.Logger);
                worldTransmutation = null;
            }
            var mapper = new EmcMapper(Sapi, Config, Rules, Mod.Logger);
            EmcMappingReport report = null;
            string fingerprint = null;
            if (Config.CacheEmcValues)
            {
                try
                {
                    fingerprint = EmcCache.Fingerprint(Sapi, this, Mod.Logger);
                    if (allowCache && EmcCache.TryLoad(Sapi, fingerprint, Mod.Logger, out EmcCacheFile cached, out var cachedValues, out var cachedVariants))
                    {
                        report = mapper.FromValues(cachedValues);
                        report.Variants = cachedVariants;
                        EmcCache.CopyCounts(cached, report);
                    }
                }
                catch (Exception e)
                {
                    Mod.Logger.Warning("EMC cache check failed, remapping: {0}", e.Message);
                    fingerprint = null;
                }
            }
            if (report == null)
            {
                try
                {
                    report = mapper.Run();
                }
                catch (Exception e)
                {
                    Mod.Logger.Error("EMC mapping failed ({0}): {1}", reason, e);
                    return null;
                }
                if (Config.CacheEmcValues && fingerprint != null) EmcCache.Save(Sapi, fingerprint, report, Mod.Logger);
            }
            Emc.Replace(report.Values);
            Emc.DurabilityScales = Config.DurabilityScalesEmc;
            Emc.World = Sapi.World;
            BuildStackData(report);
            sw.Stop();
            report.Milliseconds = sw.ElapsedMilliseconds;
            if (report.FromCache)
            {
                Mod.Logger.Notification("EMC values loaded from cache ({0}) in {1} ms: {2} valued collectibles. Nothing that affects them has changed since they were mapped ({3}); /emc recalc forces a fresh mapping.",
                    reason, report.Milliseconds, report.Values.Count, report.CachedAt);
            }
            else Mod.Logger.Notification(
                "EMC mapped ({0}) in {1} ms: {2} valued collectibles ({3} base, {4} from recipes/processing, {5} inferred backwards), {6} conversions from {7} sources.",
                reason, report.Milliseconds, report.Values.Count, report.BaseCount, report.DerivedCount, report.InferredCount,
                report.ConversionCount, report.SourceCount);
            if (report.RelativeCount > 0 || report.SiblingCount > 0 || report.FallbackCount > 0 || report.TraderCount > 0 || report.DefaultCount > 0 || report.GuessCount > 0)
            {
                Mod.Logger.Notification("EMC: items with no recipe path: {0} valued by relativeValues rules, {1} from trader prices, {2} from a variant that only looks or sits differently, {3} other mods' variants of base-game families, {4} as fuel, {5} as food, {6} guessed from other variants, {7} from defaultValues, {8} got the configured FallbackValue.",
                    report.RelativeCount, report.TraderCount, report.SiblingCount, report.OtherModCount, report.FuelCount, report.FoodCount, report.GuessCount, report.DefaultCount, report.FallbackCount);
            }
            var data = Emc.StackData;
            Mod.Logger.Notification("EMC: {0} blocks/items hold contents or keep a material ({1} item only, {2} item + contents, {3} item price only); {4} have material-specific values.",
                data.Kinds.Count, data.Kinds.Keys.Count(c => data.ModeOf(c) == StackDataMode.Item), data.Kinds.Keys.Count(c => data.ModeOf(c) == StackDataMode.Full),
                data.Kinds.Keys.Count(c => data.ModeOf(c) == StackDataMode.ItemPrice), data.Variants.Count);
            LogCoverage(report);
            LastReport = report;
            if (Config.DumpValuesOnStart)
            {
                string file = WriteValuesFile(report);
                if (file != null) Mod.Logger.Notification("EMC values written to {0}", file);
            }
            if (report.LoopFrozen > 0 || report.UnstableRemoved > 0)
            {
                Mod.Logger.Notification("EMC: {0} items sit in self-cheapening recipe loops and keep their first value ({1} of them only settled that way at the end); {2} had to be dropped.", report.LoopFrozen, report.KeptAtFirstValue, report.UnstableRemoved);
            }

            serverChannel?.BroadcastPacket(Emc.ToPacket());
            OnEmcTableUpdated?.Invoke();
            // Players joining later get the same line from PlayerNowPlaying.
            foreach (IPlayer online in Sapi.World.AllOnlinePlayers ?? Array.Empty<IPlayer>())
            {
                if (online is IServerPlayer sp && sp.ConnectionState == EnumClientState.Playing) Announce(sp, report);
            }
            return report;
        }

        /// <summary>Works out which items carry contents/materials and their modes (cheap; no remapping needed).</summary>
        public void BuildStackData(EmcMappingReport report = null)
        {
            report ??= LastReport;
            try
            {
                Emc.StackData = StackDataTable.Build(Sapi.World, Rules, Config, report?.Variants);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Could not work out which items keep their contents: {0}", e);
            }
        }

        /// <summary>After a change to the contents modes: rebuild them and send everyone the new table.</summary>
        public void RefreshStackData()
        {
            BuildStackData();
            serverChannel?.BroadcastPacket(Emc.ToPacket());
            OnEmcTableUpdated?.Invoke();
        }

        /// <summary>The mod's version as loaded from modinfo.json (falls back to the assembly version).</summary>
        public string Version => Mod?.Info?.Version
            ?? typeof(VintageEModSystem).Assembly.GetName().Version?.ToString(3)
            ?? "?";

        /// <summary>One-line summary of the last mapping, e.g. "Vintage-E version 1.3.0: calculated 4321 EMC values, 12 missing."</summary>
        public string AnnouncementText(EmcMappingReport report, string languageCode = null)
        {
            string key = report.FromCache ? "vintagee:chat-announce-cached" : "vintagee:chat-announce";
            object[] args = { Version, report.Values.Count, report.Missing.Count, report.CreativeTotal };
            return string.IsNullOrEmpty(languageCode) ? Lang.Get(key, args) : Lang.GetL(languageCode, key, args);
        }

        void Announce(IServerPlayer player, EmcMappingReport report)
        {
            if (player == null || report == null || !Config.AnnounceInChat) return;
            try
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, AnnouncementText(report, player.LanguageCode), EnumChatType.Notification);
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Could not send the EMC summary to {0}: {1}", player.PlayerName, e.Message);
            }
        }

        /// <summary>Logs how much real content got a value and writes the leftovers to ModData/vintagee/emc-missing.csv.</summary>
        void LogCoverage(EmcMappingReport report)
        {
            int valued = report.CreativeTotal - report.Missing.Count;
            double pct = report.CreativeTotal == 0 ? 100 : valued * 100.0 / report.CreativeTotal;
            Mod.Logger.Notification("EMC coverage: {0} of {1} blocks/items in the creative inventory have a value ({2:0.0}%).", valued, report.CreativeTotal, pct);
            if (report.Missing.Count == 0) return;

            string top = string.Join(", ", MissingFamilies(report).Take(15).Select(f => f.Key + " (" + f.Value + ")"));
            Mod.Logger.Notification("EMC: {0} without a value, by family: {1}. Full list: {2}. Give them values in ModConfig/{3} (BaseValues) or a config/vintagee-emc.json asset.",
                report.Missing.Count, top, WriteMissingFile(report) ?? "(could not write file)", ConfigFile);
        }

        public static IEnumerable<KeyValuePair<string, int>> MissingFamilies(EmcMappingReport report)
        {
            return report.Missing
                .GroupBy(c => c.Code.Domain + ":" + c.Code.Path.Split('-')[0])
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal);
        }

        /// <summary>Writes every value (and, after a fresh mapping, what it was worked out from) to ModData/vintagee/emc-values.csv.</summary>
        public string WriteValuesFile(EmcMappingReport report)
        {
            try
            {
                string file = System.IO.Path.Combine(Sapi.GetOrCreateDataPath("vintagee"), "emc-values.csv");
                var sb = new System.Text.StringBuilder("type,code,emc,emc_per_litre,via,name\n");
                foreach (var kv in Emc.All.OrderBy(k => k.Key.Code.ToString(), StringComparer.Ordinal))
                {
                    string via = report == null ? "" : report.FromCache ? "cached - run /emc recalc to see sources" : report.Origins.TryGetValue(kv.Key, out string o) ? o : "";
                    double perLitre = EmcRegistry.ItemsPerLitre(kv.Key);
                    sb.Append(kv.Key.ItemClass == EnumItemClass.Block ? "block" : "item").Append(',')
                      .Append(kv.Key.Code).Append(',')
                      .Append(kv.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                      .Append(perLitre > 0 ? (kv.Value * perLitre).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) : "").Append(',')
                      .Append(Csv(via)).Append(',')
                      .Append(Csv(NameOf(kv.Key))).Append('\n');
                }
                System.IO.File.WriteAllText(file, sb.ToString());

                // Everything the blacklist keeps from having a value, next to it.
                if (report?.Blacklisted != null)
                {
                    var bl = new System.Text.StringBuilder("type,code,in_creative_inventory,name\n");
                    foreach (CollectibleObject c in report.Blacklisted.OrderBy(c => c.Code.ToString(), StringComparer.Ordinal))
                    {
                        bool listed = (c.CreativeInventoryTabs?.Length ?? 0) > 0 || (c.CreativeInventoryStacks?.Length ?? 0) > 0;
                        bl.Append(c.ItemClass == EnumItemClass.Block ? "block" : "item").Append(',').Append(c.Code).Append(',')
                          .Append(listed ? "yes" : "no").Append(',').Append(Csv(NameOf(c))).Append('\n');
                    }
                    System.IO.File.WriteAllText(System.IO.Path.Combine(Sapi.GetOrCreateDataPath("vintagee"), "emc-blacklisted.csv"), bl.ToString());
                }

                // Items that hold contents or keep a material: their kind, mode and material-specific values.
                var data = Emc.StackData;
                var ct = new System.Text.StringBuilder("type,code,kind,mode,set_by,data,emc,via,name\n");
                foreach (var kv in data.Kinds.OrderBy(k => k.Key.Code.ToString(), StringComparer.Ordinal))
                {
                    CollectibleObject c = kv.Key;
                    string type = c.ItemClass == EnumItemClass.Block ? "block" : "item";
                    string head = type + "," + c.Code + "," + kv.Value.ToString().ToLowerInvariant() + "," + StackDataTable.ModeName(data.ModeOf(c)) + "," +
                                  (data.Sources.TryGetValue(c, out string src) ? src : "") + ",";
                    ct.Append(head).Append(",").Append(Emc.GetValue(c).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(",plain item,").Append(Csv(NameOf(c))).Append('\n');
                    if (!data.Variants.TryGetValue(c, out var list)) continue;
                    foreach (VariantValue v in list.OrderBy(v => v.Value))
                    {
                        ct.Append(head).Append(Csv(v.Attributes.ToJsonToken())).Append(',')
                          .Append(v.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                          .Append(Csv(v.Origin ?? "")).Append(',').Append(Csv(NameOf(c))).Append('\n');
                    }
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(Sapi.GetOrCreateDataPath("vintagee"), "emc-contents.csv"), ct.ToString());
                return file;
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Could not write emc-values.csv: {0}", e.Message);
                return null;
            }
        }

        static string NameOf(CollectibleObject c)
        {
            try { return c.GetHeldItemName(new ItemStack(c)); }
            catch { return c.Code?.ToShortString() ?? ""; }
        }

        static string Csv(string s) => string.IsNullOrEmpty(s) ? "" : s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        public string WriteMissingFile(EmcMappingReport report)
        {
            try
            {
                string file = System.IO.Path.Combine(Sapi.GetOrCreateDataPath("vintagee"), "emc-missing.csv");
                var sb = new System.Text.StringBuilder("type,code\n");
                foreach (CollectibleObject c in report.Missing.OrderBy(c => c.Code.ToString(), StringComparer.Ordinal))
                {
                    sb.Append(c.ItemClass == EnumItemClass.Block ? "block" : "item").Append(',').Append(c.Code).Append('\n');
                }
                System.IO.File.WriteAllText(file, sb.ToString());
                return file;
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Could not write emc-missing.csv: {0}", e.Message);
                return null;
            }
        }

        void SendEmcTable(IServerPlayer player)
        {
            if (Emc.Count == 0) return; // mapping not done yet; it broadcasts when finished
            serverChannel.SendPacket(Emc.ToPacket(), player);
        }

        public void SendResult(IServerPlayer player, TransmuteResultPacket result)
        {
            serverChannel.SendPacket(result, player);
        }

        /// <summary>
        /// Pins an item's EMC value (saved to ModConfig FixedValues, like /emc set) and remaps. Returns the config key
        /// used, or null if the code didn't resolve.
        /// </summary>
        public string SetFixedValue(CollectibleObject collectible, double? value, bool save = true)
        {
            if (collectible?.Code == null) return null;
            string key = ConfigKey(collectible);
            if (value.HasValue)
            {
                Config.FixedValues[key] = value.Value;
                if (value.Value > 0) Config.Blacklist.Remove(key); // a value takes it off the blacklist
            }
            else Config.FixedValues.Remove(key);
            if (save) SaveConfig();
            return key;
        }

        static string ConfigKey(CollectibleObject c) => (c.ItemClass == EnumItemClass.Block ? "block:" : "item:") + c.Code;

        /// <summary>
        /// Puts a block/item on the server blacklist (ModConfig Blacklist) or takes it off: removed from Blacklist if
        /// it was added there, otherwise added to Allow so the built-in blacklist no longer applies. Remaps.
        /// </summary>
        public void SetBlacklisted(CollectibleObject c, bool blacklisted, bool save = true) => SetBlacklistedPattern(ConfigKey(c), blacklisted, save);

        /// <summary>Same as <see cref="SetBlacklisted"/> for a config pattern (a code, or "modid:*" for a whole mod).</summary>
        public void SetBlacklistedPattern(string key, bool blacklisted, bool save = true)
        {
            if (blacklisted)
            {
                Config.Allow.Remove(key);
                Config.FixedValues.Remove(key);
                if (!Config.Blacklist.Contains(key)) Config.Blacklist.Add(key);
            }
            else if (!Config.Blacklist.Remove(key) && !Config.Allow.Contains(key))
            {
                Config.Allow.Add(key);
            }
            if (save) SaveConfig();
        }

        void OnBlacklistRequest(IServerPlayer player, EmcBlacklistPacket packet)
        {
            string lang = player.LanguageCode;
            if (!player.HasPrivilege(Privilege.controlserver))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "vintagee:error-noprivilege"), EnumChatType.CommandError);
                return;
            }
            string msg;
            if (!string.IsNullOrEmpty(packet.Domain))
            {
                // A whole mod. Not the base game: blacklisting it would remove every vanilla value, and taking it off
                // wholesale would give values to creative-only and technical blocks.
                if (packet.Domain == "game")
                {
                    player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "vintagee:missing-nogame"), EnumChatType.CommandError);
                    return;
                }
                SetBlacklistedPattern(packet.Domain + ":*", packet.Add);
                msg = Lang.GetL(lang, packet.Add ? "vintagee:cmd-blacklistedmod" : "vintagee:cmd-unblacklistedmod", packet.Domain);
            }
            else
            {
                List<CollectibleObject> items = ResolveKeys(Sapi.World, packet.Key, packet.Keys);
                if (items.Count == 0) return;
                foreach (CollectibleObject c in items) SetBlacklisted(c, packet.Add, save: false);
                SaveConfig();
                msg = items.Count > 1 ? Lang.GetL(lang, packet.Add ? "vintagee:cmd-blacklistedmany" : "vintagee:cmd-unblacklistedmany", items.Count) : null;
                if (items.Count == 1)
                {
                    CollectibleObject c = items[0];
                    msg = packet.Add ? Lang.GetL(lang, "vintagee:cmd-blacklisted", NameOf(c), c.Code) : null;
                }
            }
            EmcMappingReport report = RecalculateEmc(packet.Add ? "blacklisted from the /emc missing window" : "removed from the blacklist");
            if (msg == null)
            {
                CollectibleObject c = ResolveKeys(Sapi.World, packet.Key, packet.Keys)[0];
                double v = Emc.GetValue(c);
                msg = v > 0 ? Lang.GetL(lang, "vintagee:cmd-unblacklisted-value", NameOf(c), c.Code, EmcFormat.Format(v))
                    : Lang.GetL(lang, "vintagee:cmd-unblacklisted", NameOf(c), c.Code);
            }
            player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
            if (report != null) SendMissingList(player, report, null, packet.FromBlacklist);
        }

        static List<CollectibleObject> ResolveKeys(IWorldAccessor world, string key, string[] keys)
        {
            var list = new List<CollectibleObject>();
            foreach (string k in keys != null && keys.Length > 0 ? keys : new[] { key })
            {
                CollectibleObject c = PlayerEmc.Resolve(world, k);
                if (c?.Code != null && !list.Contains(c)) list.Add(c);
            }
            return list;
        }

        void OnSetValueRequest(IServerPlayer player, EmcSetValuePacket packet)
        {
            string lang = player.LanguageCode;
            if (!player.HasPrivilege(Privilege.controlserver))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "vintagee:error-noprivilege"), EnumChatType.CommandError);
                return;
            }
            if (double.IsNaN(packet.Value) || double.IsInfinity(packet.Value) || packet.Value < 0) return;
            List<CollectibleObject> items = ResolveKeys(Sapi.World, packet.Key, packet.Keys);
            if (items.Count == 0) return;
            foreach (CollectibleObject c in items) SetFixedValue(c, packet.Value, save: false);
            SaveConfig();
            EmcMappingReport report = RecalculateEmc("/emc missing window");
            string msg = items.Count == 1
                ? Lang.GetL(lang, "vintagee:cmd-itemvalue", NameOf(items[0]), items[0].Code, EmcFormat.Format(Emc.GetValue(items[0])))
                : Lang.GetL(lang, "vintagee:cmd-setmany", items.Count, EmcFormat.Format(packet.Value));
            player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
            if (report != null) SendMissingList(player, report, null, packet.FromBlacklist);
        }

        /// <summary>Opens the /emc contents window for a player.</summary>
        public void SendContentsList(IServerPlayer player)
        {
            StackDataTable table = Emc.StackData;
            // What each would be without the server config: the table built with an empty StackDataModes.
            var plainConfig = new VintageEConfig { StackDataModes = new Dictionary<string, string>() };
            StackDataTable defaults = StackDataTable.Build(Sapi.World, Rules, plainConfig, table.Variants);
            var keys = new List<string>();
            var kinds = new List<byte>();
            var modes = new List<byte>();
            var defaultModes = new List<byte>();
            var fromConfig = new List<bool>();
            foreach (var kv in table.Kinds)
            {
                string key = PlayerEmc.KeyOf(kv.Key);
                if (key == null) continue;
                keys.Add(key);
                kinds.Add((byte)kv.Value);
                modes.Add((byte)table.ModeOf(kv.Key));
                defaultModes.Add((byte)(defaults.CarriesData(kv.Key) ? defaults.ModeOf(kv.Key) : StackDataMode.Item));
                fromConfig.Add(table.Sources.TryGetValue(kv.Key, out string src) && src == "server config");
            }
            serverChannel.SendPacket(new EmcContentsPacket
            {
                Keys = keys.ToArray(),
                Kinds = kinds.ToArray(),
                Modes = modes.ToArray(),
                DefaultModes = defaultModes.ToArray(),
                FromConfig = fromConfig.ToArray(),
                Version = Version
            }, player);
        }

        /// <summary>
        /// Sets (or with <paramref name="mode"/> null, clears) the server config's contents mode for blocks/items and
        /// sends everyone the new table. Values don't change, so nothing is remapped.
        /// </summary>
        public void SetStackDataModes(IEnumerable<CollectibleObject> items, StackDataMode? mode)
        {
            foreach (CollectibleObject c in items)
            {
                string key = ConfigKey(c);
                if (mode == null) Config.StackDataModes.Remove(key);
                else Config.StackDataModes[key] = StackDataTable.ModeName(mode.Value);
            }
            SaveConfig();
            RefreshStackData();
        }

        void OnContentsSetRequest(IServerPlayer player, EmcContentsSetPacket packet)
        {
            if (packet == null || !player.HasPrivilege(Privilege.controlserver)) return;
            string lang = player.LanguageCode;
            var items = (packet.Keys ?? Array.Empty<string>()).Select(k => PlayerEmc.Resolve(Sapi.World, k)).Where(c => c != null && !(c is IEmcStorageItem)).Distinct().ToList();
            if (items.Count == 0) return;
            StackDataMode? mode = packet.Mode < 0 ? (StackDataMode?)null : (StackDataMode)Math.Clamp(packet.Mode, 0, 2);
            SetStackDataModes(items, mode);
            string modeName = mode == null ? Lang.GetL(lang, "vintagee:contents-mode-default") : Lang.GetL(lang, "vintagee:contents-mode-" + StackDataTable.ModeName(mode.Value));
            string msg = items.Count == 1
                ? Lang.GetL(lang, "vintagee:cmd-contents-set", NameOf(items[0]), modeName)
                : Lang.GetL(lang, "vintagee:cmd-contents-setmany", items.Count, modeName);
            player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.CommandSuccess);
            SendContentsList(player);
        }

        /// <summary>Tells every client about a change to the watch's time speed, so their clocks and skies keep up.</summary>
        public void BroadcastWatchSpeed(float modifier) => serverChannel?.BroadcastPacket(new WatchSpeedPacket { Modifier = modifier });

        /// <summary>Opens the /emc missing window (or with <paramref name="blacklist"/>, the /emc blacklist window) for a player.</summary>
        public void SendMissingList(IServerPlayer player, EmcMappingReport report, string file, bool blacklist = false)
        {
            serverChannel.SendPacket(new EmcMissingPacket
            {
                Keys = (blacklist ? report.Blacklisted : report.Missing).Select(PlayerEmc.KeyOf).Where(k => k != null).ToArray(),
                CreativeTotal = report.CreativeTotal,
                ValueCount = report.Values.Count,
                Version = Version,
                File = file,
                Blacklist = blacklist
            }, player);
        }

        #endregion

        #region Client

        public override void StartClientSide(ICoreClientAPI capi)
        {
            Capi = capi;
            clientChannel = capi.Network.GetChannel(ChannelName);
            clientChannel.SetMessageHandler<EmcTablePacket>(OnEmcTableReceived);
            clientChannel.SetMessageHandler<TransmuteResultPacket>(OnTransmuteResult);
            clientChannel.SetMessageHandler<EmcMissingPacket>(OnMissingList);
            clientChannel.SetMessageHandler<EmcContentsPacket>(OnContentsList);
            clientChannel.SetMessageHandler<WatchSpeedPacket>(p => TrinketEffects.ApplyTimeModifier(capi.World.Calendar, p.Modifier));

            capi.Input.RegisterHotKey(FireHotkey, Lang.Get("vintagee:hotkey-fire"), GlKeys.R, HotkeyType.CharacterControls);
            capi.Input.SetHotKeyHandler(FireHotkey, _ =>
            {
                ItemSlot slot = capi.World.Player?.InventoryManager?.ActiveHotbarSlot;
                if (!(slot?.Itemstack?.Collectible is IVintageEShooter shooter) || !shooter.CanShoot(slot)) return false;
                clientChannel?.SendPacket(new FireProjectilePacket { Hand = 0 });
                return true;
            });

            // Tooltips: give every collectible a tiny client-only behaviour that prints its EMC.
            capi.Event.BlockTexturesLoaded += () =>
            {
                int count = 0;
                foreach (CollectibleObject obj in capi.World.Collectibles)
                {
                    if (obj?.Code == null) continue;
                    var bh = new CollectibleBehaviorEmcTooltip(obj, this);
                    var list = new List<CollectibleBehavior>(obj.CollectibleBehaviors ?? Array.Empty<CollectibleBehavior>()) { bh };
                    obj.CollectibleBehaviors = list.ToArray();
                    count++;
                }
                Mod.Logger.VerboseDebug("Added EMC tooltips to {0} collectibles", count);
            };
        }

        void OnEmcTableReceived(EmcTablePacket packet)
        {
            Emc.LoadFromPacket(packet, Capi.World);
            OnEmcTableUpdated?.Invoke();
        }

        void OnTransmuteResult(TransmuteResultPacket packet)
        {
            if (!string.IsNullOrEmpty(packet.ErrorCode))
            {
                Capi.TriggerIngameError(this, packet.ErrorCode, Lang.Get("vintagee:error-" + packet.ErrorCode, packet.Args ?? Array.Empty<string>()));
            }
            transmutationDialog?.OnServerResult(packet);
        }

        public void SendTransmuteRequest(TransmuteRequestPacket packet) => clientChannel?.SendPacket(packet);

        GuiDialogEmcMissing missingDialog;

        void OnMissingList(EmcMissingPacket packet)
        {
            GuiDialogEmcMissing previous = missingDialog != null && missingDialog.IsOpened() && missingDialog.IsBlacklist == packet.Blacklist ? missingDialog : null;
            var dialog = new GuiDialogEmcMissing(Capi, packet, previous,
                (keys, value) => clientChannel?.SendPacket(new EmcSetValuePacket { Keys = keys, Value = value, FromBlacklist = packet.Blacklist }),
                (keys, add) => clientChannel?.SendPacket(new EmcBlacklistPacket { Keys = keys, Add = add, FromBlacklist = packet.Blacklist }),
                (domain, add) => clientChannel?.SendPacket(new EmcBlacklistPacket { Domain = domain, Add = add, FromBlacklist = packet.Blacklist }));
            missingDialog?.TryClose();
            missingDialog?.Dispose();
            missingDialog = dialog;
            missingDialog.TryOpen();
        }

        /// <summary>Opens the transmutation GUI (from a table at <paramref name="tablePos"/>, or from a tablet when null).</summary>
        GuiDialogEmcContents contentsDialog;

        void OnContentsList(EmcContentsPacket packet)
        {
            GuiDialogEmcContents previous = contentsDialog != null && contentsDialog.IsOpened() ? contentsDialog : null;
            var dialog = new GuiDialogEmcContents(Capi, packet, previous, (keys, mode) => clientChannel.SendPacket(new EmcContentsSetPacket { Keys = keys, Mode = mode }));
            previous?.TryClose();
            contentsDialog = dialog;
            dialog.TryOpen();
        }

        public void OpenTransmutationDialog(Vintagestory.API.MathTools.BlockPos tablePos)
        {
            if (Capi == null) return;
            if (transmutationDialog != null && transmutationDialog.IsOpened())
            {
                transmutationDialog.TryClose();
                return;
            }
            transmutationDialog?.Dispose();
            transmutationDialog = new GuiDialogTransmutation(Capi, this, tablePos);
            transmutationDialog.OnClosed += () =>
            {
                SendTransmuteRequest(new TransmuteRequestPacket { Action = TransmuteAction.Close });
            };
            transmutationDialog.TryOpen();
        }

        #endregion

        public override void Dispose()
        {
            transmutationDialog?.Dispose();
            missingDialog?.Dispose();
            try { (Sapi?.World ?? (IWorldAccessor)Capi?.World)?.Calendar?.RemoveTimeSpeedModifier(TrinketEffects.TimeModifierName); }
            catch { /* world already gone */ }
            base.Dispose();
        }
    }
}

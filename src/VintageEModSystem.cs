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

            api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<EmcTablePacket>()
                .RegisterMessageType<TransmuteRequestPacket>()
                .RegisterMessageType<TransmuteResultPacket>();
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

            serverChannel = sapi.Network.GetChannel(ChannelName);
            serverChannel.SetMessageHandler<TransmuteRequestPacket>(Transmutation.OnRequest);

            // Every mod has registered its recipes by the time the game starts running, so map then.
            sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => RecalculateEmc("server start", allowCache: true));
            sapi.Event.PlayerNowPlaying += player =>
            {
                SendEmcTable(player);
                Announce(player, LastReport);
            };

            EmcCommands.Register(this, sapi);
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
                    if (allowCache && EmcCache.TryLoad(Sapi, fingerprint, Mod.Logger, out EmcCacheFile cached, out var cachedValues))
                    {
                        report = mapper.FromValues(cachedValues);
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
            if (report.RelativeCount > 0 || report.SiblingCount > 0 || report.FallbackCount > 0)
            {
                Mod.Logger.Notification("EMC: items with no recipe path: {0} valued by relativeValues rules, {1} borrowed their closest variant's value, {2} got the configured FallbackValue.",
                    report.RelativeCount, report.SiblingCount, report.FallbackCount);
            }
            LogCoverage(report);
            LastReport = report;
            if (Config.DumpValuesOnStart)
            {
                string file = WriteValuesFile(report);
                if (file != null) Mod.Logger.Notification("EMC values written to {0}", file);
            }
            if (report.LoopFrozen > 0 || report.UnstableRemoved > 0)
            {
                Mod.Logger.Notification("EMC: {0} items sit in self-cheapening recipe loops and keep their first value; {1} had to be dropped.", report.LoopFrozen, report.UnstableRemoved);
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
                var sb = new System.Text.StringBuilder("type,code,emc,emc_per_litre,via\n");
                foreach (var kv in Emc.All.OrderBy(k => k.Key.Code.ToString(), StringComparer.Ordinal))
                {
                    string via = report == null ? "" : report.FromCache ? "cached - run /emc recalc to see sources" : report.Origins.TryGetValue(kv.Key, out string o) ? o : "";
                    double perLitre = EmcRegistry.ItemsPerLitre(kv.Key);
                    sb.Append(kv.Key.ItemClass == EnumItemClass.Block ? "block" : "item").Append(',')
                      .Append(kv.Key.Code).Append(',')
                      .Append(kv.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                      .Append(perLitre > 0 ? (kv.Value * perLitre).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) : "").Append(',')
                      .Append(Csv(via)).Append('\n');
                }
                System.IO.File.WriteAllText(file, sb.ToString());
                return file;
            }
            catch (Exception e)
            {
                Mod.Logger.Warning("Could not write emc-values.csv: {0}", e.Message);
                return null;
            }
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

        #endregion

        #region Client

        public override void StartClientSide(ICoreClientAPI capi)
        {
            Capi = capi;
            clientChannel = capi.Network.GetChannel(ChannelName);
            clientChannel.SetMessageHandler<EmcTablePacket>(OnEmcTableReceived);
            clientChannel.SetMessageHandler<TransmuteResultPacket>(OnTransmuteResult);

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

        /// <summary>Opens the transmutation GUI (from a table at <paramref name="tablePos"/>, or from a tablet when null).</summary>
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
            base.Dispose();
        }
    }
}

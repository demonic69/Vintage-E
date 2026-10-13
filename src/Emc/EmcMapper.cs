using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VintageE
{
    public class EmcMappingReport
    {
        public Dictionary<CollectibleObject, double> Values;
        public int BaseCount;
        public int DerivedCount;
        public int InferredCount;
        public int ConversionCount;
        public int SourceCount;
        public int UnstableRemoved;
        public int LoopFrozen;
        public int UnmatchedRules;
        public int SkippedRecipes;
        public int SiblingCount;
        public int FallbackCount;
        public long Milliseconds;
        public SortedDictionary<string, int> Sources;
        /// <summary>Blocks/items that appear in the creative inventory (i.e. are real, obtainable content), excluding the blacklist.</summary>
        public int CreativeTotal;
        /// <summary>Of those, the ones that still have no value.</summary>
        public List<CollectibleObject> Missing = new List<CollectibleObject>();
        /// <summary>True when the values were loaded from the on-disk cache instead of being mapped.</summary>
        public bool FromCache;
        public string CachedAt;
    }

    /// <summary>
    /// Works out an EMC value for every block and item from scratch at server start:
    /// seeds a small set of base values, collects every recipe and processing step the loaded
    /// mods define, then propagates values through that graph with <see cref="EmcSolver{T}"/>.
    /// Nothing is hard-coded per item, so modded content is valued automatically as long as
    /// it is ultimately made from something with a value.
    /// </summary>
    public class EmcMapper
    {
        readonly ICoreServerAPI sapi;
        readonly VintageEConfig config;
        readonly EmcRuleSet rules;
        readonly ILogger logger;

        public EmcMapper(ICoreServerAPI sapi, VintageEConfig config, EmcRuleSet rules, ILogger logger)
        {
            this.sapi = sapi;
            this.config = config;
            this.rules = rules;
            this.logger = logger;
        }

        public EmcMappingReport Run()
        {
            var report = new EmcMappingReport();
            var index = new CollectibleIndex(sapi.World.Collectibles);

            Seed(index, report, out Dictionary<CollectibleObject, double> fixedValues, out HashSet<CollectibleObject> excluded);

            // 3. Every way of making things.
            var collector = new ConversionCollector(sapi, index, logger);
            collector.CollectAll();
            report.SkippedRecipes = collector.SkippedRecipes;

            // 4. Propagate.
            var edges = new List<EmcSolver<CollectibleObject>.Edge>();
            foreach (Conversion conv in collector.Conversions)
            {
                if (excluded.Contains(conv.Output)) continue;
                var edge = new EmcSolver<CollectibleObject>.Edge
                {
                    Output = conv.Output,
                    OutputQuantity = conv.OutputQuantity,
                    Choices = conv.Inputs.Select(i => i.Choices).ToArray(),
                    Quantities = conv.Inputs.Select(i => i.Quantity).ToArray(),
                    Reversible = conv.Kind == ConversionKind.Processing
                };
                if (conv.Returned != null)
                {
                    edge.Returned = conv.Returned.Select(r => r.Key).ToArray();
                    edge.ReturnedQuantities = conv.Returned.Select(r => r.Value).ToArray();
                }
                edges.Add(edge);
            }
            EmcSolver<CollectibleObject> solver = Solve(fixedValues, excluded, edges);

            // 5. Last resort for whatever is still unvalued: borrow from its closest variant sibling
            //    (ore-poor-galena-chalk ← ore-poor-galena-granite, bread-spelt-charred ← bread-spelt-perfect).
            //    The highest sibling value is used, so a guessed value can never be cheaper to create than to burn.
            //    Then, only if the server owner asked for it, a flat fallback value for anything still left.
            var extra = new Dictionary<CollectibleObject, double>();
            if (config.InferFromVariantSiblings)
            {
                foreach (var kv in SiblingValues(index, solver.Values, excluded)) extra[kv.Key] = kv.Value;
                report.SiblingCount = extra.Count;
            }
            if (config.FallbackValue > 0)
            {
                foreach (CollectibleObject c in index.All)
                {
                    if (IsListed(c) && !excluded.Contains(c) && !solver.Values.ContainsKey(c) && !extra.ContainsKey(c))
                    {
                        extra[c] = config.FallbackValue;
                        report.FallbackCount++;
                    }
                }
            }
            if (extra.Count > 0)
            {
                var seeded = new Dictionary<CollectibleObject, double>(fixedValues);
                foreach (var kv in extra) seeded[kv.Key] = kv.Value;
                solver = Solve(seeded, excluded, edges);
            }

            report.Values = new Dictionary<CollectibleObject, double>();
            foreach (var kv in solver.Values)
            {
                if (kv.Value > 0 && !excluded.Contains(kv.Key) && !solver.Unstable.Contains(kv.Key)) report.Values[kv.Key] = Tidy(kv.Value);
            }
            report.BaseCount = fixedValues.Count;
            report.InferredCount = solver.Inferred.Keys.Count(k => report.Values.ContainsKey(k));
            report.DerivedCount = report.Values.Count - report.BaseCount - report.InferredCount - report.SiblingCount - report.FallbackCount;
            report.ConversionCount = solver.Edges.Count;
            FillCoverage(index, report, excluded);
            report.Sources = collector.SourceCounts;
            report.SourceCount = collector.SourceCounts.Count(s => s.Value > 0);
            report.UnstableRemoved = solver.Unstable.Count;
            report.LoopFrozen = solver.Frozen.Count;

            foreach (var src in collector.SourceCounts)
            {
                logger.VerboseDebug("EMC source {0}: {1} conversions", src.Key, src.Value);
            }
            if (config.LogAllValues)
            {
                foreach (var kv in report.Values.OrderBy(k => k.Key.Code.ToString(), StringComparer.Ordinal))
                {
                    logger.Notification("EMC {0} {1} = {2}", kv.Key.ItemClass == EnumItemClass.Block ? "block" : "item", kv.Key.Code, EmcFormat.Format(kv.Value));
                }
            }
            return report;
        }

        /// <summary>Blacklist and base values: asset files (ours, then other mods') → item attributes → server config.</summary>
        void Seed(CollectibleIndex index, EmcMappingReport report, out Dictionary<CollectibleObject, double> fixedValues, out HashSet<CollectibleObject> excluded)
        {
            // 1. Things that must never have a value (creative/debug blocks etc.).
            excluded = new HashSet<CollectibleObject>();
            foreach (string pattern in rules.Blacklist.Concat(config.Blacklist))
            {
                excluded.UnionWith(index.Match(pattern));
            }

            // 2. Seed values. Later sources win: asset files (ours, then other mods') → item attributes → server config.
            fixedValues = new Dictionary<CollectibleObject, double>();
            foreach (EmcValueRule rule in rules.Values)
            {
                if (config.DisableBuiltInBaseValues && rule.Source.StartsWith(VintageEModSystem.ModId + ":", StringComparison.Ordinal)) continue;
                if (!Apply(index, rule.Pattern, rule.Value, fixedValues, excluded, overrideBlacklist: false))
                {
                    report.UnmatchedRules++;
                    logger.VerboseDebug("EMC rule '{0}' from {1} matched nothing", rule.Pattern, rule.Source);
                }
            }
            foreach (CollectibleObject c in index.All)
            {
                // Any mod can give its items a value with  "attributes": { "emcValue": 128 }  (0 = no EMC).
                JsonObject attr = c.Attributes?["emcValue"];
                if (attr != null && attr.Exists)
                {
                    double v = attr.AsDouble(-1);
                    if (v > 0) fixedValues[c] = v;
                    else if (v == 0) { excluded.Add(c); fixedValues.Remove(c); }
                }
            }
            foreach (var kv in config.BaseValues) Apply(index, kv.Key, kv.Value, fixedValues, excluded, overrideBlacklist: true);
            foreach (var kv in config.FixedValues) Apply(index, kv.Key, kv.Value, fixedValues, excluded, overrideBlacklist: true);
            foreach (CollectibleObject c in excluded) fixedValues.Remove(c);

        }

        /// <summary>
        /// Builds a report around values that came from the cache: re-applies the (cheap) blacklist/seeding
        /// so coverage numbers are exact, without collecting recipes or solving anything.
        /// </summary>
        public EmcMappingReport FromValues(Dictionary<CollectibleObject, double> values)
        {
            var report = new EmcMappingReport { FromCache = true, Values = values };
            var index = new CollectibleIndex(sapi.World.Collectibles);
            Seed(index, report, out _, out HashSet<CollectibleObject> excluded);
            foreach (CollectibleObject c in excluded) values.Remove(c);
            FillCoverage(index, report, excluded);
            return report;
        }

        static void FillCoverage(CollectibleIndex index, EmcMappingReport report, HashSet<CollectibleObject> excluded)
        {
            foreach (CollectibleObject c in index.All)
            {
                if (!IsListed(c) || excluded.Contains(c)) continue;
                report.CreativeTotal++;
                if (!report.Values.ContainsKey(c)) report.Missing.Add(c);
            }
        }

        /// <summary>Real, obtainable content: anything shown in a creative inventory tab.</summary>
        static bool IsListed(CollectibleObject c) => (c.CreativeInventoryTabs?.Length ?? 0) > 0 || (c.CreativeInventoryStacks?.Length ?? 0) > 0;

        EmcSolver<CollectibleObject> Solve(Dictionary<CollectibleObject, double> fixedValues, HashSet<CollectibleObject> excluded, List<EmcSolver<CollectibleObject>.Edge> edges)
        {
            var solver = new EmcSolver<CollectibleObject>
            {
                Fixed = fixedValues,
                Excluded = excluded,
                MaxPasses = config.MaxMappingPasses,
                InferBackwards = config.InferValuesFromProcessing
            };
            solver.Edges.AddRange(edges);
            solver.Solve();
            return solver;
        }

        /// <summary>
        /// For each unvalued collectible, find valued ones whose code differs in exactly one dash-separated part,
        /// trying the last part first (rock type, colour, orientation...) and never the first (the item family).
        /// </summary>
        static Dictionary<CollectibleObject, double> SiblingValues(CollectibleIndex index, Dictionary<CollectibleObject, double> values, HashSet<CollectibleObject> excluded)
        {
            var groups = new Dictionary<string, double>();
            foreach (var kv in values)
            {
                if (kv.Value <= 0 || kv.Key.Code == null) continue;
                string[] parts = kv.Key.Code.Path.Split('-');
                for (int k = 1; k < parts.Length; k++)
                {
                    string key = SiblingKey(kv.Key, parts, k);
                    groups[key] = groups.TryGetValue(key, out double prev) ? Math.Max(prev, kv.Value) : kv.Value;
                }
            }

            var result = new Dictionary<CollectibleObject, double>();
            foreach (CollectibleObject c in index.All)
            {
                if (values.ContainsKey(c) || excluded.Contains(c)) continue;
                string[] parts = c.Code.Path.Split('-');
                for (int k = parts.Length - 1; k >= 1; k--)
                {
                    if (groups.TryGetValue(SiblingKey(c, parts, k), out double v))
                    {
                        result[c] = v;
                        break;
                    }
                }
            }
            return result;
        }

        static string SiblingKey(CollectibleObject c, string[] parts, int skip)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(c.ItemClass == EnumItemClass.Block ? 'b' : 'i').Append('|').Append(c.Code.Domain).Append('|').Append(parts.Length).Append('|').Append(skip).Append('|');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i != skip) sb.Append(parts[i]);
                sb.Append('-');
            }
            return sb.ToString();
        }

        static bool Apply(CollectibleIndex index, string pattern, double value, Dictionary<CollectibleObject, double> fixedValues, HashSet<CollectibleObject> excluded, bool overrideBlacklist)
        {
            bool any = false;
            foreach (CollectibleObject c in index.Match(pattern))
            {
                any = true;
                if (!overrideBlacklist && excluded.Contains(c)) continue; // default files never resurrect blacklisted things
                if (value > 0)
                {
                    fixedValues[c] = value;
                    excluded.Remove(c);
                }
                else
                {
                    fixedValues.Remove(c);
                    excluded.Add(c);
                }
            }
            return any;
        }

        /// <summary>Rounds away floating-point noise (33.333333334 → 33.3333) without changing meaningful precision.</summary>
        static double Tidy(double v)
        {
            if (v >= 1000) return Math.Round(v, 1);
            return Math.Round(v, 4);
        }
    }
}

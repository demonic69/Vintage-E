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
        /// <summary>Items whose value never settled and that kept the first value they got.</summary>
        public int KeptAtFirstValue;
        public int LoopFrozen;
        public int UnmatchedRules;
        public int SkippedRecipes;
        public int SiblingCount;
        public int RelativeCount;
        public int FallbackCount;
        /// <summary>Items valued from trader prices, and the measured EMC per unit of trader currency.</summary>
        public int TraderCount;
        /// <summary>Items valued by "defaultValues" rules (nothing else could value them).</summary>
        public int DefaultCount;
        public double TraderRate;
        public long Milliseconds;
        public SortedDictionary<string, int> Sources;
        /// <summary>Blocks/items that appear in the creative inventory (i.e. are real, obtainable content), excluding the blacklist.</summary>
        public int CreativeTotal;
        /// <summary>Of those, the ones that still have no value.</summary>
        public List<CollectibleObject> Missing = new List<CollectibleObject>();
        /// <summary>Everything the blacklist keeps from having a value (for the /emc blacklist window).</summary>
        public List<CollectibleObject> Blacklisted = new List<CollectibleObject>();
        /// <summary>True when the values were loaded from the on-disk cache instead of being mapped.</summary>
        public bool FromCache;
        public string CachedAt;
        /// <summary>Why each item has its value ("base value", "grid: 1 game:log-... -> 12", ...). Empty when loaded from cache.</summary>
        public Dictionary<CollectibleObject, string> Origins = new Dictionary<CollectibleObject, string>();
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
            var collector = new ConversionCollector(sapi, index, logger, config);
            collector.CollectAll();
            report.SkippedRecipes = collector.SkippedRecipes;

            // 4. Propagate.
            var edges = new List<EmcSolver<CollectibleObject>.Edge>();
            var joints = new List<EmcSolver<CollectibleObject>.JointEdge>();
            foreach (Conversion conv in collector.Conversions)
            {
                if (conv.JointOutputs != null)
                {
                    joints.Add(new EmcSolver<CollectibleObject>.JointEdge
                    {
                        Input = conv.Inputs[0].Choices[0],
                        InputQuantity = conv.Inputs[0].Quantity,
                        Outputs = conv.JointOutputs.Select(o => o.Key).ToArray(),
                        OutputQuantities = conv.JointOutputs.Select(o => o.Value).ToArray(),
                        Tag = conv
                    });
                    continue;
                }
                if (excluded.Contains(conv.Output)) continue;
                var edge = new EmcSolver<CollectibleObject>.Edge
                {
                    Output = conv.Output,
                    OutputQuantity = conv.OutputQuantity,
                    Choices = conv.Inputs.Select(i => i.Choices).ToArray(),
                    Quantities = conv.Inputs.Select(i => i.Quantity).ToArray(),
                    Reversible = conv.Kind == ConversionKind.Processing,
                    ExtraCost = conv.ExtraCost,
                    Tag = conv
                };
                if (conv.Returned != null)
                {
                    edge.Returned = conv.Returned.Select(r => r.Key).ToArray();
                    edge.ReturnedQuantities = conv.Returned.Select(r => r.Value).ToArray();
                }
                edges.Add(edge);
            }
            EmcSolver<CollectibleObject> solver = Solve(fixedValues, excluded, edges, joints);

            // 5. Last resort for whatever is still unvalued: borrow from its closest variant sibling
            //    (ore-poor-galena-chalk ← ore-poor-galena-granite, bread-spelt-charred ← bread-spelt-perfect).
            //    The highest sibling value is used, so a guessed value can never be cheaper to create than to burn.
            //    Then, only if the server owner asked for it, a flat fallback value for anything still left.
            var extra = new Dictionary<CollectibleObject, double>();
            var extraOrigin = new Dictionary<CollectibleObject, string>();
            // Data-driven ratios first (metal bit = 1/20 ingot...), then variant siblings for whatever is left.
            foreach (var kv in RelativeValues(index, solver.Values, excluded, extraOrigin)) extra[kv.Key] = kv.Value;
            report.RelativeCount = extra.Count;
            if (config.UseTraderPrices)
            {
                try
                {
                    var traders = new TraderPrices(sapi, logger);
                    traders.Collect();
                    double rate = traders.MeasureRate(solver.Values, out int samples);
                    report.TraderRate = rate;
                    logger.Notification("EMC: {0} traders price {1} different items; {2} of them already have a value, giving {3:0.##} EMC per unit of trader currency (median).",
                        traders.TraderCount, traders.PricePerItem.Count, samples, rate);
                    if (rate > 0)
                    {
                        // The currency itself (rusty gears) is worth what it buys, so selling things for gears and
                        // burning the gears can never come out ahead.
                        foreach (string pattern in rules.TraderCurrency)
                        {
                            foreach (CollectibleObject c in index.Match(pattern))
                            {
                                if (excluded.Contains(c) || PinnedByConfig(index, c)) continue;
                                extra[c] = rate;
                                extraOrigin[c] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "trader currency: {0:0.##} EMC per gear, measured on {1} traded items", rate, samples);
                            }
                        }
                        foreach (var kv in traders.PricePerItem)
                        {
                            CollectibleObject c = kv.Key;
                            if (solver.Values.ContainsKey(c) || excluded.Contains(c) || extra.ContainsKey(c)) continue;
                            extra[c] = kv.Value * rate;
                            extraOrigin[c] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "trader price: {0:0.##} x {1:0.##} EMC per gear", kv.Value, rate);
                            report.TraderCount++;
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.Warning("EMC: could not read trader prices: {0}", e.Message);
                }
            }
            if (config.InferFromVariantSiblings)
            {
                foreach (var kv in SiblingValues(index, solver.Values, excluded, extraOrigin))
                {
                    if (extra.ContainsKey(kv.Key)) continue;
                    extra[kv.Key] = kv.Value;
                    report.SiblingCount++;
                }
            }
            // Family-level guesses from the value files, for things no recipe, trader or variant could value.
            var defaults = new Dictionary<CollectibleObject, (double value, string pattern)>();
            foreach (EmcValueRule rule in rules.Defaults)
            {
                if (config.DisableBuiltInBaseValues && rule.Source.StartsWith(VintageEModSystem.ModId + ":", StringComparison.Ordinal)) continue;
                foreach (CollectibleObject c in index.Match(rule.Pattern))
                {
                    if (solver.Values.ContainsKey(c) || excluded.Contains(c) || extra.ContainsKey(c)) continue;
                    if (rule.Value > 0) defaults[c] = (rule.Value, rule.Pattern);
                    else defaults.Remove(c);
                }
            }
            foreach (var kv in defaults)
            {
                extra[kv.Key] = kv.Value.value;
                extraOrigin[kv.Key] = "default value (" + kv.Value.pattern + ")";
                report.DefaultCount++;
            }
            if (config.FallbackValue > 0)
            {
                foreach (CollectibleObject c in index.All)
                {
                    if (IsListed(c) && !excluded.Contains(c) && !solver.Values.ContainsKey(c) && !extra.ContainsKey(c))
                    {
                        extra[c] = config.FallbackValue;
                        extraOrigin[c] = "FallbackValue from the server config";
                        report.FallbackCount++;
                    }
                }
            }
            EmcSolver<CollectibleObject> firstSolver = solver;
            if (extra.Count > 0)
            {
                var seeded = new Dictionary<CollectibleObject, double>(fixedValues);
                foreach (var kv in extra) seeded[kv.Key] = kv.Value;
                solver = Solve(seeded, excluded, edges, joints);
                // Backward inference depends on what's known when it runs, so the second solve can occasionally
                // miss something the first one valued. Keep those first values.
                foreach (var kv in firstSolver.Values)
                {
                    if (solver.Values.ContainsKey(kv.Key) || excluded.Contains(kv.Key) || solver.Unstable.Contains(kv.Key)) continue;
                    solver.Values[kv.Key] = kv.Value;
                    if (firstSolver.Via.TryGetValue(kv.Key, out object via)) solver.Via[kv.Key] = via;
                    if (firstSolver.Inferred.ContainsKey(kv.Key)) solver.Inferred[kv.Key] = kv.Value;
                }
            }

            report.Values = new Dictionary<CollectibleObject, double>();
            foreach (var kv in solver.Values)
            {
                if (kv.Value > 0 && !excluded.Contains(kv.Key) && !solver.Unstable.Contains(kv.Key)) report.Values[kv.Key] = Tidy(kv.Value);
            }
            report.BaseCount = fixedValues.Count;
            report.InferredCount = solver.Inferred.Keys.Count(k => report.Values.ContainsKey(k));
            report.DerivedCount = report.Values.Count - report.BaseCount - report.InferredCount - report.SiblingCount - report.FallbackCount - report.RelativeCount - report.TraderCount - report.DefaultCount;
            report.ConversionCount = solver.Edges.Count + solver.JointEdges.Count;
            FillCoverage(index, report, excluded);
            report.Sources = collector.SourceCounts;
            report.SourceCount = collector.SourceCounts.Count(s => s.Value > 0);
            report.UnstableRemoved = solver.Unstable.Count;
            report.KeptAtFirstValue = solver.KeptAtFirstValue.Count;
            report.LoopFrozen = solver.Frozen.Count;
            foreach (CollectibleObject c in report.Values.Keys)
            {
                string origin;
                if (extraOrigin.TryGetValue(c, out string how)) origin = how;
                else if (fixedValues.ContainsKey(c)) origin = "base value";
                else if (solver.Via.TryGetValue(c, out object via))
                {
                    object tag = via is EmcSolver<CollectibleObject>.Edge ed ? ed.Tag : via is EmcSolver<CollectibleObject>.JointEdge je ? je.Tag : via;
                    origin = (solver.Inferred.ContainsKey(c) ? "worked backwards from " : "") + Describe(tag as Conversion);
                    if (solver.Frozen.Contains(c)) origin += " (recipe loop: kept its first value)";
                }
                else origin = "?";
                report.Origins[c] = origin;
            }

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
            foreach (string pattern in rules.Blacklist)
            {
                excluded.UnionWith(index.Match(pattern));
            }
            foreach (string pattern in rules.Allow)
            {
                excluded.ExceptWith(index.Match(pattern));
            }
            // The server's own lists last: its blacklist (single items or whole mods), then its exceptions.
            foreach (string pattern in config.Blacklist)
            {
                excluded.UnionWith(index.Match(pattern));
            }
            foreach (string pattern in config.Allow)
            {
                excluded.ExceptWith(index.Match(pattern));
            }

            // 2. Seed values. Later sources win: asset files (ours, then other mods') → item attributes → server config.
            fixedValues = new Dictionary<CollectibleObject, double>();
            foreach (EmcValueRule rule in rules.Values)
            {
                if (config.DisableBuiltInBaseValues && rule.Source.StartsWith(VintageEModSystem.ModId + ":", StringComparison.Ordinal)) continue;
                if (!Apply(index, rule.Pattern, rule.Value, fixedValues, excluded, overrideBlacklist: false, perLitre: rule.PerLitre))
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
            report.Blacklisted = excluded.Where(c => c?.Code != null).ToList();
            foreach (CollectibleObject c in index.All)
            {
                if (!IsListed(c) || excluded.Contains(c)) continue;
                report.CreativeTotal++;
                if (!report.Values.ContainsKey(c)) report.Missing.Add(c);
            }
        }

        /// <summary>Real, obtainable content: anything shown in a creative inventory tab.</summary>
        static bool IsListed(CollectibleObject c) => (c.CreativeInventoryTabs?.Length ?? 0) > 0 || (c.CreativeInventoryStacks?.Length ?? 0) > 0;

        EmcSolver<CollectibleObject> Solve(Dictionary<CollectibleObject, double> fixedValues, HashSet<CollectibleObject> excluded, List<EmcSolver<CollectibleObject>.Edge> edges, List<EmcSolver<CollectibleObject>.JointEdge> joints)
        {
            var solver = new EmcSolver<CollectibleObject>
            {
                Fixed = fixedValues,
                Excluded = excluded,
                MaxPasses = config.MaxMappingPasses,
                InferBackwards = config.InferValuesFromProcessing,
                InferFromCrafting = config.InferValuesFromProcessing && config.InferValuesFromCrafting
            };
            solver.Edges.AddRange(edges);
            solver.JointEdges.AddRange(joints);
            solver.Solve();
            return solver;
        }

        /// <summary>
        /// relativeValues rules: an unvalued item matching Code gets Ratio × the value of the item named by Of,
        /// with the '*' part of Code carried over ("game:metalbit-*" of "game:ingot-*" → metalbit-copper from ingot-copper).
        /// </summary>
        /// <summary>True if the server owner set this item's value by hand (ModConfig BaseValues/FixedValues).</summary>
        bool PinnedByConfig(CollectibleIndex index, CollectibleObject c)
        {
            foreach (string key in config.FixedValues.Keys.Concat(config.BaseValues.Keys))
            {
                if (index.Match(key).Contains(c)) return true;
            }
            return false;
        }

        /// <summary>"grid: 1 game:log-placed-oak-ud (+11 more) -> 12" — a short, human-readable description of a conversion.</summary>
        internal static string Describe(Conversion conv)
        {
            if (conv == null) return "?";
            static string Num(double d) => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            static string Choice(CollectibleObject[] choices) => choices.Length == 0 ? "?" : choices[0].Code + (choices.Length > 1 ? $" (or {choices.Length - 1} more)" : "");
            string inputs = string.Join(" + ", conv.Inputs.Select(i => Num(i.Quantity) + " " + Choice(i.Choices)));
            string outputs = conv.JointOutputs != null
                ? string.Join(" + ", conv.JointOutputs.Select(o => Num(o.Value) + " " + o.Key.Code))
                : Num(conv.OutputQuantity) + " " + conv.Output?.Code;
            string extra = conv.ExtraCost > 0 ? " (+" + Num(conv.ExtraCost) + " time bonus)" : "";
            string returned = conv.Returned != null && conv.Returned.Count > 0 ? " (returns " + string.Join(", ", conv.Returned.Select(r => r.Key.Code)) + ")" : "";
            return $"{conv.Source}: {inputs}{extra} -> {outputs}{returned}";
        }

        Dictionary<CollectibleObject, double> RelativeValues(CollectibleIndex index, Dictionary<CollectibleObject, double> values, HashSet<CollectibleObject> excluded, Dictionary<CollectibleObject, string> origin)
        {
            var result = new Dictionary<CollectibleObject, double>();
            foreach (RelativeValueRule rule in rules.Relative)
            {
                int star = rule.Code.IndexOf('*');
                foreach (CollectibleObject c in index.Match(rule.Code))
                {
                    if (values.ContainsKey(c) || excluded.Contains(c) || result.ContainsKey(c)) continue;
                    string target = rule.Of;
                    if (star >= 0)
                    {
                        var pattern = new AssetLocation(rule.Code.StartsWith("item:") || rule.Code.StartsWith("block:") ? rule.Code.Substring(rule.Code.IndexOf(':') + 1) : rule.Code);
                        string prefix = pattern.Path.Substring(0, pattern.Path.IndexOf('*'));
                        string suffix = pattern.Path.Substring(pattern.Path.IndexOf('*') + 1);
                        string path = c.Code.Path;
                        if (path.Length < prefix.Length + suffix.Length) continue;
                        string captured = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
                        target = rule.Of.Replace("*", captured);
                    }
                    double best = 0;
                    foreach (CollectibleObject source in index.Match(target))
                    {
                        if (values.TryGetValue(source, out double v) && v > best) best = v;
                    }
                    if (best > 0)
                    {
                        result[c] = best * rule.Ratio;
                        origin[c] = $"relativeValues rule: {rule.Ratio} x {target}";
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// For each unvalued collectible, find valued ones whose code differs in exactly one dash-separated part,
        /// trying the last part first (rock type, colour, orientation...) and never the first (the item family).
        /// </summary>
        static Dictionary<CollectibleObject, double> SiblingValues(CollectibleIndex index, Dictionary<CollectibleObject, double> values, HashSet<CollectibleObject> excluded, Dictionary<CollectibleObject, string> origin)
        {
            var groups = new Dictionary<string, (double value, CollectibleObject from)>();
            foreach (var kv in values)
            {
                if (kv.Value <= 0 || kv.Key.Code == null) continue;
                string[] parts = kv.Key.Code.Path.Split('-');
                for (int k = 1; k < parts.Length; k++)
                {
                    string key = SiblingKey(kv.Key, parts, k);
                    if (!groups.TryGetValue(key, out var prev) || kv.Value > prev.value) groups[key] = (kv.Value, kv.Key);
                }
            }

            var result = new Dictionary<CollectibleObject, double>();
            foreach (CollectibleObject c in index.All)
            {
                if (values.ContainsKey(c) || excluded.Contains(c)) continue;
                string[] parts = c.Code.Path.Split('-');
                for (int k = parts.Length - 1; k >= 1; k--)
                {
                    if (groups.TryGetValue(SiblingKey(c, parts, k), out var g))
                    {
                        result[c] = g.value;
                        origin[c] = "same as its variant " + g.from.Code + " (differs in: " + DifferingGroups(c, g.from) + ")";
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>The variant group codes (as the block/item type defines them) whose states differ between two variants.</summary>
        internal static string DifferingGroups(CollectibleObject a, CollectibleObject b)
        {
            var va = a.Variant;
            var vb = b.Variant;
            if (va == null || vb == null || va.Count == 0) return "?";
            var diff = new List<string>();
            foreach (var kv in va)
            {
                if (!vb.TryGetValue(kv.Key, out string other) || other != kv.Value) diff.Add(kv.Key);
            }
            return diff.Count == 0 ? "?" : string.Join("+", diff);
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

        static bool Apply(CollectibleIndex index, string pattern, double value, Dictionary<CollectibleObject, double> fixedValues, HashSet<CollectibleObject> excluded, bool overrideBlacklist, bool perLitre = false)
        {
            bool any = false;
            foreach (CollectibleObject c in index.Match(pattern))
            {
                any = true;
                if (!overrideBlacklist && excluded.Contains(c)) continue; // default files never resurrect blacklisted things
                if (value > 0)
                {
                    double itemsPerLitre = perLitre ? EmcRegistry.ItemsPerLitre(c) : 0;
                    fixedValues[c] = itemsPerLitre > 0 ? value / itemsPerLitre : value;
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

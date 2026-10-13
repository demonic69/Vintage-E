using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace VintageE
{
    public class EmcValueRule
    {
        public string Pattern;
        public double Value;
        public string Source;
        /// <summary>From "litreValues": Value is per litre of liquid and gets divided by the liquid's itemsPerLitre.</summary>
        public bool PerLitre;
    }

    /// <summary>"game:metalbit-*" is worth 0.05 × "game:ingot-*" (the '*' part is carried over).</summary>
    public class RelativeValueRule
    {
        public string Code;
        public string Of;
        public double Ratio;
        public string Source;
    }

    public class SwapRule
    {
        public string From;
        public string To;
    }

    public class WorldTransmutationRules
    {
        /// <summary>Variant name → ordered list of values to cycle through (e.g. "rock" → granite, andesite, ...).</summary>
        public Dictionary<string, List<string>> VariantCycles = new Dictionary<string, List<string>>();
        /// <summary>Direct block swaps, tried before variant cycles. A single '*' in From is carried over into To.</summary>
        public List<SwapRule> Swaps = new List<SwapRule>();
        /// <summary>Blocks the philosopher's stone never touches.</summary>
        public List<string> Blacklist = new List<string>();
    }

    /// <summary>
    /// Everything data-driven that feeds the mapper, merged from every loaded mod's
    /// <c>assets/&lt;domain&gt;/config/vintagee-emc*.json</c>. This mod's file loads first and other
    /// mods load after it, so a mod can add or override values just by shipping that file
    /// (or JSON-patching ours). Within the merged list, later rules win.
    /// </summary>
    public class EmcRuleSet
    {
        public const string AssetPath = "config/vintagee-emc";

        public List<EmcValueRule> Values = new List<EmcValueRule>();
        public List<RelativeValueRule> Relative = new List<RelativeValueRule>();
        public List<string> Blacklist = new List<string>();
        /// <summary>Exceptions to the blacklist ("allow": ["game:creature-butterfly-*"]), applied after it.</summary>
        public List<string> Allow = new List<string>();
        public WorldTransmutationRules WorldTransmutation = new WorldTransmutationRules();
        public List<string> LoadedFiles = new List<string>();

        public static EmcRuleSet Load(ICoreAPI api, ILogger logger)
        {
            var set = new EmcRuleSet();
            List<IAsset> assets;
            try
            {
                assets = api.Assets.GetMany(AssetPath, null, true);
            }
            catch (Exception e)
            {
                logger.Error("Could not enumerate {0} assets: {1}", AssetPath, e.Message);
                return set;
            }

            // Our own defaults first, everybody else afterwards (stable, alphabetical) so they can override.
            var ordered = assets
                .Where(a => a.Location.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Location.Domain == VintageEModSystem.ModId ? 0 : 1)
                .ThenBy(a => a.Location.Domain, StringComparer.Ordinal)
                .ThenBy(a => a.Location.Path, StringComparer.Ordinal);

            foreach (IAsset asset in ordered)
            {
                try
                {
                    JObject root = JObject.Parse(asset.ToText());
                    set.Merge(root, asset.Location.ToString());
                    set.LoadedFiles.Add(asset.Location.ToString());
                }
                catch (Exception e)
                {
                    logger.Error("Skipping malformed EMC file {0}: {1}", asset.Location, e.Message);
                }
            }
            logger.Notification("Loaded {0} EMC base value rules from {1} file(s): {2}", set.Values.Count, set.LoadedFiles.Count, string.Join(", ", set.LoadedFiles));
            return set;
        }

        void Merge(JObject root, string source)
        {
            if (root["values"] is JObject values)
            {
                foreach (JProperty prop in values.Properties())
                {
                    if (prop.Value.Type == JTokenType.Integer || prop.Value.Type == JTokenType.Float)
                    {
                        Values.Add(new EmcValueRule { Pattern = prop.Name, Value = prop.Value.Value<double>(), Source = source });
                    }
                }
            }
            if (root["litreValues"] is JObject litres)
            {
                foreach (JProperty prop in litres.Properties())
                {
                    if (prop.Value.Type == JTokenType.Integer || prop.Value.Type == JTokenType.Float)
                    {
                        Values.Add(new EmcValueRule { Pattern = prop.Name, Value = prop.Value.Value<double>(), Source = source, PerLitre = true });
                    }
                }
            }
            if (root["relativeValues"] is JArray rel)
            {
                foreach (JToken tok in rel)
                {
                    string code = (string)tok["code"], of = (string)tok["of"];
                    double ratio = tok["ratio"]?.Value<double>() ?? 0;
                    if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(of) && ratio > 0)
                    {
                        Relative.Add(new RelativeValueRule { Code = code, Of = of, Ratio = ratio, Source = source });
                    }
                }
            }
            if (root["blacklist"] is JArray bl)
            {
                Blacklist.AddRange(bl.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            if (root["allow"] is JArray allow)
            {
                Allow.AddRange(allow.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            if (root["worldTransmutation"] is JObject wt)
            {
                if (wt["variantCycles"] is JObject cycles)
                {
                    foreach (JProperty prop in cycles.Properties())
                    {
                        if (prop.Value is JArray arr)
                        {
                            WorldTransmutation.VariantCycles[prop.Name] = arr.Values<string>().ToList();
                        }
                    }
                }
                if (wt["swaps"] is JArray swaps)
                {
                    foreach (JToken tok in swaps)
                    {
                        string from = (string)tok["from"], to = (string)tok["to"];
                        if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
                        {
                            WorldTransmutation.Swaps.Add(new SwapRule { From = from, To = to });
                        }
                    }
                }
                if (wt["blacklist"] is JArray wbl)
                {
                    WorldTransmutation.Blacklist.AddRange(wbl.Values<string>());
                }
            }
        }
    }
}

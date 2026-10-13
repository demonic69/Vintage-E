using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace VintageE
{
    /// <summary>
    /// Decides what a block turns into when hit with the philosopher's stone, from the data-driven
    /// rules in config/vintagee-emc.json: explicit swaps first (sand → gravel ...), then variant cycles
    /// (granite → andesite → ..., oak → birch → ...). Sneaking walks a cycle backwards.
    /// Blocks with a block entity are never changed, so no container or machine can lose its contents.
    /// </summary>
    public class WorldTransmutation
    {
        readonly ICoreAPI api;
        readonly WorldTransmutationRules rules;
        readonly Dictionary<long, int> cache = new Dictionary<long, int>();
        readonly List<AssetLocation> blacklist = new List<AssetLocation>();

        public WorldTransmutation(ICoreAPI api, WorldTransmutationRules rules)
        {
            this.api = api;
            this.rules = rules ?? new WorldTransmutationRules();
            foreach (string b in this.rules.Blacklist) blacklist.Add(new AssetLocation(b));
        }

        public Block GetTarget(Block from, bool reverse)
        {
            if (from?.Code == null || from.Id == 0) return null;
            long key = ((long)from.Id << 1) | (reverse ? 1L : 0L);
            if (cache.TryGetValue(key, out int cached)) return cached <= 0 ? null : api.World.GetBlock(cached);

            Block target = Compute(from, reverse);
            cache[key] = target?.Id ?? 0;
            return target;
        }

        Block Compute(Block from, bool reverse)
        {
            if (from.EntityClass != null) return null;
            foreach (AssetLocation b in blacklist) if (WildcardUtil.Match(b, from.Code)) return null;

            foreach (SwapRule swap in rules.Swaps)
            {
                AssetLocation targetCode = Substitute(swap.From, swap.To, from.Code);
                if (targetCode == null) continue;
                Block target = api.World.GetBlock(targetCode);
                if (target != null && target.Id != 0 && target.Id != from.Id && target.EntityClass == null) return target;
            }

            if (from.Variant == null) return null;
            foreach (var cycle in rules.VariantCycles)
            {
                if (!from.Variant.TryGetValue(cycle.Key, out string current)) continue;
                List<string> values = cycle.Value;
                int idx = values.IndexOf(current);
                if (idx < 0) continue;
                for (int step = 1; step < values.Count; step++)
                {
                    int next = reverse ? (idx - step + values.Count * step) % values.Count : (idx + step) % values.Count;
                    Block target = api.World.GetBlock(from.CodeWithVariant(cycle.Key, values[next]));
                    if (target != null && target.Id != 0 && target.EntityClass == null) return target;
                }
            }
            return null;
        }

        /// <summary>"game:sand-*" / "game:gravel-*" applied to game:sand-granite gives game:gravel-granite. Null if it doesn't match.</summary>
        static AssetLocation Substitute(string fromPattern, string toPattern, AssetLocation code)
        {
            var from = new AssetLocation(fromPattern);
            if (from.Domain != "*" && from.Domain != code.Domain) return null;
            string path = code.Path;
            int star = from.Path.IndexOf('*');
            if (star < 0)
            {
                return from.Path == path ? new AssetLocation(toPattern) : null;
            }
            string prefix = from.Path.Substring(0, star);
            string suffix = from.Path.Substring(star + 1);
            if (suffix.Contains('*') || path.Length < prefix.Length + suffix.Length) return null;
            if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(suffix, StringComparison.Ordinal)) return null;
            string captured = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
            var to = new AssetLocation(toPattern.Replace("*", captured));
            if (to.Domain == "*") to.Domain = code.Domain;
            return to;
        }
    }
}

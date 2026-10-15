using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>
    /// Last-resort values for things no recipe, drop, rule or trader could value, from their variants.
    /// Uses the variant groups the block/item type itself defines (rock, colour, wood, ore, metal...):
    /// <list type="bullet">
    /// <item>a value is copied freely across groups that only change the look or placement ("copyAcross" in the value
    ///   files: colour, rock, wood, facing, snow cover...) - the same thing in another rock is worth the same;</item>
    /// <item>it is never copied across groups that change what the thing is ("neverAcross": ore, metal, gem, mold
    ///   type...) - cinnabar ore is not gold-bearing quartz, so it stays unvalued (and shows in /emc missing);</item>
    /// <item>other mods' variants of a base-game family (their own wood types for the game's logs, say) borrow the
    ///   base game's value across a copyAcross group;</item>
    /// <item>groups in neither list are only used afterwards, if "otherGroups" allows it.</item>
    /// </list>
    /// The highest matching value is used, so a guessed value is never cheaper to create than its siblings.
    /// </summary>
    public sealed class VariantGuesser
    {
        readonly EmcRuleSet rules;

        public int SameFamilyCount, OtherModCount, OtherGroupCount;

        public VariantGuesser(EmcRuleSet rules)
        {
            this.rules = rules;
        }

        enum Tier { CopyAcross = 0, OtherMod = 1, OtherGroup = 2 }

        /// <summary>Every valued variant sharing one signature. Look/placement guesses take the highest; the rougher
        /// guesses across other groups take the middle one, so one odd variant (a gear worth 8,192) can't set the rest.</summary>
        sealed class Best
        {
            public double Value;
            public CollectibleObject From;
            public readonly List<KeyValuePair<double, CollectibleObject>> All = new List<KeyValuePair<double, CollectibleObject>>();

            public KeyValuePair<double, CollectibleObject> Median()
            {
                var sorted = All.OrderBy(x => x.Key).ToList();
                return sorted[(sorted.Count - 1) / 2];
            }
        }

        /// <param name="otherGroups">False: only copyAcross groups and other mods' families. True: only the remaining groups.</param>
        public Dictionary<CollectibleObject, double> Guess(CollectibleIndex index, IReadOnlyDictionary<CollectibleObject, double> values,
            System.Func<CollectibleObject, bool> skip, Dictionary<CollectibleObject, string> origin, bool otherGroups)
        {
            var same = new Dictionary<string, Best>();
            var baseGame = new Dictionary<string, Best>();
            var byParts = new Dictionary<string, Best>();
            foreach (var kv in values)
            {
                CollectibleObject v = kv.Key;
                if (kv.Value <= 0 || v.Code == null) continue;
                var groups = v.VariantStrict;
                if (groups != null && groups.Count > 0)
                {
                    foreach (string g in groups.Keys)
                    {
                        Keep(same, Signature(v, g, anyDomain: false), kv.Value, v);
                        if (v.Code.Domain == "game") Keep(baseGame, Signature(v, g, anyDomain: true), kv.Value, v);
                    }
                }
                else if (otherGroups)
                {
                    string[] parts = v.Code.Path.Split('-');
                    for (int k = 1; k < parts.Length; k++) Keep(byParts, PartsKey(v, parts, k), kv.Value, v);
                }
            }

            var result = new Dictionary<CollectibleObject, double>();
            foreach (CollectibleObject c in index.All)
            {
                if (values.ContainsKey(c) || skip(c)) continue;
                var groups = c.VariantStrict;
                if (groups == null || groups.Count == 0)
                {
                    if (!otherGroups) continue;
                    string[] parts = c.Code.Path.Split('-');
                    for (int k = parts.Length - 1; k >= 1; k--)
                    {
                        if (byParts.TryGetValue(PartsKey(c, parts, k), out Best b))
                        {
                            var mid = b.Median();
                            result[c] = mid.Key;
                            origin[c] = MedianOrigin(mid.Value, b.All.Count, null);
                            OtherGroupCount++;
                            break;
                        }
                    }
                    continue;
                }

                Best pick = null;
                string pickGroup = null;
                Tier pickTier = otherGroups ? Tier.OtherGroup : Tier.CopyAcross;
                // First pass: copyAcross groups only; second pass: groups in neither list.
                string family = c.FirstCodePart();
                foreach (string g in groups.Keys.Reverse())
                {
                    if (Never(family, g) || Copyable(family, g) == otherGroups) continue;
                    if (same.TryGetValue(Signature(c, g, anyDomain: false), out Best b) && (pick == null || b.Value > pick.Value))
                    {
                        pick = b;
                        pickGroup = g;
                    }
                }
                // Another mod's variant of a base-game family (its own wood type, rock...).
                if (pick == null && !otherGroups && c.Code.Domain != "game")
                {
                    foreach (string g in groups.Keys.Reverse())
                    {
                        if (Never(family, g) || !Copyable(family, g)) continue;
                        if (baseGame.TryGetValue(Signature(c, g, anyDomain: true), out Best b) && (pick == null || b.Value > pick.Value))
                        {
                            pick = b;
                            pickGroup = g;
                            pickTier = Tier.OtherMod;
                        }
                    }
                }
                if (pick == null) continue;
                if (pickTier == Tier.OtherGroup)
                {
                    var mid = pick.Median();
                    result[c] = mid.Key;
                    origin[c] = MedianOrigin(mid.Value, pick.All.Count, pickGroup);
                    OtherGroupCount++;
                    continue;
                }
                result[c] = pick.Value;
                switch (pickTier)
                {
                    case Tier.CopyAcross:
                        origin[c] = "same as its variant " + pick.From.Code + " (differs in: " + pickGroup + ")";
                        SameFamilyCount++;
                        break;
                    case Tier.OtherMod:
                        origin[c] = "same as the base game's " + pick.From.Code + " (another mod's " + pickGroup + ")";
                        OtherModCount++;
                        break;
                }
            }
            return result;
        }

        /// <summary>A group in the never list, by name ("metal") or for one family only ("ore:type").</summary>
        bool Never(string family, string group) => rules.NeverAcrossGroups.Contains(group) || rules.NeverAcrossGroups.Contains(family + ":" + group);

        bool Copyable(string family, string group) => rules.CopyAcrossGroups.Contains(group) || rules.CopyAcrossGroups.Contains(family + ":" + group);

        static void Keep(Dictionary<string, Best> dict, string key, double value, CollectibleObject from)
        {
            if (!dict.TryGetValue(key, out Best b)) dict[key] = b = new Best { Value = value, From = from };
            else if (value > b.Value) { b.Value = value; b.From = from; }
            b.All.Add(new KeyValuePair<double, CollectibleObject>(value, from));
        }

        static string MedianOrigin(CollectibleObject like, int count, string group) => count == 1
            ? "guessed from its only valued variant " + like.Code + (group == null ? "" : " (differs in: " + group + ")")
            : "guessed: the middle value of its " + count + " valued variants" + (group == null ? "" : " that differ in " + group) + " (" + like.Code + ")";

        /// <summary>Type + every variant group's state except <paramref name="wild"/> (and the domain unless <paramref name="anyDomain"/>).</summary>
        static string Signature(CollectibleObject c, string wild, bool anyDomain)
        {
            var sb = new StringBuilder();
            sb.Append(c.ItemClass == EnumItemClass.Block ? 'b' : 'i').Append('|').Append(anyDomain ? "*" : c.Code.Domain).Append('|').Append(c.FirstCodePart()).Append('|');
            foreach (var kv in c.VariantStrict)
            {
                sb.Append(kv.Key).Append('=').Append(kv.Key == wild ? "*" : kv.Value).Append(';');
            }
            return sb.ToString();
        }

        static string PartsKey(CollectibleObject c, string[] parts, int skip)
        {
            var sb = new StringBuilder();
            sb.Append(c.ItemClass == EnumItemClass.Block ? 'b' : 'i').Append('|').Append(c.Code.Domain).Append('|').Append(parts.Length).Append('|').Append(skip).Append('|');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i != skip) sb.Append(parts[i]);
                sb.Append('-');
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Estimates for things no recipe or variant could value, from what they do:
    /// <list type="bullet">
    /// <item>fuels by how hot and how long they burn, at the rate the reference fuels ("fuelReference" in the value
    ///   files: charcoal, coal, coke...) are valued at;</item>
    /// <item>food by how filling it is, at the median value per point of satiety of valued food in the same category
    ///   (fruit, vegetable, protein, grain, dairy).</item>
    /// </list>
    /// </summary>
    public static class UseEstimates
    {
        public static Dictionary<CollectibleObject, double> Fuel(CollectibleIndex index, IReadOnlyDictionary<CollectibleObject, double> values,
            IEnumerable<string> referencePatterns, System.Func<CollectibleObject, bool> skip, Dictionary<CollectibleObject, string> origin)
        {
            var rates = new List<double>();
            foreach (string pattern in referencePatterns)
            {
                foreach (CollectibleObject c in index.Match(pattern))
                {
                    double energy = Energy(c);
                    if (energy > 0 && values.TryGetValue(c, out double v) && v > 0) rates.Add(v / energy);
                }
            }
            var result = new Dictionary<CollectibleObject, double>();
            if (rates.Count == 0) return result;
            double rate = Median(rates);
            foreach (CollectibleObject c in index.All)
            {
                if (values.ContainsKey(c) || skip(c)) continue;
                var props = c.CombustibleProps;
                if (props == null || props.SmeltedStack != null) continue; // things that smelt into something are valued from that
                double energy = Energy(c);
                if (energy <= 0) continue;
                result[c] = rate * energy;
                origin[c] = string.Format(CultureInfo.InvariantCulture, "as fuel: burns at {0} °C for {1:0.#} s ({2:0.####} EMC per °C·s, like charcoal and coal)",
                    props.BurnTemperature, props.BurnDuration, rate);
            }
            return result;
        }

        static double Energy(CollectibleObject c)
        {
            var p = c.CombustibleProps;
            if (p == null || p.BurnDuration <= 0 || p.BurnTemperature <= 0) return 0;
            return p.BurnDuration * p.BurnTemperature;
        }

        public static Dictionary<CollectibleObject, double> Food(CollectibleIndex index, IReadOnlyDictionary<CollectibleObject, double> values,
            System.Func<CollectibleObject, bool> skip, Dictionary<CollectibleObject, string> origin)
        {
            var byCategory = new Dictionary<EnumFoodCategory, List<double>>();
            var all = new List<double>();
            foreach (var kv in values)
            {
                var n = kv.Key.NutritionProps;
                if (n == null || n.Satiety <= 0 || kv.Value <= 0) continue;
                double r = kv.Value / n.Satiety;
                if (!byCategory.TryGetValue(n.FoodCategory, out var list)) byCategory[n.FoodCategory] = list = new List<double>();
                list.Add(r);
                all.Add(r);
            }
            var result = new Dictionary<CollectibleObject, double>();
            if (all.Count == 0) return result;
            double overall = Median(all);
            foreach (CollectibleObject c in index.All)
            {
                if (values.ContainsKey(c) || skip(c)) continue;
                var n = c.NutritionProps;
                if (n == null || n.Satiety <= 0) continue;
                double rate = byCategory.TryGetValue(n.FoodCategory, out var list) && list.Count >= 3 ? Median(list) : overall;
                result[c] = rate * n.Satiety;
                origin[c] = string.Format(CultureInfo.InvariantCulture, "as food: {0:0.#} satiety ({1}) at {2:0.###} EMC per point, like other valued {1} food",
                    n.Satiety, n.FoodCategory.ToString().ToLowerInvariant(), rate);
            }
            return result;
        }

        static double Median(List<double> list)
        {
            var sorted = list.OrderBy(x => x).ToList();
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
        }
    }
}

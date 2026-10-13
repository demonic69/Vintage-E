using System;
using System.Collections.Generic;
using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>The live EMC table for one side. The server fills it from the mapper; clients get a copy over the network.</summary>
    public class EmcRegistry
    {
        Dictionary<CollectibleObject, double> values = new Dictionary<CollectibleObject, double>();

        /// <summary>Whether damaged tools are worth proportionally less (mirrors the server config on clients).</summary>
        public bool DurabilityScales { get; set; } = true;

        public int Count => values.Count;

        public IReadOnlyDictionary<CollectibleObject, double> All => values;

        public void Replace(Dictionary<CollectibleObject, double> newValues)
        {
            values = new Dictionary<CollectibleObject, double>(newValues);
        }

        /// <summary>Value of one pristine item, or 0 if it has none.</summary>
        public double GetValue(CollectibleObject collectible)
        {
            if (collectible == null) return 0;
            return values.TryGetValue(collectible, out double v) ? v : 0;
        }

        public bool HasValue(CollectibleObject collectible) => collectible != null && values.ContainsKey(collectible);

        /// <summary>
        /// For liquid portion items (water, brine, ale...): how many items make one litre (100 for vanilla liquids).
        /// 0 for anything that isn't a liquid. Values are stored per item, so a litre is worth value × this.
        /// </summary>
        public static double ItemsPerLitre(CollectibleObject c)
        {
            JsonObject props = c?.Attributes?["waterTightContainerProps"];
            if (props == null || !props.Exists) return 0;
            double perLitre = props["itemsPerLitre"].AsDouble(1);
            return perLitre > 0 ? perLitre : 1;
        }

        /// <summary>Value of one item of this stack, accounting for wear.</summary>
        public double GetValue(ItemStack stack)
        {
            if (stack?.Collectible == null) return 0;
            double v = GetValue(stack.Collectible);
            if (v <= 0 || !DurabilityScales) return v;

            int max = stack.Collectible.GetMaxDurability(stack);
            if (max > 1)
            {
                int remaining = stack.Collectible.GetRemainingDurability(stack);
                v *= Math.Clamp(remaining / (double)max, 0, 1);
            }
            return v;
        }

        public EmcTablePacket ToPacket()
        {
            var itemIds = new List<int>();
            var itemValues = new List<double>();
            var blockIds = new List<int>();
            var blockValues = new List<double>();
            foreach (var kv in values)
            {
                if (kv.Key.ItemClass == EnumItemClass.Block) { blockIds.Add(kv.Key.Id); blockValues.Add(kv.Value); }
                else { itemIds.Add(kv.Key.Id); itemValues.Add(kv.Value); }
            }
            return new EmcTablePacket
            {
                ItemIds = itemIds.ToArray(),
                ItemValues = itemValues.ToArray(),
                BlockIds = blockIds.ToArray(),
                BlockValues = blockValues.ToArray(),
                DurabilityScales = DurabilityScales
            };
        }

        public void LoadFromPacket(EmcTablePacket packet, IWorldAccessor world)
        {
            var fresh = new Dictionary<CollectibleObject, double>();
            int[] ids = packet.ItemIds ?? Array.Empty<int>();
            double[] vals = packet.ItemValues ?? Array.Empty<double>();
            for (int i = 0; i < ids.Length && i < vals.Length; i++)
            {
                Item item = world.GetItem(ids[i]);
                if (item?.Code != null) fresh[item] = vals[i];
            }
            ids = packet.BlockIds ?? Array.Empty<int>();
            vals = packet.BlockValues ?? Array.Empty<double>();
            for (int i = 0; i < ids.Length && i < vals.Length; i++)
            {
                Block block = world.GetBlock(ids[i]);
                if (block?.Code != null) fresh[block] = vals[i];
            }
            values = fresh;
            DurabilityScales = packet.DurabilityScales;
        }
    }

    public static class EmcFormat
    {
        /// <summary>1234567 → "1,234,567"; 12.5 → "12.5"; 0.0312 → "0.031".</summary>
        public static string Format(double v)
        {
            if (v >= 100) return Math.Floor(v).ToString("#,0", CultureInfo.InvariantCulture);
            if (v >= 1) return v.ToString("0.##", CultureInfo.InvariantCulture);
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Stacks that carry other stacks inside their attributes (bags, filled pots, crates...) are containers with contents.
    /// </summary>
    public static class StackContents
    {
        public static bool HasNestedStacks(ItemStack stack)
        {
            return stack?.Attributes != null && ContainsStack(stack.Attributes, 0);
        }

        /// <summary>Every stack stored inside this stack's attributes (bucket contents, bag slots, pot ingredients...).</summary>
        public static List<ItemStack> NestedStacks(ItemStack stack)
        {
            var list = new List<ItemStack>();
            if (stack?.Attributes != null) Collect(stack.Attributes, list, 0);
            return list;
        }

        static void Collect(ITreeAttribute tree, List<ItemStack> list, int depth)
        {
            if (depth > 8) return;
            foreach (var kv in tree)
            {
                if (kv.Value is ItemstackAttribute isa && isa.value != null) list.Add(isa.value);
                else if (kv.Value is ITreeAttribute sub) Collect(sub, list, depth + 1);
                else if (kv.Value is TreeArrayAttribute arr && arr.value != null)
                {
                    foreach (TreeAttribute t in arr.value) if (t != null) Collect(t, list, depth + 1);
                }
            }
        }

        static bool ContainsStack(ITreeAttribute tree, int depth)
        {
            if (depth > 8) return false;
            foreach (var kv in tree)
            {
                if (kv.Value is ItemstackAttribute isa && isa.value != null) return true;
                if (kv.Value is ITreeAttribute sub && ContainsStack(sub, depth + 1)) return true;
                if (kv.Value is TreeArrayAttribute arr && arr.value != null)
                {
                    foreach (TreeAttribute t in arr.value) if (t != null && ContainsStack(t, depth + 1)) return true;
                }
            }
            return false;
        }
    }
}

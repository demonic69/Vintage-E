using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageE
{
    /// <summary>
    /// A player's personal EMC and transmutation knowledge. Stored in the player entity's watched attributes,
    /// so it is saved with the world and synced to that player's client automatically.
    /// Knowledge entries are "i:domain:code" for items and "b:domain:code" for blocks, so they survive id changes.
    /// </summary>
    public static class PlayerEmc
    {
        public const string EmcKey = "vintageeEmc";
        public const string KnownKey = "vintageeKnown";

        static SyncedTreeAttribute Attrs(IPlayer player) => player?.Entity?.WatchedAttributes;

        public static double GetEmc(IPlayer player) => Attrs(player)?.GetDouble(EmcKey, 0) ?? 0;

        public static void SetEmc(IPlayer player, double value)
        {
            var attrs = Attrs(player);
            if (attrs == null) return;
            if (double.IsNaN(value) || value < 0) value = 0;
            attrs.SetDouble(EmcKey, value);
            attrs.MarkPathDirty(EmcKey);
        }

        public static void AddEmc(IPlayer player, double amount) => SetEmc(player, GetEmc(player) + amount);

        public static string[] GetKnown(IPlayer player) => Attrs(player)?.GetStringArray(KnownKey, Array.Empty<string>()) ?? Array.Empty<string>();

        public static bool Knows(IPlayer player, string key) => Array.IndexOf(GetKnown(player), key) >= 0;

        /// <summary>Adds the key; returns true if it was new.</summary>
        public static bool Learn(IPlayer player, string key)
        {
            var attrs = Attrs(player);
            if (attrs == null || string.IsNullOrEmpty(key)) return false;
            string[] known = GetKnown(player);
            if (Array.IndexOf(known, key) >= 0) return false;
            var list = new List<string>(known) { key };
            attrs.SetStringArray(KnownKey, list.ToArray());
            attrs.MarkPathDirty(KnownKey);
            return true;
        }

        public static bool Forget(IPlayer player, string key)
        {
            var attrs = Attrs(player);
            if (attrs == null) return false;
            string[] known = GetKnown(player);
            if (Array.IndexOf(known, key) < 0) return false;
            attrs.SetStringArray(KnownKey, known.Where(k => k != key).ToArray());
            attrs.MarkPathDirty(KnownKey);
            return true;
        }

        public static string KeyOf(CollectibleObject collectible)
        {
            if (collectible?.Code == null) return null;
            return (collectible.ItemClass == EnumItemClass.Block ? "b:" : "i:") + collectible.Code.ToString();
        }

        public static CollectibleObject Resolve(IWorldAccessor world, string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 3 || key[1] != ':') return null;
            var code = new AssetLocation(key.Substring(2));
            return key[0] == 'b' ? world.GetBlock(code) : (CollectibleObject)world.GetItem(code);
        }
    }
}

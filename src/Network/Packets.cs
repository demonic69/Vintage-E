using ProtoBuf;

namespace VintageE
{
    /// <summary>Server → client: the full EMC table, by numeric id (ids are identical on both sides in a session).</summary>
    [ProtoContract]
    public class EmcTablePacket
    {
        [ProtoMember(1, IsPacked = true)] public int[] ItemIds;
        [ProtoMember(2, IsPacked = true)] public double[] ItemValues;
        [ProtoMember(3, IsPacked = true)] public int[] BlockIds;
        [ProtoMember(4, IsPacked = true)] public double[] BlockValues;
        [ProtoMember(5)] public bool DurabilityScales;
    }

    public enum TransmuteAction
    {
        None = 0,
        /// <summary>Burn what the player holds on the mouse cursor. Mode 0 = whole stack, 1 = one item.</summary>
        Burn = 1,
        /// <summary>Create a known item. Mode 0 = one onto the cursor, 1 = a stack onto the cursor, 2 = a stack into the inventory.</summary>
        Create = 2,
        /// <summary>The GUI was closed.</summary>
        Close = 3,
        /// <summary>Remove an item from the player's knowledge.</summary>
        Forget = 4
    }

    /// <summary>Client → server request from the transmutation GUI.</summary>
    [ProtoContract]
    public class TransmuteRequestPacket
    {
        [ProtoMember(1)] public TransmuteAction Action;
        [ProtoMember(2)] public string Key;
        [ProtoMember(3)] public int Mode;
    }

    /// <summary>Server → client outcome; ErrorCode maps to the lang key "vintagee:error-{code}".</summary>
    [ProtoContract]
    public class TransmuteResultPacket
    {
        [ProtoMember(1)] public string ErrorCode;
        [ProtoMember(2)] public string[] Args;
        [ProtoMember(3)] public string LearnedKey;
    }

    /// <summary>Server → client: the blocks/items still without an EMC value, for the /emc missing window. Keys are "i:code" / "b:code".</summary>
    [ProtoContract]
    public class EmcMissingPacket
    {
        [ProtoMember(1)] public string[] Keys;
        [ProtoMember(2)] public int CreativeTotal;
        [ProtoMember(3)] public int ValueCount;
        [ProtoMember(4)] public string Version;
        [ProtoMember(5)] public string File;
        /// <summary>True: this is the /emc blacklist window (Keys are the blacklisted blocks/items).</summary>
        [ProtoMember(6)] public bool Blacklist;
    }

    /// <summary>Client → server: set a block/item's EMC value from the /emc missing window (admins only). Key is "i:code" / "b:code".</summary>
    [ProtoContract]
    public class EmcSetValuePacket
    {
        [ProtoMember(1)] public string Key;
        [ProtoMember(2)] public double Value;
        /// <summary>Sent from the blacklist window (the reply reopens that one).</summary>
        [ProtoMember(3)] public bool FromBlacklist;
    }

    /// <summary>Client → server: put a block/item on the blacklist (Add) or take it off, from the missing/blacklist windows (admins only).</summary>
    [ProtoContract]
    public class EmcBlacklistPacket
    {
        [ProtoMember(1)] public string Key;
        [ProtoMember(2)] public bool Add;
        [ProtoMember(3)] public bool FromBlacklist;
    }

    /// <summary>Server → client: the Watch of Flowing Time's current world time speed modifier (0 = none).</summary>
    [ProtoContract]
    public class WatchSpeedPacket
    {
        [ProtoMember(1)] public float Modifier;
    }

    /// <summary>Client → server: the fire-projectile key was pressed (shoot the held lens).</summary>
    [ProtoContract]
    public class FireProjectilePacket
    {
        [ProtoMember(1)] public int Hand;
    }
}

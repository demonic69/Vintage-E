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
        /// <summary>Blocks/items that carry contents or a material: "i:code"/"b:code" keys, with kind × 4 + mode.</summary>
        [ProtoMember(6)] public string[] DataKeys;
        [ProtoMember(7)] public byte[] DataKindModes;
        /// <summary>Material-specific values: key, the data it applies to (base64 tree attributes), value.</summary>
        [ProtoMember(8)] public string[] VariantKeys;
        [ProtoMember(9)] public string[] VariantData;
        [ProtoMember(10, IsPacked = true)] public double[] VariantValues;
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
        /// <summary>Why each one has no value (same order as Keys; missing window only).</summary>
        [ProtoMember(7)] public string[] Why;
        /// <summary>
        /// The other list, mixed in on request: in the missing window everything blacklisted ("Show blacklisted"),
        /// in the blacklist window everything missing (untick "Hide missing").
        /// </summary>
        [ProtoMember(8)] public string[] OtherKeys;
        /// <summary>Why each of <see cref="OtherKeys"/> has no value (blacklist window only, same order).</summary>
        [ProtoMember(9)] public string[] OtherWhy;
    }

    /// <summary>Client → server: set a block/item's EMC value from the /emc missing window (admins only). Key is "i:code" / "b:code".</summary>
    [ProtoContract]
    public class EmcSetValuePacket
    {
        [ProtoMember(1)] public string Key;
        [ProtoMember(2)] public double Value;
        /// <summary>Sent from the blacklist window (the reply reopens that one).</summary>
        [ProtoMember(3)] public bool FromBlacklist;
        /// <summary>Several blocks/items at once (Key is then unused).</summary>
        [ProtoMember(4)] public string[] Keys;
    }

    /// <summary>Client → server: put a block/item on the blacklist (Add) or take it off, from the missing/blacklist windows (admins only).</summary>
    [ProtoContract]
    public class EmcBlacklistPacket
    {
        [ProtoMember(1)] public string Key;
        [ProtoMember(2)] public bool Add;
        [ProtoMember(3)] public bool FromBlacklist;
        /// <summary>Several blocks/items at once (Key is then unused).</summary>
        [ProtoMember(4)] public string[] Keys;
        /// <summary>A whole mod (its domain) instead of single blocks/items.</summary>
        [ProtoMember(5)] public string Domain;
    }

    /// <summary>Server → client: every block/item that can hold contents or carry a material, for the /emc contents window.</summary>
    [ProtoContract]
    public class EmcContentsPacket
    {
        /// <summary>"i:code" / "b:code".</summary>
        [ProtoMember(1)] public string[] Keys;
        /// <summary>StackDataKind per key.</summary>
        [ProtoMember(2)] public byte[] Kinds;
        /// <summary>StackDataMode in effect per key.</summary>
        [ProtoMember(3)] public byte[] Modes;
        /// <summary>The mode it would have without the server config (built-in default or value file).</summary>
        [ProtoMember(4)] public byte[] DefaultModes;
        /// <summary>True where the server config sets the mode.</summary>
        [ProtoMember(5)] public bool[] FromConfig;
        [ProtoMember(6)] public string Version;
    }

    /// <summary>Client → server: switch blocks/items to a contents mode from the /emc contents window (admins only). Mode -1 = back to the default.</summary>
    [ProtoContract]
    public class EmcContentsSetPacket
    {
        [ProtoMember(1)] public string[] Keys;
        [ProtoMember(2)] public int Mode;
    }

    /// <summary>Server → client: every server setting and its value as text, for the /emc configure window.</summary>
    [ProtoContract]
    public class EmcConfigPacket
    {
        [ProtoMember(1)] public string[] Names;
        /// <summary>The value as editable text: true/false, a number, one list entry per line, or "key = value" per line.</summary>
        [ProtoMember(2)] public string[] Values;
        /// <summary>What happened to the last change (empty when just opening).</summary>
        [ProtoMember(3)] public string Message;
        [ProtoMember(4)] public bool Error;
        /// <summary>The setting to show selected.</summary>
        [ProtoMember(5)] public string Selected;
    }

    /// <summary>Client → server: change one setting from the /emc configure window (admins only). Text is in the same form as <see cref="EmcConfigPacket.Values"/>.</summary>
    [ProtoContract]
    public class EmcConfigSetPacket
    {
        [ProtoMember(1)] public string Name;
        [ProtoMember(2)] public string Text;
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

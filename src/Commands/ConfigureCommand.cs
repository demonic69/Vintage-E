using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// /emc configure - read and change ModConfig/vintagee.json in game:
    /// <list type="bullet">
    /// <item>/emc configure - every setting and its value</item>
    /// <item>/emc configure &lt;setting&gt; - its value and what it does</item>
    /// <item>/emc configure &lt;setting&gt; &lt;value&gt; - change a true/false or number setting</item>
    /// <item>/emc configure &lt;list&gt; add|remove &lt;entry&gt; / clear</item>
    /// <item>/emc configure &lt;table&gt; set &lt;key&gt; &lt;value&gt; / remove &lt;key&gt; / clear</item>
    /// <item>/emc configure reload - re-read the file after editing it by hand</item>
    /// </list>
    /// Changes are saved straight away and applied: settings that change EMC values remap them, the rest apply at once
    /// (SelfTestOnStart only matters at start-up).
    /// </summary>
    public static class ConfigureCommand
    {
        /// <summary>Settings whose change means the EMC values must be worked out again.</summary>
        static readonly HashSet<string> Remaps = new HashSet<string>(StringComparer.Ordinal)
        {
            "BaseValues", "FixedValues", "Blacklist", "Allow", "DisableBuiltInBaseValues", "InferValuesFromProcessing", "InferValuesFromCrafting",
            "InferFromVariantSiblings", "EstimateFuelAndFood", "UseTraderPrices", "FallbackValue", "TransitionEmcPerDay", "MaxMappingPasses"
        };

        static IEnumerable<FieldInfo> Settings => typeof(VintageEConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);

        public static TextCommandResult Handle(VintageEModSystem mod, TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            string name = args[0] as string;
            string rest = (args[1] as string)?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) return TextCommandResult.Success(List(mod.Config, lang));
            if (name.Equals("reload", StringComparison.OrdinalIgnoreCase))
            {
                string error = mod.ReloadConfig();
                return error == null ? TextCommandResult.Success(Lang.GetL(lang, "vintagee:cfg-reloaded")) : TextCommandResult.Error(error);
            }

            FieldInfo field = Find(name, out string problem);
            if (field == null) return TextCommandResult.Error(problem ?? Lang.GetL(lang, "vintagee:cfg-unknown", name));
            object value = field.GetValue(mod.Config);
            if (rest.Length == 0) return TextCommandResult.Success(Describe(field, value, lang));

            string result;
            try
            {
                result = Change(mod.Config, field, value, rest, lang);
            }
            catch (FormatException e)
            {
                return TextCommandResult.Error(e.Message);
            }
            mod.Config.Normalize();
            mod.SaveConfig();
            string applied = Apply(mod, field.Name, lang);
            return TextCommandResult.Success(result + " " + applied + "\n" + Lang.GetL(lang, "vintagee:cfg-now", field.Name, Show(field.GetValue(mod.Config))));
        }

        /// <summary>The setting with this name (any case), or the only one starting with it.</summary>
        static FieldInfo Find(string name, out string problem)
        {
            problem = null;
            FieldInfo exact = Settings.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            var matches = Settings.Where(f => f.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1) problem = Lang.Get("vintagee:cfg-ambiguous", name, string.Join(", ", matches.Select(f => f.Name)));
            return null;
        }

        static string List(VintageEConfig config, string lang)
        {
            var sb = new StringBuilder(Lang.GetL(lang, "vintagee:cfg-title")).Append('\n');
            foreach (FieldInfo f in Settings)
            {
                sb.Append("<strong>").Append(f.Name).Append("</strong> = ").Append(Short(f.GetValue(config))).Append('\n');
            }
            sb.Append(Lang.GetL(lang, "vintagee:cfg-howto"));
            return sb.ToString();
        }

        static string Describe(FieldInfo f, object value, string lang)
        {
            string about = f.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            string how = value is IDictionary ? Lang.GetL(lang, "vintagee:cfg-howto-table", f.Name)
                : value is IList ? Lang.GetL(lang, "vintagee:cfg-howto-list", f.Name)
                : value is bool ? Lang.GetL(lang, "vintagee:cfg-howto-bool", f.Name)
                : Lang.GetL(lang, "vintagee:cfg-howto-value", f.Name);
            return "<strong>" + f.Name + "</strong> = " + Show(value) + "\n" + about + "\n" + how;
        }

        static string Short(object value)
        {
            if (value is IDictionary d) return d.Count == 0 ? "{}" : "{" + d.Count + " entries}";
            if (value is IList l) return l.Count == 0 ? "[]" : "[" + l.Count + " entries]";
            return Show(value);
        }

        static string Show(object value)
        {
            switch (value)
            {
                case null: return "null";
                case bool b: return b ? "true" : "false";
                case double d: return d.ToString("0.####", CultureInfo.InvariantCulture);
                case float fl: return fl.ToString("0.####", CultureInfo.InvariantCulture);
                case IDictionary dict:
                    if (dict.Count == 0) return "{}";
                    var parts = new List<string>();
                    foreach (DictionaryEntry e in dict) parts.Add(e.Key + ": " + Show(e.Value));
                    return "{ " + string.Join(", ", parts.Take(60)) + (parts.Count > 60 ? ", ..." : "") + " }";
                case IList list:
                    if (list.Count == 0) return "[]";
                    return "[ " + string.Join(", ", list.Cast<object>().Take(60).Select(Show)) + (list.Count > 60 ? ", ..." : "") + " ]";
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        static string Change(VintageEConfig config, FieldInfo field, object value, string rest, string lang)
        {
            string[] words = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string verb = words[0].ToLowerInvariant();
            string arg = words.Length > 1 ? words[1].Trim() : "";

            if (value is IDictionary dict)
            {
                Type valueType = field.FieldType.GetGenericArguments()[1];
                switch (verb)
                {
                    case "clear":
                        dict.Clear();
                        return Lang.GetL(lang, "vintagee:cfg-cleared", field.Name);
                    case "remove":
                        if (arg.Length == 0) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-howto-table", field.Name));
                        if (!dict.Contains(arg)) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-notthere", arg, field.Name));
                        dict.Remove(arg);
                        return Lang.GetL(lang, "vintagee:cfg-removed", arg, field.Name);
                    case "set":
                        string[] kv = arg.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (kv.Length < 2) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-howto-table", field.Name));
                        if (field.Name == "StackDataModes" && !StackDataTable.TryParseMode(kv[1], out _)) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-badmode", kv[1]));
                        dict[kv[0]] = Parse(kv[1], valueType, lang);
                        return Lang.GetL(lang, "vintagee:cfg-set", kv[0], field.Name, kv[1]);
                    default:
                        throw new FormatException(Lang.GetL(lang, "vintagee:cfg-howto-table", field.Name));
                }
            }
            if (value is IList list)
            {
                switch (verb)
                {
                    case "clear":
                        list.Clear();
                        return Lang.GetL(lang, "vintagee:cfg-cleared", field.Name);
                    case "add":
                        if (arg.Length == 0) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-howto-list", field.Name));
                        if (!list.Contains(arg)) list.Add(arg);
                        return Lang.GetL(lang, "vintagee:cfg-added", arg, field.Name);
                    case "remove":
                        if (!list.Contains(arg)) throw new FormatException(Lang.GetL(lang, "vintagee:cfg-notthere", arg, field.Name));
                        list.Remove(arg);
                        return Lang.GetL(lang, "vintagee:cfg-removed", arg, field.Name);
                    default:
                        throw new FormatException(Lang.GetL(lang, "vintagee:cfg-howto-list", field.Name));
                }
            }
            object parsed = Parse(rest, field.FieldType, lang);
            field.SetValue(config, parsed);
            return Lang.GetL(lang, "vintagee:cfg-changed", field.Name, Show(parsed));
        }

        static object Parse(string text, Type type, string lang)
        {
            text = text.Trim();
            if (type == typeof(bool))
            {
                switch (text.ToLowerInvariant())
                {
                    case "true": case "on": case "yes": case "1": return true;
                    case "false": case "off": case "no": case "0": return false;
                }
            }
            else if (type == typeof(double) && double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && !double.IsNaN(d) && !double.IsInfinity(d)) return d;
            else if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
            else if (type == typeof(string)) return text;
            throw new FormatException(Lang.GetL(lang, "vintagee:cfg-badvalue", text, type == typeof(bool) ? "true/false" : type == typeof(int) ? "a whole number" : "a number"));
        }

        /// <summary>Makes the change take effect and says how.</summary>
        static string Apply(VintageEModSystem mod, string setting, string lang)
        {
            if (Remaps.Contains(setting))
            {
                mod.RecalculateEmc("/emc configure " + setting);
                return Lang.GetL(lang, "vintagee:cfg-remapped");
            }
            switch (setting)
            {
                case "StackDataModes":
                    mod.RefreshStackData();
                    break;
                case "DurabilityScalesEmc":
                    mod.Emc.DurabilityScales = mod.Config.DurabilityScalesEmc;
                    mod.RefreshStackData();
                    break;
                case "HostileCreatures":
                    mod.Trinkets?.ResetHostile();
                    break;
                case "SelfTestOnStart":
                    return Lang.GetL(lang, "vintagee:cfg-restart");
            }
            return Lang.GetL(lang, "vintagee:cfg-applied");
        }
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// /emc                 – EMC of the held item and your personal EMC
    /// /emc set &lt;value&gt;   – (admin) pin the held item's value (0 = no EMC), saved to config, remaps
    /// /emc clear           – (admin) remove that pin, remaps
    /// /emc recalc          – (admin) rerun the mapper
    /// /emc dump            – (admin) write every value to ModData/vintagee/emc-values.csv
    /// /emc missing         – (admin) list blocks/items without a value (also written to ModData/vintagee/emc-missing.csv)
    /// /emc give &lt;amount&gt;  – (admin) add personal EMC
    /// /emc learn           – (admin) learn the held item without burning it
    /// </summary>
    public static class EmcCommands
    {
        public static void Register(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            var parsers = sapi.ChatCommands.Parsers;
            sapi.ChatCommands.Create("emc")
                .WithDescription("Vintage-E: show and manage EMC values")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(args => Info(mod, args))
                .BeginSubCommand("help")
                    .WithDescription("List every Vintage-E command")
                    .HandleWith(args => Help(args))
                .EndSubCommand()
                .BeginSubCommand("set")
                    .RequiresPlayer()
                    .WithDescription("Pin the held item's EMC value (0 removes it). Saved to ModConfig/vintagee.json.")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Double("value"))
                    .HandleWith(args => Set(mod, args, (double)args[0]))
                .EndSubCommand()
                .BeginSubCommand("clear")
                    .RequiresPlayer()
                    .WithDescription("Remove the pinned value for the held item")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args => Set(mod, args, null))
                .EndSubCommand()
                .BeginSubCommand("recalc")
                    .WithDescription("Recalculate all EMC values from recipes")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args => Recalc(mod))
                .EndSubCommand()
                .BeginSubCommand("dump")
                    .WithDescription("Write all EMC values to a CSV file")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args => Dump(mod, sapi))
                .EndSubCommand()
                .BeginSubCommand("missing")
                    .WithDescription("List blocks/items that ended up without an EMC value")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args => Missing(mod, args))
                .EndSubCommand()
                .BeginSubCommand("give")
                    .RequiresPlayer()
                    .WithDescription("Add personal EMC to yourself")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Double("amount"))
                    .HandleWith(args =>
                    {
                        PlayerEmc.AddEmc(args.Caller.Player, (double)args[0]);
                        return TextCommandResult.Success(Lang.Get("vintagee:cmd-balance", EmcFormat.Format(PlayerEmc.GetEmc(args.Caller.Player))));
                    })
                .EndSubCommand()
                .BeginSubCommand("learn")
                    .RequiresPlayer()
                    .WithDescription("Learn the held item without burning it")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args =>
                    {
                        ItemStack held = args.Caller.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
                        if (held == null) return TextCommandResult.Error(Lang.Get("vintagee:cmd-holdsomething"));
                        PlayerEmc.Learn(args.Caller.Player, PlayerEmc.KeyOf(held.Collectible));
                        return TextCommandResult.Success(Lang.Get("vintagee:learned", held.GetName()));
                    })
                .EndSubCommand();
        }

        /// <summary>(usage, lang key of the description, admin only)</summary>
        static readonly (string usage, string key, bool admin)[] HelpLines =
        {
            ("/emc", "cmdhelp-emc", false),
            ("/emc help", "cmdhelp-help", false),
            ("/emc missing", "cmdhelp-missing", true),
            ("/emc set <value>", "cmdhelp-set", true),
            ("/emc clear", "cmdhelp-clear", true),
            ("/emc recalc", "cmdhelp-recalc", true),
            ("/emc dump", "cmdhelp-dump", true),
            ("/emc give <amount>", "cmdhelp-give", true),
            ("/emc learn", "cmdhelp-learn", true),
        };

        static TextCommandResult Help(TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            bool isAdmin = args.Caller.Player == null || args.Caller.HasPrivilege(Privilege.controlserver);
            var sb = new StringBuilder(Lang.GetL(lang, "vintagee:cmdhelp-title")).Append('\n');
            foreach (var (usage, key, admin) in HelpLines)
            {
                sb.Append("<strong>").Append(usage).Append("</strong>");
                if (admin) sb.Append(' ').Append(Lang.GetL(lang, "vintagee:cmdhelp-admin"));
                sb.Append(" - ").Append(Lang.GetL(lang, "vintagee:" + key)).Append('\n');
            }
            if (!isAdmin) sb.Append(Lang.GetL(lang, "vintagee:cmdhelp-notadmin"));
            return TextCommandResult.Success(sb.ToString().TrimEnd());
        }

        static TextCommandResult Info(VintageEModSystem mod, TextCommandCallingArgs args)
        {
            IPlayer player = args.Caller.Player;
            if (player == null) return TextCommandResult.Error("Run this in game, or use /emc recalc or /emc dump from the console.");
            var sb = new StringBuilder();
            sb.AppendLine(Lang.Get("vintagee:cmd-balance", EmcFormat.Format(PlayerEmc.GetEmc(player))));
            ItemStack held = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (held != null)
            {
                double v = mod.Emc.GetValue(held);
                sb.Append(v > 0
                    ? Lang.Get("vintagee:cmd-itemvalue", held.GetName(), held.Collectible.Code, EmcFormat.Format(v))
                    : Lang.Get("vintagee:cmd-novalue", held.GetName(), held.Collectible.Code));
            }
            return TextCommandResult.Success(sb.ToString().TrimEnd());
        }

        static TextCommandResult Set(VintageEModSystem mod, TextCommandCallingArgs args, double? value)
        {
            ItemStack held = args.Caller.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (held == null) return TextCommandResult.Error(Lang.Get("vintagee:cmd-holdsomething"));
            string key = (held.Class == EnumItemClass.Block ? "block:" : "item:") + held.Collectible.Code;
            if (value.HasValue)
            {
                if (value.Value < 0) return TextCommandResult.Error("Value must be 0 or more");
            }
            else if (!mod.Config.FixedValues.ContainsKey(key))
            {
                return TextCommandResult.Error(Lang.Get("vintagee:cmd-nopin", key));
            }
            mod.SetFixedValue(held.Collectible, value);
            var report = mod.RecalculateEmc("/emc " + (value.HasValue ? "set" : "clear"));
            if (report == null) return TextCommandResult.Error("Mapping failed, see server log");
            return TextCommandResult.Success(Lang.Get("vintagee:cmd-itemvalue", held.GetName(), held.Collectible.Code, EmcFormat.Format(mod.Emc.GetValue(held.Collectible))));
        }

        static TextCommandResult Recalc(VintageEModSystem mod)
        {
            var report = mod.RecalculateEmc("/emc recalc");
            if (report == null) return TextCommandResult.Error("Mapping failed, see server log");
            return TextCommandResult.Success(Lang.Get("vintagee:cmd-recalc", report.Values.Count, report.Milliseconds, report.ConversionCount));
        }

        static TextCommandResult Missing(VintageEModSystem mod, TextCommandCallingArgs args)
        {
            EmcMappingReport report = mod.LastReport;
            if (report == null) return TextCommandResult.Error("EMC has not been mapped yet.");
            string file = report.Missing.Count == 0 ? null : mod.WriteMissingFile(report);
            if (args.Caller.Player is IServerPlayer player)
            {
                mod.SendMissingList(player, report, file); // the window shows everything, so nothing goes to chat
                return TextCommandResult.Success();
            }
            if (report.Missing.Count == 0)
            {
                return TextCommandResult.Success(Lang.Get("vintagee:cmd-missing-none", report.CreativeTotal));
            }
            string top = string.Join(", ", VintageEModSystem.MissingFamilies(report).Take(12).Select(f => f.Key + " (" + f.Value + ")"));
            return TextCommandResult.Success(Lang.Get("vintagee:cmd-missing", report.Missing.Count, report.CreativeTotal, top, file ?? "-"));
        }

        static TextCommandResult Dump(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            string file = mod.WriteValuesFile(mod.LastReport);
            if (file == null) return TextCommandResult.Error("Could not write the file, see server log");
            return TextCommandResult.Success(Lang.Get("vintagee:cmd-dumped", mod.Emc.Count, file));
        }
    }
}

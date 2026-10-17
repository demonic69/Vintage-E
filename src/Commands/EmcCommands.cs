using System;
using System.Collections.Generic;
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
    /// /emc add &lt;amount&gt; [player]    – (admin) add personal EMC to a player (yourself by default)
    /// /emc remove &lt;amount&gt; [player] – (admin) take personal EMC from a player
    /// /emc give &lt;amount&gt;  – (admin) old name for /emc add on yourself
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
                    .WithDescription("Write all EMC values to a CSV file, or with 'hand' show how the held item's value was worked out")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWordRange("what", "hand"))
                    .HandleWith(args => (args[0] as string) == "hand" ? DumpHeld(mod, sapi, args) : Dump(mod, sapi))
                .EndSubCommand()
                .BeginSubCommand("loop")
                    .WithAlias("loops")
                    .WithDescription("List the value loops the mapper found: unvalued items that are each only made from the next")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args => Loops(mod, sapi, args.LanguageCode))
                .EndSubCommand()
                .BeginSubCommand("missing")
                    .WithDescription("List blocks/items that ended up without an EMC value")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args => Missing(mod, args))
                .EndSubCommand()
                .BeginSubCommand("blacklist")
                    .WithDescription("Open a window listing everything on the EMC blacklist")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args => Blacklist(mod, args))
                .EndSubCommand()
                .BeginSubCommand("notransmute")
                    .WithDescription("Open a window of everything that keeps its EMC value but can't be burned, learned or created; or add/remove the held item")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWordRange("action", "add", "remove"))
                    .HandleWith(args => NoTransmute(mod, args, args[0] as string))
                .EndSubCommand()
                .BeginSubCommand("graylist")
                    .WithAlias("greylist")
                    .WithDescription("Open the gray list (left out of /emc missing, valued only by recipes); or add/remove the held item")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWordRange("action", "add", "remove"))
                    .HandleWith(args => GrayList(mod, args, args[0] as string))
                .EndSubCommand()
                .BeginSubCommand("contents")
                    .WithDescription("Open a window of everything that holds things or keeps its material, and how each is transmuted; or set the held item: item, full, itemprice or default")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWordRange("mode", "item", "full", "itemprice", "default"))
                    .HandleWith(args => Contents(mod, args, args[0] as string))
                .EndSubCommand()
                .BeginSubCommand("configure")
                    .WithAlias("config")
                    .WithDescription("Show or change the server settings (ModConfig/vintagee.json) in game")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWord("setting"), parsers.OptionalAll("value"))
                    .HandleWith(args => ConfigureCommand.Handle(mod, args))
                .EndSubCommand()
                .BeginSubCommand("add")
                    .WithDescription("Add EMC to a player's personal EMC (yours if no player is named)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Double("amount"), new OnlinePlayerArgParser("player", sapi, false))
                    .HandleWith(args => ChangeBalance(args, +1))
                .EndSubCommand()
                .BeginSubCommand("remove")
                    .WithDescription("Take EMC from a player's personal EMC (yours if no player is named)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Double("amount"), new OnlinePlayerArgParser("player", sapi, false))
                    .HandleWith(args => ChangeBalance(args, -1))
                .EndSubCommand()
                .BeginSubCommand("give")
                    .WithDescription("Same as /emc add")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Double("amount"), new OnlinePlayerArgParser("player", sapi, false))
                    .HandleWith(args => ChangeBalance(args, +1))
                .EndSubCommand()
                .BeginSubCommand("learn")
                    .RequiresPlayer()
                    .WithDescription("Learn the held item without burning it")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(args =>
                    {
                        ItemStack held = args.Caller.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
                        if (held == null) return TextCommandResult.Error(Lang.Get("vintagee:cmd-holdsomething"));
                        PlayerEmc.Learn(args.Caller.Player, KnowledgeKey(mod, held));
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
            ("/emc blacklist", "cmdhelp-blacklist", true),
            ("/emc contents", "cmdhelp-contents", true),
            ("/emc contents <item|full|itemprice|default>", "cmdhelp-contentsset", true),
            ("/emc configure [setting] [value]", "cmdhelp-configure", true),
            ("/emc set <value>", "cmdhelp-set", true),
            ("/emc clear", "cmdhelp-clear", true),
            ("/emc recalc", "cmdhelp-recalc", true),
            ("/emc dump", "cmdhelp-dump", true),
            ("/emc dump hand", "cmdhelp-dumphand", true),
            ("/emc loop", "cmdhelp-loop", true),
            ("/emc notransmute", "cmdhelp-notransmute", true),
            ("/emc notransmute add|remove", "cmdhelp-notransmuteset", true),
            ("/emc graylist", "cmdhelp-graylist", true),
            ("/emc graylist add|remove", "cmdhelp-graylistset", true),
            ("/emc add <amount> [player]", "cmdhelp-add", true),
            ("/emc remove <amount> [player]", "cmdhelp-remove", true),
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

        /// <summary>/emc add and /emc remove: changes a player's personal EMC (never below 0).</summary>
        static TextCommandResult ChangeBalance(TextCommandCallingArgs args, int sign)
        {
            string lang = args.LanguageCode;
            double amount = (double)args[0];
            IPlayer target = args[1] as IPlayer ?? args.Caller.Player;
            if (target == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-emc-needplayer"));
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-emc-positive"));

            double before = PlayerEmc.GetEmc(target);
            double after = Math.Max(0, before + sign * amount);
            PlayerEmc.SetEmc(target, after);
            double changed = Math.Abs(after - before);

            if (target != args.Caller.Player && target is IServerPlayer sp)
            {
                string who = args.Caller.Player?.PlayerName ?? "Server";
                sp.SendMessage(GlobalConstants.GeneralChatGroup,
                    Lang.GetL(sp.LanguageCode, sign > 0 ? "vintagee:cmd-emc-gotadded" : "vintagee:cmd-emc-gotremoved", who, EmcFormat.Format(changed), EmcFormat.Format(after)),
                    EnumChatType.Notification);
            }
            return TextCommandResult.Success(Lang.GetL(lang, sign > 0 ? "vintagee:cmd-emc-added" : "vintagee:cmd-emc-removed",
                EmcFormat.Format(changed), target.PlayerName, EmcFormat.Format(after)));
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

        /// <summary>/emc notransmute: the window; /emc notransmute add|remove: the held item.</summary>
        static TextCommandResult NoTransmute(VintageEModSystem mod, TextCommandCallingArgs args, string action)
        {
            string lang = args.LanguageCode;
            EmcMappingReport report = mod.LastReport;
            if (report == null) return TextCommandResult.Error("EMC has not been mapped yet.");
            if (action == "add" || action == "remove")
            {
                ItemStack held = args.Caller.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                if (held == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-holdsomething"));
                bool add = action == "add";
                mod.SetNoTransmute(new[] { VintageEModSystem.ConfigKey(held.Collectible) }, add);
                string msg = Lang.GetL(lang, add ? "vintagee:cmd-notransmute-held-added" : "vintagee:cmd-notransmute-held-removed", held.GetName(), held.Collectible.Code);
                if (!add && mod.Emc.IsNoTransmute(held.Collectible)) msg += " " + Lang.GetL(lang, "vintagee:cmd-notransmute-stillwildcard", 1);
                return TextCommandResult.Success(msg);
            }
            if (args.Caller.Player is IServerPlayer player)
            {
                mod.SendMissingList(player, report, null, EmcListKind.NoTransmute);
                return TextCommandResult.Success();
            }
            return TextCommandResult.Success(mod.Emc.NoTransmute.Count + " can't be transmuted: " + string.Join(", ", mod.Emc.NoTransmute.Take(40).Select(c => c.Code.ToString())));
        }

        /// <summary>/emc graylist: the window; /emc graylist add|remove: the held item.</summary>
        static TextCommandResult GrayList(VintageEModSystem mod, TextCommandCallingArgs args, string action)
        {
            string lang = args.LanguageCode;
            EmcMappingReport report = mod.LastReport;
            if (report == null) return TextCommandResult.Error("EMC has not been mapped yet.");
            if (action == "add" || action == "remove")
            {
                ItemStack held = args.Caller.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                if (held == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-holdsomething"));
                bool add = action == "add";
                mod.SetGrayList(new[] { VintageEModSystem.ConfigKey(held.Collectible) }, add);
                mod.RecalculateEmc(add ? "/emc graylist add" : "/emc graylist remove");
                return TextCommandResult.Success(Lang.GetL(lang, add ? "vintagee:cmd-gray-held-added" : "vintagee:cmd-gray-held-removed", held.GetName(), held.Collectible.Code));
            }
            if (args.Caller.Player is IServerPlayer player)
            {
                mod.SendMissingList(player, mod.LastReport ?? report, null, EmcListKind.GrayList);
                return TextCommandResult.Success();
            }
            return TextCommandResult.Success((report.Gray?.Count ?? 0) + " gray-listed: " + string.Join(", ", (report.Gray ?? new List<CollectibleObject>()).Take(40).Select(c => c.Code.ToString())));
        }

        static TextCommandResult Blacklist(VintageEModSystem mod, TextCommandCallingArgs args)
        {
            EmcMappingReport report = mod.LastReport;
            if (report == null) return TextCommandResult.Error("EMC has not been mapped yet.");
            if (args.Caller.Player is IServerPlayer player)
            {
                mod.SendMissingList(player, report, null, EmcListKind.Blacklist);
                return TextCommandResult.Success();
            }
            return TextCommandResult.Success(report.Blacklisted.Count + " blacklisted: " + string.Join(", ", report.Blacklisted.Take(40).Select(c => c.Code.ToString())));
        }

        /// <summary>The knowledge entry a held item teaches: the plain item, or exactly this version if it keeps its contents/material.</summary>
        static string KnowledgeKey(VintageEModSystem mod, ItemStack stack)
        {
            StackDataTable data = mod.Emc.StackData;
            if (data.CarriesData(stack.Collectible) && data.ModeOf(stack.Collectible) != StackDataMode.Item)
            {
                var kept = StackDataUtil.KeptData(stack);
                if (kept != null) return PlayerEmc.ExactKey(stack.Collectible, kept);
            }
            return PlayerEmc.KeyOf(stack.Collectible);
        }

        /// <summary>/emc contents: the window, or with a mode, set the held item's mode.</summary>
        static TextCommandResult Contents(VintageEModSystem mod, TextCommandCallingArgs args, string modeText)
        {
            string lang = args.LanguageCode;
            if (string.IsNullOrEmpty(modeText))
            {
                if (args.Caller.Player is IServerPlayer sp)
                {
                    mod.SendContentsList(sp);
                    return TextCommandResult.Success();
                }
                var table = mod.Emc.StackData;
                return TextCommandResult.Success(string.Join(", ", new[] { StackDataMode.Item, StackDataMode.Full, StackDataMode.ItemPrice }
                    .Select(m => StackDataTable.ModeName(m) + ": " + table.Kinds.Keys.Count(c => table.ModeOf(c) == m))));
            }
            ItemStack held = args.Caller.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            if (held == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-holdsomething"));
            if (held.Collectible is IEmcStorageItem) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-contents-kleinstar"));
            StackDataMode? mode = null;
            if (modeText != "default")
            {
                StackDataTable.TryParseMode(modeText, out StackDataMode m);
                mode = m;
            }
            mod.SetStackDataModes(new[] { held.Collectible }, mode);
            string modeName = mode == null ? Lang.GetL(lang, "vintagee:contents-mode-default") : Lang.GetL(lang, "vintagee:contents-mode-" + StackDataTable.ModeName(mode.Value));
            return TextCommandResult.Success(Lang.GetL(lang, "vintagee:cmd-contents-set", held.GetName(), modeName));
        }

        static readonly System.Text.RegularExpressions.Regex CodeToken = new System.Text.RegularExpressions.Regex(@"\b([a-z0-9_]+:[a-z0-9_.\-/]+)", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// /emc dump hand: the held item's value and where it came from, then the same for each block/item that source
        /// mentions. For something without a value: every way the mod knows of making it, which ingredients have no
        /// value and why (two levels), and the value loop it's stuck in, if any. Shown in chat and written to
        /// ModData/vintagee/emc-dump-&lt;code&gt;.txt (the chat says where).
        /// </summary>
        static TextCommandResult DumpHeld(VintageEModSystem mod, ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            string lang = args.LanguageCode;
            ItemStack held = args.Caller.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            if (held == null) return TextCommandResult.Error(Lang.GetL(lang, "vintagee:cmd-holdsomething"));
            EmcMappingReport report = mod.LastReport;
            var sb = new StringBuilder();
            if (report == null || report.FromCache)
            {
                // The cache keeps the values, not the recipes behind them: work everything out again to explain.
                sb.AppendLine(Lang.GetL(lang, "vintagee:dump-remapping"));
                report = mod.RecalculateEmc("/emc dump hand") ?? report;
            }
            CollectibleObject c = held.Collectible;
            double stackValue = mod.Emc.GetValue(held);
            double baseValue = mod.Emc.GetValue(c);
            sb.AppendLine(Lang.GetL(lang, "vintagee:dump-held", held.GetName(), c.Code, EmcFormat.Format(baseValue)));
            if (Math.Abs(stackValue - baseValue * held.StackSize) > 0.0001 && held.StackSize > 0)
            {
                sb.AppendLine(Lang.GetL(lang, "vintagee:dump-stack", held.StackSize, EmcFormat.Format(stackValue)));
            }
            if (mod.Emc.IsNoTransmute(c)) sb.AppendLine(Lang.GetL(lang, "vintagee:dump-notransmute"));
            if (report == null || report.FromCache)
            {
                sb.AppendLine(Lang.GetL(lang, "vintagee:dump-cached"));
            }
            else if (report.Values.ContainsKey(c))
            {
                var seen = new HashSet<CollectibleObject> { c };
                Explain(mod, sapi, report, c, sb, "  ", 0, seen, lang);
            }
            else
            {
                ExplainMissing(mod, report, c, sb, "  ", 0, new HashSet<CollectibleObject> { c }, lang);
            }

            string text = sb.ToString().TrimEnd();
            string file = null;
            try
            {
                string safe = c.Code.ToString().Replace(':', '_').Replace('/', '_').Replace('*', '_');
                file = System.IO.Path.Combine(VintageEModSystem.DataFolder(sapi), "emc-dump-" + safe + ".txt");
                System.IO.File.WriteAllText(file, text + "\n");
            }
            catch (Exception e)
            {
                sapi.Logger.Warning("Vintage-E: could not write the dump file: {0}", e.Message);
                file = null;
            }
            // Chat shows the start; the file has all of it.
            string[] lines = text.Split('\n');
            string chat = lines.Length > 40 ? string.Join("\n", lines.Take(40)) + "\n" + Lang.GetL(lang, "vintagee:dump-more", lines.Length - 40) : text;
            chat += "\n" + (file != null ? Lang.GetL(lang, "vintagee:dump-file", file) : Lang.GetL(lang, "vintagee:dump-nofile"));
            return TextCommandResult.Success(chat);
        }

        /// <summary>Why <paramref name="c"/> has no value: blacklisted, nothing makes it, a value loop, or which ingredients are missing.</summary>
        static void ExplainMissing(VintageEModSystem mod, EmcMappingReport report, CollectibleObject c, StringBuilder sb, string indent, int depth, HashSet<CollectibleObject> seen, string lang)
        {
            if (report.Free.Contains(c)) { sb.AppendLine(indent + Lang.GetL(lang, "vintagee:dump-free")); return; }
            if (report.Blacklisted.Contains(c)) { sb.AppendLine(indent + Lang.GetL(lang, "vintagee:dump-blacklisted")); return; }
            if (report.LoopOf.TryGetValue(c, out int loop))
            {
                sb.AppendLine(indent + Lang.GetL(lang, "vintagee:dump-loop", MissingAnalysis.LoopText(report.Loops[loop])));
            }
            if (!report.MissingRecipes.TryGetValue(c, out List<Conversion> recipes) || recipes.Count == 0)
            {
                sb.AppendLine(indent + Lang.GetL(lang, "vintagee:dump-norecipe"));
                return;
            }
            bool Valued(CollectibleObject x) => x != null && (report.Values.ContainsKey(x) || report.Free.Contains(x));
            sb.AppendLine(indent + Lang.GetL(lang, "vintagee:dump-ways", recipes.Count));
            int n = 0;
            foreach (Conversion conv in recipes)
            {
                if (++n > (depth == 0 ? 12 : 4))
                {
                    sb.AppendLine(indent + "  " + Lang.GetL(lang, "vintagee:dump-moreways", recipes.Count - n + 1));
                    break;
                }
                sb.AppendLine(indent + "  " + n + ". " + EmcMapper.Describe(conv));
                foreach (CollectibleObject part in MissingAnalysis.Lacking(conv, Valued).Distinct())
                {
                    string name;
                    try { name = part.GetHeldItemName(new ItemStack(part)); } catch { name = part.Code.ToShortString(); }
                    sb.AppendLine(indent + "     " + Lang.GetL(lang, "vintagee:dump-lacks", name, part.Code));
                    if (depth < 2 && seen.Add(part)) ExplainMissing(mod, report, part, sb, indent + "       ", depth + 1, seen, lang);
                    else if (report.LoopOf.TryGetValue(part, out int l) && report.LoopOf.TryGetValue(c, out int mine) && l == mine)
                        sb.AppendLine(indent + "       " + Lang.GetL(lang, "vintagee:dump-inloop"));
                }
            }
        }

        static void Explain(VintageEModSystem mod, ICoreServerAPI sapi, EmcMappingReport report, CollectibleObject c, StringBuilder sb, string indent, int depth, HashSet<CollectibleObject> seen, string lang)
        {
            if (!report.Origins.TryGetValue(c, out string origin) || string.IsNullOrEmpty(origin))
            {
                if (mod.Emc.GetValue(c) > 0) sb.AppendLine(indent + Lang.GetL(lang, "vintagee:dump-noorigin"));
                else ExplainMissing(mod, report, c, sb, indent, depth + 1, seen, lang);
                return;
            }
            sb.AppendLine(indent + Lang.GetL(lang, "vintagee:dump-from", origin));
            if (depth >= 1) return;
            int shown = 0;
            foreach (System.Text.RegularExpressions.Match m in CodeToken.Matches(origin))
            {
                var loc = new AssetLocation(m.Groups[1].Value);
                CollectibleObject part = (CollectibleObject)sapi.World.GetItem(loc) ?? sapi.World.GetBlock(loc);
                if (part == null || !seen.Add(part)) continue;
                if (++shown > 8) break;
                string name;
                try { name = part.GetHeldItemName(new ItemStack(part)); } catch { name = part.Code.ToShortString(); }
                sb.AppendLine(indent + "- " + Lang.GetL(lang, "vintagee:dump-part", name, part.Code, EmcFormat.Format(mod.Emc.GetValue(part))));
                Explain(mod, sapi, report, part, sb, indent + "    ", depth + 1, seen, lang);
            }
        }

        static TextCommandResult Dump(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            string file = mod.WriteValuesFile(mod.LastReport);
            if (file == null) return TextCommandResult.Error("Could not write the file, see server log");
            return TextCommandResult.Success(Lang.Get("vintagee:cmd-dumped", mod.Emc.Count, file));
        }

        /// <summary>/emc loop: every value loop (and settled loop) the mapper found, also written to ModData/vintagee/emc-loops.txt.</summary>
        static TextCommandResult Loops(VintageEModSystem mod, ICoreServerAPI sapi, string lang)
        {
            EmcMappingReport report = mod.LastReport;
            if (report == null) return TextCommandResult.Error("EMC has not been mapped yet.");
            var sb = new StringBuilder();
            sb.AppendLine(Lang.GetL(lang, report.Loops.Count == 0 ? "vintagee:loop-none" : "vintagee:loop-header", report.Loops.Count));
            for (int i = 0; i < report.Loops.Count; i++) sb.AppendLine((i + 1) + ". " + MissingAnalysis.LoopText(report.Loops[i]));
            if (report.SettledLoops.Count > 0)
            {
                sb.AppendLine(Lang.GetL(lang, "vintagee:loop-settled", report.SettledLoops.Count));
                for (int i = 0; i < report.SettledLoops.Count; i++) sb.AppendLine((i + 1) + ". " + MissingAnalysis.LoopText(report.SettledLoops[i]));
            }
            string text = sb.ToString().TrimEnd();
            string file = null;
            try
            {
                file = System.IO.Path.Combine(VintageEModSystem.DataFolder(sapi), "emc-loops.txt");
                System.IO.File.WriteAllText(file, text + "\n");
            }
            catch (Exception e) { sapi.Logger.Warning("Vintage-E: could not write the loop list: {0}", e.Message); file = null; }
            string[] lines = text.Split('\n');
            string chat = lines.Length > 40 ? string.Join("\n", lines.Take(40)) + "\n" + Lang.GetL(lang, "vintagee:dump-more", lines.Length - 40) : text;
            if (file != null) chat += "\n" + Lang.GetL(lang, "vintagee:dump-file", file);
            return TextCommandResult.Success(chat);
        }
    }
}

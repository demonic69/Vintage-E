using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VintageE
{
    /// <summary>
    /// Runs when the hidden <c>SelfTestOnStart</c> setting is on (the "Test on a real server" GitHub workflow turns it
    /// on): next to the world spawn it builds matter furnaces (one on charcoal, one on EMC) with raw meat in them,
    /// fires lens, water and fire projectiles at the ground, checks a few EMC values, and writes what happened to
    /// <c>ModData/vintagee/selftest.txt</c>. Never runs on normal servers.
    /// </summary>
    public class SelfTest
    {
        readonly VintageEModSystem mod;
        readonly ICoreServerAPI sapi;
        readonly StringBuilder log = new StringBuilder();
        int failures;

        public SelfTest(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            this.mod = mod;
            this.sapi = sapi;
        }

        void Line(string text)
        {
            log.AppendLine(text);
            sapi.Logger.Notification("[Vintage-E self-test] " + text);
        }

        void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Line((ok ? "PASS " : "FAIL ") + what);
        }

        public void Start()
        {
            Line("Vintage-E " + mod.Version + " self-test");
            EntityPos spawn = sapi.World.DefaultSpawnPosition;
            int x = (int)(spawn?.X ?? sapi.World.BlockAccessor.MapSizeX / 2), z = (int)(spawn?.Z ?? sapi.World.BlockAccessor.MapSizeZ / 2);
            int cs = GlobalConstants.ChunkSize;
            sapi.WorldManager.LoadChunkColumnPriority(x / cs - 1, z / cs - 1, x / cs + 1, z / cs + 1, new ChunkLoadOptions
            {
                KeepLoaded = true,
                OnLoaded = () => sapi.Event.EnqueueMainThreadTask(() => Run(x, z), "vintagee-selftest")
            });
        }

        void Run(int x, int z)
        {
            try
            {
                CheckValues();
                CheckWorn();
                CheckGuesses();
                CheckContents();
                CheckConfigure();
                CheckProjectELook();
                Check180();
                IBlockAccessor ba = sapi.World.BlockAccessor;
                int y = ba.GetTerrainMapheightAt(new BlockPos(x, 0, z)) + 1;
                Line($"site {x},{y},{z}");

                BlockEntityFirepit fuelled = PlaceFurnace(new BlockPos(x + 2, y, z), "dark");
                BlockEntityFirepit onEmc = PlaceFurnace(new BlockPos(x + 4, y, z), "red");
                Item charcoal = sapi.World.GetItem(new AssetLocation("game:charcoal"));
                Item meat = sapi.World.GetItem(new AssetLocation("game:redmeat-raw"));
                Check(charcoal != null && meat != null, "found charcoal and raw red meat");
                if (fuelled != null && charcoal != null && meat != null)
                {
                    fuelled.fuelSlot.Itemstack = new ItemStack(charcoal, 4);
                    fuelled.inputSlot.Itemstack = new ItemStack(meat, 1);
                    fuelled.canIgniteFuel = true;
                    fuelled.MarkDirty(true);
                }
                if (onEmc is BEMatterFurnace emcFurnace && meat != null)
                {
                    emcFurnace.inputSlot.Itemstack = new ItemStack(meat, 1);
                    double took = emcFurnace.AcceptEmc(500);
                    Check(took == 500, "red furnace accepts 500 EMC");
                    emcFurnace.MarkDirty(true);
                }

                // Projectiles fired straight down at the ground a little way off. The lens aims at its own column's
                // ground height (hilly sites made it land outside the area that was counted).
                int lensY = ba.GetTerrainMapheightAt(new BlockPos(x, 0, z + 12)) + 1;
                var lens = Fire("lens", new Vec3d(x + 0.5, lensY + 12, z + 12.5), p => p.Radius = 4);
                // Well away from the furnaces: the water flows, and firepits go out in water.
                var water = Fire("water", new Vec3d(x + 30.5, y + 8, z + 0.5), null);
                var fire = Fire("fire", new Vec3d(x + 36.5, y + 8, z + 0.5), null);
                BlockPos lensTarget = new BlockPos(x, lensY - 1, z + 12);
                int solidBefore = CountSolid(lensTarget, 2);
                bool lensInWater = ba.GetBlock(lensTarget.UpCopy(), BlockLayersAccess.Fluid)?.IsLiquid() == true;

                sapi.Event.RegisterCallback(_ =>
                {
                    try
                    {
                        Check(lens == null || !lens.Alive, "lens charge hit the ground and is gone");
                        int solidAfter = CountSolid(lensTarget, 2);
                        if (lensInWater) Line($"  lens landed in water (fizzles there, like ProjectE's): {solidBefore} -> {solidAfter} solid");
                        else Check(solidAfter < solidBefore, $"lens explosion broke blocks ({solidBefore} -> {solidAfter} solid around the impact)");
                        Check(water == null || !water.Alive, "water shot landed");
                        Block placed = sapi.World.BlockAccessor.GetBlock(new BlockPos(x + 30, ba.GetTerrainMapheightAt(new BlockPos(x + 30, 0, z)) + 1, z), BlockLayersAccess.Fluid);
                        Line("  fluid where the water shot landed: " + (placed?.Code?.ToString() ?? "none"));
                        Check(fire == null || !fire.Alive, "fireball landed");
                    }
                    catch (Exception e) { Check(false, "projectile check threw " + e); }
                }, 4000);

                sapi.Event.RegisterCallback(_ =>
                {
                    try
                    {
                        Report("dark furnace on charcoal", fuelled);
                        Report("red furnace on EMC", onEmc);
                    }
                    catch (Exception e) { Check(false, "furnace check threw " + e); }
                    Finish();
                }, 40000);
            }
            catch (Exception e)
            {
                Check(false, "self-test threw " + e);
                Finish();
            }
        }

        void CheckValues()
        {
            foreach (var (code, expectMin, expectMax) in new[] { ("game:fat", 1.0, 64.0), ("game:fat-rendered", 1.0, 64.0), ("game:gem-emerald-rough", 2048.0, 2048.0), ("game:gem-diamond-rough", 8192.0, 8192.0),
                ("game:creature-fish-freshwater-bass-largemouth-adult", 112.0, 112.0), ("game:jonasparts-tank01", 1024.0, 1024.0),
                ("game:creature-trader-female-artisan-cold", 0.0, 0.0), ("game:locatormap-treasures", 0.0, 0.0),
                ("game:lore-paper", 1.0, 1000.0), ("game:metal-parts", 1.0, 1000.0), ("game:nightvisiondevice", 4096.0, 4096.0),
                ("game:ore-fluorite", 32.0, 32.0), ("game:ore-fluorite-limestone", 1.0, 1000.0) })
            {
                CollectibleObject c = (CollectibleObject)sapi.World.GetItem(new AssetLocation(code)) ?? sapi.World.GetBlock(new AssetLocation(code));
                double v = c == null ? -1 : mod.Emc.GetValue(c);
                Check(v >= expectMin && v <= expectMax, $"value of {code} = {v} (expected {expectMin}-{expectMax})");
            }
            foreach (string code in new[] { "game:altar-north", "vintagee:furnace-dark-cold", "vintagee:trinket-voidring" })
            {
                CollectibleObject c = (CollectibleObject)sapi.World.GetBlock(new AssetLocation(code)) ?? sapi.World.GetItem(new AssetLocation(code));
                Line($"  value of {code} = {(c == null ? "missing" : mod.Emc.GetValue(c).ToString())}");
            }
        }

        /// <summary>
        /// The worn Klein star and rings: they fit their clothing slots, their models load and hang on the player model.
        /// Also prints where they end up next to the game's own emblem and bracelet, to compare positions.
        /// </summary>
        void CheckWorn()
        {
            Shape seraph = Shape.TryGet(sapi, new AssetLocation("game:shapes/entity/humanoid/seraph.json"));
            Check(seraph != null, "player model loads");
            if (seraph == null) return;
            seraph.ResolveReferences(sapi.Logger, "seraph");
            foreach (string name in new[] { "UpperTorso", "Head", "LowerArmL", "LowerArmR" })
            {
                ShapeElement e = seraph.GetElementByName(name);
                Line($"  {name}: " + (e == null ? "missing" : $"{Describe(e)} (own box {string.Join(",", e.From)} to {string.Join(",", e.To)}, rotation {e.RotationX},{e.RotationY},{e.RotationZ})"));
            }
            foreach (var (code, dress, path, root) in new[] {
                ("vintagee:kleinstar-omega", EnumCharacterDressType.Emblem, (string)null, "vintageekleinstar"),
                ("vintagee:trinket-voidring", EnumCharacterDressType.Arm, null, "vintageering"),
                (null, EnumCharacterDressType.Emblem, "game:entity/humanoid/seraph/clothing/emblem/silver-star", "origin"),
                (null, EnumCharacterDressType.Arm, "game:entity/humanoid/seraph/clothing/arm/copper-bracelet", "hidelowerarm") })
            {
                string shapeBase = path;
                if (code != null)
                {
                    Item item = sapi.World.GetItem(new AssetLocation(code));
                    Check(item != null, code + " exists");
                    if (item == null) continue;
                    Check(ItemSlotCharacter.IsDressType(new ItemStack(item), dress), $"{code} fits the {dress} slot");
                    shapeBase = item.Attributes?["attachableToEntity"]["attachedShape"]["base"].AsString();
                }
                Shape worn = shapeBase == null ? null : Shape.TryGet(sapi, new AssetLocation(shapeBase).WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
                if (code != null) Check(worn != null, $"{code} worn model {shapeBase} loads");
                if (worn == null) { Line($"  {shapeBase}: not found"); continue; }
                worn.ResolveReferences(sapi.Logger, shapeBase);
                Shape target = seraph.Clone();
                target.ResolveReferences(sapi.Logger, "seraph");
                // Step-parenting prefixes the worn model's element and texture names.
                bool added = target.StepParentShape(worn, "vintageetest-", shapeBase, "seraph", sapi.Logger, (a, b) => { });
                ShapeElement r = target.GetElementByName("vintageetest-" + root);
                if (code != null) Check(added && r?.ParentElement != null, $"{code} worn model hangs on {r?.ParentElement?.Name ?? "nothing"}");
                else Line($"  {shapeBase} hangs on {r?.ParentElement?.Name ?? "nothing"}");
                var all = new List<ShapeElement>();
                void Collect(ShapeElement e) { all.Add(e); if (e.Children != null) foreach (var c in e.Children) Collect(c); }
                if (r != null) Collect(r);
                foreach (ShapeElement e in all) if (e != r) Line($"    {e.Name}: {Describe(e)}");
            }
        }

        string OriginOf(string code)
        {
            CollectibleObject c = (CollectibleObject)sapi.World.GetBlock(new AssetLocation(code)) ?? sapi.World.GetItem(new AssetLocation(code));
            if (c is Block b && b.Id == 0) c = sapi.World.GetItem(new AssetLocation(code));
            if (c == null) return "missing";
            return mod.LastReport?.Origins != null && mod.LastReport.Origins.TryGetValue(c, out string o) ? o : (mod.Emc.HasValue(c) ? "?" : "no value");
        }

        double ValueOf(string code)
        {
            CollectibleObject c = (CollectibleObject)sapi.World.GetBlock(new AssetLocation(code));
            if (c == null || c.Id == 0) c = sapi.World.GetItem(new AssetLocation(code));
            return c == null ? -1 : mod.Emc.GetValue(c);
        }

        /// <summary>1.7.0 value methods: placement states, smarter variant guesses, staged solving, fuel/food.</summary>
        void CheckGuesses()
        {
            EmcMappingReport r = mod.LastReport;
            if (r == null) { Check(false, "values are mapped"); return; }
            Line($"  guesses: {r.SiblingCount} look/placement variants, {r.OtherModCount} other mods' variants, {r.FuelCount} fuel, {r.FoodCount} food, {r.GuessCount} other variants, {r.DefaultCount} defaults; {r.Missing.Count} missing");
            string cinnabar = OriginOf("game:ore-cinnabar-andesite");
            Line("  cinnabar ore: " + ValueOf("game:ore-cinnabar-andesite") + " (" + cinnabar + ")");
            Check(!cinnabar.Contains("quartz"), "cinnabar ore isn't priced like quartz");
            string loose = OriginOf("game:looseores-cinnabar-andesite-free");
            Line("  loose cinnabar: " + ValueOf("game:looseores-cinnabar-andesite-free") + " (" + loose + ")");
            Check(!loose.Contains("nativegold"), "loose cinnabar isn't priced like gold-bearing quartz");
            string lens = OriginOf("vintagee:gadget-hyperkineticlens");
            Line("  hyperkinetic lens: " + ValueOf("vintagee:gadget-hyperkineticlens") + " (" + lens + ")");
            Check(lens.StartsWith("grid"), "the lenses are valued from their recipes");
            string fence = OriginOf("game:woodenfence-acacia-e-free");
            Line("  fence piece: " + ValueOf("game:woodenfence-acacia-e-free") + " (" + fence + ")");
            Check(fence.Contains("placement state") && Math.Abs(ValueOf("game:woodenfence-acacia-e-free") - ValueOf("game:woodenfence-acacia-ew-free")) < 1e-6, "fence pieces are worth the fence");
            string brown = OriginOf("game:shingle-burned-brown");
            Line("  brown shingle: " + ValueOf("game:shingle-burned-brown") + " (" + brown + "), black shingle: " + ValueOf("game:shingle-burned-black"));
            Check(brown.Contains("beehive kiln") && Math.Abs(ValueOf("game:shingle-burned-brown") - ValueOf("game:shingle-burned-black")) < 1e-6, "kiln-coloured shingles cost the same clay as the others");
            Check(ValueOf("game:slantedroofing-agedthatch-east-free") > 0, "aged thatch roofing has a value");
            string boat = OriginOf("game:boat-sailed-oak");
            Line("  oak sailboat: " + ValueOf("game:boat-sailed-oak") + " (" + (boat.Length > 120 ? boat.Substring(0, 120) + "..." : boat) + ")");
            Check(boat.StartsWith("built in stages") && ValueOf("game:boat-sailed-oak") > ValueOf("game:boat-raft-oak"), "sailboats are valued from their build stages");
            double four = ValueOf("game:cheese-cheddar-4slice"), two = ValueOf("game:cheese-cheddar-2slice");
            Line($"  cheddar: 4 slices {four}, 2 slices {two} ({OriginOf("game:cheese-cheddar-2slice")})");
            Check(four > 0 && Math.Abs(two - four / 2) < 1e-6, "half a cheese is worth half a cheese");
            string mold = OriginOf("game:toolmold-black-fired-axe");
            Line("  black axe mold: " + ValueOf("game:toolmold-black-fired-axe") + " (" + mold + ")");
            Check(!mold.Contains("anvil"), "the axe mold isn't priced like the anvil mold");
        }

        /// <summary>1.7.0 contents/material modes: kinds, material-specific values, exact copies, chiseled blocks, filled containers.</summary>
        void CheckContents()
        {
            StackDataTable data = mod.Emc.StackData;
            Line($"  contents: {data.Kinds.Count} carry data ({data.Kinds.Values.Count(k => k == StackDataKind.Contents)} containers, {data.Kinds.Values.Count(k => k == StackDataKind.Material)} material, {data.Kinds.Values.Count(k => k == StackDataKind.Chiseled)} chiseled); {data.Variants.Count} with material values");
            foreach (var (code, kind, mode) in new[] {
                ("game:shield-woodmetal", StackDataKind.Material, StackDataMode.Full),
                ("game:woodbucket", StackDataKind.Contents, StackDataMode.Item),
                ("game:chiseledblock", StackDataKind.Chiseled, StackDataMode.Full) })
            {
                CollectibleObject c = (CollectibleObject)sapi.World.GetItem(new AssetLocation(code)) ?? sapi.World.GetBlock(new AssetLocation(code));
                bool ok = c != null && data.CarriesData(c) && data.KindOf(c) == kind && data.ModeOf(c) == mode;
                Check(ok, $"{code} is {kind}, {mode} ({(c == null ? "missing" : data.CarriesData(c) ? data.KindOf(c) + ", " + data.ModeOf(c) : "not listed")})");
            }

            // Round shields: each metal its own value, and an exact copy is worth the same.
            Item shield = sapi.World.GetItem(new AssetLocation("game:shield-woodmetal"));
            var shieldValues = new List<(string, double)>();
            ItemStack goldShield = null;
            foreach (var tab in shield?.CreativeInventoryStacks ?? Array.Empty<CreativeTabAndStackList>())
            {
                foreach (JsonItemStack js in tab.Stacks ?? Array.Empty<JsonItemStack>())
                {
                    ItemStack st = js.ResolvedItemstack;
                    if (st == null) continue;
                    string metal = st.Attributes.GetString("metal");
                    if (metal == null || shieldValues.Any(v => v.Item1 == metal)) continue;
                    shieldValues.Add((metal, mod.Emc.GetValue(st)));
                    if (metal == "gold") goldShield = st;
                }
            }
            Line("  round shields by metal: " + string.Join(", ", shieldValues.Select(v => v.Item1 + " " + EmcFormat.Format(v.Item2))));
            Check(shieldValues.Count > 1 && shieldValues.Max(v => v.Item2) > shieldValues.Min(v => v.Item2), "round shields made of different metals are worth different amounts");
            if (goldShield != null)
            {
                var kept = StackDataUtil.KeptData(goldShield);
                string key = PlayerEmc.ExactKey(goldShield.Collectible, kept);
                ItemStack copy = TransmutationService.Prototype(mod, sapi.World, key);
                Check(copy != null && copy.Attributes.GetString("metal") == "gold" && Math.Abs(mod.Emc.GetValue(copy) - mod.Emc.GetValue(goldShield)) < 1e-6, "an exact copy of a gold shield is a gold shield at the same price");
            }

            // Chiseled block: a full block of granite is worth a granite block, half of one half.
            Block chiseled = sapi.World.GetBlock(new AssetLocation("game:chiseledblock"));
            Block granite = sapi.World.GetBlock(new AssetLocation("game:rock-granite"));
            if (chiseled != null && granite != null)
            {
                ItemStack Chiseled(int y2)
                {
                    var st = new ItemStack(chiseled);
                    st.Attributes["materials"] = new IntArrayAttribute(new[] { granite.Id });
                    uint cuboid = (15u << 12) | ((uint)(y2 - 1) << 16) | (15u << 20);
                    st.Attributes["cuboids"] = new IntArrayAttribute(new[] { (int)cuboid });
                    return st;
                }
                double full = mod.Emc.GetValue(Chiseled(16)), half = mod.Emc.GetValue(Chiseled(8)), rock = mod.Emc.GetValue(granite);
                Line($"  chiseled granite: full {full}, half {half}, rock {rock}");
                Check(rock > 0 && Math.Abs(full - rock) < 1e-6 && Math.Abs(half - rock / 2) < 1e-6, "chiseled blocks are worth their voxels of material");
            }

            // A crucible holding 2 ingots' worth of molten copper (200 units).
            Block crucible = sapi.World.GetBlock(new AssetLocation("game:crucible-blue-smelted"));
            Item copper = sapi.World.GetItem(new AssetLocation("game:ingot-copper"));
            if (crucible != null && copper != null)
            {
                var st = new ItemStack(crucible);
                st.Attributes.SetItemstack("output", new ItemStack(copper));
                st.Attributes.SetInt("units", 200);
                StackDataMode before = data.ModeOf(crucible);
                data.Modes[crucible] = StackDataMode.Full;
                double full = mod.Emc.GetValue(st);
                data.Modes[crucible] = before;
                double expect = mod.Emc.GetValue(crucible) + 2 * mod.Emc.GetValue(copper);
                Line($"  crucible with 200 units of copper, item + contents: {full} (expected {expect}); kind {data.KindOf(crucible)}");
                Check(data.CarriesData(crucible) && Math.Abs(full - expect) < 1e-6, "a crucible of molten metal counts its metal units");
            }

            // A bucket of water: item only by default; item + contents adds the water.
            Block bucket = sapi.World.GetBlock(new AssetLocation("game:woodbucket"));
            Item water = sapi.World.GetItem(new AssetLocation("game:waterportion"));
            if (bucket != null && water != null)
            {
                var st = new ItemStack(bucket);
                var contents = new TreeAttribute();
                contents["0"] = new ItemstackAttribute(new ItemStack(water, 1000));
                st.Attributes["contents"] = contents;
                double plain = mod.Emc.GetValue(st);
                StackDataMode before = data.ModeOf(bucket);
                data.Modes[bucket] = StackDataMode.Full;
                double full = mod.Emc.GetValue(st);
                data.Modes[bucket] = StackDataMode.ItemPrice;
                double price = mod.Emc.GetValue(st);
                data.Modes[bucket] = before;
                Line($"  bucket of 10 L water: item only {plain}, item + contents {full}, item price {price}");
                Check(full > plain && Math.Abs(price - plain) < 1e-6, "filled buckets count their water only in item + contents mode");
            }
        }

        string RunCommand(string line)
        {
            string answer = null;
            var args = new TextCommandCallingArgs
            {
                Caller = new Caller { Type = EnumCallerType.Console, CallerRole = "admin", CallerPrivileges = new[] { "*" }, FromChatGroupId = GlobalConstants.ConsoleGroup }
            };
            sapi.ChatCommands.ExecuteUnparsed(line, args, r => answer = r.StatusMessage + " [" + r.Status + "]");
            return answer ?? "(no answer)";
        }

        /// <summary>
        /// 1.8.0: value rules (bone arrow, free schematic, translocator parts, creative-only Tome), the red matter pedestal,
        /// the no-transmute list, the tablet magnet's filter, value loops, dump files in ModData/vintagee, and resetting settings.
        /// </summary>
        void Check180()
        {
            EmcMappingReport report = mod.LastReport;
            CollectibleObject C(string code) => (CollectibleObject)sapi.World.GetItem(new AssetLocation(code)) ?? sapi.World.GetBlock(new AssetLocation(code));
            double V(string code) => mod.Emc.GetValue(C(code));

            double flint = V("game:arrow-flint"), bone = V("game:arrow-bone");
            Check(flint > 0 && Math.Abs(bone - flint) < 1e-6, $"bone arrow is worth a flint arrow: {EmcFormat.Format(bone)} / {EmcFormat.Format(flint)}");

            CollectibleObject schematic = C("game:schematic-customtranslocator");
            Check(schematic != null && report.Free.Contains(schematic) && report.Blacklisted.Contains(schematic) && V("game:schematic-customtranslocator") <= 0,
                "custom translocator schematic is a free ingredient (blacklisted, no value)");
            CollectibleObject part = C("game:tobtlocatorpart");
            var tobt = sapi.World.Collectibles.Where(c => c.Code?.Domain == "game" && c.Code.Path.StartsWith("tobtlocator-")).ToList();
            Check(part != null && !report.Blacklisted.Contains(part) && tobt.Count > 0 && tobt.All(c => !report.Blacklisted.Contains(c)),
                $"translocator part and {tobt.Count} rickety translocators are off the blacklist (part: {EmcFormat.Format(V("game:tobtlocatorpart"))} EMC)");
            foreach (CollectibleObject t in tobt.Take(1))
            {
                string why = report.MissingWhy.TryGetValue(t, out string w) ? w : null;
                Check(why == null || !why.Contains("schematic-customtranslocator"), $"{t.Code}: the schematic isn't what's missing ({(why == null ? EmcFormat.Format(mod.Emc.GetValue(t)) + " EMC" : why)})");
            }

            CollectibleObject tome = C("vintagee:gadget-tomeofknowledge");
            bool tomeRecipe = sapi.World.GridRecipes.Any(r => r.Output?.Code != null && r.Output.Code.ToString() == "vintagee:gadget-tomeofknowledge");
            Check(tome != null && !tomeRecipe && V("vintagee:gadget-tomeofknowledge") <= 0, "Tome of Knowledge has no recipe and no EMC value");

            CollectibleObject dmPed = C("vintagee:pedestal-empty"), rmPed = C("vintagee:redmatterpedestal-empty");
            Check(rmPed is Block rb && rb.Attributes?["pedestalRange"].AsFloat(1) == 2 && rb.Attributes?["pedestalPower"].AsFloat(1) == 2,
                "red matter pedestal: twice the range and power");
            Check(V("vintagee:redmatterpedestal-empty") > V("vintagee:pedestal-empty") && V("vintagee:pedestal-empty") > 0,
                $"red matter pedestal is worth more than the dark one: {EmcFormat.Format(V("vintagee:redmatterpedestal-empty"))} > {EmcFormat.Format(V("vintagee:pedestal-empty"))}");

            // No-transmute list: a value is kept, burning and creating are refused, and taking it off works.
            CollectibleObject granite = C("game:rock-granite");
            string key = VintageEModSystem.ConfigKey(granite);
            mod.SetNoTransmute(new[] { key }, true);
            string refused = mod.Transmutation.PlanBurn(null, new ItemStack(granite), out _, out _);
            Check(mod.Emc.IsNoTransmute(granite) && mod.Emc.GetValue(granite) > 0 && refused == "notransmute", $"no-transmute: granite keeps its value but can't be burned ({refused})");
            Check(mod.Emc.ToPacket().NoTransmuteKeys?.Contains(PlayerEmc.KeyOf(granite)) == true, "no-transmute list goes to clients");
            mod.SetNoTransmute(new[] { key }, false);
            Check(!mod.Emc.IsNoTransmute(granite) && mod.Transmutation.PlanBurn(null, new ItemStack(granite), out _, out _) == null, "no-transmute: taken off again");

            // Tablet magnet filter.
            Item tabletItem = sapi.World.GetItem(new AssetLocation("vintagee:transmutationtablet"));
            var tablet = new ItemStack(tabletItem);
            Check(!TabletMagnet.Passes(tablet, granite), "magnet: an empty 'only these' filter pulls nothing");
            ((TreeAttribute)tablet.Attributes).SetStringArray(TabletMagnet.FilterKey, new[] { PlayerEmc.KeyOf(granite), "", "" });
            Check(TabletMagnet.Passes(tablet, granite) && !TabletMagnet.Passes(tablet, C("game:rock-andesite")), "magnet: 'only these' pulls just the filtered item");
            tablet.Attributes.SetBool(TabletMagnet.ExcludeKey, true);
            Check(!TabletMagnet.Passes(tablet, granite) && TabletMagnet.Passes(tablet, C("game:rock-andesite")), "magnet: 'all except these' skips the filtered item");
            Check(TabletMagnet.IsMagnet(new ItemStack(sapi.World.GetItem(new AssetLocation("vintagee:trinket-blackholeband")))) && !TabletMagnet.IsMagnet(new ItemStack(granite)),
                "magnet: Black Hole Band fits, a rock doesn't");

            // Loops and reasons.
            Line($"  value loops: {report.Loops.Count}, settled loops: {report.SettledLoops.Count}");
            foreach (var loop in report.Loops.Take(5)) Line("    " + MissingAnalysis.LoopText(loop));
            Check(report.Missing.All(c => report.MissingWhy.ContainsKey(c)), "every missing item has a reason");

            // Files go to ModData/vintagee and the chat says where.
            string dir = VintageEModSystem.DataFolder(sapi);
            string dumpAnswer = RunCommand("/emc dump");
            Check(File.Exists(Path.Combine(dir, "emc-values.csv")) && dumpAnswer.Contains(dir), "/emc dump writes ModData/vintagee/emc-values.csv and says where: " + dumpAnswer);
            string loopAnswer = RunCommand("/emc loop");
            Check(File.Exists(Path.Combine(dir, "emc-loops.txt")) && loopAnswer.Contains("emc-loops.txt"), "/emc loop writes emc-loops.txt: " + loopAnswer.Split('\n')[0]);
            Check(dir.Replace('\\', '/').Contains("ModData/vintagee"), "data folder is ModData/vintagee: " + dir);

            // Resetting settings.
            double defaultRange = new VintageEConfig().TableUseRange;
            RunCommand("/emc configure TableUseRange 13");
            string reset = RunCommand("/emc configure TableUseRange default");
            Check(Math.Abs(mod.Config.TableUseRange - defaultRange) < 1e-9, "/emc configure <setting> default resets it: " + reset);
            Check(ConfigureCommand.DefaultTexts().Length == ConfigureCommand.Snapshot(mod.Config).names.Length, "every setting has a default for the window");
        }

        /// <summary>
        /// ProjectETextures / ProjectESounds: every ProjectE file the swap list names is there, and - when the server
        /// test turns the settings on - blocks, items and projectiles really use ProjectE's textures.
        /// </summary>
        void CheckProjectELook()
        {
            Line("ProjectE look: textures " + (mod.Config.ProjectETextures ? "on" : "off") + ", sounds " + (mod.Config.ProjectESounds ? "on" : "off"));
            foreach (string s in new[] { PESounds.Charge, PESounds.Uncharge, PESounds.Heal, PESounds.Destruct, PESounds.Power, PESounds.Transmute, PESounds.WaterMagic, PESounds.WindMagic })
            {
                Check(sapi.Assets.Exists(PESounds.Location(s).WithPathAppendixOnce(".ogg")), "ProjectE sound " + s + " is there");
            }
            Check(PESounds.On(sapi.World) == mod.Config.ProjectESounds, "ProjectE sounds follow the setting");

            bool Tex(string path) => sapi.Assets.Exists(new AssetLocation(path).WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
            if (!mod.Config.ProjectETextures)
            {
                Check(mod.LookResult == null, "without ProjectE textures nothing is swapped");
                return;
            }

            ProjectELook.Result r = mod.LookResult;
            Check(r != null && r.Textures > 60, $"ProjectE textures swapped at start-up ({r?.Textures ?? 0} textures, {r?.Collectibles ?? 0} blocks/items, {r?.Entities ?? 0} entity types)");
            if (r == null) return;
            Check(r.MissingFiles.Count == 0, "every ProjectE texture in the swap list is there" + (r.MissingFiles.Count == 0 ? "" : ": missing " + string.Join(", ", r.MissingFiles)));
            // A dedicated server frees block/item textures once they're sent to players, so check what the swap recorded.
            string Got(string key) => r.Swapped.TryGetValue(key, out string v) ? v : null;
            void Expect(string key, string want, string what) => Check(Got(key) == want, what + ": " + (Got(key) ?? "unchanged"));

            Expect("vintagee:trinket-zeroring#all", "vintagee:projecte/item/rings/zero_off", "Zero Ring uses ProjectE's texture");
            Expect("vintagee:energycollector-mk2#up", "vintagee:projecte/block/collectors/top_2", "collector MK2 top is ProjectE's");
            Expect("vintagee:energycollector-mk2#north", "vintagee:projecte/block/collectors/front", "collector front is ProjectE's");
            Expect("vintagee:energycollector-mk2#east", "vintagee:projecte/block/collectors/other", "collector sides are ProjectE's plain face");
            Expect("vintagee:energycollector-mk2#down", "vintagee:projecte/block/collectors/other", "collector underside is ProjectE's plain face");
            Expect("vintagee:energycondenser-mk2#down", "vintagee:projecte/derived/condenser_mk2-bottom", "condenser MK2 underside is cut from ProjectE's chest texture");
            Expect("vintagee:energycondenser-mk1#up", "vintagee:projecte/derived/condenser_mk1-top", "condenser MK1 top is cut from ProjectE's chest texture");
            Expect("vintagee:transmutationtable#top", "vintagee:projecte/block/transmutation_stone/top", "transmutation table top is ProjectE's");
            Expect("vintagee:interdictiontorch#glow", "vintagee:projecte/derived/interdiction_torch-glow", "interdiction torch flame is ProjectE's");
            Expect("vintagee:alchemicalbag-red#sprite", "vintagee:projecte/item/alchemy_bags/red", "red alchemical bag is ProjectE's flat bag");
            Item bag = sapi.World.GetItem(new AssetLocation("vintagee:alchemicalbag-red"));
            Check(bag != null && bag.Shape == null, "red alchemical bag has no sack model");
            string pickHead = r.Swapped.Where(kv => kv.Key.StartsWith("vintagee:") && kv.Key.Contains("dark") && kv.Key.Contains("pickaxe#")).Select(kv => kv.Value).FirstOrDefault();
            Check(pickHead == "vintagee:projecte/block/dark_matter_block", "dark matter pickaxe head is ProjectE's dark matter: " + pickHead);
            Expect("vintagee:projectile-fire#orb", "vintagee:projecte/entity/fireball", "fireball uses ProjectE's sprite");
            Expect("vintagee:projectile-fire#shape", "vintagee:entity/projectile-sprite", "fireball is drawn whole");
            Check(Got("vintagee:projectile-arrow#shape") == null && Got("vintagee:projectile-arrow#orb") == null, "smite arrows (no ProjectE texture) keep their own look");
            Check(Got("vintagee:kleinstar-omega#all") == null, "Klein Stars (already ProjectE's art) are left alone");

            // The looks rings switch to while rendering (worked out on the client from the texture it received).
            AssetLocation Look(Item item, int mode, bool on)
            {
                var stack = new ItemStack(item);
                stack.Attributes.SetInt(ItemEmcTrinket.ModeKey, mode);
                if (on) stack.Attributes.SetBool(ItemEmcTrinket.ActiveKey, true);
                string tex = Got(item?.Code + "#all");
                return tex == null ? null : ProjectELook.TrinketLook(item as ItemEmcTrinket, stack, new AssetLocation(tex));
            }
            Item zero = sapi.World.GetItem(new AssetLocation("vintagee:trinket-zeroring"));
            Item arcana = sapi.World.GetItem(new AssetLocation("vintagee:trinket-ringofarcana"));
            Item gale = sapi.World.GetItem(new AssetLocation("vintagee:trinket-swiftwolfsrendinggale"));
            Check(Look(zero, 0, false) == null, "Zero Ring switched off keeps its texture");
            AssetLocation look = Look(zero, 0, true);
            Check(look?.ToString() == "vintagee:projecte/item/rings/zero_on" && Tex(look.ToString()), "Zero Ring switched on lights up: " + look);
            look = Look(arcana, 2, true);
            Check(look?.ToString() == "vintagee:projecte/item/rings/arcana_2_on" && Tex(look.ToString()), "Ring of Arcana shows its mode: " + look);
            look = Look(gale, 2, true); // shield only
            Check(look?.ToString() == "vintagee:projecte/item/rings/swrg_on3" && Tex(look.ToString()), "Swiftwolf's shield mode shows ProjectE's shield look: " + look);
            int lit = 0;
            foreach (Item t in sapi.World.Collectibles.OfType<ItemEmcTrinket>())
            {
                for (int mode = 0; mode < 4; mode++)
                {
                    look = Look(t, mode, true);
                    if (look == null) continue;
                    lit++;
                    if (!Tex(look.ToString())) Check(false, $"{t.Code} switched-on texture {look} is there");
                }
            }
            Check(lit > 20, $"switched-on looks found for the rings and stones ({lit})");
        }

        /// <summary>/emc configure: a number, a list entry and a table entry can be changed, then put back.</summary>
        void CheckConfigure()
        {
            VintageEConfig cfg = mod.Config;
            double range = cfg.TableUseRange;
            string answer = RunCommand("/emc configure tableuserange 9.5");
            Line("  /emc configure tableuserange 9.5 -> " + answer.Replace("\n", " | "));
            Check(Math.Abs(mod.Config.TableUseRange - 9.5) < 1e-9, "/emc configure changes a number setting");
            RunCommand("/emc configure TableUseRange " + range.ToString(System.Globalization.CultureInfo.InvariantCulture));

            RunCommand("/emc configure HostileCreatures add game:fox-*");
            bool added = mod.Config.HostileCreatures.Contains("game:fox-*");
            RunCommand("/emc configure HostileCreatures remove game:fox-*");
            Check(added && !mod.Config.HostileCreatures.Contains("game:fox-*"), "/emc configure adds and removes list entries");

            Block bucket = sapi.World.GetBlock(new AssetLocation("game:woodbucket"));
            RunCommand("/emc configure StackDataModes set block:game:woodbucket full");
            bool full = bucket != null && mod.Emc.StackData.ModeOf(bucket) == StackDataMode.Full;
            RunCommand("/emc configure StackDataModes remove block:game:woodbucket");
            Check(full && mod.Emc.StackData.ModeOf(bucket) == StackDataMode.Item, "/emc configure sets a contents mode and puts it back");

            string bad = RunCommand("/emc configure WatchSpeedUp lots");
            Check(bad.Contains("Error"), "/emc configure refuses a bad value (" + bad + ")");
            string list = RunCommand("/emc configure list");
            Check(list.Contains("RepairTalismanClothingPercentPerMinute"), "/emc configure list shows the settings");

            // The settings window sends whole values as text.
            var (names, values) = ConfigureCommand.Snapshot(cfg);
            string hostile = values[Array.IndexOf(names, "HostileCreatures")];
            ConfigureCommand.SetFromText(mod, "HostileCreatures", hostile + "\ngame:fox-*", "en");
            bool viaWindow = mod.Config.HostileCreatures.Contains("game:fox-*");
            ConfigureCommand.SetFromText(mod, "HostileCreatures", hostile, "en");
            Check(viaWindow && !mod.Config.HostileCreatures.Contains("game:fox-*"), "the settings window replaces a list");
            bool refused = false;
            try { ConfigureCommand.SetFromText(mod, "TransitionEmcPerDay", "dry = lots", "en"); }
            catch (FormatException) { refused = true; }
            Check(refused && mod.Config.TransitionEmcPerDay.TryGetValue("dry", out double dry) && dry == 64, "the settings window refuses a bad table line and keeps the old value");
            ConfigureCommand.SetFromText(mod, "WatchSpeedUp", "12", "en");
            bool number = Math.Abs(mod.Config.WatchSpeedUp - 12) < 1e-9;
            ConfigureCommand.SetFromText(mod, "WatchSpeedUp", "10", "en");
            Check(number, "the settings window changes a number");
        }

        /// <summary>An element's box in model space at rest, in voxels.</summary>
        static string Describe(ShapeElement e)
        {
            var chain = new List<ShapeElement>();
            for (ShapeElement p = e; p != null; p = p.ParentElement) chain.Insert(0, p);
            float[] m = Mat4f.Create();
            foreach (ShapeElement p in chain) m = Mat4f.Mul(Mat4f.Create(), m, p.GetLocalTransformMatrix(0, Mat4f.Create()));
            float[] a = Mat4f.MulWithVec4(m, 0, 0, 0, 1);
            float[] b = Mat4f.MulWithVec4(m, (float)((e.To[0] - e.From[0]) / 16), (float)((e.To[1] - e.From[1]) / 16), (float)((e.To[2] - e.From[2]) / 16), 1);
            string V(float[] v) => $"[{v[0] * 16:0.##}, {v[1] * 16:0.##}, {v[2] * 16:0.##}]";
            return $"{V(a)} to {V(b)}";
        }

        BlockEntityFirepit PlaceFurnace(BlockPos pos, string type)
        {
            Block block = sapi.World.GetBlock(new AssetLocation("vintagee", "furnace-" + type + "-cold"));
            Check(block != null && block.Id != 0, $"furnace-{type}-cold exists");
            if (block == null || block.Id == 0) return null;
            sapi.World.BlockAccessor.SetBlock(block.Id, pos);
            // A roof, so rain can't put it out (firepits go out in the rain).
            Block roof = sapi.World.GetBlock(new AssetLocation("game:rock-granite"));
            if (roof != null) sapi.World.BlockAccessor.SetBlock(roof.Id, pos.UpCopy(3));
            var be = sapi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityFirepit;
            Check(be is BEMatterFurnace, $"{type} furnace has its block entity ({be?.GetType().Name ?? "none"})");
            return be;
        }

        void Report(string label, BlockEntityFirepit be)
        {
            if (be == null) { Check(false, label + ": no furnace"); return; }
            Block now = sapi.World.BlockAccessor.GetBlock(be.Pos);
            ItemStack output = be.outputSlot?.Itemstack;
            ItemStack input = be.inputSlot?.Itemstack;
            Line($"  {label}: block {now?.Code}, {be.furnaceTemperature:0} °C, max {be.maxTemperature}, burning {be.IsBurning}, input {input?.Collectible.Code} x{input?.StackSize}, output {output?.Collectible.Code} x{output?.StackSize}");
            Check(output?.Collectible?.Code?.Path?.Contains("cooked") == true, label + " cooked the meat");
        }

        EntityVintageEProjectile Fire(string kind, Vec3d from, Action<EntityVintageEProjectile> setup)
        {
            EntityProperties type = sapi.World.GetEntityType(new AssetLocation("vintagee", "projectile-" + kind));
            Check(type != null, $"entity type vintagee:projectile-{kind} exists");
            if (type == null) return null;
            var shot = sapi.World.ClassRegistry.CreateEntity(type) as EntityVintageEProjectile;
            Check(shot != null, $"projectile-{kind} uses the projectile class");
            if (shot == null) return null;
            shot.Pos.SetPos(from);
            shot.Pos.Motion.Set(0, -0.5, 0);
            setup?.Invoke(shot);
            sapi.World.SpawnEntity(shot);
            return shot;
        }

        int CountSolid(BlockPos center, int r)
        {
            int n = 0;
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dz = -r; dz <= r; dz++)
                        if (sapi.World.BlockAccessor.GetBlock(center.AddCopy(dx, dy, dz))?.Id > 0) n++;
            return n;
        }

        void Finish()
        {
            Line(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            try
            {
                string dir = VintageEModSystem.DataFolder(sapi);
                File.WriteAllText(Path.Combine(dir, "selftest.txt"), log.ToString());
            }
            catch (Exception e) { sapi.Logger.Error("Vintage-E self-test: could not write the report: {0}", e); }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Walks everything the game knows about turning one thing into another and turns it into
    /// <see cref="Conversion"/>s for the mapper:
    /// <list type="bullet">
    /// <item>crafting-grid recipes (<c>World.GridRecipes</c>);</item>
    /// <item>every recipe list held by any loaded mod system whose element type implements <see cref="IRecipeBase"/> —
    ///   this finds vanilla smithing, knapping, clay forming and barrel recipes <b>and</b> other mods' custom recipe types
    ///   without needing to know about them;</item>
    /// <item>alloy recipes (found the same way, by shape);</item>
    /// <item>per-item properties: smelting/firing/cooking output, quern grinding, pulverizer crushing, and drying,
    ///   curing, ripening, hardening, melting and converting transitions.</item>
    /// </list>
    /// </summary>
    public class ConversionCollector
    {
        // Voxels per material unit in the layered-voxel recipe types.
        const double VoxelsPerIngot = 42;     // ItemIngot.VoxelCount in VSSurvivalMod
        const double VoxelsPerClay = 25;      // BlockEntityClayForm adds 25 voxels per clay

        static readonly EnumTransitionType[] UsefulTransitions =
        {
            EnumTransitionType.Dry, EnumTransitionType.Cure, EnumTransitionType.Ripen,
            EnumTransitionType.Harden, EnumTransitionType.Melt, EnumTransitionType.Convert
        };

        readonly ICoreServerAPI sapi;
        readonly VintageEConfig config;
        readonly CollectibleIndex index;
        readonly ILogger logger;
        readonly Dictionary<string, CollectibleObject[]> choiceCache = new Dictionary<string, CollectibleObject[]>();
        readonly HashSet<object> seenRecipes = new HashSet<object>(ReferenceEqualityComparer.Instance);
        readonly HashSet<object> seenLists = new HashSet<object>(ReferenceEqualityComparer.Instance);

        public readonly List<Conversion> Conversions = new List<Conversion>();
        /// <summary>Human-readable name of every recipe list found → number of conversions it produced.</summary>
        public readonly SortedDictionary<string, int> SourceCounts = new SortedDictionary<string, int>();
        public int SkippedRecipes;

        public ConversionCollector(ICoreServerAPI sapi, CollectibleIndex index, ILogger logger, VintageEConfig config = null)
        {
            this.sapi = sapi;
            this.config = config ?? new VintageEConfig();
            this.index = index;
            this.logger = logger;
        }

        public void CollectAll()
        {
            CollectRecipeList(sapi.World.GridRecipes, "grid");
            CollectFromModSystems();
            CollectFromCollectibleProperties();
            CollectFromBlockDrops();
            CollectFromAttributes();
            CollectFromMolds();
            CollectFromBehaviors();
        }

        #region Recipe lists discovered by reflection

        void CollectFromModSystems()
        {
            foreach (DiscoveredRecipeList found in DiscoverRecipeLists(sapi, logger))
            {
                if (found.Alloy != null) CollectAlloys(found.Recipes, found.Alloy, found.Label);
                else CollectRecipeList(found.Recipes, found.Label);
            }
        }

        /// <summary>A recipe list found on some mod system: either IRecipeBase recipes or alloy-shaped ones.</summary>
        public sealed class DiscoveredRecipeList
        {
            public string Label;
            public IEnumerable Recipes;
            internal AlloyShape Alloy;
        }

        /// <summary>Every recipe list held by any loaded mod system (fields, properties, recipe registries), found by type.</summary>
        public static List<DiscoveredRecipeList> DiscoverRecipeLists(ICoreServerAPI sapi, ILogger logger)
        {
            var result = new List<DiscoveredRecipeList>();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (ModSystem system in sapi.ModLoader.Systems)
            {
                if (system == null || system is VintageEModSystem) continue;
                for (Type t = system.GetType(); t != null && t != typeof(ModSystem) && t != typeof(object); t = t.BaseType)
                {
                    foreach (FieldInfo field in t.GetFields(flags))
                    {
                        Discover(field.FieldType, () => field.GetValue(system), $"{system.GetType().Name}.{field.Name}", result, seen, logger);
                    }
                    foreach (PropertyInfo prop in t.GetProperties(flags))
                    {
                        if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                        Discover(prop.PropertyType, () => prop.GetValue(system), $"{system.GetType().Name}.{prop.Name}", result, seen, logger);
                    }
                }
            }
            return result;
        }

        static void Discover(Type declaredType, Func<object> getter, string label, List<DiscoveredRecipeList> result, HashSet<object> seen, ILogger logger)
        {
            try
            {
                if (typeof(RecipeRegistryBase).IsAssignableFrom(declaredType))
                {
                    object registry = getter();
                    FieldInfo recipesField = registry?.GetType().GetField("Recipes");
                    if (recipesField != null) Discover(recipesField.FieldType, () => recipesField.GetValue(registry), label, result, seen, logger);
                    return;
                }

                Type element = EnumerableElementType(declaredType);
                if (element == null) return;

                AlloyShape shape = null;
                if (!typeof(IRecipeBase).IsAssignableFrom(element) && !AlloyShape.TryCreate(element, out shape)) return;
                if (!(getter() is IEnumerable list) || !seen.Add(list)) return;
                result.Add(new DiscoveredRecipeList { Label = label, Recipes = list, Alloy = shape });
            }
            catch (Exception e)
            {
                logger.VerboseDebug("EMC: could not read {0}: {1}", label, e.Message);
            }
        }

        static Type EnumerableElementType(Type type)
        {
            if (type == null || type == typeof(string)) return null;
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return type.GetGenericArguments()[0];
            Type ienum = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return ienum?.GetGenericArguments()[0];
        }

        void CollectRecipeList(IEnumerable recipes, string label)
        {
            if (recipes == null || !seenLists.Add(recipes)) return;
            int before = Conversions.Count;
            foreach (object obj in recipes)
            {
                if (obj is IRecipeBase recipe && seenRecipes.Add(recipe))
                {
                    try { AddRecipe(recipe, label); }
                    catch (Exception e)
                    {
                        SkippedRecipes++;
                        logger.VerboseDebug("EMC: skipped recipe {0} from {1}: {2}", recipe.Name, label, e.Message);
                    }
                }
            }
            Count(label, Conversions.Count - before);
        }

        void Count(string label, int n)
        {
            SourceCounts.TryGetValue(label, out int prev);
            SourceCounts[label] = prev + n;
        }

        void AddRecipe(IRecipeBase recipe, string label)
        {
            if (!recipe.Enabled) return;
            ItemStack output = recipe.RecipeOutput?.ResolvedItemStack;
            if (output?.Collectible == null || output.StackSize <= 0) return;

            // Voxel-based recipes: the real material cost comes from how many voxels the pattern fills.
            double? voxelQuantity = null;
            if (recipe is LayeredVoxelRecipe voxelRecipe && voxelRecipe.Voxels != null)
            {
                int voxels = 0;
                foreach (bool v in voxelRecipe.Voxels) if (v) voxels++;
                string typeName = recipe.GetType().Name;
                if (typeName.Contains("Smithing")) voxelQuantity = Math.Max(voxels / VoxelsPerIngot, 1.0 / VoxelsPerIngot);
                else if (typeName.Contains("ClayForming")) voxelQuantity = Math.Max(voxels / VoxelsPerClay, 1.0 / VoxelsPerClay);
            }

            var conv = new Conversion
            {
                Output = output.Collectible,
                OutputQuantity = RecipeOutputQuantity(recipe.RecipeOutput, output),
                Kind = ConversionKind.Crafting,
                Source = label
            };
            JsonObject recipeAttributes = (recipe as RecipeBase)?.Attributes;

            bool first = true;
            foreach (IRecipeIngredient ing in recipe.RecipeIngredients)
            {
                if (ing == null) continue;
                RecipeIngredientConsumeProperties consume = ing.ConsumeProperties;
                bool isFirst = first;
                first = false;
                if (!consume.Consume) continue; // tools and catalysts (e.g. the philosopher's stone) cost nothing

                CollectibleObject[] choices = ResolveChoices(ing);
                if (choices.Length == 0) return; // unresolvable ingredient → recipe unusable

                // A bucket/bowl/jug of liquid in a crafting recipe: the game only takes the liquid out and hands the
                // container back (unless the recipe says consumeContainer), so cost the liquid, not the container.
                if (TryLiquidContent(ing, recipeAttributes, choices, out ConversionInput liquid, out bool consumeContainer))
                {
                    conv.Inputs.Add(liquid);
                    if (!consumeContainer) continue;
                }

                double qty = ing.Quantity;
                int? consumeQty = ReadOptionalInt(ing, "ConsumeQuantity"); // barrel recipes may only use part of the stack
                if (consumeQty.HasValue) qty = consumeQty.Value;
                if (isFirst && voxelQuantity.HasValue) qty = voxelQuantity.Value;
                if (qty <= 0) continue;

                conv.Inputs.Add(new ConversionInput { Choices = choices, Quantity = qty });

                ItemStack returned = ing.ReturnedStack?.ResolvedItemstack;
                if (returned?.Collectible != null)
                {
                    conv.Returned ??= new List<KeyValuePair<CollectibleObject, double>>();
                    conv.Returned.Add(new KeyValuePair<CollectibleObject, double>(returned.Collectible, (double)returned.StackSize));
                }
            }

            AddIfSane(conv);
        }

        /// <summary>
        /// How many items one craft really makes. Barrel recipes convert litres to portions on the output's own
        /// StackSize after its ResolvedItemStack was created, so the resolved stack still says 1 while the game
        /// produces e.g. 100 portions. Read the number the game itself uses.
        /// </summary>
        internal static double RecipeOutputQuantity(IRecipeOutput recipeOutput, ItemStack resolved)
        {
            // JsonItemStack outputs (barrel, smithing, knapping...) are handed out using their own StackSize;
            // crafting-grid outputs are cloned from the resolved stack. They only differ for converted liquids.
            if (recipeOutput is JsonItemStack js && js.StackSize > 0) return js.StackSize;
            return resolved.StackSize;
        }

        /// <summary>
        /// Liquid-container ingredients (vanilla BlockLiquidContainerBase reads "requiresContent"/"requiresLitres"
        /// from the ingredient's recipeAttributes or the recipe's liquidContainerProps). Returns the liquid as an input,
        /// in portions: litres × the liquid's itemsPerLitre.
        /// </summary>
        bool TryLiquidContent(IRecipeIngredient ing, JsonObject recipeAttributes, CollectibleObject[] choices, out ConversionInput liquid, out bool consumeContainer)
        {
            liquid = null;
            consumeContainer = false;
            if (!choices.Any(IsLiquidContainer)) return false;

            JsonObject props = ing.RecipeAttributes;
            if (props?.Exists != true || props["requiresContent"].Exists != true) props = recipeAttributes?["liquidContainerProps"];
            if (props?.Exists != true || props["requiresContent"].Exists != true) return false;

            string code = props["requiresContent"]["code"].AsString(null);
            if (code == null) return false;
            EnumItemClass type = string.Equals(props["requiresContent"]["type"].AsString("item"), "block", StringComparison.OrdinalIgnoreCase) ? EnumItemClass.Block : EnumItemClass.Item;
            string domain = ing.Code?.Domain ?? "game";
            string pattern = (type == EnumItemClass.Block ? "block:" : "item:") + AssetLocation.Create(code, domain);
            CollectibleObject[] contents = index.Match(pattern).ToArray();
            if (contents.Length == 0) return false;

            double litres = props["requiresLitres"].AsDouble(0);
            if (litres <= 0) return false;
            double perLitre = contents[0].Attributes?["waterTightContainerProps"]?["itemsPerLitre"].AsDouble(1) ?? 1;
            liquid = new ConversionInput { Choices = contents, Quantity = litres * perLitre };
            consumeContainer = props.IsTrue("consumeContainer");
            return true;
        }

        static readonly Dictionary<Type, bool> liquidContainerTypes = new Dictionary<Type, bool>();

        /// <summary>True for vanilla liquid containers (and modded subclasses), detected by class name so the survival mod isn't a compile-time dependency.</summary>
        static bool IsLiquidContainer(CollectibleObject c)
        {
            Type t = c.GetType();
            lock (liquidContainerTypes)
            {
                if (liquidContainerTypes.TryGetValue(t, out bool known)) return known;
                bool result = false;
                for (Type x = t; x != null && x != typeof(object); x = x.BaseType)
                {
                    if (x.Name == "BlockLiquidContainerBase") { result = true; break; }
                }
                liquidContainerTypes[t] = result;
                return result;
            }
        }

        static int? ReadOptionalInt(object obj, string member)
        {
            Type t = obj.GetType();
            object val = t.GetField(member)?.GetValue(obj) ?? t.GetProperty(member)?.GetValue(obj);
            return val is int i ? i : (int?)null;
        }

        /// <summary>All collectibles an ingredient accepts. Exact codes resolve directly; wildcards/tags are expanded once and cached.</summary>
        CollectibleObject[] ResolveChoices(IRecipeIngredientBase ing)
        {
            if (ing.MatchingType == EnumRecipeMatchType.Exact)
            {
                CollectibleObject c = ing.ResolvedItemStack?.Collectible;
                return c == null ? Array.Empty<CollectibleObject>() : new[] { c };
            }

            string key = $"{ing.Type}|{ing.MatchingType}|{ing.Code}|{Join(ing.AllowedVariants)}|{Join(ing.SkipVariants)}|{ing.Tags}";
            if (choiceCache.TryGetValue(key, out var cached)) return cached;

            IEnumerable<CollectibleObject> pool = ing.Code == null || ing.MatchingType == EnumRecipeMatchType.TagsOnly || ing.MatchingType == EnumRecipeMatchType.Regex
                ? index.All
                : index.Candidates(ing.Code);

            var result = new List<CollectibleObject>();
            foreach (CollectibleObject c in pool)
            {
                if (c.ItemClass != ing.Type) continue;
                try
                {
                    if (ing.SatisfiesAsIngredient(new ItemStack(c), false)) result.Add(c);
                }
                catch
                {
                    // some collectibles cannot be stacked without attributes; they just don't count as a match
                }
            }
            CollectibleObject[] arr = result.ToArray();
            choiceCache[key] = arr;
            return arr;
        }

        static string Join(string[] arr) => arr == null ? "" : string.Join(",", arr);

        #endregion

        #region Alloys (matched by shape so the survival mod is not a compile-time dependency)

        internal sealed class AlloyShape
        {
            public FieldInfo Ingredients, Output, Enabled, MinRatio, MaxRatio;

            public static bool TryCreate(Type t, out AlloyShape shape)
            {
                shape = null;
                FieldInfo ingredients = t.GetField("Ingredients");
                FieldInfo output = t.GetField("Output");
                if (ingredients == null || output == null || !typeof(JsonItemStack).IsAssignableFrom(output.FieldType)) return false;
                Type ingType = ingredients.FieldType.IsArray ? ingredients.FieldType.GetElementType() : null;
                if (ingType == null || !typeof(JsonItemStack).IsAssignableFrom(ingType)) return false;
                FieldInfo min = ingType.GetField("MinRatio"), max = ingType.GetField("MaxRatio");
                if (min == null || max == null) return false;
                shape = new AlloyShape { Ingredients = ingredients, Output = output, Enabled = t.GetField("Enabled"), MinRatio = min, MaxRatio = max };
                return true;
            }
        }

        void CollectAlloys(IEnumerable alloys, AlloyShape shape, string label)
        {
            if (alloys == null || !seenLists.Add(alloys)) return;
            int before = Conversions.Count;
            foreach (object alloy in alloys)
            {
                if (alloy == null || !seenRecipes.Add(alloy)) continue;
                if (shape.Enabled?.GetValue(alloy) is bool en && !en) continue;
                ItemStack outStack = (shape.Output.GetValue(alloy) as JsonItemStack)?.ResolvedItemstack;
                if (outStack?.Collectible == null || !(shape.Ingredients.GetValue(alloy) is Array ings)) continue;

                var parts = new List<(CollectibleObject c, double ratio)>();
                foreach (object ingObj in ings)
                {
                    var js = ingObj as JsonItemStack;
                    CollectibleObject c = js?.ResolvedItemstack?.Collectible;
                    if (c == null) { parts = null; break; }
                    double ratio = (Convert.ToDouble(shape.MinRatio.GetValue(ingObj)) + Convert.ToDouble(shape.MaxRatio.GetValue(ingObj))) / 2;
                    parts.Add((c, ratio));
                }
                if (parts == null || parts.Count == 0) continue;
                double total = parts.Sum(p => p.ratio);
                if (total <= 0) continue;

                // An alloy is made in metal units; per unit of output it takes `ratio` units of each input metal.
                var conv = new Conversion { Output = outStack.Collectible, OutputQuantity = 1, Kind = ConversionKind.Crafting, Source = label };
                foreach (var (c, ratio) in parts)
                {
                    conv.Inputs.Add(new ConversionInput { Choices = new[] { c }, Quantity = ratio / total });
                }
                AddIfSane(conv);
            }
            Count(label + " (alloys)", Conversions.Count - before);
        }

        #endregion

        #region Item properties

        void CollectFromCollectibleProperties()
        {
            int smelt = 0, grind = 0, crush = 0, transition = 0;
            foreach (CollectibleObject c in index.All)
            {
                CombustibleProperties comb = c.CombustibleProps ?? CombustibleFor(c);
                ItemStack smelted = comb?.SmeltedStack?.ResolvedItemstack;
                if (smelted?.Collectible != null)
                {
                    if (AddProcessing(c, Math.Max(1, comb.SmeltedRatio), smelted.Collectible, smelted.StackSize, "smelting/firing/cooking")) smelt++;
                }

                ItemStack ground = c.GrindingProps?.GroundStack?.ResolvedItemstack;
                if (ground?.Collectible != null && AddProcessing(c, 1, ground.Collectible, ground.StackSize, "quern grinding")) grind++;

                ItemStack crushed = c.CrushingProps?.CrushedStack?.ResolvedItemstack;
                if (crushed?.Collectible != null)
                {
                    double avg = c.CrushingProps.Quantity?.avg ?? 1;
                    if (AddProcessing(c, 1, crushed.Collectible, crushed.StackSize * Math.Max(avg, 0.01), "pulverizer crushing")) crush++;
                }

                if (c.TransitionableProps != null)
                {
                    foreach (TransitionableProperties tp in c.TransitionableProps)
                    {
                        if (tp == null || Array.IndexOf(UsefulTransitions, tp.Type) < 0) continue;
                        ItemStack result = tp.TransitionedStack?.ResolvedItemstack;
                        if (result?.Collectible == null) continue;
                        string type = tp.Type.ToString().ToLowerInvariant();
                        double bonus = 0;
                        if (config.TransitionEmcPerDay.TryGetValue(type, out double perDay) && perDay > 0)
                        {
                            double hoursPerDay = sapi.World.Calendar?.HoursPerDay ?? 24;
                            if (hoursPerDay <= 0) hoursPerDay = 24;
                            double hours = Math.Max(0, tp.FreshHours?.avg ?? 0) + Math.Max(0, tp.TransitionHours?.avg ?? 0);
                            bonus = perDay * hours / hoursPerDay;
                        }
                        if (AddProcessing(c, 1, result.Collectible, result.StackSize * Math.Max(tp.TransitionRatio, 0.0001f), "transitions (" + type + ")", bonus)) transition++;
                    }
                }
            }
            Count("item properties: smelting/firing/cooking", smelt);
            Count("item properties: quern grinding", grind);
            Count("item properties: pulverizer crushing", crush);
            Count("item properties: drying/curing/ripening/etc.", transition);
        }

        /// <summary>
        /// Casting: a tool mold takes requiredUnits of molten metal (100 units = 1 ingot) and drops "drop"/"drops",
        /// whose code contains {metal} (pickaxehead-{metal}, arrowhead-{metal}...). One conversion per metal that has
        /// a matching output. The mold is reused, so it only costs something if it breaks when filled.
        /// </summary>
        void CollectFromMolds()
        {
            const double UnitsPerIngot = 100;
            var ingotsByDomain = new Dictionary<string, List<CollectibleObject>>();
            foreach (CollectibleObject c in index.Match("item:*:ingot-*"))
            {
                if (!ingotsByDomain.TryGetValue(c.Code.Domain, out var list)) ingotsByDomain[c.Code.Domain] = list = new List<CollectibleObject>();
                list.Add(c);
            }
            List<CollectibleObject> allIngots = ingotsByDomain.Values.SelectMany(l => l).ToList();

            int casts = 0;
            foreach (CollectibleObject c in index.All)
            {
                if (!(c is Block mold) || mold.Attributes == null || !IsToolMold(mold)) continue;
                // Only fired/burned molds can take metal (same rule as the game); skip raw duplicates.
                if (mold.Variant != null && mold.Variant.TryGetValue("materialtype", out string mt) && mt != "fired" && !mold.Code.Path.Contains("burned")) continue;

                var drops = new List<JsonObject>();
                if (mold.Attributes["drop"].Exists) drops.Add(mold.Attributes["drop"]);
                else if (mold.Attributes["drops"].Exists) drops.AddRange(mold.Attributes["drops"].AsArray() ?? Array.Empty<JsonObject>());
                if (drops.Count == 0) continue;

                double units = mold.Attributes["requiredUnits"].AsDouble(100);
                bool breaks = mold.Attributes["breaksWhenFilled"].AsBool(false);
                bool perMetal = drops.Any(d => (d["code"].AsString("") ?? "").Contains("{metal}"));
                IEnumerable<CollectibleObject> metals = perMetal ? allIngots : new CollectibleObject[] { null };

                foreach (CollectibleObject ingot in metals)
                {
                    string metal = ingot?.Code.Path.Substring(ingot.Code.Path.LastIndexOf('-') + 1);
                    var outputs = new List<(CollectibleObject item, int size)>();
                    foreach (JsonObject d in drops)
                    {
                        string code = d["code"].AsString(null);
                        if (code == null) continue;
                        if (metal != null) code = code.Replace("{metal}", metal);
                        var (item, size) = ResolveStack(new JsonObject(MakeDropSpec(d, code)), mold.Code.Domain);
                        if (item != null) outputs.Add((item, Math.Max(1, size)));
                    }
                    if (outputs.Count == 0) continue;
                    int total = outputs.Sum(o => o.size);

                    foreach (var (item, size) in outputs.GroupBy(o => o.item).Select(g => (g.Key, g.Sum(o => o.size))))
                    {
                        var conv = new Conversion { Output = item, OutputQuantity = total, Kind = ConversionKind.Crafting, Source = "casting (tool molds)" };
                        CollectibleObject[] metalChoices = ingot != null ? new[] { ingot } : allIngots.ToArray();
                        if (metalChoices.Length == 0) continue;
                        conv.Inputs.Add(new ConversionInput { Choices = metalChoices, Quantity = units / UnitsPerIngot });
                        if (breaks) conv.Inputs.Add(new ConversionInput { Choices = new CollectibleObject[] { mold }, Quantity = 1 });
                        if (AddIfSane(conv)) casts++;
                    }
                }
            }
            Count("casting (tool molds)", casts);
        }

        static Newtonsoft.Json.Linq.JToken MakeDropSpec(JsonObject original, string resolvedCode)
        {
            var copy = original.Token.DeepClone();
            copy["code"] = resolvedCode;
            return copy;
        }

        /// <summary>Vanilla BlockToolMold (and modded subclasses), detected by class name like the liquid containers.</summary>
        static bool IsToolMold(CollectibleObject c)
        {
            for (Type x = c.GetType(); x != null && x != typeof(object); x = x.BaseType)
            {
                if (x.Name == "BlockToolMold") return true;
            }
            return false;
        }

        static double PortionsPerLitre(CollectibleObject liquid)
            => liquid.Attributes?["waterTightContainerProps"]?["itemsPerLitre"].AsDouble(1) ?? 1; // game default is 1

        /// <summary>
        /// Processes defined by collectible behaviours, read from each behaviour's own JSON settings so any mod
        /// using the same behaviours is covered:
        /// squeezing ("Squeezable": squeezedLitres of liquidItemCode, plus returnStacks such as beeswax) and
        /// ground-storage processing ("GroundStoredProcessable": processedStacks, plus a remainingItem left behind;
        /// e.g. sawing into boards). Tools used are not consumed.
        /// </summary>
        void CollectFromBehaviors()
        {
            int squeeze = 0, ground = 0;
            foreach (CollectibleObject c in index.All)
            {
                if (c.CollectibleBehaviors == null) continue;
                foreach (CollectibleBehavior bh in c.CollectibleBehaviors)
                {
                    string json = bh?.propertiesAtString;
                    if (string.IsNullOrEmpty(json) || json.Length < 10) continue;
                    if (!json.Contains("squeezedLitres") && !json.Contains("processedStack") && !json.Contains("remainingItem")) continue;
                    Newtonsoft.Json.Linq.JObject props;
                    try { props = Newtonsoft.Json.Linq.JObject.Parse(json); }
                    catch { continue; }
                    var p = new JsonObject(props);
                    try
                    {
                        if (p["squeezedLitres"].Exists && p["liquidItemCode"].Exists)
                        {
                            var outputs = new List<KeyValuePair<CollectibleObject, double>>();
                            CollectibleObject liquid = ResolveCode(p["liquidItemCode"].AsString(null), c.Code.Domain, EnumItemClass.Item);
                            double litres = p["squeezedLitres"].AsDouble(0);
                            if (liquid != null && litres > 0) outputs.Add(new(liquid, litres * PortionsPerLitre(liquid)));
                            foreach (JsonObject ret in p["returnStacks"].AsArray() ?? Array.Empty<JsonObject>())
                            {
                                var (item, size) = ResolveStack(ret, c.Code.Domain);
                                if (item != null) outputs.Add(new(item, Math.Max(1, size)));
                            }
                            if (AddJoint(c, 1, outputs, "squeezing")) squeeze++;
                        }
                        else
                        {
                            var outputs = new List<KeyValuePair<CollectibleObject, double>>();
                            var stacks = new List<JsonObject>();
                            if (p["processedStacks"].Exists) stacks.AddRange(p["processedStacks"].AsArray() ?? Array.Empty<JsonObject>());
                            if (p["processedStack"].Exists) stacks.Add(p["processedStack"]);
                            foreach (JsonObject st in stacks)
                            {
                                var (item, size) = ResolveStack(st, c.Code.Domain);
                                double qty = Quantity(st["quantity"], 1) * Math.Max(1, st["stacksize"].AsInt(1));
                                if (item != null && qty > 0) outputs.Add(new(item, qty));
                            }
                            var (remaining, remSize) = ResolveStack(p["remainingItem"], c.Code.Domain);
                            if (remaining != null) outputs.Add(new(remaining, Math.Max(1, remSize)));
                            if (AddJoint(c, 1, outputs, "ground-storage processing")) ground++;
                        }
                    }
                    catch (Exception e)
                    {
                        logger.VerboseDebug("EMC: could not read behaviour settings of {0}: {1}", c.Code, e.Message);
                    }
                }
            }
            Count("behaviours: squeezing", squeeze);
            Count("behaviours: ground-storage processing", ground);
        }

        /// <summary>Number or NatFloat ({ "avg": 4.4, "var": 1 }) → its average.</summary>
        static double Quantity(JsonObject q, double fallback)
        {
            if (q == null || !q.Exists) return fallback;
            if (q["avg"].Exists) return q["avg"].AsDouble(fallback);
            return q.AsDouble(fallback);
        }

        /// <summary>One input, several outputs (merged per item; outputs equal to the input are ignored). A single output becomes a normal processing step.</summary>
        bool AddJoint(CollectibleObject input, double inputQty, List<KeyValuePair<CollectibleObject, double>> outputs, string label)
        {
            var merged = outputs
                .Where(o => o.Key != null && o.Key != input && o.Value > 0)
                .GroupBy(o => o.Key)
                .Select(g => new KeyValuePair<CollectibleObject, double>(g.Key, g.Sum(o => o.Value)))
                .ToList();
            if (merged.Count == 0) return false;
            if (merged.Count == 1) return AddProcessing(input, inputQty, merged[0].Key, merged[0].Value, label);
            var conv = new Conversion { Kind = ConversionKind.Processing, Source = label, JointOutputs = merged, OutputQuantity = 1 };
            conv.Inputs.Add(new ConversionInput { Choices = new[] { input }, Quantity = inputQty });
            Conversions.Add(conv);
            return true;
        }

        /// <summary>Some item classes compute their smelting data in code instead of JSON; ask them, safely.</summary>
        CombustibleProperties CombustibleFor(CollectibleObject c)
        {
            try { return c.GetCombustibleProperties(sapi.World, new ItemStack(c), null); }
            catch { return null; }
        }

        /// <summary>
        /// Natural blocks (ores, crops, plants, clay...) are mostly never crafted, but they drop things that do have a value.
        /// A block that drops other items is worth what it drops; a block that drops a single kind of item also values that item.
        /// Blocks that drop themselves, or have a block entity (dynamic drops), are skipped.
        /// </summary>
        void CollectFromBlockDrops()
        {
            int blockValue = 0, itemValue = 0;
            foreach (CollectibleObject c in index.All)
            {
                if (!(c is Block block) || block.Drops == null || block.Drops.Length == 0 || block.EntityClass != null) continue;

                var drops = new List<ConversionInput>();
                bool dropsItself = false;
                foreach (BlockDropItemStack drop in block.Drops)
                {
                    ItemStack stack = drop?.ResolvedItemstack;
                    if (stack?.Collectible == null) continue;
                    if (stack.Collectible == block) { dropsItself = true; break; }
                    double qty = (drop.Quantity?.avg ?? 1) * Math.Max(1, stack.StackSize);
                    if (qty <= 0) continue;
                    ConversionInput existing = drops.FirstOrDefault(d => d.Choices[0] == stack.Collectible);
                    if (existing != null) existing.Quantity += qty;
                    else drops.Add(new ConversionInput { Choices = new[] { stack.Collectible }, Quantity = qty });
                }
                if (dropsItself || drops.Count == 0) continue;

                // block = what it drops
                var conv = new Conversion { Output = block, OutputQuantity = 1, Kind = ConversionKind.Crafting, Source = "block drops" };
                conv.Inputs.AddRange(drops);
                if (AddIfSane(conv)) blockValue++;

                // single drop: the item is worth its share of the block (and vice versa via inference)
                if (drops.Count == 1 && AddProcessing(block, 1, drops[0].Choices[0], drops[0].Quantity, "block drops (single item)")) itemValue++;
            }
            Count("block drops", blockValue);
            Count("block drops (single item)", itemValue);
        }

        /// <summary>
        /// Processing steps that vanilla stores in item attributes rather than recipes:
        /// clay-oven baking (bakingProperties.resultCode), the fruit press (juiceableProperties) and the still (distillationProps).
        /// Read as plain JSON so other mods using the same attributes are covered too.
        /// </summary>
        void CollectFromAttributes()
        {
            int baking = 0, juicing = 0, distilling = 0;
            foreach (CollectibleObject c in index.All)
            {
                JsonObject attrs = c.Attributes;
                if (attrs == null) continue;
                try
                {
                    string result = attrs["bakingProperties"]?["resultCode"].AsString(null);
                    if (result != null)
                    {
                        CollectibleObject baked = ResolveCode(result, c.Code.Domain, c.ItemClass);
                        if (baked != null && baked != c && AddProcessing(c, 1, baked, 1, "clay oven baking")) baking++;
                    }

                    JsonObject juice = attrs["juiceableProperties"];
                    if (juice != null && juice.Exists)
                    {
                        var (liquid, size) = ResolveStack(juice["liquidStack"], c.Code.Domain);
                        double litres = juice["litresPerItem"].AsDouble(0);
                        if (liquid != null && litres > 0)
                        {
                            var outputs = new List<KeyValuePair<CollectibleObject, double>> { new(liquid, litres * PortionsPerLitre(liquid) * Math.Max(1, size)) };
                            var (pressed, pressedSize) = ResolveStack(juice["pressedStack"], c.Code.Domain);
                            if (pressed != null) outputs.Add(new(pressed, Math.Max(1, pressedSize)));
                            if (AddJoint(c, 1, outputs, "fruit press")) juicing++;
                        }
                    }

                    JsonObject distill = attrs["distillationProps"];
                    if (distill != null && distill.Exists)
                    {
                        var (spirit, size) = ResolveStack(distill["distilledStack"], c.Code.Domain);
                        double ratio = distill["ratio"].AsDouble(1);
                        if (spirit != null && ratio > 0 && AddProcessing(c, 1, spirit, ratio * Math.Max(1, size), "distillation")) distilling++;
                    }
                }
                catch (Exception e)
                {
                    logger.VerboseDebug("EMC: could not read processing attributes of {0}: {1}", c.Code, e.Message);
                }
            }
            Count("attributes: clay oven baking", baking);
            Count("attributes: fruit press", juicing);
            Count("attributes: distillation", distilling);
        }

        (CollectibleObject, int) ResolveStack(JsonObject spec, string defaultDomain)
        {
            if (spec == null || !spec.Exists) return (null, 0);
            string code = spec["code"].AsString(null);
            if (code == null) return (null, 0);
            EnumItemClass type = string.Equals(spec["type"].AsString("item"), "block", StringComparison.OrdinalIgnoreCase) ? EnumItemClass.Block : EnumItemClass.Item;
            int size = spec["stacksize"].AsInt(spec["quantity"].AsInt(1));
            return (ResolveCode(code, defaultDomain, type), size);
        }

        CollectibleObject ResolveCode(string code, string defaultDomain, EnumItemClass preferred)
        {
            var loc = AssetLocation.Create(code, defaultDomain);
            CollectibleObject item = sapi.World.GetItem(loc);
            CollectibleObject block = sapi.World.GetBlock(loc);
            if (block != null && block.Id == 0) block = null;
            return preferred == EnumItemClass.Block ? (block ?? item) : (item ?? block);
        }

        bool AddProcessing(CollectibleObject input, double inputQty, CollectibleObject output, double outputQty, string label, double extraCost = 0)
        {
            if (outputQty <= 0 || inputQty <= 0) return false;
            var conv = new Conversion
            {
                Output = output,
                OutputQuantity = outputQty,
                ExtraCost = extraCost,
                Kind = ConversionKind.Processing,
                Source = label
            };
            conv.Inputs.Add(new ConversionInput { Choices = new[] { input }, Quantity = inputQty });
            return AddIfSane(conv);
        }

        #endregion

        bool AddIfSane(Conversion conv)
        {
            if (conv.Inputs.Count == 0 || conv.OutputQuantity <= 0) return false;
            // A recipe whose output is also an input (repairs, re-dyeing, "add water to X") says nothing about value.
            foreach (ConversionInput input in conv.Inputs)
            {
                if (Array.IndexOf(input.Choices, conv.Output) >= 0)
                {
                    if (input.Choices.Length == 1) return false;
                    input.Choices = input.Choices.Where(c => c != conv.Output).ToArray();
                }
            }
            Conversions.Add(conv);
            return true;
        }
    }
}

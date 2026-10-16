using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace VintageE
{
    /// <summary>
    /// The ProjectETextures setting: swaps Vintage-E's textures for ProjectE's originals (assets/vintagee/textures/projecte,
    /// copied by tools/import_projecte_assets.py), following config/vintagee-projecte-look.json.
    /// <para>The swap is done on the server while assets are finalised. Clients get block, item and entity definitions
    /// (textures, shapes, transforms) from the server, so players see ProjectE's look without setting anything, and the
    /// client can tell it's on because the item textures it received live under <see cref="PathPrefix"/>.</para>
    /// <para>On the client, rings, stones and the Gem of Eternal Density switch to ProjectE's "on" texture while active,
    /// and the Ring of Arcana and Swiftwolf's Rending Gale show their mode, like ProjectE's item models.</para>
    /// </summary>
    public static class ProjectELook
    {
        public const string MapFile = "config/vintagee-projecte-look.json";

        /// <summary>ProjectE's textures live under textures/projecte/ in this mod.</summary>
        public const string PathPrefix = "projecte/";

        sealed class Override
        {
            public AssetLocation Code;
            public Dictionary<string, AssetLocation> Textures = new Dictionary<string, AssetLocation>();
            public AssetLocation Shape;
            public AssetLocation Sprite;
        }

        /// <summary>What <see cref="Apply"/> changed, for the log and the self-test.</summary>
        public class Result
        {
            public int Collectibles;
            public int Entities;
            public int Textures;
            public List<string> MissingFiles = new List<string>();
        }

        /// <summary>Swaps textures on every Vintage-E block, item and entity type. Server side, from AssetsFinalize.</summary>
        public static Result Apply(ICoreAPI api, ILogger logger)
        {
            var result = new Result();
            IAsset asset = api.Assets.TryGet(new AssetLocation(VintageEModSystem.ModId, MapFile));
            if (asset == null)
            {
                logger.Warning("ProjectETextures is on but {0} is missing; keeping Vintage-E's textures.", MapFile);
                return result;
            }

            JObject root;
            try { root = JObject.Parse(asset.ToText()); }
            catch (Exception e)
            {
                logger.Error("Could not read {0}: {1}; keeping Vintage-E's textures.", MapFile, e.Message);
                return result;
            }

            var files = new Dictionary<string, AssetLocation>(StringComparer.Ordinal);
            if (root["textures"] is JObject textures)
            {
                foreach (JProperty p in textures.Properties())
                {
                    if (p.Name.StartsWith("//") || p.Value.Type != JTokenType.String) continue;
                    files[new AssetLocation(p.Name).ToString()] = new AssetLocation((string)p.Value);
                }
            }
            var overrides = new List<Override>();
            if (root["overrides"] is JArray arr)
            {
                foreach (JToken t in arr)
                {
                    string code = (string)t["code"];
                    if (string.IsNullOrEmpty(code)) continue;
                    var o = new Override { Code = new AssetLocation(code) };
                    if (t["textures"] is JObject ot)
                    {
                        foreach (JProperty p in ot.Properties()) o.Textures[p.Name] = new AssetLocation((string)p.Value);
                    }
                    if ((string)t["shape"] is string shape) o.Shape = new AssetLocation(shape);
                    if ((string)t["sprite"] is string sprite) o.Sprite = new AssetLocation(sprite);
                    overrides.Add(o);
                }
            }

            // Every ProjectE texture named must be there, or the block would show the game's "unknown" texture.
            var missing = new HashSet<string>();
            bool Exists(AssetLocation tex)
            {
                if (api.Assets.Exists(tex.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"))) return true;
                missing.Add(tex.ToString());
                return false;
            }

            foreach (CollectibleObject c in api.World.Collectibles)
            {
                if (c?.Code == null || c.Code.Domain != VintageEModSystem.ModId) continue;
                Override[] mine = overrides.Where(o => WildcardUtil.Match(o.Code, c.Code)).ToArray();
                int before = result.Textures;

                if (c is Item item)
                {
                    Override sprite = mine.LastOrDefault(o => o.Sprite != null);
                    if (sprite != null && Exists(sprite.Sprite))
                    {
                        // Drawn as a flat item from one texture, the way ProjectE draws it, with the game's usual item placement.
                        item.Shape = null;
                        item.Textures = new Dictionary<string, CompositeTexture> { ["all"] = new CompositeTexture(sprite.Sprite) };
                        item.GuiTransform = ModelTransform.ItemDefaultGui();
                        item.FpHandTransform = ModelTransform.ItemDefaultFp();
                        item.TpHandTransform = ModelTransform.ItemDefaultTp();
                        item.TpOffHandTransform = ModelTransform.ItemDefaultTp();
                        item.GroundTransform = ModelTransform.ItemDefaultGround();
                        result.Textures++;
                    }
                    else if (item.Textures != null)
                    {
                        var copy = new Dictionary<string, CompositeTexture>(item.Textures);
                        Swap(copy, mine, files, Exists, result);
                        item.Textures = copy;
                    }
                    ApplyShape(item, mine, api);
                }
                else if (c is Block block)
                {
                    if (block.Textures != null)
                    {
                        var copy = new Dictionary<string, CompositeTexture>(block.Textures);
                        Swap(copy, mine, files, Exists, result);
                        block.Textures = copy;
                    }
                    if (block.TexturesInventory != null)
                    {
                        var copy = new Dictionary<string, CompositeTexture>(block.TexturesInventory);
                        Swap(copy, mine, files, Exists, result);
                        block.TexturesInventory = copy;
                    }
                }
                if (result.Textures > before) result.Collectibles++;
            }

            foreach (EntityProperties type in api.World.EntityTypes)
            {
                if (type?.Code == null || type.Code.Domain != VintageEModSystem.ModId || type.Client == null) continue;
                Override[] mine = overrides.Where(o => WildcardUtil.Match(o.Code, type.Code)).ToArray();
                int before = result.Textures;
                if (type.Client.Textures != null)
                {
                    var copy = new Dictionary<string, CompositeTexture>(type.Client.Textures);
                    Swap(copy, mine, files, Exists, result);
                    type.Client.Textures = copy;
                }
                AssetLocation shape = mine.LastOrDefault(o => o.Shape != null)?.Shape;
                if (shape != null && result.Textures > before && type.Client.Shape != null)
                {
                    // Only once its texture is ProjectE's: these shapes show the whole 16×16 sprite.
                    CompositeShape cs = type.Client.Shape.Clone();
                    cs.Base = shape;
                    type.Client.Shape = cs;
                }
                if (result.Textures > before) result.Entities++;
            }

            result.MissingFiles = missing.OrderBy(s => s, StringComparer.Ordinal).ToList();
            foreach (string m in result.MissingFiles) logger.Warning("ProjectE texture {0} is missing; kept Vintage-E's texture there.", m);
            logger.Notification("ProjectE textures on: swapped {0} textures on {1} blocks/items and {2} entity types.", result.Textures, result.Collectibles, result.Entities);
            return result;
        }

        static void Swap(IDictionary<string, CompositeTexture> dict, Override[] mine, Dictionary<string, AssetLocation> files, System.Func<AssetLocation, bool> exists, Result result)
        {
            foreach (string key in dict.Keys.ToList())
            {
                CompositeTexture ct = dict[key];
                if (ct?.Base == null) continue;
                AssetLocation want = null;
                // Single faces / texture codes first (a collector's front), then the file-for-file swaps.
                foreach (Override o in mine) if (o.Textures.TryGetValue(key, out AssetLocation f)) want = f;
                if (want == null) files.TryGetValue(ct.Base.ToString(), out want);
                if (want == null || !exists(want)) continue;
                CompositeTexture swapped = ct.Clone();
                swapped.Base = want;
                dict[key] = swapped;
                result.Textures++;
            }
        }

        static void ApplyShape(CollectibleObject c, Override[] mine, ICoreAPI api)
        {
            AssetLocation shape = mine.LastOrDefault(o => o.Shape != null)?.Shape;
            if (shape == null || !(c is Item item) || item.Shape == null) return;
            CompositeShape cs = item.Shape.Clone();
            cs.Base = shape;
            item.Shape = cs;
        }

        /// <summary>True when this item's textures came from ProjectE (the server has ProjectETextures on).</summary>
        public static bool IsProjectE(Item item)
        {
            return item?.Textures != null && item.Textures.TryGetValue("all", out CompositeTexture ct) && ct?.Base?.Path?.StartsWith(PathPrefix) == true;
        }

        /// <summary>
        /// ProjectE's texture for this ring/stone in its current state, or null to keep the item's own: the "on" texture
        /// while it's switched on, the Ring of Arcana's current mode, Swiftwolf's flight/shield mode (like ProjectE's models).
        /// </summary>
        public static AssetLocation TrinketLook(ItemEmcTrinket trinket, ItemStack stack)
        {
            if (!IsProjectE(trinket)) return null;
            AssetLocation baseTex = trinket.Textures["all"].Base;
            string path = baseTex.Path;
            bool on = ItemEmcTrinket.IsActive(stack);
            switch (trinket.Kind)
            {
                case "ringofarcana":
                    path = PathPrefix + "item/rings/arcana_" + GameMath.Clamp(ItemEmcTrinket.GetMode(stack), 0, 3) + (on ? "_on" : "");
                    break;
                case "swiftwolfsrendinggale":
                    // ProjectE: flight = on1, flight + shield = on2, shield = on3. Vintage-E's modes: both, flight, shield.
                    if (on) path = PathPrefix + "item/rings/swrg_on" + new[] { 2, 1, 3 }[GameMath.Clamp(ItemEmcTrinket.GetMode(stack), 0, 2)];
                    break;
                default:
                    if (on && path.EndsWith("_off")) path = path.Substring(0, path.Length - 4) + "_on";
                    break;
            }
            return path == baseTex.Path ? null : new AssetLocation(baseTex.Domain, path);
        }

        static readonly Dictionary<string, MultiTextureMeshRef> spriteMeshes = new Dictionary<string, MultiTextureMeshRef>();

        /// <summary>Draws the item as a flat sprite of <paramref name="texture"/> instead of its usual mesh (client side).</summary>
        public static void UseSprite(ICoreClientAPI capi, AssetLocation texture, ref ItemRenderInfo renderinfo)
        {
            if (texture == null) return;
            string key = texture.ToString();
            if (!spriteMeshes.TryGetValue(key, out MultiTextureMeshRef mesh))
            {
                mesh = null;
                try
                {
                    if (capi.Assets.Exists(texture.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png")))
                    {
                        var ct = new CompositeTexture(texture);
                        ct.Bake(capi.Assets);
                        capi.ItemTextureAtlas.GetOrInsertTexture(ct, out _, out TextureAtlasPosition pos);
                        if (pos != null) mesh = capi.Render.UploadMultiTextureMesh(capi.Tesselator.VoxelizeTexture(ct, capi.ItemTextureAtlas.Size, pos));
                    }
                }
                catch (Exception e)
                {
                    capi.Logger.Warning("Vintage-E: could not build the ProjectE look {0}: {1}", key, e.Message);
                }
                spriteMeshes[key] = mesh;
            }
            if (mesh != null) renderinfo.ModelRef = mesh;
        }

        public static void DisposeMeshes()
        {
            foreach (MultiTextureMeshRef m in spriteMeshes.Values) m?.Dispose();
            spriteMeshes.Clear();
        }
    }
}

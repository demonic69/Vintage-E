using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
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

                // Projectiles fired straight down at the ground a little way off.
                var lens = Fire("lens", new Vec3d(x + 0.5, y + 12, z + 12.5), p => p.Radius = 4);
                var water = Fire("water", new Vec3d(x + 8.5, y + 8, z + 0.5), null);
                var fire = Fire("fire", new Vec3d(x + 12.5, y + 8, z + 0.5), null);
                BlockPos lensTarget = new BlockPos(x, y - 1, z + 12);
                int solidBefore = CountSolid(lensTarget, 2);

                sapi.Event.RegisterCallback(_ =>
                {
                    try
                    {
                        Check(lens == null || !lens.Alive, "lens charge hit the ground and is gone");
                        int solidAfter = CountSolid(lensTarget, 2);
                        Check(solidAfter < solidBefore, $"lens explosion broke blocks ({solidBefore} -> {solidAfter} solid around the impact)");
                        Check(water == null || !water.Alive, "water shot landed");
                        Block placed = sapi.World.BlockAccessor.GetBlock(new BlockPos(x + 8, ba.GetTerrainMapheightAt(new BlockPos(x + 8, 0, z)) + 1, z), BlockLayersAccess.Fluid);
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
            foreach (var (code, expectMin, expectMax) in new[] { ("game:fat", 1.0, 64.0), ("game:fat-rendered", 1.0, 64.0), ("game:gem-emerald-rough", 2048.0, 2048.0), ("game:gem-diamond-rough", 8192.0, 8192.0) })
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

        BlockEntityFirepit PlaceFurnace(BlockPos pos, string type)
        {
            Block block = sapi.World.GetBlock(new AssetLocation("vintagee", "furnace-" + type + "-cold"));
            Check(block != null && block.Id != 0, $"furnace-{type}-cold exists");
            if (block == null || block.Id == 0) return null;
            sapi.World.BlockAccessor.SetBlock(block.Id, pos);
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
                string dir = sapi.GetOrCreateDataPath(VintageEModSystem.ModId);
                File.WriteAllText(Path.Combine(dir, "selftest.txt"), log.ToString());
            }
            catch (Exception e) { sapi.Logger.Error("Vintage-E self-test: could not write the report: {0}", e); }
        }
    }
}

using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Server half of the transmutation table/tablet: validates access and performs burn/create requests.
    /// Every request is checked here; the client GUI is just a view.
    /// </summary>
    public class TransmutationService
    {
        readonly VintageEModSystem mod;
        readonly ICoreServerAPI sapi;
        readonly Dictionary<string, BlockPos> tableSessions = new Dictionary<string, BlockPos>();

        public TransmutationService(VintageEModSystem mod, ICoreServerAPI sapi)
        {
            this.mod = mod;
            this.sapi = sapi;
            sapi.Event.PlayerLeave += p => tableSessions.Remove(p.PlayerUID);
        }

        public void OpenTableSession(IServerPlayer player, BlockPos pos)
        {
            tableSessions[player.PlayerUID] = pos.Copy();
        }

        public void OnRequest(IServerPlayer player, TransmuteRequestPacket req)
        {
            if (req == null || player?.Entity == null) return;
            if (req.Action == TransmuteAction.Close)
            {
                tableSessions.Remove(player.PlayerUID);
                return;
            }
            if (!HasAccess(player))
            {
                Fail(player, "noaccess");
                return;
            }

            switch (req.Action)
            {
                case TransmuteAction.Burn: Burn(player, req.Mode == 1); break;
                case TransmuteAction.Create: Create(player, req.Key, req.Mode); break;
                case TransmuteAction.Forget: PlayerEmc.Forget(player, req.Key); break;
            }
        }

        bool HasAccess(IServerPlayer player)
        {
            if (tableSessions.TryGetValue(player.PlayerUID, out BlockPos pos))
            {
                Block block = sapi.World.BlockAccessor.GetBlock(pos);
                double range = mod.Config.TableUseRange;
                if (block is BlockTransmutationTable && player.Entity.Pos.SquareDistanceTo(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5) <= range * range)
                {
                    return true;
                }
                tableSessions.Remove(player.PlayerUID);
            }
            return CarriesTablet(player);
        }

        static bool CarriesTablet(IServerPlayer player)
        {
            foreach (string invClass in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName, GlobalConstants.characterInvClassName })
            {
                IInventory inv = player.InventoryManager.GetOwnInventory(invClass);
                if (inv == null) continue;
                foreach (ItemSlot slot in inv)
                {
                    if (slot?.Itemstack?.Collectible is ItemTransmutationTablet) return true;
                }
            }
            return false;
        }

        void Burn(IServerPlayer player, bool onlyOne)
        {
            ItemSlot mouse = player.InventoryManager.MouseItemSlot;
            if (mouse?.Itemstack == null) return;
            ItemStack stack = mouse.Itemstack;
            StackDataTable data = mod.Emc.StackData;
            StackDataMode mode = data.ModeOf(stack.Collectible);
            bool keepsData = data.CarriesData(stack.Collectible) && mode != StackDataMode.Item;

            // Item only: what's inside would be lost, so containers that hold something are refused.
            if (!keepsData && mod.Config.ProtectContainersWithContents && StackContents.HasNestedStacks(stack))
            {
                Fail(player, "hascontents", stack.GetName());
                return;
            }

            double each = mod.Emc.GetValue(stack);
            if (each <= 0)
            {
                Fail(player, "novalue", stack.GetName());
                return;
            }

            // Items that keep their contents/material are learned as exactly this version.
            TreeAttribute kept = keepsData ? StackDataUtil.KeptData(stack) : null;
            string key = kept != null ? PlayerEmc.ExactKey(stack.Collectible, kept) : PlayerEmc.KeyOf(stack.Collectible);
            if (kept != null && !PlayerEmc.Knows(player, key) && PlayerEmc.CountExact(player) >= mod.Config.MaxExactKnowledge)
            {
                Fail(player, "toomanyexact", mod.Config.MaxExactKnowledge.ToString());
                return;
            }

            int count = onlyOne ? 1 : stack.StackSize;
            ItemStack taken = mouse.TakeOut(count);
            if (taken == null || taken.StackSize <= 0) return;
            mouse.MarkDirty();

            PlayerEmc.AddEmc(player, each * taken.StackSize);
            bool learned = PlayerEmc.Learn(player, key);
            // A container also teaches the plain (empty) container.
            if (kept != null && data.KindOf(stack.Collectible) == StackDataKind.Contents && mod.Emc.GetValue(stack.Collectible) > 0)
            {
                learned |= PlayerEmc.Learn(player, PlayerEmc.KeyOf(stack.Collectible));
            }

            sapi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/timeswitch"), player.Entity, null, true, 8, 0.5f);
            mod.SendResult(player, new TransmuteResultPacket { LearnedKey = learned ? key : null });
        }

        /// <summary>
        /// What a knowledge entry makes: the plain item, or exactly the version that was learned. If an item has since
        /// been switched to "item only", its exact versions make the plain item.
        /// </summary>
        public static ItemStack Prototype(VintageEModSystem mod, IWorldAccessor world, string key)
        {
            ItemStack stack = PlayerEmc.ResolveStack(world, key);
            if (stack == null) return null;
            if (PlayerEmc.IsExact(key) && mod.Emc.StackData.ModeOf(stack.Collectible) == StackDataMode.Item) return new ItemStack(stack.Collectible, 1);
            return stack;
        }

        /// <param name="mode">0 = one onto cursor, 1 = stack onto cursor, 2 = stack into inventory</param>
        void Create(IServerPlayer player, string key, int mode)
        {
            if (string.IsNullOrEmpty(key) || !PlayerEmc.Knows(player, key))
            {
                Fail(player, "unknown");
                return;
            }
            ItemStack proto = Prototype(mod, sapi.World, key);
            double each = proto == null ? 0 : mod.Emc.GetValue(proto);
            if (proto == null || each <= 0)
            {
                Fail(player, "novalue", proto?.GetName() ?? key);
                return;
            }
            CollectibleObject collectible = proto.Collectible;

            double emc = PlayerEmc.GetEmc(player);
            int maxStack = Math.Max(1, collectible.MaxStackSize);
            ItemSlot mouse = player.InventoryManager.MouseItemSlot;

            int want;
            if (mode == 2)
            {
                want = maxStack;
            }
            else
            {
                if (!mouse.Empty && !mouse.Itemstack.Equals(sapi.World, proto, GlobalConstants.IgnoredStackAttributes))
                {
                    Fail(player, "cursorbusy");
                    return;
                }
                int room = mouse.Empty ? maxStack : maxStack - mouse.StackSize;
                want = mode == 0 ? Math.Min(1, room) : room;
                if (want <= 0) return;
            }

            int affordable = (int)Math.Min(int.MaxValue, Math.Floor(emc / each + 1e-9));
            int amount = Math.Min(want, affordable);
            if (amount <= 0)
            {
                Fail(player, "notenoughemc", EmcFormat.Format(each), EmcFormat.Format(emc));
                return;
            }

            ItemStack made = proto.Clone();
            made.StackSize = amount;
            if (mode == 2)
            {
                // Same pattern as vanilla: whatever could not be given is dropped at the player's feet.
                if (!player.InventoryManager.TryGiveItemstack(made, true))
                {
                    sapi.World.SpawnItemEntity(made, player.Entity.Pos.XYZ);
                }
            }
            else
            {
                if (mouse.Empty) mouse.Itemstack = made;
                else mouse.Itemstack.StackSize += amount;
                mouse.MarkDirty();
            }

            PlayerEmc.SetEmc(player, emc - each * amount);
            mod.SendResult(player, new TransmuteResultPacket());
        }

        void Fail(IServerPlayer player, string code, params string[] args)
        {
            mod.SendResult(player, new TransmuteResultPacket { ErrorCode = code, Args = args });
        }
    }
}

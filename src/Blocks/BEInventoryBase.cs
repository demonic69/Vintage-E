using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>
    /// Shared plumbing for this mod's container blocks (alchemical chest, matter furnaces): the inventory, opening
    /// it with the game's standard block-inventory window, claims checks, saving, id remapping and dropping the
    /// contents when broken. Works with hoppers and chutes through <see cref="IBlockEntityContainer"/>.
    /// </summary>
    public abstract class BEInventoryBase : BlockEntity, IBlockEntityContainer, IEmcInteractable
    {
        protected InventoryBase inventory;
        GuiDialogBlockEntityInventory dialog;

        public IInventory Inventory => inventory;
        public abstract string InventoryClassName { get; }

        /// <summary>Slot columns in the window.</summary>
        protected virtual int Columns => 4;

        protected abstract InventoryBase CreateInventory();

        public override void Initialize(ICoreAPI api)
        {
            inventory ??= CreateInventory();
            base.Initialize(api);
            inventory.LateInitialize(InventoryClassName + "-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
            inventory.Pos = Pos;
            inventory.ResolveBlocksOrItems();
            inventory.SlotModified += OnSlotModified;
        }

        protected virtual void OnSlotModified(int slotId)
        {
            if (Api?.Side == EnumAppSide.Server)
            {
                MarkDirty(false);
                Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
            }
        }

        public virtual void CheckInventoryClearedMidTick() { }

        public virtual void DropContents(Vintagestory.API.MathTools.Vec3d atPos)
        {
            inventory?.DropAll(atPos);
        }

        public virtual bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.Side == EnumAppSide.Client) ToggleDialog(byPlayer);
            return true;
        }

        void ToggleDialog(IPlayer byPlayer)
        {
            var capi = (ICoreClientAPI)Api;
            if (dialog == null)
            {
                string title = Block.GetPlacedBlockName(Api.World, Pos);
                dialog = new GuiDialogBlockEntityInventory(title, inventory, Pos, Columns, capi);
                dialog.OnClosed += () =>
                {
                    dialog?.Dispose();
                    dialog = null;
                    capi.Network.SendBlockEntityPacket(Pos, (int)EnumBlockEntityPacketId.Close);
                    capi.World.Player.InventoryManager.CloseInventory(inventory);
                };
                dialog.TryOpen();
                capi.Network.SendPacketClient(inventory.Open(byPlayer));
                capi.Network.SendBlockEntityPacket(Pos, (int)EnumBlockEntityPacketId.Open);
            }
            else
            {
                dialog.TryClose();
            }
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == (int)EnumBlockEntityPacketId.Close)
            {
                player.InventoryManager?.CloseInventory(inventory);
                return;
            }
            bool allowed = Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use);
            if (packetid == (int)EnumBlockEntityPacketId.Open)
            {
                if (allowed) player.InventoryManager?.OpenInventory(inventory);
                return;
            }
            if (packetid < 1000)
            {
                if (!allowed)
                {
                    inventory.InvNetworkUtil.SendInventoryRollback((IServerPlayer)player, packetid, data);
                    return;
                }
                inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
            }
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid == (int)EnumBlockEntityPacketId.Close)
            {
                (Api as ICoreClientAPI)?.World.Player.InventoryManager.CloseInventory(inventory);
                dialog?.TryClose();
            }
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            if (Api.Side == EnumAppSide.Server && inventory != null && !inventory.Empty)
            {
                inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
            base.OnBlockBroken(byPlayer);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            dialog?.TryClose();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            dialog?.TryClose();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (inventory == null) return;
            var invTree = new TreeAttribute();
            inventory.ToTreeAttributes(invTree);
            tree["inventory"] = invTree;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            inventory ??= CreateInventory();
            ITreeAttribute invTree = tree.GetTreeAttribute("inventory");
            if (invTree != null)
            {
                inventory.FromTreeAttributes(invTree);
                if (Api != null) inventory.AfterBlocksLoaded(Api.World);
            }
        }

        public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
        {
            if (inventory == null) return;
            foreach (ItemSlot slot in inventory)
            {
                slot.Itemstack?.Collectible.OnStoreCollectibleMappings(Api.World, slot, blockIdMapping, itemIdMapping);
            }
        }

        public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
        {
            if (inventory == null) return;
            foreach (ItemSlot slot in inventory)
            {
                if (slot.Itemstack == null) continue;
                if (!slot.Itemstack.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve)) slot.Itemstack = null;
            }
        }
    }

    /// <summary>Alchemical Chest: 104 slots (8 rows of 13) for all your transmutation materials.</summary>
    public class BEAlchemicalChest : BEInventoryBase
    {
        public override string InventoryClassName => "vintageechest";
        protected override int Columns => 13;

        protected override InventoryBase CreateInventory()
        {
            int slots = Block?.Attributes?["quantitySlots"].AsInt(104) ?? 104;
            return new InventoryGeneric(slots, null, null, null);
        }
    }
}

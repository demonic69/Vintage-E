using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VintageE
{
    /// <summary>Right-click to open the transmutation GUI. The server remembers which table you opened so it can check you stay near it.</summary>
    public class BlockTransmutationTable : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var mod = api.ModLoader.GetModSystem<VintageEModSystem>();
            if (world.Side == EnumAppSide.Server)
            {
                if (byPlayer is IServerPlayer sp) mod.Transmutation?.OpenTableSession(sp, blockSel.Position);
            }
            else
            {
                mod.OpenTransmutationDialog(blockSel.Position);
            }
            return true;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return new[]
            {
                new WorldInteraction { ActionLangCode = "vintagee:blockhelp-opentable", MouseButton = EnumMouseButton.Right }
            };
        }
    }
}

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VintageE
{
    /// <summary>Anything that can take EMC pushed into it (condensers accept power from collectors).</summary>
    public interface IEmcAcceptor
    {
        /// <summary>Offers <paramref name="amount"/> EMC; returns how much was taken.</summary>
        double AcceptEmc(double amount);
    }

    public interface IEmcInteractable
    {
        bool OnInteract(IPlayer byPlayer, BlockSelection blockSel);
    }

    /// <summary>Block class for collectors and condensers: forwards right-clicks to the block entity.</summary>
    public class BlockEmcMachine : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is IEmcInteractable machine)
            {
                return machine.OnInteract(byPlayer, blockSel);
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            string code = Attributes?["interactionHelp"].AsString(null);
            if (code == null) return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
            var help = new System.Collections.Generic.List<WorldInteraction>
            {
                new WorldInteraction { ActionLangCode = code, MouseButton = EnumMouseButton.Right }
            };
            string sneakCode = Attributes?["sneakInteractionHelp"].AsString(null);
            if (sneakCode != null)
            {
                help.Add(new WorldInteraction { ActionLangCode = sneakCode, HotKeyCode = "sneak", MouseButton = EnumMouseButton.Right });
            }
            return help.ToArray();
        }
    }
}

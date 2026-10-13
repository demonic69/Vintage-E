using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace VintageE
{
    /// <summary>
    /// Added to every player on the server: Swiftwolf's Rending Gale (or the Ring of Arcana in gale mode) and a full
    /// set of gem armour stop fall damage; an active Ignition Ring (or arcana in ignition mode) and full gem armour
    /// stop fire damage.
    /// </summary>
    public class EntityBehaviorVintageE : EntityBehavior
    {
        public const string Code = "vintageeplayer";

        public EntityBehaviorVintageE(Entity entity) : base(entity) { }

        public override string PropertyName() => Code;

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            IPlayer player = (entity as EntityPlayer)?.Player;
            if (player == null || damage <= 0 || damageSource.Type == EnumDamageType.Heal) return;

            int gem = 0;
            IInventory gear = player.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (gear != null)
            {
                foreach (ItemSlot slot in gear)
                {
                    string path = slot?.Itemstack?.Collectible?.Code?.Path;
                    if (slot?.Itemstack?.Collectible?.Code?.Domain == VintageEModSystem.ModId && path != null && path.StartsWith("matterarmor-gem-")) gem++;
                }
            }

            bool fall = damageSource.Source == EnumDamageSource.Fall || damageSource.Type == EnumDamageType.Gravity;
            if (fall && (gem >= 3 || TrinketEffects.HasActiveEffect(player, "swiftwolfsrendinggale")))
            {
                damage = 0;
                return;
            }
            bool fire = damageSource.Type == EnumDamageType.Fire;
            if (fire && (gem >= 3 || TrinketEffects.HasActiveEffect(player, "ignitionring")))
            {
                damage = 0;
            }
        }
    }
}

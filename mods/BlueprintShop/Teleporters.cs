using Kitchen;
using KitchenMods;
using Unity.Collections;
using Unity.Entities;

namespace BlueprintShop
{
    /// <summary>
    /// The base game's Teleporter, one of the shop's desk upgrades (ShopAdditions). In the game's
    /// data it's the one appliance with CConveyTeleport among its properties.
    /// </summary>
    internal static class Teleporters
    {
        public const int ApplianceID = 459840623;
    }

    /// <summary>
    /// Host, at night: frees a teleporter whose partner no longer exists, so the game pairs it
    /// again. TeleportFindPartners only looks at teleporters without a Target, and nothing in
    /// the game clears the Target when the partner is destroyed (thrown away outside, sold, or
    /// handed back to the build queue), so the survivor would stay useless and the next one
    /// bought wouldn't link to it.
    ///
    /// Only Teleporters: the Christmas sheds also use CConveyTeleport, with fixed targets.
    /// </summary>
    [UpdateBefore(typeof(TeleportFindPartners))]
    public class TeleporterRepair : NightSystem, IModSystem
    {
        private EntityQuery Linked;

        protected override void Initialise()
        {
            base.Initialise();
            Linked = GetEntityQuery(typeof(CConveyTeleport), typeof(CAppliance));
        }

        protected override void OnUpdate()
        {
            if (Linked.IsEmpty)
            {
                return;
            }
            using NativeArray<Entity> entities = Linked.ToEntityArray(Allocator.Temp);
            foreach (Entity e in entities)
            {
                if (EntityManager.GetComponentData<CAppliance>(e).ID != Teleporters.ApplianceID)
                {
                    continue;
                }
                CConveyTeleport teleport = EntityManager.GetComponentData<CConveyTeleport>(e);
                if (teleport.Target == Entity.Null || EntityManager.Exists(teleport.Target))
                {
                    continue;
                }
                teleport.Target = Entity.Null;
                teleport.GroupID = 0;
                teleport.HasReceivedTeleport = false;
                EntityManager.SetComponentData(e, teleport);
                ShopLog.Debug(() => $"Teleporter {e}: its partner is gone, so it can pair again");
            }
        }
    }
}

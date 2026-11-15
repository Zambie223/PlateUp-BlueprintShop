using System.Collections.Generic;
using HarmonyLib;
using Kitchen;
using KitchenMods;
using Unity.Collections;
using Unity.Entities;

namespace BlueprintShop
{
    /// <summary>
    /// Records what was paid for appliances bought through vanilla blueprints: the
    /// "Blueprint" button, blueprint cabinets (day or night), free card letters, copies, and
    /// vanilla's rebuy. PurchaseAfterDuration turns the blueprint entity itself into the bought
    /// appliance, so: remember every for-sale blueprint's price, and when one of those entities
    /// stops being a blueprint but still exists as an appliance, it was bought at that price.
    ///
    /// Also enforces prepare_uninstall by stripping every CPaidPrice tag.
    /// </summary>
    public class PaidPriceTracker : RestaurantSystem, IModSystem
    {
        private EntityQuery ForSaleBlueprints;
        private EntityQuery Tagged;
        private readonly Dictionary<Entity, int> Tracked = new Dictionary<Entity, int>();
        private readonly List<Entity> Finished = new List<Entity>();
        private bool LoggedStrip;

        protected override void Initialise()
        {
            base.Initialise();
            ForSaleBlueprints = GetEntityQuery(new QueryHelper().All(typeof(CApplianceBlueprint), typeof(CForSale)));
            Tagged = GetEntityQuery(typeof(CPaidPrice));
        }

        protected override void OnUpdate()
        {
            if (ShopLog.PrepareUninstall)
            {
                Tracked.Clear();
                if (!Tagged.IsEmpty)
                {
                    int count = Tagged.CalculateEntityCount();
                    EntityManager.RemoveComponent<CPaidPrice>(Tagged);
                    ShopLog.Info($"prepare_uninstall: removed purchase records from {count} appliance(s). After the next night's autosave this run loads without the mod.");
                    LoggedStrip = true;
                }
                else if (!LoggedStrip)
                {
                    ShopLog.Info("prepare_uninstall is on: no purchase records in this run; refunds are $0.");
                    LoggedStrip = true;
                }
                return;
            }

            using (NativeArray<Entity> entities = ForSaleBlueprints.ToEntityArray(Allocator.Temp))
            using (NativeArray<CForSale> sales = ForSaleBlueprints.ToComponentDataArray<CForSale>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Tracked[entities[i]] = sales[i].Price;
                }
            }

            Finished.Clear();
            foreach (KeyValuePair<Entity, int> kv in Tracked)
            {
                Entity e = kv.Key;
                if (!EntityManager.Exists(e))
                {
                    // Destroyed: cleaned up at day start, stored in a cabinet, rerolled...
                    Finished.Add(e);
                    continue;
                }
                if (Has<CApplianceBlueprint>(e))
                {
                    continue; // still for sale (possibly being carried)
                }
                if (!Has<CAppliance>(e))
                {
                    continue; // bought this frame; CreateNewAppliances hasn't run yet
                }
                Finished.Add(e);
                if (kv.Value > 0 && !Has<CPaidPrice>(e))
                {
                    EntityManager.AddComponentData(e, new CPaidPrice { Price = kv.Value });
                    int price = kv.Value;
                    ShopLog.Debug(() => $"Blueprint purchase: appliance {e} bought for {price}");
                }
            }
            foreach (Entity e in Finished)
            {
                Tracked.Remove(e);
            }
        }
    }

    /// <summary>
    /// Opening a parcel creates a brand-new appliance entity and copies only CModifiers and
    /// CPlant across (OpenApplianceParcel.Perform). For parcels the shop tagged with a paid
    /// price, run the same steps plus the CPaidPrice transfer. Every other parcel goes through
    /// the untouched vanilla method.
    /// </summary>
    [HarmonyPatch(typeof(OpenApplianceParcel), "Perform")]
    internal static class OpenParcelPatch
    {
        private static bool Prefix(ref InteractionData data)
        {
            if (!data.Context.Require(data.Target, out CPaidPrice paid)
                || !data.Context.Require(data.Target, out CLetterAppliance letter)
                || !data.Context.Require(data.Target, out CPosition position))
            {
                return true;
            }
            Entity entity = data.Context.CreateEntity();
            data.Context.Set(entity, new CCreateAppliance { ID = letter.ApplianceID });
            data.Context.Set(entity, new CPosition(position));
            data.Context.TransferOrSkip<CModifiers>(data.Target, entity);
            data.Context.TransferOrSkip<CPlant>(data.Target, entity);
            data.Context.Set(entity, paid);
            data.Context.Destroy(data.Target);
            ShopLog.Debug(() => $"Opened a shop parcel (appliance {letter.ApplianceID}, paid {paid.Price})");
            return false;
        }
    }

    /// <summary>
    /// Press interact on the shop terminal to open the menu. Ordered before RotateAppliances:
    /// the first interaction system to perform claims the press, so the terminal opens instead
    /// of rotating.
    /// </summary>
    [UpdateBefore(typeof(RotateAppliances))]
    public class OpenShopInteraction : ApplianceInteractionSystem, IModSystem
    {
        private ShopService Service;

        protected override bool IsPossible(ref InteractionData data)
        {
            if (!Has<CShopTerminal>(data.Target) || !Has<CPlayer>(data.Interactor))
            {
                return false;
            }
            Service = Service ?? World.GetExistingSystem<ShopService>();
            return Service != null && Service.IsShopNight;
        }

        protected override void Perform(ref InteractionData data)
        {
            Service.RequestOpen(data.Interactor);
        }
    }
}

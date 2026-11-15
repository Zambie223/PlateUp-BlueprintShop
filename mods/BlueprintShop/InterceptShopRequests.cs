using Kitchen;
using KitchenMods;
using Unity.Collections;
using Unity.Entities;

namespace BlueprintShop
{
    /// <summary>
    /// Removes the vanilla random shop requests before HandleNewShop turns them into letters:
    ///   - CNewShop from CreateShopRequests (the nightly 5 letters or the decoration-night set),
    ///     Halloween's ExtraBlueprints, the "refresh on purchase" card, and rerolls;
    ///   - CNewDecorShop (the 6 random free wallpapers/floors on decoration nights).
    /// Everything they could have produced is in the shop instead.
    ///
    /// Left alone on purpose:
    ///   - CNewShop with IsFree (the "extra starting blueprints" card): a free bonus, not a
    ///     purchase, so it still arrives as vanilla's free random letter;
    ///   - CGrantsExtraBlueprint (specific blueprints granted by cards);
    ///   - day 0 while the player has no money (no shop terminal then), so no shop is ever lost.
    /// </summary>
    [UpdateInGroup(typeof(EndOfDayProgressionGroup))]
    [UpdateAfter(typeof(CreateShopRequests))]
    [UpdateAfter(typeof(ExtraBlueprints))]
    [UpdateBefore(typeof(HandleNewShop))]
    public class InterceptShopRequests : NightSystem, IModSystem
    {
        private EntityQuery NewShops;
        private EntityQuery NewDecorShops;

        protected override void Initialise()
        {
            base.Initialise();
            NewShops = GetEntityQuery(typeof(CNewShop));
            NewDecorShops = GetEntityQuery(typeof(CNewDecorShop));
        }

        protected override void OnUpdate()
        {
            if (NewShops.IsEmpty && NewDecorShops.IsEmpty)
            {
                return;
            }
            // Day 0 has a shop only once the player has money (ShopService spawns the terminal
            // then). Without money there's no shop, so leave any vanilla day-0 letters alone.
            if (GetOrDefault<SDay>().Day <= 0 && GetOrDefault<SMoney>().Amount <= 0)
            {
                return;
            }

            int removed = 0;
            int kept = 0;
            using (NativeArray<Entity> entities = NewShops.ToEntityArray(Allocator.Temp))
            using (NativeArray<CNewShop> shops = NewShops.ToComponentDataArray<CNewShop>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (shops[i].IsFree)
                    {
                        kept++;
                        continue;
                    }
                    EntityManager.DestroyEntity(entities[i]);
                    removed++;
                }
            }
            int decor = NewDecorShops.CalculateEntityCount();
            EntityManager.DestroyEntity(NewDecorShops);

            ShopLog.Debug(() => $"Day {GetOrDefault<SDay>().Day}: removed {removed} random letter request(s) and {decor} decor request(s); kept {kept} free one(s)");
        }
    }
}

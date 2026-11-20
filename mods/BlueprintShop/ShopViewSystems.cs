using System.Collections.Generic;
using Kitchen;
using KitchenMods;
using Unity.Collections;
using Unity.Entities;

namespace BlueprintShop
{
    /// <summary>
    /// Host: pushes catalog/money/status to every open shop menu and applies the menus'
    /// responses (purchase, take blueprint, close). Responses from clients arrive through the
    /// game's PerformCommandRouter, which finds this system by its response type.
    /// </summary>
    public class ShopViewSystem : ResponsiveViewSystemBase<ShopViewData, ShopResponseData>, IModSystem
    {
        private EntityQuery Sessions;
        private ShopService Service;
        private BuildQueueSystem Queue;

        protected override void Initialise()
        {
            base.Initialise();
            Sessions =GetEntityQuery(new QueryHelper().All(typeof(CShopSession), typeof(CLinkedView)));
        }

        protected override void OnUpdate()
        {
            if (Sessions.IsEmpty)
            {
                ClearUpdates();
                return;
            }
            if (Service == null)
            {
                Service = World.GetExistingSystem<ShopService>();
                if (Service == null)
                {
                    ShopLog.Error("ShopService is missing from the world; the shop can't work.");
                    ClearUpdates();
                    return;
                }
            }

            ShopService.CatalogContext ctx = Service.GetContext();
            List<ShopEntry> catalog = Service.BuildCatalog(ctx);
            int hash = ShopService.HashCatalog(catalog);
            Service.LogCatalogIfChanged(ctx, catalog, hash);
            int money = GetOrDefault<SMoney>().Amount;
            ShopStateFlags flags = ShopSettings.PrepareUninstall ? ShopStateFlags.UninstallPrep : ShopStateFlags.None;

            using NativeArray<Entity> entities = Sessions.ToEntityArray(Allocator.Temp);
            using NativeArray<CShopSession> sessions = Sessions.ToComponentDataArray<CShopSession>(Allocator.Temp);
            using NativeArray<CLinkedView> views = Sessions.ToComponentDataArray<CLinkedView>(Allocator.Temp);

            // Gather responses first: handling them can destroy sessions (Close).
            var pending = new List<(Entity, ShopResponseData)>();
            for (int i = 0; i < entities.Length; i++)
            {
                Entity session = entities[i];
                ApplyUpdates(views[i].Identifier, response => pending.Add((session, response)));
            }
            ClearUpdates();
            // ApplyUpdates doesn't promise arrival order; apply in the order the player sent them.
            pending.Sort((a, b) => a.Item2.Sequence.CompareTo(b.Item2.Sequence));
            foreach ((Entity session, ShopResponseData response) in pending)
            {
                Service.HandleResponse(session, response);
            }
            if (pending.Count > 0)
            {
                money = GetOrDefault<SMoney>().Amount;
            }
            // After orders/sales, so the counts include what was just bought or sold.
            List<OwnedCount> owned = Service.CountOwned(catalog, out int ownedHash);
            Queue = Queue ?? World.GetExistingSystem<BuildQueueSystem>();
            ShopService.OutsideSale outside = Service.ComputeOutsideSale();

            for (int i = 0; i < entities.Length; i++)
            {
                if (!Service.IsOpenSession(entities[i]))
                {
                    continue;
                }
                ShopService.SessionStatus status = Service.GetStatus(entities[i]);
                ShopService.HeldInfo held = Service.GetHeld(sessions[i].Player);
                (int areaSequence, List<OwnedCount> areaItems) = Service.GetAreaResult(entities[i]);
                SendUpdate(views[i], new ShopViewData
                {
                    OwnerPlayerID = sessions[i].PlayerID,
                    OwnerInputSource = sessions[i].InputSource,
                    Money = money,
                    Day = ctx.Day,
                    IsDecorNight = ctx.IsDecorNight,
                    CatalogHash = hash,
                    Entries = catalog,
                    AckSequence = status.AckSequence,
                    LastSucceeded = status.LastSucceeded,
                    Status = status.Message ?? "",
                    StateFlags = flags,
                    HeldKind = held.Kind,
                    HeldID = held.ApplianceID,
                    HeldRefund = held.Refund,
                    OwnedHash = ownedHash,
                    Owned = owned,
                    HeldRequired = held.Required,
                    OutsideHash = outside.Hash,
                    Outside = outside.Lines,
                    QueueCount = Queue?.CountWaiting(sessions[i].Player) ?? 0,
                    HeldPaid = held.Paid,
                    SaleLossPercent = ShopSettings.SaleLossPercent,
                    UpgradeState = ctx.UpgradeState,
                    UpgradeNight = ctx.UpgradeNight,
                    UpgradeChanceTonight = UnityEngine.Mathf.RoundToInt(ctx.UpgradeChanceTonight * 1000f),
                    UpgradeChanceNext = UnityEngine.Mathf.RoundToInt(ctx.UpgradeChanceNext * 1000f),
                    AreaSequence = areaSequence,
                    AreaItems = areaItems
                });
            }
        }
    }

    /// <summary>
    /// Host: tells each shop terminal's reroll view to show "Shop" (see TerminalLabelPatch).
    /// The vanilla reroll view system no longer updates it once CRerollShopAfterDuration is removed.
    /// </summary>
    public class TerminalLabelViewSystem : IncrementalViewSystemBase<RerollBlueprintView.ViewData>, IModSystem
    {
        private EntityQuery Terminals;

        protected override void Initialise()
        {
            base.Initialise();
            Terminals = GetEntityQuery(new QueryHelper().All(typeof(CShopTerminal), typeof(CLinkedView)));
        }

        protected override void OnUpdate()
        {
            if (Terminals.IsEmpty)
            {
                return;
            }
            using NativeArray<CLinkedView> views = Terminals.ToComponentDataArray<CLinkedView>(Allocator.Temp);
            foreach (CLinkedView view in views)
            {
                SendUpdate(view, new RerollBlueprintView.ViewData
                {
                    Price = ShopIds.TerminalPriceSentinel,
                    PlayerMoney = 0
                });
            }
        }
    }
}

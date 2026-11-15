using System;
using System.Collections.Generic;
using System.Text;
using Kitchen;
using Kitchen.Layouts;
using Kitchen.ShopBuilder;
using KitchenData;
using KitchenMods;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace BlueprintShop
{
    /// <summary>
    /// Host-side shop logic:
    ///   - converts the vanilla reroll trigger into a click-to-open shop terminal each night,
    ///     and spawns one on day 0 once the player has money (vanilla has no trigger on day 0),
    ///   - opens sessions (menus) requested by OpenShopInteraction,
    ///   - closes sessions when their player is gone (ShopDayCleanup handles day start),
    ///   - builds the catalog with the same eligibility and price rules as HandleNewShop,
    ///   - validates and executes orders and sales sent from the menu (via ShopViewSystem).
    /// </summary>
    public class ShopService : NightSystem, IModSystem
    {
        private EntityQuery RerollTriggers;
        private EntityQuery Terminals;
        private EntityQuery HiddenTerminals;
        private EntityQuery Sessions;
        private EntityQuery ShopOptions;
        private EntityQuery Discounts;
        private EntityQuery UpgradeChances;
        private EntityQuery RandomShopPrices;
        private EntityQuery PendingParcels;
        private EntityQuery PendingBlueprints;
        private EntityQuery LetterBlueprints;
        private EntityQuery PlacedAppliances;

        /// <summary>Open requests from OpenShopInteraction, handled on our next update.</summary>
        private readonly List<Entity> OpenRequests = new List<Entity>();

        /// <summary>Tiles handed out recently. Newly spawned parcels may not register as tile
        /// occupants for a frame or two, so keep reservations briefly to avoid stacking.</summary>
        private readonly Dictionary<Vector3Int, int> ReservedTiles = new Dictionary<Vector3Int, int>();
        private const int ReservationFrames = 30;

        private readonly Dictionary<Entity, SessionStatus> Statuses = new Dictionary<Entity, SessionStatus>();

        private int LastLoggedCatalogHash;

        public struct SessionStatus
        {
            public int AckSequence;
            public bool LastSucceeded;
            public string Message;
        }

        public struct HeldInfo
        {
            public HeldKind Kind;
            public int ApplianceID;
            public int Refund;
            public Entity Entity;
        }

        public const int MaxQuantityPerLine = 99;

        protected override void Initialise()
        {
            base.Initialise();
            // Only convert vanilla's own reroll trigger (tagged with the private
            // CreateRerollTrigger.SRerollTrigger) and our day-0 terminal. Other mods can spawn
            // reroll-trigger appliances for their own purposes (as terminals of their own), and
            // converting those would hijack them.
            Type vanillaRerollTag = typeof(CreateRerollTrigger).GetNestedType("SRerollTrigger", System.Reflection.BindingFlags.NonPublic);
            if (vanillaRerollTag != null)
            {
                RerollTriggers = GetEntityQuery(new EntityQueryDesc
                {
                    All = new ComponentType[] { typeof(CRerollShopAfterDuration) },
                    Any = new ComponentType[] { vanillaRerollTag, typeof(CShopTerminal) },
                });
            }
            else
            {
                ShopLog.Warn("CreateRerollTrigger.SRerollTrigger not found (game changed?); converting every reroll trigger.");
                RerollTriggers = GetEntityQuery(new QueryHelper().All(typeof(CRerollShopAfterDuration)));
            }
            Terminals = GetEntityQuery(typeof(CShopTerminal));
            HiddenTerminals = GetEntityQuery(new QueryHelper().All(typeof(CShopTerminal), typeof(CHideView)));
            Sessions = GetEntityQuery(typeof(CShopSession));
            ShopOptions = GetEntityQuery(typeof(CShopBuilderOption));
            Discounts = GetEntityQuery(typeof(CGrantsShopDiscount));
            UpgradeChances = GetEntityQuery(typeof(CUpgradedShopChance));
            RandomShopPrices = GetEntityQuery(typeof(CRandomiseShopPrices));
            PendingParcels = GetEntityQuery(typeof(CLetterAppliance));
            PendingBlueprints = GetEntityQuery(typeof(CApplianceBlueprint));
            LetterBlueprints = GetEntityQuery(typeof(CLetterBlueprint));
            PlacedAppliances = GetEntityQuery(typeof(CAppliance));
        }

        /// <summary>
        /// The shop is open at night from day 1 (when vanilla has a reroll trigger), and on day 0
        /// once a terminal exists (spawned when the player first has money).
        /// </summary>
        public bool IsShopNight => Has<SIsNightTime>() && (GetOrDefault<SDay>().Day > 0 || !Terminals.IsEmpty);

        protected override void OnUpdate()
        {
            int day = GetOrDefault<SDay>().Day;
            if (day <= 0)
            {
                SpawnDayZeroTerminal();
            }
            ConvertRerollTriggers();
            if (!HiddenTerminals.IsEmpty)
            {
                // ShopDayCleanup hides terminals during the day; show them again at night.
                EntityManager.RemoveComponent<CHideView>(HiddenTerminals);
            }
            HandleOpenRequests();
            CloseStaleSessions();
            ExpireReservations();
        }

        // ----------------------------------------------------------------- terminal & sessions

        private void SpawnDayZeroTerminal()
        {
            if (!Terminals.IsEmpty || !RerollTriggers.IsEmpty || GetOrDefault<SMoney>().Amount <= 0)
            {
                return;
            }
            Entity e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, new CCreateAppliance { ID = AssetReference.ShopRerollTrigger });
            EntityManager.AddComponentData(e, new CPosition(GetRerollTile()));
            EntityManager.AddComponent<CDoNotPersist>(e);
            EntityManager.AddComponent<CShopTerminal>(e);
            EntityManager.AddComponent<CShopTerminalSpawned>(e);
            ShopLog.Debug(() => $"Day 0 with money: spawned shop terminal {e}");
        }

        private void ConvertRerollTriggers()
        {
            if (RerollTriggers.IsEmpty)
            {
                return;
            }
            using NativeArray<Entity> triggers = RerollTriggers.ToEntityArray(Allocator.Temp);
            foreach (Entity e in triggers)
            {
                // CDoNotPersist keeps the converted trigger out of saves. On load the vanilla
                // CreateRerollTrigger system finds no trigger and makes a fresh one, which we
                // convert again; without the mod, that fresh one is an ordinary reroll.
                EntityManager.RemoveComponent<CRerollShopAfterDuration>(e);
                EntityManager.RemoveComponent<CHideView>(e);
                EntityManager.RemoveComponent<CIsInactive>(e);
                // Click to open (OpenShopInteraction) instead of the reroll's hold-to-complete.
                EntityManager.RemoveComponent<CTakesDuration>(e);
                EntityManager.RemoveComponent<CDisplayDuration>(e);
                EntityManager.RemoveComponent<CHasIndicator>(e);
                EntityManager.AddComponent<CDoNotPersist>(e);
                EntityManager.AddComponent<CShopTerminal>(e);
                ShopLog.Debug(() => $"Converted reroll trigger {e} into a shop terminal");
            }
        }

        /// <summary>Called by OpenShopInteraction (inside the interaction pass).</summary>
        public void RequestOpen(Entity player)
        {
            if (!OpenRequests.Contains(player))
            {
                OpenRequests.Add(player);
            }
        }

        private void HandleOpenRequests()
        {
            if (OpenRequests.Count == 0)
            {
                return;
            }
            foreach (Entity player in OpenRequests)
            {
                if (!EntityManager.Exists(player) || !Require(player, out CPlayer cPlayer))
                {
                    continue;
                }
                if (FindSession(player) != Entity.Null)
                {
                    ShopLog.Debug(() => $"Player {cPlayer.ID} already has a shop open");
                    continue;
                }
                OpenSession(player, cPlayer);
            }
            OpenRequests.Clear();
        }

        private Entity FindSession(Entity player)
        {
            using NativeArray<CShopSession> sessions = Sessions.ToComponentDataArray<CShopSession>(Allocator.Temp);
            using NativeArray<Entity> entities = Sessions.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < sessions.Length; i++)
            {
                if (sessions[i].Player == player)
                {
                    return entities[i];
                }
            }
            return Entity.Null;
        }

        private void OpenSession(Entity player, CPlayer cPlayer)
        {
            Entity session = EntityManager.CreateEntity();
            EntityManager.AddComponentData(session, new CShopSession
            {
                Player = player,
                PlayerID = cPlayer.ID,
                InputSource = cPlayer.InputSource
            });
            EntityManager.AddComponentData(session, new CPosition(Vector3.zero));
            EntityManager.AddComponentData(session, new CRequiresView
            {
                Type = ShopIds.ViewType,
                ViewMode = ViewMode.Screen
            });
            EntityManager.AddComponent<CDoNotPersist>(session);
            ShopLog.Debug(() => $"Opened shop session {session} for player {cPlayer.ID} (input source {cPlayer.InputSource})");
        }

        public void CloseSession(Entity session, string reason)
        {
            if (!IsOpenSession(session))
            {
                return;
            }
            Statuses.Remove(session);
            EntityManager.DestroyEntity(session);
            ShopLog.Debug(() => $"Closed shop session {session}: {reason}");
        }

        private void CloseStaleSessions()
        {
            // NightSystem only runs at night; day-time cleanup is in ShopDayCleanup.
            using NativeArray<Entity> entities = Sessions.ToEntityArray(Allocator.Temp);
            foreach (Entity session in entities)
            {
                CShopSession s = EntityManager.GetComponentData<CShopSession>(session);
                if (!EntityManager.Exists(s.Player) || !Has<CPlayer>(s.Player))
                {
                    CloseSession(session, "player left");
                }
            }
        }

        public SessionStatus GetStatus(Entity session)
        {
            return Statuses.TryGetValue(session, out SessionStatus status) ? status : default;
        }

        // ----------------------------------------------------------------- catalog

        public struct CatalogContext
        {
            public int Day;
            public bool IsDecorNight;
            public bool UpgradesAvailable;
            public float PriceMultiplier;
            public bool RandomPrices;
        }

        public CatalogContext GetContext()
        {
            int day = GetOrDefault<SDay>().Day;
            bool redEnvelopes = HasStatus(RestaurantStatus.JanuaryRedEnvelopes);

            // Same formula as HandleNewShop. Vanilla rolls this chance once per letter; the shop
            // offers upgrades whenever the chance is above zero (day 5+, or earlier with cards).
            float upgradeChance = DifficultyHelpers.BaseUpgradedShopChance(day);
            using (NativeArray<CUpgradedShopChance> chances = UpgradeChances.ToComponentDataArray<CUpgradedShopChance>(Allocator.Temp))
            {
                foreach (CUpgradedShopChance c in chances)
                {
                    upgradeChance += (1f - upgradeChance) * c.Chance;
                }
            }
            if (redEnvelopes)
            {
                upgradeChance += (1f - upgradeChance) * 0.2f;
            }

            // Every price discount in the game (cards, restaurant settings such as Turbo, the
            // discount part of shop effects) arrives as a CGrantsShopDiscount entity.
            float multiplier = 1f;
            using (NativeArray<CGrantsShopDiscount> discounts = Discounts.ToComponentDataArray<CGrantsShopDiscount>(Allocator.Temp))
            {
                foreach (CGrantsShopDiscount d in discounts)
                {
                    multiplier *= 1f - d.Amount;
                }
            }
            if (redEnvelopes)
            {
                multiplier *= 0.75f;
            }

            return new CatalogContext
            {
                Day = day,
                // Vanilla: every 5th night (CreateShopRequests). Configurable: decor_interval.
                IsDecorNight = day > 0 && day % ShopLog.DecorInterval == 0,
                UpgradesAvailable = upgradeChance > 0f,
                PriceMultiplier = multiplier,
                RandomPrices = !RandomShopPrices.IsEmpty
            };
        }

        /// <summary>
        /// Every blueprint the vanilla shop could offer tonight. Eligibility comes from the
        /// game's own CShopBuilderOption pool, which the ShopBuilder filters already narrow by
        /// dish, theme, owned appliances and staples; this applies the per-letter rules from
        /// HandleNewShop.OnUpdate on top.
        /// </summary>
        public List<ShopEntry> BuildCatalog(CatalogContext ctx)
        {
            var result = new List<ShopEntry>();
            var seen = new HashSet<int>();
            using NativeArray<CShopBuilderOption> options = ShopOptions.ToComponentDataArray<CShopBuilderOption>(Allocator.Temp);
            foreach (CShopBuilderOption option in options)
            {
                if (option.IsRemoved)
                {
                    continue;
                }
                // Vanilla decoration nights offer only Decoration/SpecialEvent letters; with
                // normal_items_on_decor_nights the normal (Basic/default) pool is offered as well.
                bool decorMatch = ctx.IsDecorNight
                    && (option.MatchesRequestTags(ShoppingTags.Decoration) || option.MatchesRequestTags(ShoppingTags.SpecialEvent));
                bool normalMatch = (!ctx.IsDecorNight || ShopLog.NormalItemsOnDecorNights)
                    && (option.MatchesRequestTags(ShoppingTags.Basic) || option.MatchesRequestTags(ShoppingTagsExtensions.DefaultShoppingTag));
                bool matches = decorMatch || normalMatch;
                if (!matches)
                {
                    continue;
                }
                // Vanilla: (upgradeRoll || Staple != NonStaple || !SellAsUpgrade).
                if (!ctx.UpgradesAvailable && option.Staple == ShopStapleType.NonStaple && option.SellAsUpgrade)
                {
                    continue;
                }
                if (!seen.Add(option.Appliance) || !Data.TryGet(option.Appliance, out Appliance appliance))
                {
                    continue;
                }
                ShopEntryFlags flags = ShopEntryFlags.None;
                if (option.Staple != ShopStapleType.NonStaple)
                {
                    flags |= ShopEntryFlags.Staple;
                }
                if (option.SellAsUpgrade)
                {
                    flags |= ShopEntryFlags.Upgrade;
                }
                if (appliance.SellOnlyAsUnique)
                {
                    flags |= ShopEntryFlags.Unique;
                }
                result.Add(new ShopEntry
                {
                    Kind = ShopEntryKind.Appliance,
                    ID = appliance.ID,
                    Price = AppliancePrice(appliance, ctx),
                    Flags = flags
                });
            }

            if (ctx.IsDecorNight)
            {
                foreach (Decor decor in Data.Get<Decor>())
                {
                    if (decor.IsAvailable && decor.ApplicatorAppliance != null)
                    {
                        result.Add(new ShopEntry { Kind = ShopEntryKind.Decor, ID = decor.ID, Price = 0 });
                    }
                }
            }

            result.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.ID.CompareTo(b.ID));
            return result;
        }

        private static int BasePrice(Appliance appliance, CatalogContext ctx) =>
            ctx.RandomPrices ? DeterministicRandomPrice(ctx.Day, appliance.ID) : appliance.PurchaseCost;

        private static int AppliancePrice(Appliance appliance, CatalogContext ctx)
        {
            // PostHelpers.CreateBlueprintLetter: CeilToInt(price * multiplier).
            return Mathf.CeilToInt(BasePrice(appliance, ctx) * ctx.PriceMultiplier);
        }

        /// <summary>
        /// HandleNewShop.RandomPrice draws a fresh price tier per letter. To keep this a shop
        /// rather than a slot machine, each appliance gets one tier per night, drawn with the
        /// same distribution from a seed of (day, appliance).
        /// </summary>
        private static int DeterministicRandomPrice(int day, int applianceId)
        {
            var rng = new System.Random(unchecked(day * 486187739 ^ applianceId));
            int tier = 0;
            for (int i = 0; i < 5 && !(rng.NextDouble() < 0.1 * (i + 1)); i++)
            {
                tier++;
            }
            return Appliance.GetPrice((PriceTier)tier);
        }

        public static int HashCatalog(List<ShopEntry> entries)
        {
            unchecked
            {
                int hash = 17;
                foreach (ShopEntry e in entries)
                {
                    hash = hash * 31 + (int)e.Kind;
                    hash = hash * 31 + e.ID;
                    hash = hash * 31 + e.Price;
                    hash = hash * 31 + (int)e.Flags;
                }
                return hash;
            }
        }

        /// <summary>Verbose only: how every price was derived, logged when the catalog changes.</summary>
        public void LogCatalogIfChanged(CatalogContext ctx, List<ShopEntry> catalog, int hash)
        {
            if (!ShopLog.Verbose || hash == LastLoggedCatalogHash)
            {
                return;
            }
            LastLoggedCatalogHash = hash;
            var sb = new StringBuilder();
            sb.Append($"Catalog for day {ctx.Day}: {catalog.Count} entries, price multiplier {ctx.PriceMultiplier:0.###} (discounts), ");
            sb.Append($"random prices {(ctx.RandomPrices ? "ON" : "off")}, upgrades {(ctx.UpgradesAvailable ? "available" : "not yet")}, decoration night {ctx.IsDecorNight}");
            foreach (ShopEntry e in catalog)
            {
                if (e.Kind == ShopEntryKind.Appliance && Data.TryGet(e.ID, out Appliance a))
                {
                    sb.Append($"\n  {a.Name} (#{a.ID}): tier {a.PriceTier} = {a.PurchaseCost}, base {BasePrice(a, ctx)} x {ctx.PriceMultiplier:0.###} -> {e.Price}  [{e.Flags}]  tags {a.ShoppingTags}");
                }
                else
                {
                    sb.Append($"\n  decor #{e.ID}: free");
                }
            }
            string text = sb.ToString();
            ShopLog.Debug(() => text);
        }

        /// <summary>How many of each catalog appliance the restaurant has (placed, carried or in a parcel).</summary>
        public List<OwnedCount> CountOwned(List<ShopEntry> catalog, out int hash)
        {
            var wanted = new HashSet<int>();
            foreach (ShopEntry e in catalog)
            {
                if (e.Kind == ShopEntryKind.Appliance)
                {
                    wanted.Add(e.ID);
                }
            }
            var counts = new Dictionary<int, int>();
            using (NativeArray<CAppliance> appliances = PlacedAppliances.ToComponentDataArray<CAppliance>(Allocator.Temp))
            {
                foreach (CAppliance a in appliances)
                {
                    if (wanted.Contains(a.ID))
                    {
                        counts.TryGetValue(a.ID, out int n);
                        counts[a.ID] = n + 1;
                    }
                }
            }
            using (NativeArray<CLetterAppliance> parcels = PendingParcels.ToComponentDataArray<CLetterAppliance>(Allocator.Temp))
            {
                foreach (CLetterAppliance p in parcels)
                {
                    if (wanted.Contains(p.ApplianceID))
                    {
                        counts.TryGetValue(p.ApplianceID, out int n);
                        counts[p.ApplianceID] = n + 1;
                    }
                }
            }
            var result = new List<OwnedCount>(counts.Count);
            hash = 17;
            foreach (KeyValuePair<int, int> kv in counts)
            {
                result.Add(new OwnedCount { ID = kv.Key, Count = kv.Value });
            }
            result.Sort((x, y) => x.ID.CompareTo(y.ID));
            unchecked
            {
                foreach (OwnedCount c in result)
                {
                    hash = hash * 31 + c.ID;
                    hash = hash * 31 + c.Count;
                }
            }
            return result;
        }

        // ----------------------------------------------------------------- held item / selling

        /// <summary>What the session's player is holding, and what selling it would refund.</summary>
        public HeldInfo GetHeld(Entity player)
        {
            var info = new HeldInfo();
            if (!EntityManager.Exists(player) || !Require(player, out CItemHolder holder) || holder.HeldItem == Entity.Null
                || !EntityManager.Exists(holder.HeldItem))
            {
                return info;
            }
            Entity held = holder.HeldItem;
            info.Entity = held;
            if (Require(held, out CLetterAppliance parcel))
            {
                info.Kind = HeldKind.Parcel;
                info.ApplianceID = parcel.ApplianceID;
            }
            else if (Has<CHeldAppliance>(held) && !Has<CApplianceBlueprint>(held) && Require(held, out CAppliance appliance))
            {
                info.Kind = HeldKind.Appliance;
                info.ApplianceID = appliance.ID;
            }
            else
            {
                info.Kind = HeldKind.Other;
                return info;
            }
            info.Refund = Require(held, out CPaidPrice paid) ? Math.Max(0, paid.Price) : 0;
            return info;
        }

        private bool ExecuteSell(Entity session, List<ShopOrderLine> lines, out string message)
        {
            CShopSession s = EntityManager.GetComponentData<CShopSession>(session);
            HeldInfo held = GetHeld(s.Player);
            if (held.Kind != HeldKind.Appliance && held.Kind != HeldKind.Parcel)
            {
                message = "Pick up the appliance you want to sell, then open the shop.";
                return false;
            }
            if (lines == null || lines.Count != 1 || lines[0].ID != held.ApplianceID)
            {
                message = "You're holding something different now. Try again.";
                return false;
            }

            string name = Data.TryGet(held.ApplianceID, out Appliance appliance) ? appliance.Name : $"Item {held.ApplianceID}";
            // Anything sitting on the appliance goes with it (vanilla's day-start bin does the same).
            if (Require(held.Entity, out CItemHolder contents) && contents.HeldItem != Entity.Null && EntityManager.Exists(contents.HeldItem))
            {
                EntityManager.DestroyEntity(contents.HeldItem);
            }
            EntityManager.SetComponentData(s.Player, new CItemHolder { HeldItem = Entity.Null });
            EntityManager.DestroyEntity(held.Entity);

            if (held.Refund > 0)
            {
                int money = GetOrDefault<SMoney>().Amount;
                Set(new SMoney { Amount = money + held.Refund });
            }
            message = held.Refund > 0 ? $"Sold {name} for {held.Refund}." : $"Got rid of {name} (it was free, so no refund).";
            ShopLog.Debug(() => $"Session {session}: sold {name} ({held.Kind}) for {held.Refund}");
            return true;
        }

        // ----------------------------------------------------------------- orders

        /// <summary>
        /// True while the session is open. Not EntityManager.Exists: the session carries the
        /// system-state CLinkedView, so a destroyed session still "exists" (without CShopSession)
        /// until its view is cleaned up a frame later.
        /// </summary>
        public bool IsOpenSession(Entity session) =>
            EntityManager.Exists(session) && EntityManager.HasComponent<CShopSession>(session);

        public void HandleResponse(Entity session, ShopResponseData response)
        {
            if (!IsOpenSession(session))
            {
                return;
            }
            SessionStatus status = GetStatus(session);
            if (response.Sequence != 0 && response.Sequence <= status.AckSequence)
            {
                ShopLog.Debug(() => $"Ignoring duplicate response {response.Sequence} for session {session}");
                return;
            }
            ShopLog.Debug(() => $"Session {session}: response #{response.Sequence} {response.Action} with {response.Lines?.Count ?? 0} line(s)");

            if (response.Action == ShopAction.Close)
            {
                CloseSession(session, "closed by player");
                return;
            }

            string message;
            bool ok;
            if (!IsShopNight)
            {
                ok = false;
                message = "The shop is closed during the day.";
            }
            else if (response.Action == ShopAction.Purchase || response.Action == ShopAction.TakeBlueprint)
            {
                ok = ExecuteOrder(response.Lines, response.Action == ShopAction.TakeBlueprint, out message);
            }
            else if (response.Action == ShopAction.Sell)
            {
                ok = ExecuteSell(session, response.Lines, out message);
            }
            else
            {
                ok = false;
                message = "Unknown request.";
            }

            Statuses[session] = new SessionStatus
            {
                AckSequence = Math.Max(status.AckSequence, response.Sequence),
                LastSucceeded = ok,
                Message = message
            };
            ShopLog.Debug(() => $"Session {session}: {(ok ? "OK" : "rejected")} - {message}");
        }

        private bool ExecuteOrder(List<ShopOrderLine> lines, bool asBlueprints, out string message)
        {
            if (lines == null || lines.Count == 0)
            {
                message = "Your cart is empty.";
                return false;
            }

            CatalogContext ctx = GetContext();
            var catalog = new Dictionary<(ShopEntryKind, int), ShopEntry>();
            foreach (ShopEntry e in BuildCatalog(ctx))
            {
                catalog[(e.Kind, e.ID)] = e;
            }

            // Merge duplicate lines and validate against the host's own catalog; the client's
            // idea of prices or availability is never trusted.
            var merged = new Dictionary<(ShopEntryKind, int), int>();
            foreach (ShopOrderLine line in lines)
            {
                if (line.Quantity <= 0)
                {
                    continue;
                }
                var key = (line.Kind, line.ID);
                merged.TryGetValue(key, out int qty);
                merged[key] = Math.Min(MaxQuantityPerLine, qty + line.Quantity);
            }

            int total = 0;
            int applianceItems = 0;
            int decorItems = 0;
            foreach (KeyValuePair<(ShopEntryKind, int), int> kv in merged)
            {
                if (!catalog.TryGetValue(kv.Key, out ShopEntry entry))
                {
                    message = $"{EntryName(kv.Key.Item1, kv.Key.Item2)} isn't available right now.";
                    return false;
                }
                if (asBlueprints && entry.Kind != ShopEntryKind.Appliance)
                {
                    message = "Wallpapers and floors don't come as blueprints.";
                    return false;
                }
                if ((entry.Flags & ShopEntryFlags.Unique) != 0)
                {
                    if (kv.Value > 1)
                    {
                        message = $"Only one {EntryName(entry.Kind, entry.ID)} is allowed.";
                        return false;
                    }
                    if (IsPending(entry.ID))
                    {
                        message = $"You already have a {EntryName(entry.Kind, entry.ID)} waiting to be unpacked.";
                        return false;
                    }
                }
                if (!asBlueprints)
                {
                    total += entry.Price * kv.Value;
                }
                if (entry.Kind == ShopEntryKind.Decor)
                {
                    decorItems += kv.Value;
                }
                else
                {
                    applianceItems += kv.Value;
                }
            }

            int money = GetOrDefault<SMoney>().Amount;
            if (total > money)
            {
                message = $"Not enough money: this costs {total}, you have {money}.";
                return false;
            }

            // Same placement as vanilla: appliances on the letter tiles, decor inside.
            List<Vector3> applianceTiles = applianceItems > 0 ? FindFreeTiles(GetPostTiles(), applianceItems) : new List<Vector3>();
            List<Vector3> decorTiles = decorItems > 0 ? FindFreeTiles(GetPostTiles(force_inside: true), decorItems) : new List<Vector3>();
            if (applianceTiles.Count < applianceItems || decorTiles.Count < decorItems)
            {
                int free = applianceTiles.Count + decorTiles.Count;
                message = $"Not enough floor space for {applianceItems + decorItems} item(s), only {free} free spot(s). Open or move some parcels first.";
                return false;
            }

            int a = 0;
            int d = 0;
            var ectx = new EntityContext(EntityManager);
            foreach (KeyValuePair<(ShopEntryKind, int), int> kv in merged)
            {
                ShopEntry entry = catalog[kv.Key];
                for (int i = 0; i < kv.Value; i++)
                {
                    if (entry.Kind == ShopEntryKind.Decor)
                    {
                        SpawnDecor(entry.ID, decorTiles[d++]);
                    }
                    else if (asBlueprints)
                    {
                        // An ordinary unpaid blueprint at the shop price, exactly like an opened
                        // letter, so blueprint cabinets and desks work on it as usual. If it's
                        // bought later, PaidPriceTracker records the price actually paid.
                        PostHelpers.CreateOpenedLetter(ectx, applianceTiles[a++], entry.ID, 1f, entry.Price);
                    }
                    else
                    {
                        Entity parcel = PostHelpers.CreateApplianceParcel(EntityManager, applianceTiles[a++], entry.ID);
                        if (entry.Price > 0 && !ShopLog.PrepareUninstall)
                        {
                            // Carried onto the appliance when the parcel is opened (OpenParcelPatch).
                            EntityManager.AddComponentData(parcel, new CPaidPrice { Price = entry.Price });
                        }
                    }
                }
            }

            if (total > 0)
            {
                Set(new SMoney { Amount = money - total });
            }

            int count = applianceItems + decorItems;
            message = asBlueprints
                ? $"{count} blueprint(s) placed outside."
                : total > 0 ? $"Bought {count} item(s) for {total}. They're waiting by the door." : $"{count} item(s) delivered.";
            return true;
        }

        private bool IsPending(int applianceId)
        {
            using (NativeArray<CLetterAppliance> parcels = PendingParcels.ToComponentDataArray<CLetterAppliance>(Allocator.Temp))
            {
                foreach (CLetterAppliance p in parcels)
                {
                    if (p.ApplianceID == applianceId)
                    {
                        return true;
                    }
                }
            }
            using (NativeArray<CApplianceBlueprint> blueprints = PendingBlueprints.ToComponentDataArray<CApplianceBlueprint>(Allocator.Temp))
            {
                foreach (CApplianceBlueprint b in blueprints)
                {
                    if (b.Appliance == applianceId)
                    {
                        return true;
                    }
                }
            }
            using (NativeArray<CLetterBlueprint> letters = LetterBlueprints.ToComponentDataArray<CLetterBlueprint>(Allocator.Temp))
            {
                foreach (CLetterBlueprint l in letters)
                {
                    if (l.ApplianceID == applianceId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Mirrors HandleNewShop.FindTile: empty, not next to a table, and not reserved.</summary>
        private List<Vector3> FindFreeTiles(List<Vector3> candidates, int needed)
        {
            var result = new List<Vector3>();
            foreach (Vector3 tile in candidates)
            {
                if (result.Count >= needed)
                {
                    break;
                }
                Vector3Int key = Vector3Int.RoundToInt(tile);
                if (ReservedTiles.ContainsKey(key) || TileManager.GetOccupant(tile) != Entity.Null)
                {
                    continue;
                }
                bool nextToTable = false;
                foreach (LayoutPosition direction in LayoutHelpers.Directions)
                {
                    Entity neighbour = TileManager.GetOccupant(tile + (Vector3)direction);
                    if (neighbour != Entity.Null && Has<CApplianceTable>(neighbour))
                    {
                        nextToTable = true;
                        break;
                    }
                }
                if (nextToTable)
                {
                    continue;
                }
                result.Add(tile);
            }
            foreach (Vector3 tile in result)
            {
                ReservedTiles[Vector3Int.RoundToInt(tile)] = UnityEngine.Time.frameCount + ReservationFrames;
            }
            return result;
        }

        private void ExpireReservations()
        {
            if (ReservedTiles.Count == 0)
            {
                return;
            }
            int now = UnityEngine.Time.frameCount;
            var expired = new List<Vector3Int>();
            foreach (KeyValuePair<Vector3Int, int> kv in ReservedTiles)
            {
                if (kv.Value <= now)
                {
                    expired.Add(kv.Key);
                }
            }
            foreach (Vector3Int key in expired)
            {
                ReservedTiles.Remove(key);
            }
        }

        /// <summary>Same entity HandleNewShop.AddDecorationItem creates for its random wallpaper/floor letters.</summary>
        private void SpawnDecor(int decorId, Vector3 position)
        {
            if (!Data.TryGet(decorId, out Decor decor))
            {
                return;
            }
            Entity entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, new CCreateAppliance { ID = decor.ApplicatorAppliance.ID });
            EntityManager.AddComponentData(entity, new CPosition(position));
            EntityManager.AddComponentData(entity, new CApplyDecor { ID = decor.ID, Type = decor.Type });
            EntityManager.AddComponentData(entity, new CDrawApplianceUsing { DrawApplianceID = decor.ID });
            EntityManager.AddComponentData<CShopEntity>(entity, default);
        }

        private string EntryName(ShopEntryKind kind, int id)
        {
            if (kind == ShopEntryKind.Appliance && Data.TryGet(id, out Appliance appliance))
            {
                return appliance.Name;
            }
            if (kind == ShopEntryKind.Decor && Data.TryGet(id, out Decor decor))
            {
                return decor.name;
            }
            return $"Item {id}";
        }
    }

    /// <summary>
    /// Day start: closes open shop menus, removes the terminal the mod spawned on day 0, and
    /// hides the converted reroll trigger until night (vanilla hides it too once the day's
    /// blueprints are gone).
    /// </summary>
    public class ShopDayCleanup : DaySystem, IModSystem
    {
        private EntityQuery Sessions;
        private EntityQuery SpawnedTerminals;
        private EntityQuery VisibleTerminals;

        protected override void Initialise()
        {
            base.Initialise();
            Sessions = GetEntityQuery(typeof(CShopSession));
            SpawnedTerminals = GetEntityQuery(typeof(CShopTerminalSpawned));
            VisibleTerminals = GetEntityQuery(new QueryHelper().All(typeof(CShopTerminal)).None(typeof(CHideView)));
        }

        protected override void OnUpdate()
        {
            if (!Sessions.IsEmpty)
            {
                int count = Sessions.CalculateEntityCount();
                ShopLog.Debug(() => $"Day started: closing {count} shop session(s)");
                EntityManager.DestroyEntity(Sessions);
            }
            if (!SpawnedTerminals.IsEmpty)
            {
                ShopLog.Debug(() => "Day started: removing the day-0 shop terminal");
                EntityManager.DestroyEntity(SpawnedTerminals);
            }
            if (!VisibleTerminals.IsEmpty)
            {
                EntityManager.AddComponent<CHideView>(VisibleTerminals);
            }
        }
    }
}

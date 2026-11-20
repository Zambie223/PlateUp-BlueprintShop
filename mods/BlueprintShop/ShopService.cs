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
        private EntityQuery BinnedAppliances;
        private EntityQuery UnbinnedAppliances;
        private EntityQuery Unlocks;

        /// <summary>Appliance ID -> IDs of the processes it registers (static game data).</summary>
        private readonly Dictionary<int, int[]> ProcessCache = new Dictionary<int, int[]>();

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
            /// <summary>What was paid for it (CPaidPrice); 0 for free items.</summary>
            public int Paid;
            /// <summary>What selling it gives back: Paid minus the sale loss setting.</summary>
            public int Refund;
            public Entity Entity;
            /// <summary>Selling it would leave the menu without a required process (see RequirementCheck).</summary>
            public bool Required;
        }

        /// <summary>One binned (outside) appliance or parcel that was bought through the shop.</summary>
        public struct OutsideItem
        {
            public Entity Entity;
            public int ApplianceID;
            public bool IsParcel;
            public int Refund;
            public bool Required;
        }

        public sealed class OutsideSale
        {
            public readonly List<OutsideItem> Items = new List<OutsideItem>();
            public readonly List<OutsideLine> Lines = new List<OutsideLine>();
            public int Hash;
            public int Total;
            public int SellCount;
            public int RequiredCount;
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
            // Vanilla's MarkOutsideAppliancesForDestruction tags appliances on outside tiles
            // (the bin icon); DestroyAppliancesAtDay deletes them for $0 at day start.
            BinnedAppliances = GetEntityQuery(new QueryHelper()
                .All(typeof(CAppliance), typeof(CDestroyApplianceAtDay), typeof(CPosition))
                .None(typeof(CHeldAppliance)));
            UnbinnedAppliances = GetEntityQuery(new QueryHelper().All(typeof(CAppliance)).None(typeof(CDestroyApplianceAtDay)));
            Unlocks = GetEntityQuery(typeof(CProgressionUnlock));
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

        public bool HasOpenSession(Entity player) => FindSession(player) != Entity.Null;

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
            /// <summary>What the shop tells players about upgrades (status line under the title).</summary>
            public UpgradeState UpgradeState;
            /// <summary>Roll mode: tonight's and next night's unlock chance (0 to 1), cards included.</summary>
            public float UpgradeChanceTonight;
            public float UpgradeChanceNext;
            public float PriceMultiplier;
            public bool RandomPrices;
        }

        public CatalogContext GetContext()
        {
            int day = GetOrDefault<SDay>().Day;
            bool redEnvelopes = HasStatus(RestaurantStatus.JanuaryRedEnvelopes);

            // HandleNewShop's chance is DifficultyHelpers.BaseUpgradedShopChance(day), which is
            // floor(day / 5) * 10% and so above zero from night 5, plus every card's chance and
            // the red envelopes. Vanilla rolls it once per letter; the shop offers upgrades
            // whenever it's above zero. The night-based part is configurable (upgrade_night,
            // default 5 = vanilla); the card and event parts still unlock upgrades earlier.
            float extraChance = 0f;
            using (NativeArray<CUpgradedShopChance> chances = UpgradeChances.ToComponentDataArray<CUpgradedShopChance>(Allocator.Temp))
            {
                foreach (CUpgradedShopChance c in chances)
                {
                    extraChance += (1f - extraChance) * c.Chance;
                }
            }
            if (redEnvelopes)
            {
                extraChance += (1f - extraChance) * 0.2f;
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
            // Balance setting price_percent (100 = vanilla, which multiplies by exactly 1).
            multiplier *= ShopSettings.PricePercent / 100f;

            var ctx = new CatalogContext
            {
                Day = day,
                // Vanilla: every 5th night (CreateShopRequests). Configurable: decor_interval.
                IsDecorNight = day > 0 && day % ShopSettings.DecorInterval == 0,
                PriceMultiplier = multiplier,
                RandomPrices = !RandomShopPrices.IsEmpty
            };
            // Saved unlock (and the roll salt) from UpgradeUnlockSystem. Before that singleton
            // exists (first frames of a night, or prepare_uninstall), the salt comes from the
            // run's seed directly, which is what the singleton would hold anyway.
            bool hasSaved = Require(out SUpgradeUnlock saved);
            int salt = hasSaved ? saved.Salt : (Require(out SFixedSeed seed) ? UpgradeUnlock.SaltFrom(seed) : 0);
            UpgradeUnlock.Evaluate(ref ctx, extraChance, hasSaved && saved.Unlocked, saved.UnlockedDay, salt);
            return ctx;
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
                bool normalMatch = (!ctx.IsDecorNight || ShopSettings.NormalItemsOnDecorNights)
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
            // Bought to place by hand but not handed out yet (one in someone's hands is
            // already an appliance, counted above).
            var waiting = new Dictionary<int, int>();
            World.GetExistingSystem<BuildQueueSystem>()?.AddWaitingCounts(waiting);
            foreach (KeyValuePair<int, int> kv in waiting)
            {
                if (wanted.Contains(kv.Key))
                {
                    counts.TryGetValue(kv.Key, out int n);
                    counts[kv.Key] = n + kv.Value;
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
            info.Paid = Require(held, out CPaidPrice paid) ? Math.Max(0, paid.Price) : 0;
            info.Refund = ShopSettings.RefundFor(info.Paid);
            RequirementCheck check = NewRequirementCheck();
            if (check.Active)
            {
                AddBaseCoverage(check, held, null);
                info.Required = check.WouldBreak(ProcessesOf(info.ApplianceID));
            }
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
            if (held.Required)
            {
                message = $"Your menu needs {name}: nothing else in your kitchen can do its job, so it can't be sold.";
                return false;
            }
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
            message = held.Refund > 0 ? $"Sold {name} for {held.Refund}."
                : held.Paid > 0 ? $"Got rid of {name} (the sale loss setting left no refund)."
                : $"Got rid of {name} (it was free, so no refund).";
            ShopLog.Debug(() => $"Session {session}: sold {name} ({held.Kind}) for {held.Refund}");
            return true;
        }

        // ----------------------------------------------------------------- selling what's outside

        /// <summary>
        /// Everything outside (bin icon) that was bought through the shop, with its refund.
        /// Free items (no CPaidPrice, or prepare_uninstall) are left out: vanilla bins them as
        /// usual. Items the menu can't do without are flagged Required and never sold.
        /// </summary>
        public OutsideSale ComputeOutsideSale()
        {
            var sale = new OutsideSale();
            if (ShopSettings.PrepareUninstall || BinnedAppliances.IsEmpty)
            {
                sale.Hash = 17;
                return sale;
            }
            using (NativeArray<Entity> entities = BinnedAppliances.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in entities)
                {
                    if (!Require(e, out CDestroyApplianceAtDay bin) || bin.HideBin
                        || !Require(e, out CPaidPrice paid) || paid.Price <= 0)
                    {
                        continue;
                    }
                    var item = new OutsideItem { Entity = e, Refund = ShopSettings.RefundFor(paid.Price) };
                    if (Require(e, out CLetterAppliance letter))
                    {
                        item.IsParcel = true;
                        item.ApplianceID = letter.ApplianceID;
                    }
                    else if (Require(e, out CAppliance appliance))
                    {
                        item.ApplianceID = appliance.ID;
                    }
                    sale.Items.Add(item);
                }
            }

            // Required check, cheapest first: when several copies could cover a job, the cheapest
            // is kept and the more valuable ones are sold.
            sale.Items.Sort((a, b) => a.Refund != b.Refund ? a.Refund.CompareTo(b.Refund) : a.Entity.Index.CompareTo(b.Entity.Index));
            RequirementCheck check = NewRequirementCheck();
            if (check.Active)
            {
                var candidates = new HashSet<Entity>();
                foreach (OutsideItem item in sale.Items)
                {
                    candidates.Add(item.Entity);
                }
                AddBaseCoverage(check, Entity.Null, candidates);
                for (int i = 0; i < sale.Items.Count; i++)
                {
                    OutsideItem item = sale.Items[i];
                    if (check.WouldBreak(ProcessesOf(item.ApplianceID)))
                    {
                        item.Required = true;
                        sale.Items[i] = item;
                        check.Cover(ProcessesOf(item.ApplianceID));
                    }
                }
            }

            var lines = new Dictionary<(int, bool), OutsideLine>();
            foreach (OutsideItem item in sale.Items)
            {
                lines.TryGetValue((item.ApplianceID, item.IsParcel), out OutsideLine line);
                line.ApplianceID = item.ApplianceID;
                line.IsParcel = item.IsParcel;
                if (item.Required)
                {
                    line.RequiredCount++;
                    sale.RequiredCount++;
                }
                else
                {
                    line.SellCount++;
                    line.Total += item.Refund;
                    sale.SellCount++;
                    sale.Total += item.Refund;
                }
                lines[(item.ApplianceID, item.IsParcel)] = line;
            }
            sale.Lines.AddRange(lines.Values);
            sale.Lines.Sort((a, b) => a.ApplianceID != b.ApplianceID ? a.ApplianceID.CompareTo(b.ApplianceID) : a.IsParcel.CompareTo(b.IsParcel));
            int hash = 17;
            unchecked
            {
                foreach (OutsideLine l in sale.Lines)
                {
                    hash = hash * 31 + l.ApplianceID;
                    hash = hash * 31 + (l.IsParcel ? 1 : 0);
                    hash = hash * 31 + l.SellCount;
                    hash = hash * 31 + l.Total;
                    hash = hash * 31 + l.RequiredCount;
                }
            }
            sale.Hash = hash;
            return sale;
        }

        /// <summary>Sells (destroys) every sellable outside item and adds the refunds to the money.</summary>
        private bool ExecuteSellOutside(List<ShopOrderLine> lines, out string message)
        {
            OutsideSale sale = ComputeOutsideSale();
            // The menu sends the hash of the list it showed; refuse if it has changed since.
            if (lines == null || lines.Count != 1 || lines[0].Quantity != sale.Hash)
            {
                message = "The items outside changed. Check the list and confirm again.";
                return false;
            }
            if (sale.SellCount == 0)
            {
                message = sale.RequiredCount > 0
                    ? "Everything outside is needed by your menu, so nothing was sold."
                    : "There's nothing outside to sell.";
                return false;
            }
            foreach (OutsideItem item in sale.Items)
            {
                if (item.Required || !EntityManager.Exists(item.Entity))
                {
                    continue;
                }
                // Anything sitting on it goes too, as with single sells and vanilla's day-start bin.
                if (Require(item.Entity, out CItemHolder contents) && contents.HeldItem != Entity.Null && EntityManager.Exists(contents.HeldItem))
                {
                    EntityManager.DestroyEntity(contents.HeldItem);
                }
                EntityManager.DestroyEntity(item.Entity);
            }
            int money = GetOrDefault<SMoney>().Amount;
            Set(new SMoney { Amount = money + sale.Total });
            message = $"Sold {sale.SellCount} item{(sale.SellCount == 1 ? "" : "s")} from outside for {sale.Total}.";
            if (sale.RequiredCount > 0)
            {
                message += $" Kept {sale.RequiredCount} your menu needs.";
            }
            int count = sale.SellCount, total = sale.Total, kept = sale.RequiredCount;
            ShopLog.Debug(() => $"Sold {count} outside item(s) for {total}; kept {kept} required");
            return true;
        }

        // ----------------------------------------------------------------- required appliances

        /// <summary>
        /// Mirrors vanilla's CheckSellingRequiredAppliance (the start-of-day "you're selling
        /// something you need" warning): every process an unlocked dish requires must be provided
        /// by some appliance or parcel, and dishes that need cleaning (with a main course) need a
        /// cleaning process. Selling something that breaks this is refused: vanilla would warn,
        /// and GrantNecessaryAppliances hands out a free sink when cleaning is missing, which
        /// would turn a refund into free money.
        /// </summary>
        internal sealed class RequirementCheck
        {
            public readonly HashSet<int> Needed = new HashSet<int>();
            public readonly HashSet<int> Covered = new HashSet<int>();
            public bool NeedsCleaning;
            public bool CleaningCovered;

            public bool Active => Needed.Count > 0 || NeedsCleaning;

            public void Cover(int[] processes)
            {
                foreach (int p in processes)
                {
                    Covered.Add(p);
                    if (AssetReference.MustHaveOneOfProcesses.Contains(p))
                    {
                        CleaningCovered = true;
                    }
                }
            }

            /// <summary>True if removing an appliance with these processes leaves a requirement unmet.</summary>
            public bool WouldBreak(int[] processes)
            {
                foreach (int p in processes)
                {
                    if (Needed.Contains(p) && !Covered.Contains(p))
                    {
                        return true;
                    }
                    if (NeedsCleaning && !CleaningCovered && AssetReference.MustHaveOneOfProcesses.Contains(p))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private RequirementCheck NewRequirementCheck()
        {
            var check = new RequirementCheck();
            bool cleaningDish = false;
            bool mainNeedingCleaning = false;
            using (NativeArray<CProgressionUnlock> unlocks = Unlocks.ToComponentDataArray<CProgressionUnlock>(Allocator.Temp))
            {
                foreach (CProgressionUnlock unlock in unlocks)
                {
                    if (!Data.TryGet(unlock.ID, out Dish dish))
                    {
                        continue;
                    }
                    if (dish.RequiresCleaning)
                    {
                        cleaningDish = true;
                        if (dish.UnlocksMenuItems != null)
                        {
                            foreach (Dish.MenuItem item in dish.UnlocksMenuItems)
                            {
                                if (item.Phase == MenuPhase.Main)
                                {
                                    mainNeedingCleaning = true;
                                }
                            }
                        }
                    }
                    if (dish.RequiredProcesses == null)
                    {
                        continue;
                    }
                    foreach (Process process in dish.RequiredProcesses)
                    {
                        if (process == null)
                        {
                            continue;
                        }
                        // Unity objects: compare with == null, not ??.
                        Process real = process.IsPseudoprocessFor != null ? process.IsPseudoprocessFor : process;
                        check.Needed.Add(real.ID);
                    }
                }
            }
            check.NeedsCleaning = cleaningDish && mainNeedingCleaning;
            return check;
        }

        /// <summary>
        /// What the kitchen provides without the items being checked: every appliance that isn't
        /// outside (vanilla doesn't count binned ones) and every parcel, except
        /// <paramref name="exclude"/> and <paramref name="candidates"/>.
        /// </summary>
        private void AddBaseCoverage(RequirementCheck check, Entity exclude, HashSet<Entity> candidates)
        {
            using (NativeArray<Entity> entities = UnbinnedAppliances.ToEntityArray(Allocator.Temp))
            using (NativeArray<CAppliance> appliances = UnbinnedAppliances.ToComponentDataArray<CAppliance>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (entities[i] == exclude || (candidates != null && candidates.Contains(entities[i])))
                    {
                        continue;
                    }
                    check.Cover(ProcessesOf(appliances[i].ID));
                }
            }
            using (NativeArray<Entity> entities = PendingParcels.ToEntityArray(Allocator.Temp))
            using (NativeArray<CLetterAppliance> parcels = PendingParcels.ToComponentDataArray<CLetterAppliance>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (entities[i] == exclude || (candidates != null && candidates.Contains(entities[i])))
                    {
                        continue;
                    }
                    check.Cover(ProcessesOf(parcels[i].ApplianceID));
                }
            }
        }

        private int[] ProcessesOf(int applianceId)
        {
            if (ProcessCache.TryGetValue(applianceId, out int[] cached))
            {
                return cached;
            }
            var ids = new List<int>();
            if (applianceId != 0 && Data.TryGet(applianceId, out Appliance appliance) && appliance.Processes != null)
            {
                foreach (Appliance.ApplianceProcesses ap in appliance.Processes)
                {
                    if (ap.Validity != ProcessValidity.DoesNotRegister && ap.Process != null)
                    {
                        ids.Add(ap.Process.ID);
                    }
                }
            }
            int[] result = ids.ToArray();
            ProcessCache[applianceId] = result;
            return result;
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
            else if (response.Action == ShopAction.Purchase || response.Action == ShopAction.TakeBlueprint
                || response.Action == ShopAction.PurchaseToHands)
            {
                ok = ExecuteOrder(session, response.Lines, response.Action, out message);
            }
            else if (response.Action == ShopAction.QueueToDoor)
            {
                ok = ExecuteQueueToDoor(session, out message);
            }
            else if (response.Action == ShopAction.Sell)
            {
                ok = ExecuteSell(session, response.Lines, out message);
            }
            else if (response.Action == ShopAction.SellOutside)
            {
                ok = ExecuteSellOutside(response.Lines, out message);
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

        private bool ExecuteOrder(Entity session, List<ShopOrderLine> lines, ShopAction action, out string message)
        {
            if (lines == null || lines.Count == 0)
            {
                message = "Your cart is empty.";
                return false;
            }
            bool asBlueprints = action == ShopAction.TakeBlueprint;
            // "Place items myself": appliances go into the buyer's hands (BuildQueueSystem)
            // instead of parcels. Decorations are still delivered inside, as usual.
            bool toHands = action == ShopAction.PurchaseToHands;
            CShopSession buyer = EntityManager.GetComponentData<CShopSession>(session);
            BuildQueueSystem queue = null;
            if (toHands)
            {
                queue = World.GetExistingSystem<BuildQueueSystem>();
                if (queue == null || !EntityManager.Exists(buyer.Player) || !Require(buyer.Player, out CPlayer _))
                {
                    message = "Couldn't hand you the items. Untick \"Place items myself\" to get parcels instead.";
                    return false;
                }
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
            List<Vector3> applianceTiles = applianceItems > 0 && !toHands ? FindFreeTiles(GetPostTiles(), applianceItems) : new List<Vector3>();
            int tilesNeeded = toHands ? 0 : applianceItems;
            List<Vector3> decorTiles = decorItems > 0 ? FindFreeTiles(GetPostTiles(force_inside: true), decorItems) : new List<Vector3>();
            if (applianceTiles.Count < tilesNeeded || decorTiles.Count < decorItems)
            {
                int free = applianceTiles.Count + decorTiles.Count;
                message = $"Not enough floor space for {tilesNeeded + decorItems} item(s), only {free} free spot(s). Open or move some parcels first.";
                return false;
            }

            int a = 0;
            int d = 0;
            var ectx = new EntityContext(EntityManager);
            foreach (KeyValuePair<(ShopEntryKind, int), int> kv in merged)
            {
                ShopEntry entry = catalog[kv.Key];
                if (toHands && entry.Kind == ShopEntryKind.Appliance)
                {
                    queue.Enqueue(buyer.Player, GetComponent<CPlayer>(buyer.Player), entry.ID, entry.Price, kv.Value);
                    continue;
                }
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
                        if (entry.Price > 0 && !ShopSettings.PrepareUninstall)
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
            if (toHands && applianceItems > 0)
            {
                message = (total > 0 ? $"Bought {count} item(s) for {total}." : $"Got {count} item(s) for free.")
                    + " Close the shop and they'll be handed to you to place."
                    + (decorItems > 0 ? " Decorations are delivered inside, as usual." : "");
                return true;
            }
            message = asBlueprints
                ? $"{count} blueprint(s) placed outside."
                : total > 0 ? $"Bought {count} item(s) for {total}. They're waiting by the door." : $"{count} item(s) delivered.";
            return true;
        }

        private bool ExecuteQueueToDoor(Entity session, out string message)
        {
            CShopSession s = EntityManager.GetComponentData<CShopSession>(session);
            BuildQueueSystem queue = World.GetExistingSystem<BuildQueueSystem>();
            if (queue == null || queue.CountWaiting(s.Player) == 0)
            {
                message = "Nothing is waiting to be placed.";
                return false;
            }
            int sent = queue.SendToDoor(s.Player, out int left);
            if (sent == 0)
            {
                message = "There's no free space by the door. Open or move some parcels first.";
                return false;
            }
            message = left > 0
                ? $"Sent {sent} item(s) to the door; there was no room for the other {left}. Open or move some parcels, then try again."
                : $"Sent {sent} item(s) to the door as parcels.";
            return true;
        }

        /// <summary>
        /// Build queue items to parcels on the letter tiles (with their price record), in list
        /// order, as many as there's room for. Returns how many were delivered.
        /// </summary>
        internal int DeliverAsParcels(List<BuildQueueSystem.QueuedItem> items)
        {
            if (items.Count == 0)
            {
                return 0;
            }
            List<Vector3> tiles = FindFreeTiles(GetPostTiles(), items.Count);
            for (int i = 0; i < tiles.Count; i++)
            {
                Entity parcel = PostHelpers.CreateApplianceParcel(EntityManager, tiles[i], items[i].ApplianceID);
                if (items[i].Price > 0 && !ShopSettings.PrepareUninstall)
                {
                    EntityManager.AddComponentData(parcel, new CPaidPrice { Price = items[i].Price });
                }
            }
            return tiles.Count;
        }

        private bool IsPending(int applianceId)
        {
            BuildQueueSystem queue = World.GetExistingSystem<BuildQueueSystem>();
            if (queue != null && queue.IsWaiting(applianceId))
            {
                return true;
            }
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

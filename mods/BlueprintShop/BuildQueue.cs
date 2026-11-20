using System.Collections.Generic;
using Controllers;
using Kitchen;
using KitchenMods;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace BlueprintShop
{
    /// <summary>
    /// Host: "place items myself" purchases. Instead of parcels, each bought appliance is put
    /// straight into the buyer's hands, one at a time, exactly the way vanilla hands you an
    /// appliance bought from a blueprint (PurchaseAfterDuration): an entity with
    /// CCreateAppliance + CHeldBy, which CreateNewAppliances turns into a held appliance.
    /// The player sets it down with the normal Grab, so every vanilla placement rule (walls,
    /// door, reachable tiles, the build-mode crane, auto-facing) applies untouched, and the
    /// next item arrives once their hands are empty.
    ///
    /// The third-action button (SecondaryAction1 / Interact3) swaps the held item for the next
    /// type in the queue. "Send the rest to the door" in the shop turns what's left into normal
    /// parcels, as does a player leaving. While anything is queued, the queue's view entity
    /// carries vanilla's CPreventStartDayPost, so the day can't start with paid items unplaced
    /// (vanilla already refuses while an appliance is being carried).
    ///
    /// The queue itself is host-side managed state (the mod loader only registers plain
    /// component types, not buffers). Systems are created per world, so it never outlives one.
    /// </summary>
    public class BuildQueueSystem : RestaurantSystem, IModSystem
    {
        public struct QueuedItem
        {
            public int ApplianceID;
            public int Price;
        }

        internal sealed class Queue
        {
            public Entity Player;
            public int PlayerID;
            public int InputSource;
            public Entity View;
            /// <summary>Not yet handed out.</summary>
            public readonly List<QueuedItem> Items = new List<QueuedItem>();
            /// <summary>Appliance types in the order they were bought, for cycling.</summary>
            public readonly List<int> TypeOrder = new List<int>();
            /// <summary>The queued item currently in the player's hands, if any.</summary>
            public Entity Held;
            public int HeldID;
            public int HeldPrice;
            /// <summary>Last type handed out: the next item is the same type while any are left.</summary>
            public int LastID;
            public bool CycleWasDown;
            public bool HandsBusy;
            public bool BlockingDay;
        }

        private readonly Dictionary<Entity, Queue> Queues = new Dictionary<Entity, Queue>();
        private readonly List<Entity> Finished = new List<Entity>();
        private ShopService Service;

        // ----------------------------------------------------------------- API (host)

        public void Enqueue(Entity player, CPlayer cPlayer, int applianceId, int price, int count)
        {
            if (count <= 0)
            {
                return;
            }
            if (!Queues.TryGetValue(player, out Queue q))
            {
                q = new Queue { Player = player, PlayerID = cPlayer.ID, InputSource = cPlayer.InputSource };
                q.View = EntityManager.CreateEntity();
                EntityManager.AddComponentData(q.View, new CBuildQueue { Player = player, PlayerID = cPlayer.ID, InputSource = cPlayer.InputSource });
                EntityManager.AddComponentData(q.View, new CPosition(Vector3.zero));
                EntityManager.AddComponentData(q.View, new CRequiresView { Type = ShopIds.QueueViewType, ViewMode = ViewMode.Screen });
                EntityManager.AddComponent<CDoNotPersist>(q.View);
                Queues[player] = q;
                ShopLog.Debug(() => $"Build queue created for player {cPlayer.ID}");
            }
            for (int i = 0; i < count; i++)
            {
                q.Items.Add(new QueuedItem { ApplianceID = applianceId, Price = price });
            }
            if (!q.TypeOrder.Contains(applianceId))
            {
                q.TypeOrder.Add(applianceId);
            }
            UpdateDayBlock(q);
        }

        /// <summary>Items still waiting (not counting one already in the player's hands).</summary>
        public int CountWaiting(Entity player) => Queues.TryGetValue(player, out Queue q) ? q.Items.Count : 0;

        /// <summary>Waiting items per appliance ID, across every player (for owned counts and "only one" checks).</summary>
        public void AddWaitingCounts(Dictionary<int, int> counts)
        {
            foreach (Queue q in Queues.Values)
            {
                foreach (QueuedItem item in q.Items)
                {
                    counts.TryGetValue(item.ApplianceID, out int n);
                    counts[item.ApplianceID] = n + 1;
                }
            }
        }

        public bool IsWaiting(int applianceId)
        {
            foreach (Queue q in Queues.Values)
            {
                foreach (QueuedItem item in q.Items)
                {
                    if (item.ApplianceID == applianceId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Turns the player's waiting items into parcels by the door, as many as there's room
        /// for. Returns how many were sent; the rest stay queued.
        /// </summary>
        public int SendToDoor(Entity player, out int left)
        {
            left = 0;
            if (!Queues.TryGetValue(player, out Queue q) || q.Items.Count == 0)
            {
                return 0;
            }
            Service = Service ?? World.GetExistingSystem<ShopService>();
            int sent = Service != null ? Service.DeliverAsParcels(q.Items) : 0;
            q.Items.RemoveRange(0, sent);
            left = q.Items.Count;
            UpdateDayBlock(q);
            return sent;
        }

        internal Queue Get(Entity view)
        {
            foreach (Queue q in Queues.Values)
            {
                if (q.View == view)
                {
                    return q;
                }
            }
            return null;
        }

        // ----------------------------------------------------------------- update

        protected override void OnUpdate()
        {
            if (Queues.Count == 0)
            {
                return;
            }
            Service = Service ?? World.GetExistingSystem<ShopService>();
            bool night = Has<SIsNightTime>();
            Finished.Clear();
            foreach (KeyValuePair<Entity, Queue> kv in Queues)
            {
                Queue q = kv.Value;
                if (!EntityManager.Exists(q.View) || !Require(q.View, out CBuildQueue marker) || marker.Player != q.Player)
                {
                    // The view entity is gone (shouldn't happen): nothing left to show or block with.
                    Finished.Add(kv.Key);
                    continue;
                }
                if (!EntityManager.Exists(q.Player) || !Has<CPlayer>(q.Player))
                {
                    PlayerLeft(q);
                    Finished.Add(kv.Key);
                    continue;
                }
                TrackHeld(q);
                if (night)
                {
                    HandleCycle(q);
                    HandOut(q);
                }
                if (q.Items.Count == 0 && q.Held == Entity.Null)
                {
                    Finished.Add(kv.Key);
                }
            }
            foreach (Entity player in Finished)
            {
                Queue q = Queues[player];
                Queues.Remove(player);
                if (EntityManager.Exists(q.View) && Has<CBuildQueue>(q.View))
                {
                    if (q.BlockingDay)
                    {
                        RequestWarningRecalculation();
                    }
                    EntityManager.DestroyEntity(q.View);
                }
                ShopLog.Debug(() => $"Build queue for player {q.PlayerID} finished");
            }
        }

        /// <summary>Forget the held item once it's been set down, sold, or swapped for something else.</summary>
        private void TrackHeld(Queue q)
        {
            if (q.Held == Entity.Null)
            {
                return;
            }
            if (!EntityManager.Exists(q.Held) || !Require(q.Held, out CHeldBy by) || by.Holder != q.Player)
            {
                q.Held = Entity.Null;
                q.HeldID = 0;
                q.HeldPrice = 0;
            }
        }

        private void HandleCycle(Queue q)
        {
            bool down = false;
            if (Require(q.Player, out CInputData input) && !input.IsCaptured && !input.IsDisconnected)
            {
                ButtonState b = input.State.SecondaryAction1;
                down = b == ButtonState.Pressed || b == ButtonState.Held;
            }
            bool pressed = down && !q.CycleWasDown;
            q.CycleWasDown = down;
            if (!pressed || q.Held == Entity.Null || !Has<CAppliance>(q.Held)
                || !Require(q.Player, out CItemHolder holder) || holder.HeldItem != q.Held)
            {
                return;
            }
            int next = NextType(q, q.HeldID);
            if (next == 0)
            {
                return;
            }
            int index = q.Items.FindIndex(i => i.ApplianceID == next);
            QueuedItem take = q.Items[index];
            q.Items.RemoveAt(index);
            q.Items.Add(new QueuedItem { ApplianceID = q.HeldID, Price = q.HeldPrice });
            int previous = q.HeldID;
            EntityManager.SetComponentData(q.Player, new CItemHolder { HeldItem = Entity.Null });
            EntityManager.DestroyEntity(q.Held);
            q.Held = Entity.Null;
            Give(q, take);
            ShopLog.Debug(() => $"Build queue (player {q.PlayerID}): swapped {previous} for {next}");
        }

        /// <summary>The next type after <paramref name="current"/> in purchase order that still has items waiting.</summary>
        private static int NextType(Queue q, int current)
        {
            int start = q.TypeOrder.IndexOf(current);
            for (int step = 1; step <= q.TypeOrder.Count; step++)
            {
                int id = q.TypeOrder[(start + step + q.TypeOrder.Count) % q.TypeOrder.Count];
                if (id != current && q.Items.Exists(i => i.ApplianceID == id))
                {
                    return id;
                }
            }
            return 0;
        }

        private void HandOut(Queue q)
        {
            q.HandsBusy = false;
            if (q.Held != Entity.Null || q.Items.Count == 0)
            {
                return;
            }
            if (!Require(q.Player, out CItemHolder holder))
            {
                return;
            }
            if (holder.HeldItem != Entity.Null && EntityManager.Exists(holder.HeldItem))
            {
                q.HandsBusy = true;
                return;
            }
            // Wait until the shop is closed: the item would otherwise appear behind the menu
            // (and in its "sell what you're holding" box).
            if (Service != null && Service.HasOpenSession(q.Player))
            {
                return;
            }
            int index = q.Items.FindIndex(i => i.ApplianceID == q.LastID);
            if (index < 0)
            {
                index = 0;
            }
            QueuedItem take = q.Items[index];
            q.Items.RemoveAt(index);
            Give(q, take);
        }

        /// <summary>Same entity setup as vanilla's PurchaseAfterDuration (a blueprint bought into the buyer's hands).</summary>
        private void Give(Queue q, QueuedItem item)
        {
            Entity e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, new CCreateAppliance { ID = item.ApplianceID });
            EntityManager.AddComponentData(e, CPosition.Hidden);
            EntityManager.AddComponentData(e, new CHeldBy { Holder = q.Player });
            EntityManager.AddComponent<CHeldAppliance>(e);
            EntityManager.AddComponentData(e, new CRequiresView { Type = ViewType.HeldAppliance });
            if (item.Price > 0 && !ShopSettings.PrepareUninstall)
            {
                EntityManager.AddComponentData(e, new CPaidPrice { Price = item.Price });
            }
            EntityManager.SetComponentData(q.Player, new CItemHolder { HeldItem = e });
            q.Held = e;
            q.HeldID = item.ApplianceID;
            q.HeldPrice = item.Price;
            q.LastID = item.ApplianceID;
            UpdateDayBlock(q);
        }

        /// <summary>
        /// A player left with items still queued: send them to the door as parcels; refund
        /// whatever doesn't fit. (What they were holding is handled by vanilla.)
        /// </summary>
        private void PlayerLeft(Queue q)
        {
            if (q.Items.Count == 0)
            {
                return;
            }
            int sent = Service != null ? Service.DeliverAsParcels(q.Items) : 0;
            int refund = 0;
            for (int i = sent; i < q.Items.Count; i++)
            {
                refund += q.Items[i].Price;
            }
            if (refund > 0)
            {
                Set(new SMoney { Amount = GetOrDefault<SMoney>().Amount + refund });
            }
            ShopLog.Info($"Player {q.PlayerID} left with {q.Items.Count} item(s) still to place: {sent} sent to the door as parcels, {refund} refunded for the rest.");
            q.Items.Clear();
        }

        /// <summary>Vanilla's "unopened post" blocker, on the queue's own entity, while anything is waiting.</summary>
        private void UpdateDayBlock(Queue q)
        {
            bool block = q.Items.Count > 0;
            if (block == q.BlockingDay || !EntityManager.Exists(q.View))
            {
                return;
            }
            if (block)
            {
                EntityManager.AddComponent<CPreventStartDayPost>(q.View);
            }
            else
            {
                EntityManager.RemoveComponent<CPreventStartDayPost>(q.View);
            }
            q.BlockingDay = block;
            RequestWarningRecalculation();
        }

        private void RequestWarningRecalculation()
        {
            EntityManager.CreateEntity(typeof(CRequireStartDayWarningRecalculation));
        }
    }

    /// <summary>Host: sends each queue's contents to its view (only the owner's machine shows it).</summary>
    public class BuildQueueViewSystem : IncrementalViewSystemBase<BuildQueueData>, IModSystem
    {
        private EntityQuery Views;
        private BuildQueueSystem Queues;
        private readonly Dictionary<int, int> Counts = new Dictionary<int, int>();

        protected override void Initialise()
        {
            base.Initialise();
            Views = GetEntityQuery(new QueryHelper().All(typeof(CBuildQueue), typeof(CLinkedView)));
        }

        protected override void OnUpdate()
        {
            if (Views.IsEmpty)
            {
                return;
            }
            Queues = Queues ?? World.GetExistingSystem<BuildQueueSystem>();
            if (Queues == null)
            {
                return;
            }
            using NativeArray<Entity> entities = Views.ToEntityArray(Allocator.Temp);
            using NativeArray<CBuildQueue> markers = Views.ToComponentDataArray<CBuildQueue>(Allocator.Temp);
            using NativeArray<CLinkedView> views = Views.ToComponentDataArray<CLinkedView>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                BuildQueueSystem.Queue q = Queues.Get(entities[i]);
                var data = new BuildQueueData
                {
                    PlayerID = markers[i].PlayerID,
                    InputSource = markers[i].InputSource,
                    Waiting = new List<OwnedCount>()
                };
                if (q != null)
                {
                    data.HeldID = q.Held != Entity.Null ? q.HeldID : 0;
                    data.HandsBusy = q.HandsBusy;
                    Counts.Clear();
                    foreach (BuildQueueSystem.QueuedItem item in q.Items)
                    {
                        Counts.TryGetValue(item.ApplianceID, out int n);
                        Counts[item.ApplianceID] = n + 1;
                    }
                    // Purchase order, so the list doesn't jump around as items are placed.
                    foreach (int id in q.TypeOrder)
                    {
                        if (Counts.TryGetValue(id, out int n))
                        {
                            data.Waiting.Add(new OwnedCount { ID = id, Count = n });
                        }
                    }
                }
                SendUpdate(views[i], data);
            }
        }
    }
}

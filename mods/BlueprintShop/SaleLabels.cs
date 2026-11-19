using System.Collections.Generic;
using Kitchen;
using KitchenMods;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace BlueprintShop
{
    /// <summary>
    /// Host: keeps one floating refund label per binned (outside) appliance that was bought
    /// through the shop, at night. Labels are separate entities (CSaleLabel + CRequiresView +
    /// CDoNotPersist) positioned on their appliance; the game syncs their position to every
    /// player. Nothing is added to the appliances, and vanilla's IndicatorManager (which allows
    /// one indicator per entity via CHasIndicator) isn't used, so table seat counts and other
    /// vanilla indicators are untouched.
    /// </summary>
    public class SaleLabelManager : RestaurantSystem, IModSystem
    {
        /// <summary>The sale list involves a scan of every appliance; a few updates a second is plenty.</summary>
        private const int UpdateEveryFrames = 6;

        private EntityQuery Labels;
        private ShopService Service;
        private int Frame;
        private readonly Dictionary<Entity, ShopService.OutsideItem> Desired = new Dictionary<Entity, ShopService.OutsideItem>();

        protected override void Initialise()
        {
            base.Initialise();
            Labels = GetEntityQuery(typeof(CSaleLabel));
        }

        protected override void OnUpdate()
        {
            bool night = Has<SIsNightTime>();
            if (!night && Labels.IsEmpty)
            {
                return;
            }
            if (night && ++Frame % UpdateEveryFrames != 0)
            {
                return;
            }

            Desired.Clear();
            if (night)
            {
                Service = Service ?? World.GetExistingSystem<ShopService>();
                if (Service != null)
                {
                    foreach (ShopService.OutsideItem item in Service.ComputeOutsideSale().Items)
                    {
                        Desired[item.Entity] = item;
                    }
                }
            }

            using (NativeArray<Entity> labels = Labels.ToEntityArray(Allocator.Temp))
            using (NativeArray<CSaleLabel> data = Labels.ToComponentDataArray<CSaleLabel>(Allocator.Temp))
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    CSaleLabel label = data[i];
                    if (!Desired.TryGetValue(label.Source, out ShopService.OutsideItem item)
                        || !Require(label.Source, out CPosition sourcePos))
                    {
                        EntityManager.DestroyEntity(labels[i]);
                        continue;
                    }
                    Desired.Remove(label.Source);
                    if (label.Refund != item.Refund || label.Required != item.Required)
                    {
                        label.Refund = item.Refund;
                        label.Required = item.Required;
                        EntityManager.SetComponentData(labels[i], label);
                    }
                    if (Require(labels[i], out CPosition pos) && pos.Position != sourcePos.Position)
                    {
                        EntityManager.SetComponentData(labels[i], new CPosition(sourcePos.Position));
                    }
                }
            }

            foreach (KeyValuePair<Entity, ShopService.OutsideItem> kv in Desired)
            {
                if (!Require(kv.Key, out CPosition sourcePos))
                {
                    continue;
                }
                Entity label = EntityManager.CreateEntity();
                EntityManager.AddComponentData(label, new CSaleLabel { Source = kv.Key, Refund = kv.Value.Refund, Required = kv.Value.Required });
                EntityManager.AddComponentData(label, new CPosition(sourcePos.Position));
                EntityManager.AddComponentData(label, new CRequiresView { Type = ShopIds.LabelViewType, ViewMode = ViewMode.World });
                EntityManager.AddComponentData(label, new CDoNotPersist());
            }
        }
    }

    /// <summary>Host: sends each label's refund to its view on every machine.</summary>
    public class SaleLabelViewSystem : IncrementalViewSystemBase<SaleLabelData>, IModSystem
    {
        private EntityQuery Labels;

        protected override void Initialise()
        {
            base.Initialise();
            Labels = GetEntityQuery(new QueryHelper().All(typeof(CSaleLabel), typeof(CLinkedView)));
        }

        protected override void OnUpdate()
        {
            if (Labels.IsEmpty)
            {
                return;
            }
            using NativeArray<CSaleLabel> labels = Labels.ToComponentDataArray<CSaleLabel>(Allocator.Temp);
            using NativeArray<CLinkedView> views = Labels.ToComponentDataArray<CLinkedView>(Allocator.Temp);
            for (int i = 0; i < labels.Length; i++)
            {
                SendUpdate(views[i], new SaleLabelData { Refund = labels[i].Refund, Required = labels[i].Required });
            }
        }
    }

    /// <summary>Every machine: the floating text over a binned appliance ("45 coins" or "Needed").</summary>
    public class SaleLabelView : UpdatableObjectView<SaleLabelData>
    {
        private const float Height = 1.55f;

        private TextMeshPro Text;
        private Camera MainCamera;

        public override void Initialise()
        {
            base.Initialise();
            // The router instantiates our (inactive) template; activate the instance.
            gameObject.SetActive(true);
        }

        protected override void UpdateData(SaleLabelData data)
        {
            if (Text == null)
            {
                Build();
            }
            if (data.Required)
            {
                Text.text = "Needed";
                Text.color = ShopTheme.Bad;
            }
            else
            {
                Text.text = ShopTheme.HasCoinSprite ? $"{data.Refund} <sprite name=\"coin\">" : $"${data.Refund}";
                Text.color = ShopTheme.Accent;
            }
        }

        private void Build()
        {
            var go = new GameObject("BlueprintShop Sale Label");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, Height, 0f);
            Text = go.AddComponent<TextMeshPro>();
            if (ShopTheme.Font != null)
            {
                Text.font = ShopTheme.Font;
            }
            if (ShopTheme.Sprites != null)
            {
                Text.spriteAsset = ShopTheme.Sprites;
            }
            Text.fontSize = 4.5f;
            Text.alignment = TextAlignmentOptions.Center;
            Text.enableWordWrapping = false;
            Text.richText = true;
            Text.rectTransform.sizeDelta = new Vector2(3f, 1f);
            Text.outlineWidth = 0.25f;
            Text.outlineColor = new Color32(0x1A, 0x16, 0x13, 0xFF);
            Text.sortingOrder = 100;
            Face();
        }

        private void LateUpdate()
        {
            if (Text != null)
            {
                Face();
            }
        }

        /// <summary>Billboard: face the same way as the camera so the text reads flat on screen.</summary>
        private void Face()
        {
            if (MainCamera == null)
            {
                MainCamera = Camera.main;
            }
            if (MainCamera != null)
            {
                Text.transform.rotation = MainCamera.transform.rotation;
            }
        }
    }

    /// <summary>
    /// Start of day, just before vanilla's DestroyAppliancesAtDay deletes everything outside:
    /// refund what was bought through the shop and is still outside, so nothing paid for is
    /// lost by forgetting to sell it. Items the menu needs (vanilla warned about them) aren't
    /// refunded, matching the shop's sell rules; vanilla still deletes them for $0.
    /// </summary>
    [UpdateBefore(typeof(DestroyAppliancesAtDay))]
    public class OutsideSaleRefund : StartOfDaySystem, IModSystem
    {
        protected override void OnUpdate()
        {
            ShopService service = World.GetExistingSystem<ShopService>();
            if (service == null)
            {
                return;
            }
            ShopService.OutsideSale sale = service.ComputeOutsideSale();
            if (sale.SellCount == 0)
            {
                return;
            }
            int money = GetOrDefault<SMoney>().Amount;
            Set(new SMoney { Amount = money + sale.Total });
            ShopLog.Info($"Day start: refunded {sale.Total} for {sale.SellCount} bought item(s) left outside (vanilla deletes them now).");
        }
    }
}

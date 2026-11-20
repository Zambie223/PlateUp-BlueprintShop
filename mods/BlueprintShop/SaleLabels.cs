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

    /// <summary>
    /// Every machine: the floating price over a binned appliance ("45 coin" or "Needed").
    /// It sits above vanilla's bin icon (at 1.55 it was centred on it), is auto-sized to fit
    /// within one tile so neighbouring labels never overlap, and reads against a dark rounded
    /// backing instead of a thick outline.
    /// </summary>
    public class SaleLabelView : UpdatableObjectView<SaleLabelData>
    {
        private const float Height = 2.25f;
        /// <summary>World units; a tile is 1, so this leaves a gap between neighbours.</summary>
        private const float MaxWidth = 0.8f;
        private const float MaxHeight = 0.3f;
        private static readonly Vector2 Padding = new Vector2(0.1f, 0.05f);

        /// <summary>
        /// Zoom compensation: the label keeps its world size while that's at least this many
        /// pixels tall (at 1080p; scaled with the screen height), and grows when the camera
        /// zooms out so it never gets smaller than this on screen.
        /// </summary>
        private const float MinScreenHeight1080 = 26f;
        private const float MaxScale = 2.5f;

        private Transform Root;
        private TextMeshPro Text;
        private SpriteRenderer Backing;
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
                // The coin sprite is a white shape; tint it like the shop window does.
                Text.text = ShopTheme.HasCoinSprite ? $"{data.Refund}<sprite name=\"coin\" color=#FF9800>" : $"${data.Refund}";
                Text.color = ShopTheme.Accent;
            }
            FitBacking();
        }

        private void Build()
        {
            Root = new GameObject("BlueprintShop Sale Label").transform;
            Root.SetParent(transform, false);
            Root.localPosition = new Vector3(0f, Height, 0f);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(Root, false);
            Text = textGo.AddComponent<TextMeshPro>();
            if (ShopTheme.Font != null)
            {
                Text.font = ShopTheme.Font;
            }
            if (ShopTheme.Sprites != null)
            {
                Text.spriteAsset = ShopTheme.Sprites;
            }
            Text.enableAutoSizing = true;
            Text.fontSizeMin = 0.5f;
            Text.fontSizeMax = 2.6f;
            Text.alignment = TextAlignmentOptions.Center;
            Text.enableWordWrapping = false;
            Text.overflowMode = TextOverflowModes.Overflow;
            Text.richText = true;
            Text.rectTransform.sizeDelta = new Vector2(MaxWidth - Padding.x * 2f, MaxHeight - Padding.y * 2f);
            Text.sortingOrder = 101;

            // Dark rounded backing behind the text (TMP faces -Z, so +Z is behind it).
            var backGo = new GameObject("Backing");
            backGo.transform.SetParent(Root, false);
            backGo.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            Backing = backGo.AddComponent<SpriteRenderer>();
            Backing.sprite = ShopTheme.Rounded;
            Backing.drawMode = SpriteDrawMode.Sliced;
            Backing.color = new Color(0.1f, 0.086f, 0.075f, 0.85f);
            Backing.sortingOrder = 100;
            Face();
        }

        /// <summary>Size the backing to the text actually drawn (auto-sizing shrinks long prices).</summary>
        private void FitBacking()
        {
            Text.ForceMeshUpdate();
            Vector2 drawn = Text.GetRenderedValues(false);
            if (float.IsNaN(drawn.x) || drawn.x <= 0f)
            {
                drawn = Text.rectTransform.sizeDelta;
            }
            Backing.size = new Vector2(Mathf.Min(drawn.x, MaxWidth) + Padding.x * 2f, drawn.y + Padding.y * 2f);
        }

        private void LateUpdate()
        {
            if (Root == null)
            {
                return;
            }
            // Per player: show_sale_labels only hides them on this machine.
            bool show = ShopSettings.ShowSaleLabels;
            if (Root.gameObject.activeSelf != show)
            {
                Root.gameObject.SetActive(show);
            }
            if (show)
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
            if (MainCamera == null)
            {
                return;
            }
            Transform cam = MainCamera.transform;
            Root.rotation = cam.rotation;

            // Pixels per world unit at the label's depth.
            float pixelsPerUnit;
            if (MainCamera.orthographic)
            {
                pixelsPerUnit = Screen.height / (2f * Mathf.Max(0.01f, MainCamera.orthographicSize));
            }
            else
            {
                Vector3 anchor = transform.position + Vector3.up * Height;
                float depth = Mathf.Max(0.1f, Vector3.Dot(anchor - cam.position, cam.forward));
                pixelsPerUnit = Screen.height / (2f * depth * Mathf.Tan(MainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            }
            float baseHeight = Backing != null && Backing.size.y > 0f ? Backing.size.y : MaxHeight;
            float minPixels = MinScreenHeight1080 * Screen.height / 1080f;
            float scale = Mathf.Clamp(minPixels / Mathf.Max(0.001f, baseHeight * pixelsPerUnit), 1f, MaxScale);
            Root.localScale = new Vector3(scale, scale, scale);
            // Grow upwards, so a bigger label doesn't cover the bin icon below it.
            Root.localPosition = new Vector3(0f, Height + (scale - 1f) * baseHeight * 0.5f, 0f);
        }
    }

    /// <summary>
    /// Start of day, just before vanilla's DestroyAppliancesAtDay deletes everything outside:
    /// refund what was bought through the shop and is still outside, so nothing paid for is
    /// lost by forgetting to sell it. Items the menu needs (vanilla warned about them) aren't
    /// refunded, matching the shop's sell rules; vanilla still deletes them for $0. The refund
    /// includes the sale loss setting (it's a sale), and refund_outside_at_day_start=false turns
    /// it off (vanilla behaviour).
    /// </summary>
    [UpdateBefore(typeof(DestroyAppliancesAtDay))]
    public class OutsideSaleRefund : StartOfDaySystem, IModSystem
    {
        protected override void OnUpdate()
        {
            if (!ShopSettings.RefundOutsideAtDayStart)
            {
                return;
            }
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

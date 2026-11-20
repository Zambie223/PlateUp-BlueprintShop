using System.Collections.Generic;
using System.Text;
using Controllers;
using Kitchen;
using KitchenData;
using Platforms;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlueprintShop
{
    /// <summary>
    /// Every machine: the on-screen panel listing what a player still has to place (build
    /// queue). Only the owner's machine draws anything. Several local players' panels stack
    /// up from the bottom centre of the screen.
    /// </summary>
    public class BuildQueueView : UpdatableObjectView<BuildQueueData>
    {
        private const float RefreshSeconds = 0.25f;

        private static readonly List<BuildQueueView> Visible = new List<BuildQueueView>();

        private BuildQueueData Data;
        private bool IsLocal;
        private GameObject CanvasRoot;
        private RectTransform PanelRect;
        private TextMeshProUGUI Text;
        private float NextRefresh;
        private int LayoutIndex = -1;

        public override void Initialise()
        {
            base.Initialise();
            // The router instantiates our (inactive) template; activate the instance.
            gameObject.SetActive(true);
        }

        protected override void UpdateData(BuildQueueData data)
        {
            Data = data;
            IsLocal = ShopView.IsLocalPlayer(data.PlayerID, data.InputSource);
            if (!IsLocal)
            {
                return;
            }
            if (CanvasRoot == null)
            {
                Build();
            }
            Render();
        }

        private void Build()
        {
            CanvasRoot = new GameObject("BlueprintShop Build Queue Canvas", typeof(RectTransform));
            CanvasRoot.transform.SetParent(transform, false);
            CanvasRoot.layer = 5;
            Canvas canvas = CanvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Below the shop menu (2000), so an open shop covers it.
            canvas.sortingOrder = 1500;
            CanvasScaler scaler = CanvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Image panel = Ui.Box(CanvasRoot.transform, "Panel", new Color(0.1f, 0.086f, 0.075f, 0.88f));
            PanelRect = panel.rectTransform;
            PanelRect.anchorMin = PanelRect.anchorMax = new Vector2(0.5f, 0f);
            PanelRect.pivot = new Vector2(0.5f, 0f);
            Ui.Group<VerticalLayoutGroup>(PanelRect, 0, new RectOffset(18, 18, 10, 12));
            ContentSizeFitter fit = PanelRect.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text = Ui.Text(PanelRect, "Text", "", 22, TextAlignmentOptions.Center);
            // Overflow, not Ellipsis: the game's body font draws nothing at all when an
            // ellipsised line doesn't fit (see the modding notes).
            Text.overflowMode = TextOverflowModes.Overflow;
            Text.enableWordWrapping = false;

            Visible.Add(this);
        }

        private void Update()
        {
            if (CanvasRoot == null)
            {
                return;
            }
            int index = Visible.IndexOf(this);
            if (index != LayoutIndex)
            {
                LayoutIndex = index;
                PanelRect.anchoredPosition = new Vector2(0f, 24f + index * 150f);
            }
            if (Time.unscaledTime >= NextRefresh)
            {
                // Some lines depend on local state (is the shop open, current key binding).
                Render();
            }
        }

        private void Render()
        {
            NextRefresh = Time.unscaledTime + RefreshSeconds;
            if (Text == null)
            {
                return;
            }
            bool shopOpen = ShopView.IsOpenLocallyFor(Data.PlayerID);
            CanvasRoot.SetActive(!shopOpen && (Data.HeldID != 0 || (Data.Waiting?.Count ?? 0) > 0));
            if (shopOpen)
            {
                return;
            }

            var sb = new StringBuilder("<b>Placing your purchases</b>");
            if (Data.HeldID != 0)
            {
                sb.Append($"\nHolding: <color=#FF9800>{Name(Data.HeldID)}</color>");
            }
            else if (Data.HandsBusy)
            {
                sb.Append("\n<color=#E05A47>Your hands are full. Put down what you're holding to get the next item.</color>");
            }
            int waitingTypes = Data.Waiting?.Count ?? 0;
            // Everything left to place, counting the one in the player's hands (user request
            // after 0.5.0: "Still to place" leaving out the held item read one short).
            var left = new List<OwnedCount>(Data.Waiting ?? new List<OwnedCount>());
            if (Data.HeldID != 0)
            {
                int at = left.FindIndex(c => c.ID == Data.HeldID);
                if (at >= 0)
                {
                    left[at] = new OwnedCount { ID = Data.HeldID, Count = left[at].Count + 1 };
                }
                else
                {
                    left.Insert(0, new OwnedCount { ID = Data.HeldID, Count = 1 });
                }
            }
            if (left.Count > 0)
            {
                sb.Append("\n<color=#BFB2A3>Left to place:</color> ");
                for (int i = 0; i < left.Count; i++)
                {
                    OwnedCount c = left[i];
                    sb.Append(i == 0 ? "" : ", ").Append(Name(c.ID));
                    if (c.Count > 1)
                    {
                        sb.Append($" x{c.Count}");
                    }
                }
            }
            var hints = new List<string> { "Grab to set it down" };
            bool otherType = false;
            if (Data.HeldID != 0 && Data.Waiting != null)
            {
                foreach (OwnedCount c in Data.Waiting)
                {
                    otherType |= c.ID != Data.HeldID;
                }
            }
            if (otherType)
            {
                hints.Insert(0, $"{ButtonName(Data.PlayerID)}: next item");
            }
            if (waitingTypes > 0)
            {
                hints.Add("the shop can send the rest to the door");
            }
            sb.Append("\n<size=80%><color=#BFB2A3>").Append(string.Join("  •  ", hints)).Append("</color></size>");
            // Undo (hold the third action): its own line, so the hint line doesn't get too wide.
            if (Data.UndoID != 0)
            {
                sb.Append(Data.UndoProgress > 0
                    ? $"\n<size=80%><color=#FF9800>Taking back {Name(Data.UndoID)}... {Data.UndoProgress}%</color></size>"
                    : $"\n<size=80%><color=#BFB2A3>Hold {ButtonName(Data.PlayerID)}: take back {Name(Data.UndoID)}</color></size>");
            }
            Text.text = sb.ToString();
        }

        private static string Name(int id) => GameData.Main.TryGet(id, out Appliance a) ? a.Name : $"#{id}";

        /// <summary>The player's own binding for the third action (rebindable), as a readable name.</summary>
        private static string ButtonName(int playerId)
        {
            BaseInputSource input = InputSourceIdentifier.DefaultInputSource;
            if (input == null)
            {
                return "Third action";
            }
            string path = input.GetBindingName(playerId, Controls.Interact3);
            if (string.IsNullOrEmpty(path) || path == "?")
            {
                return "Third action";
            }
            ControllerType type = input.GetCurrentController(playerId);
            bool ps = type == ControllerType.Playstation;
            bool nintendo = type == ControllerType.SwitchFull || type == ControllerType.SwitchJoyConL || type == ControllerType.SwitchJoyConR;
            switch (path)
            {
                case "buttonNorth": return ps ? "Triangle" : nintendo ? "X" : "Y";
                case "buttonSouth": return ps ? "Cross" : nintendo ? "B" : "A";
                case "buttonEast": return ps ? "Circle" : nintendo ? "A" : "B";
                case "buttonWest": return ps ? "Square" : nintendo ? "Y" : "X";
                case "leftShoulder": return ps ? "L1" : nintendo ? "L" : "LB";
                case "rightShoulder": return ps ? "R1" : nintendo ? "R" : "RB";
                case "leftTrigger": return ps ? "L2" : nintendo ? "ZL" : "LT";
                case "rightTrigger": return ps ? "R2" : nintendo ? "ZR" : "RT";
                case "leftButton": return "Left click";
                case "rightButton": return "Right click";
                case "middleButton": return "Middle click";
            }
            return path.Length == 1 ? path.ToUpperInvariant() : char.ToUpperInvariant(path[0]) + path.Substring(1);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Visible.Remove(this);
            if (CanvasRoot != null)
            {
                Destroy(CanvasRoot);
                CanvasRoot = null;
            }
        }
    }
}

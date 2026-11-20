using System;
using System.Collections.Generic;
using System.Text;
using Controllers;
using KitchenData;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BlueprintShop
{
    /// <summary>
    /// Two ways to fill the cart in one go (user requests 2026-11-19), and what they share:
    ///   - Copy from the floor: the menu hides, the player draws a box over the restaurant's
    ///     tiles, and every appliance standing in it is added to the cart. Drawn on the real
    ///     floor (mouse drag, or the controller cursor and two presses); the host counts what's
    ///     there (ShopAction.ScanArea), live while the box is drawn.
    ///   - Wishlists: named lists saved on this computer (Wishlists.cs). Save the cart as a
    ///     list; add a list back to the cart later.
    /// Both add on top of the cart, and skip and list what can't be bought tonight
    /// (AddItemsToCart). User choices: drawing on the restaurant, adding (not replacing),
    /// skip-and-list, wishlists stored on this computer, named lists.
    /// </summary>
    public partial class ShopView
    {
        // ================================================================= shared: add to cart

        private enum TextTarget
        {
            Search,
            WishName
        }

        /// <summary>Where typed text (keyboard or on-screen keyboard) goes.</summary>
        private TextTarget Typing = TextTarget.Search;

        private void AppendText(string s)
        {
            if (Typing == TextTarget.WishName)
            {
                if (WishName.Length < Wishlists.MaxNameLength)
                {
                    WishName += s;
                }
                return;
            }
            AppendSearch(s);
        }

        private void BackspaceText()
        {
            if (Typing == TextTarget.WishName)
            {
                if (WishName.Length > 0)
                {
                    WishName = WishName.Substring(0, WishName.Length - 1);
                }
                return;
            }
            BackspaceSearch();
        }

        private void ClearText()
        {
            if (Typing == TextTarget.WishName)
            {
                WishName = "";
                return;
            }
            SetSearch("");
        }

        /// <summary>
        /// Adds items on top of the cart. Anything not on sale tonight, or over a limit, is
        /// skipped; the status line says what was added and lists what was skipped, and why.
        /// </summary>
        private string AddItemsToCart(IEnumerable<(ShopEntryKind Kind, int ID, int Quantity)> items, string source)
        {
            int added = 0;
            // Reason -> (name, count), in the order first seen.
            var skipped = new List<(string reason, List<(string name, int count)> items)>();
            void Skip(string reason, string name, int count)
            {
                int r = skipped.FindIndex(s => s.reason == reason);
                if (r < 0)
                {
                    skipped.Add((reason, new List<(string, int)>()));
                    r = skipped.Count - 1;
                }
                skipped[r].items.Add((name, count));
            }

            foreach ((ShopEntryKind kind, int id, int qty) in items)
            {
                if (qty <= 0)
                {
                    continue;
                }
                var key = (kind, id);
                string name = Wishlists.ItemName(kind, id);
                if (!CatalogByKey.TryGetValue(key, out ShopEntry entry))
                {
                    Skip(WhyNotForSale(kind, id), name, qty);
                    continue;
                }
                CartQty.TryGetValue(key, out int have);
                bool unique = (entry.Flags & ShopEntryFlags.Unique) != 0;
                int max = unique ? 1 : ShopService.MaxQuantityPerLine;
                int take = Mathf.Clamp(max - have, 0, qty);
                if (take > 0)
                {
                    if (!CartQty.ContainsKey(key))
                    {
                        CartOrder.Add(key);
                    }
                    CartQty[key] = have + take;
                    added += take;
                }
                if (take < qty)
                {
                    Skip(unique ? "only one allowed" : $"the cart holds up to {ShopService.MaxQuantityPerLine} of each", name, qty - take);
                }
            }
            CartDirty = true;
            SaveCart();

            var sb = new StringBuilder();
            sb.Append(added > 0 ? $"Added {added} item{(added == 1 ? "" : "s")} from {source}." : $"Nothing from {source} could be added.");
            foreach ((string reason, List<(string name, int count)> list) in skipped)
            {
                sb.Append("\nSkipped (").Append(reason).Append("): ");
                for (int i = 0; i < list.Count && i < 4; i++)
                {
                    sb.Append(i == 0 ? "" : ", ").Append(list[i].name);
                    if (list[i].count > 1)
                    {
                        sb.Append(" x").Append(list[i].count);
                    }
                }
                if (list.Count > 4)
                {
                    sb.Append($" +{list.Count - 4} more");
                }
            }
            string message = sb.ToString();
            SetStatus(message, added > 0 ? (skipped.Count > 0 ? ShopTheme.Text : ShopTheme.Good) : ShopTheme.Bad);
            ShopLog.Info(message.Replace("\n", " "));
            return message;
        }

        /// <summary>Why something isn't in tonight's shop, as well as the menu can tell.</summary>
        private string WhyNotForSale(ShopEntryKind kind, int id)
        {
            if (kind == ShopEntryKind.Decor)
            {
                return Data.IsDecorNight ? "not on sale tonight" : "decoration nights only";
            }
            if (!GameData.Main.TryGet(id, out Appliance a))
            {
                return "unknown item";
            }
            if (!a.IsPurchasable && !a.IsPurchasableAsUpgrade)
            {
                return "not sold in the shop";
            }
            if (a.SellOnlyAsUnique && OwnedById.ContainsKey(id))
            {
                return "only one allowed";
            }
            if ((a.IsAnUpgrade || !a.IsPurchasable) && (Data.UpgradeState == UpgradeState.LockedUntilNight || Data.UpgradeState == UpgradeState.LockedRoll))
            {
                return "upgrades are locked";
            }
            if ((a.ShoppingTags & (ShoppingTags.Decoration | ShoppingTags.SpecialEvent)) != 0 && !Data.IsDecorNight)
            {
                return "decoration nights only";
            }
            return "not on sale tonight";
        }

        private static string Summary(IEnumerable<(ShopEntryKind Kind, int ID, int Quantity)> items, int max)
        {
            var sb = new StringBuilder();
            int shown = 0, more = 0;
            foreach ((ShopEntryKind kind, int id, int qty) in items)
            {
                if (shown >= max)
                {
                    more++;
                    continue;
                }
                sb.Append(shown == 0 ? "" : ", ").Append(Wishlists.ItemName(kind, id));
                if (qty > 1)
                {
                    sb.Append(" x").Append(qty);
                }
                shown++;
            }
            if (more > 0)
            {
                sb.Append($" +{more} more");
            }
            return sb.ToString();
        }

        // ================================================================= copy from the floor

        private const float AreaSendInterval = 0.15f;
        private const float AreaAnswerTimeout = 4f;

        private bool AreaMode;
        private bool AreaHasStart;
        private bool AreaDragging;
        private Vector2Int AreaStart;
        private Vector2Int AreaEnd;
        private Vector2Int? AreaHover;
        private int AreaSentSeq;
        private (int x0, int z0, int x1, int z1) AreaSentRect;
        private float AreaNextSend;
        private bool AreaConfirming;
        private float AreaDeadline;
        private RectTransform AreaHud;
        private TextMeshProUGUI AreaHudText;
        private AreaOverlay AreaGraphic;

        private void BuildAreaHud()
        {
            AreaGraphic = Ui.Rect("Area Overlay", CanvasRoot.transform).gameObject.AddComponent<AreaOverlay>();
            Ui.Stretch(AreaGraphic.rectTransform);
            AreaGraphic.raycastTarget = false;
            AreaGraphic.gameObject.SetActive(false);

            Image hud = Ui.Box(CanvasRoot.transform, "Area Hud", new Color(0.1f, 0.086f, 0.075f, 0.9f));
            AreaHud = hud.rectTransform;
            AreaHud.anchorMin = AreaHud.anchorMax = new Vector2(0.5f, 0f);
            AreaHud.pivot = new Vector2(0.5f, 0f);
            AreaHud.anchoredPosition = new Vector2(0f, 40f);
            Ui.Group<VerticalLayoutGroup>(AreaHud, 0, new RectOffset(22, 22, 12, 14));
            ContentSizeFitter fit = AreaHud.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            AreaHudText = Ui.Text(AreaHud, "Text", "", 22, TextAlignmentOptions.Center);
            AreaHudText.overflowMode = TextOverflowModes.Overflow; // see BuildQueueView: Ellipsis can blank the line
            AreaHudText.enableWordWrapping = false;
            AreaHud.gameObject.SetActive(false);
        }

        private void StartAreaMode()
        {
            if (AreaMode || !Built)
            {
                return;
            }
            SetSearchFocus(false);
            SetOsk(false);
            AreaMode = true;
            AreaHasStart = false;
            AreaDragging = false;
            AreaConfirming = false;
            AreaHover = null;
            AreaSentRect = (int.MinValue, 0, 0, 0);
            SlotArea.gameObject.SetActive(false);
            Tooltip.gameObject.SetActive(false);
            AreaGraphic.gameObject.SetActive(true);
            AreaHud.gameObject.SetActive(true);
            AreaHud.SetAsLastSibling();
            CursorImage.SetAsLastSibling();
            if (!UsesMouse)
            {
                VirtualCursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }
            AreaCamera.Begin(this); // top-down, pannable view while drawing
        }

        private void EndAreaMode()
        {
            if (!AreaMode)
            {
                return;
            }
            AreaMode = false;
            AreaDragging = false;
            AreaConfirming = false;
            AreaCamera.End(this);
            AreaGraphic.Clear();
            AreaGraphic.gameObject.SetActive(false);
            AreaHud.gameObject.SetActive(false);
            SlotArea.gameObject.SetActive(true);
            if (!UsesMouse)
            {
                VirtualCursorPlaced = false; // back to the middle of the menu
            }
        }

        private (int x0, int z0, int x1, int z1) AreaRect()
        {
            return (Math.Min(AreaStart.x, AreaEnd.x), Math.Min(AreaStart.y, AreaEnd.y),
                Math.Max(AreaStart.x, AreaEnd.x), Math.Max(AreaStart.y, AreaEnd.y));
        }

        private void SendArea((int x0, int z0, int x1, int z1) r)
        {
            int seq = NextSequence++;
            Outgoing.Enqueue(new ShopResponseData
            {
                Sequence = seq,
                Action = ShopAction.ScanArea,
                Lines = new List<ShopOrderLine>
                {
                    new ShopOrderLine { Kind = ShopEntryKind.Appliance, ID = r.x0, Quantity = r.z0 },
                    new ShopOrderLine { Kind = ShopEntryKind.Appliance, ID = r.x1, Quantity = r.z1 }
                }
            });
            AreaSentSeq = seq;
            AreaSentRect = r;
            AreaNextSend = Time.unscaledTime + AreaSendInterval;
        }

        /// <summary>The host's answer for the box as it is now, or null while it's on its way.</summary>
        private List<OwnedCount> CurrentAreaAnswer()
        {
            if (!AreaHasStart || AreaSentRect != AreaRect() || Data.AreaSequence != AreaSentSeq || Data.AreaItems == null)
            {
                return null;
            }
            return Data.AreaItems;
        }

        /// <summary>The floor tile under a screen point (the floor is the y = 0 plane), or null.</summary>
        private static Vector2Int? TileAt(Vector2 screen)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return null;
            }
            Ray ray = cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-4f)
            {
                return null;
            }
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f)
            {
                return null;
            }
            Vector3 p = ray.origin + ray.direction * t;
            return new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));
        }

        private void TickArea(InputState input)
        {
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            Vector2 pointer;
            bool press, held, release, back;
            if (UsesMouse && Mouse.current != null)
            {
                pointer = Mouse.current.position.ReadValue();
                press = Mouse.current.leftButton.wasPressedThisFrame;
                held = Mouse.current.leftButton.isPressed;
                release = Mouse.current.leftButton.wasReleasedThisFrame;
                back = Mouse.current.rightButton.wasPressedThisFrame || IsPressed(input.MenuCancel) || IsPressed(input.MenuTrigger);
            }
            else
            {
                Vector2 move = input.Movement;
                if (move.sqrMagnitude > 1f)
                {
                    move.Normalize();
                }
                float scale = Screen.height / 1080f;
                float speed = BaseCursorSpeed * (ShopSettings.CursorSpeedPercent / 100f) * scale * Mathf.Lerp(0.35f, 1f, move.magnitude);
                VirtualCursor += move * speed * dt;
                VirtualCursor.x = Mathf.Clamp(VirtualCursor.x, 0f, Screen.width);
                VirtualCursor.y = Mathf.Clamp(VirtualCursor.y, 0f, Screen.height);
                pointer = VirtualCursor;
                press = IsPressed(input.InteractAction) || IsPressed(input.GrabAction) || IsPressed(input.MenuSelect);
                held = false;
                release = false;
                back = IsPressed(input.MenuCancel) || IsPressed(input.MenuTrigger);
            }

            if (back)
            {
                if (!UsesMouse && AreaHasStart && !AreaConfirming)
                {
                    AreaHasStart = false; // controller: drop the first corner, keep going
                }
                else
                {
                    EndAreaMode();
                    SetStatus("Copy from the floor cancelled.", ShopTheme.TextDim);
                    return;
                }
            }

            // Moving the view (the shop has this player's input, so Easy Floor Planner's own
            // camera controls are paused): middle-drag, WASD and the wheel with a mouse; with a
            // controller, push the cursor against an edge of the screen.
            if (UsesMouse && Mouse.current != null)
            {
                if (Mouse.current.middleButton.isPressed)
                {
                    AreaCamera.PanPixels(Mouse.current.delta.ReadValue());
                }
                float wheel = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f)
                {
                    AreaCamera.Zoom(Mathf.Sign(wheel));
                }
                AreaCamera.PanScreen(input.Movement, 0.6f, dt);
            }
            else
            {
                float edge = Mathf.Min(Screen.width, Screen.height) * 0.06f;
                Vector2 push = Vector2.zero;
                if (edge > 0f)
                {
                    push.x = pointer.x < edge ? -(1f - pointer.x / edge) : pointer.x > Screen.width - edge ? (pointer.x - (Screen.width - edge)) / edge : 0f;
                    push.y = pointer.y < edge ? -(1f - pointer.y / edge) : pointer.y > Screen.height - edge ? (pointer.y - (Screen.height - edge)) / edge : 0f;
                }
                AreaCamera.PanScreen(push, 0.6f, dt);
            }

            AreaHover = TileAt(pointer);
            if (!AreaConfirming)
            {
                if (UsesMouse)
                {
                    if (press && AreaHover.HasValue)
                    {
                        AreaStart = AreaEnd = AreaHover.Value;
                        AreaHasStart = true;
                        AreaDragging = true;
                    }
                    else if (AreaDragging && held && AreaHover.HasValue)
                    {
                        AreaEnd = AreaHover.Value;
                    }
                    if (AreaDragging && release)
                    {
                        AreaDragging = false;
                        ConfirmArea();
                    }
                }
                else if (!AreaHasStart)
                {
                    if (press && AreaHover.HasValue)
                    {
                        AreaStart = AreaEnd = AreaHover.Value;
                        AreaHasStart = true;
                    }
                }
                else
                {
                    if (AreaHover.HasValue)
                    {
                        AreaEnd = AreaHover.Value;
                    }
                    if (press)
                    {
                        ConfirmArea();
                    }
                }
                if (AreaHasStart)
                {
                    // Keep the box within the size the host accepts.
                    int max = ShopService.MaxAreaSide - 1;
                    AreaEnd.x = Mathf.Clamp(AreaEnd.x, AreaStart.x - max, AreaStart.x + max);
                    AreaEnd.y = Mathf.Clamp(AreaEnd.y, AreaStart.y - max, AreaStart.y + max);
                }
            }

            // Live count: ask the host about the box whenever it changes (a few times a second).
            if (AreaHasStart && AreaRect() != AreaSentRect && (now >= AreaNextSend || AreaConfirming))
            {
                SendArea(AreaRect());
            }

            if (AreaConfirming)
            {
                List<OwnedCount> answer = CurrentAreaAnswer();
                if (answer != null)
                {
                    var items = new List<(ShopEntryKind, int, int)>();
                    foreach (OwnedCount c in answer)
                    {
                        items.Add((ShopEntryKind.Appliance, c.ID, c.Count));
                    }
                    (int x0, int z0, int x1, int z1) r = AreaRect();
                    EndAreaMode();
                    if (items.Count == 0)
                    {
                        SetStatus("There's nothing to copy in that area.", ShopTheme.Bad);
                    }
                    else
                    {
                        AddItemsToCart(items, $"the floor ({r.x1 - r.x0 + 1} x {r.z1 - r.z0 + 1} tiles)");
                    }
                    return;
                }
                if (now > AreaDeadline)
                {
                    EndAreaMode();
                    SetStatus("The host didn't answer, so nothing was copied. Try again.", ShopTheme.Bad);
                    return;
                }
            }

            UpdateAreaVisuals();
            UpdateCursorImage(pointer);
        }

        private void ConfirmArea()
        {
            AreaConfirming = true;
            AreaDeadline = Time.unscaledTime + AreaAnswerTimeout;
        }

        private void UpdateAreaVisuals()
        {
            // Tiles: the box, or the tile under the pointer before a corner is set.
            Camera cam = Camera.main;
            AreaGraphic.Clear();
            if (cam != null)
            {
                if (AreaHasStart)
                {
                    (int x0, int z0, int x1, int z1) r = AreaRect();
                    for (int x = r.x0; x <= r.x1; x++)
                    {
                        for (int z = r.z0; z <= r.z1; z++)
                        {
                            AddTile(cam, x, z, new Color(1f, 0.6f, 0f, 0.32f));
                        }
                    }
                    AddOutline(cam, r.x0 - 0.5f, r.z0 - 0.5f, r.x1 + 0.5f, r.z1 + 0.5f, new Color(1f, 0.6f, 0f, 0.95f));
                }
                else if (AreaHover.HasValue)
                {
                    Vector2Int t = AreaHover.Value;
                    AddTile(cam, t.x, t.y, new Color(1f, 0.6f, 0f, 0.25f));
                    AddOutline(cam, t.x - 0.5f, t.y - 0.5f, t.x + 0.5f, t.y + 0.5f, new Color(1f, 0.6f, 0f, 0.8f));
                }
            }
            AreaGraphic.SetVerticesDirty();

            var sb = new StringBuilder("<b>Copy from the floor</b>\n");
            if (!AreaHasStart)
            {
                sb.Append(UsesMouse
                    ? "Drag a box over the tiles you want to copy into your cart."
                    : "Move the cursor to one corner of the area and press Interact.");
            }
            else
            {
                (int x0, int z0, int x1, int z1) r = AreaRect();
                sb.Append($"{r.x1 - r.x0 + 1} x {r.z1 - r.z0 + 1} tiles: ");
                List<OwnedCount> answer = CurrentAreaAnswer();
                if (answer == null)
                {
                    sb.Append("<color=#BFB2A3>counting...</color>");
                }
                else if (answer.Count == 0)
                {
                    sb.Append("<color=#BFB2A3>nothing to copy</color>");
                }
                else
                {
                    var items = new List<(ShopEntryKind, int, int)>();
                    int total = 0, unavailable = 0;
                    foreach (OwnedCount c in answer)
                    {
                        items.Add((ShopEntryKind.Appliance, c.ID, c.Count));
                        total += c.Count;
                        if (!CatalogByKey.ContainsKey((ShopEntryKind.Appliance, c.ID)))
                        {
                            unavailable += c.Count;
                        }
                    }
                    sb.Append($"{total} item{(total == 1 ? "" : "s")}  <color=#BFB2A3>{Summary(items, 4)}</color>");
                    if (unavailable > 0)
                    {
                        sb.Append($"\n<color=#E05A47>{unavailable} not on sale tonight</color>");
                    }
                }
                if (AreaConfirming)
                {
                    sb.Append("\n<color=#BFB2A3>Copying...</color>");
                }
            }
            sb.Append("\n<size=80%><color=#BFB2A3>");
            if (UsesMouse)
            {
                sb.Append(AreaHasStart ? "Let go to add them to your cart  •  " : "").Append("Right-click or Esc: back to the shop");
                sb.Append("\nMiddle-drag or WASD: move the view  •  Wheel: zoom");
            }
            else
            {
                sb.Append(AreaHasStart ? "Interact: add them to your cart  •  Back: start the box again" : "Back: return to the shop");
                sb.Append("\nPush the cursor against an edge of the screen to move the view");
            }
            sb.Append("</color></size>");
            AreaHudText.text = sb.ToString();
        }

        private bool ToOverlay(Camera cam, float x, float z, out Vector2 local)
        {
            Vector3 screen = cam.WorldToScreenPoint(new Vector3(x, 0f, z));
            if (screen.z <= 0f)
            {
                local = default;
                return false;
            }
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(AreaGraphic.rectTransform, screen, null, out local);
        }

        private void AddTile(Camera cam, int x, int z, Color color)
        {
            const float inset = 0.45f; // a small gap between tiles, so they read as tiles
            if (ToOverlay(cam, x - inset, z - inset, out Vector2 a) && ToOverlay(cam, x + inset, z - inset, out Vector2 b)
                && ToOverlay(cam, x + inset, z + inset, out Vector2 c) && ToOverlay(cam, x - inset, z + inset, out Vector2 d))
            {
                AreaGraphic.AddQuad(a, b, c, d, color);
            }
        }

        private void AddOutline(Camera cam, float x0, float z0, float x1, float z1, Color color)
        {
            if (ToOverlay(cam, x0, z0, out Vector2 a) && ToOverlay(cam, x1, z0, out Vector2 b)
                && ToOverlay(cam, x1, z1, out Vector2 c) && ToOverlay(cam, x0, z1, out Vector2 d))
            {
                AreaGraphic.AddLine(a, b, 3f, color);
                AreaGraphic.AddLine(b, c, 3f, color);
                AreaGraphic.AddLine(c, d, 3f, color);
                AreaGraphic.AddLine(d, a, 3f, color);
            }
        }

        // ================================================================= wishlists

        private const int WishRowsPerPage = 6;
        private const float WishConfirmSeconds = 3f;

        private bool WishOpen;
        private string WishName = "";
        private int WishPage;
        private RectTransform WishPanel;
        private UiButton WishNameBox;
        private UiButton WishSaveButton;
        private TextMeshProUGUI WishInfo;
        private TextMeshProUGUI WishPageText;
        private UiButton WishPrev;
        private UiButton WishNext;
        private readonly List<UiButton> WishButtons = new List<UiButton>();
        private readonly List<WishRow> WishRows = new List<WishRow>();
        /// <summary>Update/Delete ask for a second click: which list, which button, until when.</summary>
        private (Wishlist list, string action, float until) WishConfirm;

        private class WishRow
        {
            public RectTransform Root;
            public TextMeshProUGUI Label;
            public UiButton Add;
            public UiButton Update;
            public UiButton Delete;
            public Wishlist List;
        }

        private void BuildWishPanel()
        {
            Image bg = Ui.Box(SlotArea, "Wishlists", ShopTheme.Panel);
            // Fills the player's part of the screen (half or a quarter of it in local co-op),
            // inset so the shop shows around it.
            WishPanel = Ui.Stretch(bg.rectTransform, 110, 110, 90, 90);
            Ui.Group<VerticalLayoutGroup>(WishPanel, 10, new RectOffset(20, 20, 16, 18)).childForceExpandWidth = true;

            RectTransform header = Ui.Rect("Header", WishPanel);
            Ui.Layout(header, minH: 48, prefH: 48, flexH: 0);
            Ui.Group<HorizontalLayoutGroup>(header, 10, null, TextAnchor.MiddleLeft).childForceExpandHeight = true;
            TextMeshProUGUI title = Ui.Text(header, "Title", "Wishlists", 32);
            Ui.Layout(title, flexW: 1);
            UiButton close = Ui.Button(header, "Close", "X", 26, () => SetWishOpen(false));
            close.Normal = ShopTheme.Bad;
            close.Hover = new Color(1f, 0.45f, 0.35f);
            close.Refresh();
            Ui.Layout(close.Rect, minW: 56, prefW: 56);
            WishButtons.Add(close);

            RectTransform saveRow = Ui.Rect("SaveRow", WishPanel);
            Ui.Layout(saveRow, minH: 46, prefH: 46, flexH: 0);
            Ui.Group<HorizontalLayoutGroup>(saveRow, 8, null, TextAnchor.MiddleLeft).childForceExpandHeight = true;
            WishNameBox = Ui.Button(saveRow, "Name", "", 20, OnWishNameClicked);
            WishNameBox.Normal = ShopTheme.PanelInner;
            WishNameBox.Hover = ShopTheme.CardHover;
            WishNameBox.Refresh();
            WishNameBox.Label.alignment = TextAlignmentOptions.Left;
            Ui.Stretch(WishNameBox.Label.rectTransform, 14, 14, 2, 2);
            Ui.Layout(WishNameBox.Rect, flexW: 1);
            WishButtons.Add(WishNameBox);
            WishSaveButton = Ui.Button(saveRow, "Save", "Save cart as a new list", 20, SaveCartAsWishlist);
            WishSaveButton.Normal = ShopTheme.Good;
            WishSaveButton.Hover = ShopTheme.GoodHover;
            WishSaveButton.Refresh();
            Ui.Layout(WishSaveButton.Rect, minW: 290, prefW: 290);
            WishButtons.Add(WishSaveButton);

            WishInfo = Ui.Text(WishPanel, "Info", "", 18, TextAlignmentOptions.TopLeft, ShopTheme.TextDim);
            Ui.AutoSize(WishInfo, 12);
            Ui.Layout(WishInfo, minH: 48, prefH: 48, flexH: 0);

            for (int i = 0; i < WishRowsPerPage; i++)
            {
                var row = new WishRow();
                Image rowBg = Ui.Box(WishPanel, "Row", ShopTheme.Card);
                row.Root = rowBg.rectTransform;
                Ui.Layout(row.Root, minH: 70, prefH: 70, flexW: 1, flexH: 0);
                Ui.Group<HorizontalLayoutGroup>(row.Root, 8, new RectOffset(14, 10, 8, 8), TextAnchor.MiddleLeft).childForceExpandHeight = true;
                row.Label = Ui.Text(row.Root, "Label", "", 20, TextAlignmentOptions.Left);
                row.Label.enableWordWrapping = false;
                Ui.AutoSize(row.Label, 12);
                Ui.Layout(row.Label, flexW: 1);
                WishRow captured = row;
                row.Add = Ui.Button(row.Root, "Add", "Add to cart", 18, () => LoadWishlist(captured.List));
                row.Add.Normal = ShopTheme.Good;
                row.Add.Hover = ShopTheme.GoodHover;
                row.Add.Refresh();
                Ui.Layout(row.Add.Rect, minW: 150, prefW: 150);
                row.Update = Ui.Button(row.Root, "Update", "Replace", 18, () => ConfirmWish(captured.List, "update"));
                Ui.Layout(row.Update.Rect, minW: 130, prefW: 130);
                row.Delete = Ui.Button(row.Root, "Delete", "Delete", 18, () => ConfirmWish(captured.List, "delete"));
                row.Delete.Normal = new Color(0.55f, 0.27f, 0.22f);
                row.Delete.Hover = ShopTheme.Bad;
                row.Delete.Refresh();
                Ui.Layout(row.Delete.Rect, minW: 110, prefW: 110);
                WishButtons.Add(row.Add);
                WishButtons.Add(row.Update);
                WishButtons.Add(row.Delete);
                WishRows.Add(row);
            }

            RectTransform pager = Ui.Rect("Pager", WishPanel);
            Ui.Layout(pager, minH: 44, prefH: 44, flexH: 0);
            Ui.Group<HorizontalLayoutGroup>(pager, 10, null, TextAnchor.MiddleCenter).childForceExpandHeight = true;
            WishPrev = Ui.Button(pager, "Prev", "<", 22, () => { WishPage--; RefreshWishPanel(); });
            Ui.Layout(WishPrev.Rect, minW: 70, prefW: 70);
            WishPageText = Ui.Text(pager, "Page", "", 20, TextAlignmentOptions.Center, ShopTheme.TextDim);
            Ui.Layout(WishPageText, minW: 220, prefW: 220);
            WishNext = Ui.Button(pager, "Next", ">", 22, () => { WishPage++; RefreshWishPanel(); });
            Ui.Layout(WishNext.Rect, minW: 70, prefW: 70);
            WishButtons.Add(WishPrev);
            WishButtons.Add(WishNext);

            WishPanel.gameObject.SetActive(false);
        }

        private void SetWishOpen(bool open)
        {
            if (open == WishOpen || WishPanel == null)
            {
                return;
            }
            WishOpen = open;
            SetOsk(false);
            SetSearchFocus(false);
            Typing = TextTarget.Search;
            WishConfirm = default;
            WishPanel.gameObject.SetActive(open);
            if (open)
            {
                WishPanel.SetAsLastSibling();
                WishPage = 0;
                SetWishInfo(null);
                RefreshWishPanel();
            }
        }

        private void OnWishNameClicked()
        {
            Typing = TextTarget.WishName;
            if (UsesMouse)
            {
                SetSearchFocus(true);
            }
            else
            {
                SetOsk(!OskOpen);
            }
        }

        private void UpdateWishNameLabel()
        {
            if (!WishOpen || WishNameBox == null)
            {
                return;
            }
            bool active = Typing == TextTarget.WishName && (SearchFocused || OskOpen);
            string caret = active && (Time.unscaledTime % 1f) < 0.5f ? "|" : "";
            WishNameBox.Label.text = WishName.Length == 0
                ? (active ? caret : "<color=#BFB2A3>Name for a new list (optional)</color>")
                : WishName + caret;
            WishSaveButton.Interactable = CartOrder.Count > 0;
            WishSaveButton.Refresh();
            if (WishConfirm.list != null && Time.unscaledTime > WishConfirm.until)
            {
                WishConfirm = default;
                RefreshWishPanel();
            }
        }

        private void SetWishInfo(string message)
        {
            WishInfo.text = message ?? "Lists are kept on this computer and work in every run. Save the cart as a list, then add a list back to the cart on any night. Mod List's options can export and import them.";
        }

        private List<(ShopEntryKind, int, int)> CartItems()
        {
            var items = new List<(ShopEntryKind, int, int)>();
            foreach ((ShopEntryKind kind, int id) in CartOrder)
            {
                items.Add((kind, id, CartQty[(kind, id)]));
            }
            return items;
        }

        private void SaveCartAsWishlist()
        {
            if (CartOrder.Count == 0)
            {
                SetWishInfo("Your cart is empty: add items first, then save them as a list.");
                return;
            }
            if (Wishlists.All.Count >= Wishlists.MaxLists)
            {
                SetWishInfo($"You have {Wishlists.MaxLists} lists already. Delete one to make room.");
                return;
            }
            var list = new Wishlist { Name = Wishlists.UniqueName(WishName) };
            list.Items.AddRange(CartItems());
            Wishlists.Add(list);
            SetSearchFocus(false);
            SetOsk(false);
            WishName = "";
            WishPage = 0;
            // Newest first in the panel, so the new list is on the first page.
            SetWishInfo($"Saved \"{list.Name}\" ({list.TotalQuantity} item{(list.TotalQuantity == 1 ? "" : "s")}).");
            RefreshWishPanel();
        }

        private void LoadWishlist(Wishlist list)
        {
            if (list == null)
            {
                return;
            }
            SetWishOpen(false);
            AddItemsToCart(list.Items, $"\"{list.Name}\"");
        }

        private void ConfirmWish(Wishlist list, string action)
        {
            if (list == null)
            {
                return;
            }
            if (WishConfirm.list != list || WishConfirm.action != action || Time.unscaledTime > WishConfirm.until)
            {
                WishConfirm = (list, action, Time.unscaledTime + WishConfirmSeconds);
                SetWishInfo(action == "delete"
                    ? $"Click Delete again to delete \"{list.Name}\"."
                    : $"Click Replace again to replace \"{list.Name}\" with what's in your cart now.");
                RefreshWishPanel();
                return;
            }
            WishConfirm = default;
            if (action == "delete")
            {
                Wishlists.Remove(list);
                SetWishInfo($"Deleted \"{list.Name}\".");
            }
            else
            {
                if (CartOrder.Count == 0)
                {
                    SetWishInfo("Your cart is empty, so there's nothing to replace the list with.");
                    RefreshWishPanel();
                    return;
                }
                list.Items.Clear();
                list.Items.AddRange(CartItems());
                Wishlists.Save();
                SetWishInfo($"\"{list.Name}\" now holds what's in your cart ({list.TotalQuantity} item{(list.TotalQuantity == 1 ? "" : "s")}).");
            }
            RefreshWishPanel();
        }

        private void RefreshWishPanel()
        {
            if (WishPanel == null)
            {
                return;
            }
            // Newest first.
            List<Wishlist> all = Wishlists.All;
            int pages = Mathf.Max(1, (all.Count + WishRowsPerPage - 1) / WishRowsPerPage);
            WishPage = Mathf.Clamp(WishPage, 0, pages - 1);
            for (int i = 0; i < WishRows.Count; i++)
            {
                WishRow row = WishRows[i];
                int index = all.Count - 1 - (WishPage * WishRowsPerPage + i);
                row.List = index >= 0 ? all[index] : null;
                row.Root.gameObject.SetActive(row.List != null || (all.Count == 0 && i == 0));
                if (row.List == null)
                {
                    row.Label.text = all.Count == 0 && i == 0 ? "<color=#BFB2A3>No lists yet. Fill the cart, then save it as a list.</color>" : "";
                    row.Add.Rect.gameObject.SetActive(false);
                    row.Update.Rect.gameObject.SetActive(false);
                    row.Delete.Rect.gameObject.SetActive(false);
                    continue;
                }
                row.Add.Rect.gameObject.SetActive(true);
                row.Update.Rect.gameObject.SetActive(true);
                row.Delete.Rect.gameObject.SetActive(true);
                row.Label.text = $"<b>{row.List.Name}</b>  <size=80%><color=#BFB2A3>{row.List.TotalQuantity} item{(row.List.TotalQuantity == 1 ? "" : "s")}</color></size>\n<size=80%><color=#BFB2A3>{Summary(row.List.Items, 5)}</color></size>";
                bool confirming = WishConfirm.list == row.List && Time.unscaledTime <= WishConfirm.until;
                row.Update.Label.text = confirming && WishConfirm.action == "update" ? "Sure?" : "Replace";
                row.Delete.Label.text = confirming && WishConfirm.action == "delete" ? "Sure?" : "Delete";
                row.Update.Interactable = CartOrder.Count > 0;
                row.Update.Refresh();
            }
            WishPageText.text = $"Page {WishPage + 1} of {pages}";
            WishPrev.Interactable = WishPage > 0;
            WishPrev.Refresh();
            WishNext.Interactable = WishPage < pages - 1;
            WishNext.Refresh();
            LayoutRebuilder.ForceRebuildLayoutImmediate(WishPanel);
        }
    }

    /// <summary>
    /// Screen-space shapes for "copy from the floor": the selected tiles and the box outline,
    /// projected from the floor each frame, drawn on the shop's overlay canvas (so they show
    /// over appliances and need no world-space material).
    /// </summary>
    internal class AreaOverlay : MaskableGraphic
    {
        private readonly List<(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)> Quads = new List<(Vector2, Vector2, Vector2, Vector2, Color)>();

        public void Clear() => Quads.Clear();

        public void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color) => Quads.Add((a, b, c, d, color));

        public void AddLine(Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 dir = to - from;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return;
            }
            Vector2 n = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);
            Vector2 ext = dir.normalized * (width * 0.5f); // overlap at the corners
            Quads.Add((from - ext - n, to + ext - n, to + ext + n, from - ext + n, color));
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach ((Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color) in Quads)
            {
                int i = vh.currentVertCount;
                vh.AddVert(a, color, Vector2.zero);
                vh.AddVert(b, color, Vector2.zero);
                vh.AddVert(c, color, Vector2.zero);
                vh.AddVert(d, color, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2);
                vh.AddTriangle(i, i + 2, i + 3);
            }
        }
    }
}

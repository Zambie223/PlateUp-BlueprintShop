using System;
using System.Collections.Generic;
using System.Text;
using Controllers;
using Kitchen;
using KitchenData;
using Platforms;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BlueprintShop
{
    /// <summary>
    /// The shop menu. One instance exists on every machine for each open session (the game
    /// creates views everywhere), but only the machine where the owning player is local builds
    /// any UI or reads input. Everything else stays dormant.
    ///
    /// Input: the view registers as an IInputConsumer and consumes the owner's input while open,
    /// and holds a NonPause lock on that player, so the owner's chef stands still and other local
    /// players keep playing. Keyboard players
    /// use the mouse (and can type to search); controller players get a stick-driven cursor that
    /// snaps to nearby buttons, plus an on-screen QWERTY keyboard for search.
    /// </summary>
    public class ShopView : ResponsiveObjectView<ShopViewData, ShopResponseData>, IInputConsumer
    {
        // ================================================================= categories

        private enum Category
        {
            All,
            Cooking,
            Automation,
            Plumbing,
            FrontOfHouse,
            Technology,
            Office,
            Essentials,
            Misc,
            Decorations,
            Events,
            Surfaces
        }

        private static readonly Dictionary<Category, string> CategoryNames = new Dictionary<Category, string>
        {
            { Category.All, "All" },
            { Category.Cooking, "Cooking" },
            { Category.Automation, "Automation" },
            { Category.Plumbing, "Plumbing" },
            { Category.FrontOfHouse, "Front of House" },
            { Category.Technology, "Technology" },
            { Category.Office, "Office" },
            { Category.Essentials, "Essentials" },
            { Category.Misc, "Misc" },
            { Category.Decorations, "Decorations" },
            { Category.Events, "Events" },
            { Category.Surfaces, "Walls & Floors" }
        };

        /// <summary>Upgrade -> the appliance it upgrades, built once from game data.</summary>
        private static Dictionary<int, int> _upgradeBase;

        // ================================================================= constants

        private static readonly List<ShopView> OpenLocal = new List<ShopView>();

        /// <summary>Cart contents per local player, kept until the day changes (memory only).</summary>
        private static readonly Dictionary<int, SavedCart> SavedCarts = new Dictionary<int, SavedCart>();

        static ShopView()
        {
            // Day numbers repeat between runs; forget saved carts whenever the scene changes
            // (leaving a run, starting another) so an old run's cart can't reappear.
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += (from, to) => SavedCarts.Clear();
        }

        private class SavedCart
        {
            public int Day;
            public List<((ShopEntryKind, int) key, int qty)> Lines = new List<((ShopEntryKind, int), int)>();
        }

        private class Card
        {
            public RectTransform Root;
            public ShopEntry Entry;
            public Category Category;
            public string SearchText;
            public string Name;
            public TextMeshProUGUI Price;
            public TextMeshProUGUI Owned;
            public UiButton Add;
            public UiButton Blueprint;
        }

        private const float CardWidth = 180f;
        private const float CardHeight = 244f;
        private const int SnapshotsPerFrame = 3;
        private const float CursorSpeed = 1100f; // reference pixels per second at full stick
        private const float SnapRadius = 44f;    // reference pixels: aim-assist reach
        private const float CloseRetrySeconds = 2f;
        private const float SellConfirmSeconds = 3f;
        private const int MaxSearchLength = 32;

        // ================================================================= state

        private ShopViewData Data;
        private bool HasData;
        private bool IsLocalOwner;
        private bool Built;
        private bool ClosedLocally;
        private float CloseRequestedAt;
        private int RenderedCatalogHash;
        private int RenderedOwnedHash;
        private int RenderedMoney = int.MinValue;
        private (HeldKind, int, int, ShopStateFlags) RenderedHeld = (HeldKind.None, -1, -1, ShopStateFlags.None);
        private Category ActiveCategory = Category.All;
        private readonly Queue<ShopResponseData> Outgoing = new Queue<ShopResponseData>();
        private int NextSequence = 1;
        private int AwaitingSequence;
        private ShopAction AwaitingAction;
        private int RenderedAck = -1;
        private float SellConfirmUntil;

        private readonly Dictionary<(ShopEntryKind, int), ShopEntry> CatalogByKey = new Dictionary<(ShopEntryKind, int), ShopEntry>();
        private readonly Dictionary<int, int> OwnedById = new Dictionary<int, int>();
        private readonly Dictionary<(ShopEntryKind, int), int> CartQty = new Dictionary<(ShopEntryKind, int), int>();
        private readonly List<(ShopEntryKind, int)> CartOrder = new List<(ShopEntryKind, int)>();
        private bool CartDirty = true;
        private bool CartRestored;

        private string Search = "";
        private bool SearchFocused;   // keyboard typing
        private bool OskOpen;         // controller on-screen keyboard
        private bool FilterDirty = true;

        // ---- input
        private InputState LastInput;
        private bool HasFreshInput;
        // Per-player NonPause lock held while the menu is open, like vanilla's dish/costume menus.
        // Consuming input alone only neutralises the ECS input: the local chef's walking
        // (PlayerWalkingComponent) reads the raw state via GetCurrentInputData, which only a lock
        // blanks. The lock also tells other mods this player is in a menu.
        private InputLock.Lock PlayerLock;
        private bool HoldsPlayerLock;
        private Vector2 VirtualCursor;
        private bool VirtualCursorPlaced;
        private bool UsesMouse;
        private bool CursorModeApplied;
        private bool? SavedCursorVisible;
        private CursorLockMode SavedCursorLock;
        private UiButton LastHovered;
        private readonly Queue<char> TypedChars = new Queue<char>();
        private Keyboard SubscribedKeyboard;

        // ---- UI
        private GameObject CanvasRoot;
        private RectTransform SlotArea;
        private RectTransform Panel;
        private TextMeshProUGUI TitleText;
        private TextMeshProUGUI MoneyText;
        private TextMeshProUGUI SubtotalText;
        private TextMeshProUGUI HintText;
        private UiButton SearchBox;
        private TextMeshProUGUI SearchLabel;
        private UiButton SearchClear;
        private RectTransform TabsContainer;
        private RectTransform GridViewport;
        private RectTransform GridContent;
        private TextMeshProUGUI EmptyText;
        private RectTransform SellBox;
        private TextMeshProUGUI SellText;
        private UiButton SellButton;
        private RectTransform CartViewport;
        private RectTransform CartContent;
        private TextMeshProUGUI TotalText;
        private TextMeshProUGUI StatusText;
        private UiButton BuyButton;
        private UiButton ClearButton;
        private RectTransform Osk;
        private RectTransform Tooltip;
        private TextMeshProUGUI TooltipText;
        private RectTransform CursorImage;
        private float GridScroll;
        private float CartScroll;

        private readonly List<UiButton> StaticButtons = new List<UiButton>();
        private readonly List<UiButton> TabButtons = new List<UiButton>();
        private readonly List<UiButton> CardButtons = new List<UiButton>();
        private readonly List<UiButton> CartButtons = new List<UiButton>();
        private readonly List<UiButton> OskButtons = new List<UiButton>();
        private readonly List<Card> Cards = new List<Card>();
        private readonly List<(RectTransform rect, ShopEntry entry)> CartLineRects = new List<(RectTransform, ShopEntry)>();
        private readonly Queue<(RawImage image, ShopEntry entry)> PendingIcons = new Queue<(RawImage, ShopEntry)>();
        private static readonly Vector3[] Corners = new Vector3[4];

        // ================================================================= view lifecycle

        public override void Initialise()
        {
            base.Initialise();
            // The router instantiates our (inactive) template; activate the instance.
            gameObject.SetActive(true);
        }

        protected override void UpdateData(ShopViewData data)
        {
            Data = data;
            if (!HasData)
            {
                HasData = true;
                IsLocalOwner = DetermineLocalOwner(data);
                ShopLog.Debug(() => $"Shop view for player {data.OwnerPlayerID} (source {data.OwnerInputSource}); local source {(int)InputSourceIdentifier.Identifier}; local owner: {IsLocalOwner}");
                if (IsLocalOwner)
                {
                    try
                    {
                        Build();
                    }
                    catch (Exception e)
                    {
                        ShopLog.Error("Failed to build the shop menu", e);
                        RequestClose();
                    }
                }
            }
        }

        private static bool DetermineLocalOwner(ShopViewData data)
        {
            BaseInputSource input = InputSourceIdentifier.DefaultInputSource;
            if (input == null || data.OwnerInputSource != (int)InputSourceIdentifier.Identifier)
            {
                return false;
            }
            return input.GetCurrentController(data.OwnerPlayerID) != ControllerType.None;
        }

        public override bool HasStateUpdate(out IResponseData state)
        {
            if (Outgoing.Count > 0)
            {
                ShopResponseData response = Outgoing.Dequeue();
                ShopLog.Debug(() => $"Sending {response.Action} #{response.Sequence}");
                state = response;
                return true;
            }
            state = null;
            return false;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            TearDown();
        }

        private void TearDown()
        {
            SaveCart();
            SetSearchFocus(false);
            LocalInputSourceConsumers.Remove(this);
            ReleasePlayerLock();
            OpenLocal.Remove(this);
            RestoreCursor();
            if (CanvasRoot != null)
            {
                Destroy(CanvasRoot);
                CanvasRoot = null;
            }
            if (Built && IsLocalOwner)
            {
                // Don't let the closing click/press (or a still-held button) reach the game.
                InputDrain.Begin(Data.OwnerPlayerID, UsesMouse);
            }
            Built = false;
        }

        /// <summary>True while this machine shows an open shop window for the given player.</summary>
        public static bool IsOpenLocallyFor(int playerId)
        {
            foreach (ShopView view in OpenLocal)
            {
                if (view != null && view.Built && !view.ClosedLocally && view.Data.OwnerPlayerID == playerId)
                {
                    return true;
                }
            }
            return false;
        }

        // ================================================================= input

        public InputConsumerState TakeInput(int player_id, InputState state)
        {
            if (!Built || ClosedLocally || !HasData || player_id != Data.OwnerPlayerID)
            {
                return InputConsumerState.NotConsumed;
            }
            LastInput = state;
            HasFreshInput = true;
            // Consumed: the ECS gets neutral input for this player (no interacting). Walking is
            // stopped by PlayerLock, since the chef view reads the raw state.
            return InputConsumerState.Consumed;
        }

        private void AcquirePlayerLock()
        {
            BaseInputSource input = InputSourceIdentifier.DefaultInputSource;
            if (HoldsPlayerLock || input == null)
            {
                return;
            }
            PlayerLock = input.SetInputLock(Data.OwnerPlayerID, PlayerLockState.NonPause);
            // SetInputLock returns an Unlocked default if the player isn't known locally.
            HoldsPlayerLock = PlayerLock.Type != PlayerLockState.Unlocked;
        }

        private void ReleasePlayerLock()
        {
            if (!HoldsPlayerLock)
            {
                return;
            }
            HoldsPlayerLock = false;
            InputSourceIdentifier.DefaultInputSource?.ReleaseLock(Data.OwnerPlayerID, PlayerLock);
        }

        /// <summary>
        /// True while a game-wide menu holds the global input lock (another local player's pause
        /// menu, the disconnect screen, a text-entry box, the Steam overlay). The shop's raw
        /// mouse/keyboard reads would otherwise keep acting on the window behind it.
        /// </summary>
        private static bool GlobalInputLocked() =>
            InputSourceIdentifier.DefaultInputSource?.GlobalLock.IsLocked ?? false;

        private static bool IsPressed(ButtonState s) => s == ButtonState.Pressed;

        private void Update()
        {
            if (!IsLocalOwner)
            {
                return;
            }
            if (ClosedLocally)
            {
                // If the host never saw our Close (it would destroy this view), ask again.
                if (Time.unscaledTime - CloseRequestedAt > CloseRetrySeconds)
                {
                    CloseRequestedAt = Time.unscaledTime;
                    EnqueueClose();
                }
                return;
            }
            if (!Built)
            {
                return;
            }
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                ShopLog.Error("Shop menu error", e);
            }
        }

        private void Tick()
        {
            ControllerType controller = InputSourceIdentifier.DefaultInputSource?.GetCurrentController(Data.OwnerPlayerID) ?? ControllerType.None;
            bool mouse = (controller & (ControllerType.Keyboard | ControllerType.Mouse)) != 0;
            if (mouse != UsesMouse || !CursorModeApplied)
            {
                UsesMouse = mouse;
                CursorModeApplied = true;
                if (UsesMouse && OskOpen)
                {
                    SetOsk(false);
                }
                ApplyCursorMode();
            }

            InputState input = HasFreshInput ? LastInput : default;
            HasFreshInput = false;
            LastInput = default;

            LayoutSlot();
            RefreshFromData();

            // Another menu has the whole game's input: keep the window drawn but ignore input,
            // including anything typed meanwhile.
            if (GlobalInputLocked())
            {
                TypedChars.Clear();
                return;
            }

            // ---- typing (keyboard players)
            if (SearchFocused)
            {
                if (HandleTyping())
                {
                    return; // Esc while typing only leaves the search box
                }
            }
            else if (UsesMouse && Keyboard.current != null && Keyboard.current.ctrlKey.isPressed && Keyboard.current.fKey.wasPressedThisFrame)
            {
                SetSearchFocus(true);
            }
            // Ctrl+A clears the search, typing or not. (While typing, Ctrl+A arrives as a control
            // character, which OnTextInput already ignores, so no "a" is added.)
            if (UsesMouse && Keyboard.current != null && Keyboard.current.ctrlKey.isPressed && Keyboard.current.aKey.wasPressedThisFrame)
            {
                SetSearch("");
            }

            if (FilterDirty)
            {
                FilterDirty = false;
                ApplyFilter();
            }

            float dt = Time.unscaledDeltaTime;
            float scale = Screen.height / 1080f;
            Vector2 pointer;
            bool click;
            bool rightClick = false;
            float wheel = 0f;
            Vector2 move = Vector2.zero;
            if (UsesMouse && Mouse.current != null)
            {
                pointer = Mouse.current.position.ReadValue();
                click = Mouse.current.leftButton.wasPressedThisFrame;
                rightClick = Mouse.current.rightButton.wasPressedThisFrame;
                wheel = Mouse.current.scroll.ReadValue().y;
            }
            else
            {
                Rect area = ScreenRect(SlotArea);
                if (!VirtualCursorPlaced)
                {
                    VirtualCursor = area.center;
                    VirtualCursorPlaced = true;
                }
                move = input.Movement;
                if (move.sqrMagnitude > 1f)
                {
                    move.Normalize();
                }
                // Mild acceleration curve, and "sticky" buttons: slower while over one.
                float speed = CursorSpeed * scale * Mathf.Lerp(0.35f, 1f, move.magnitude);
                if (LastHovered != null)
                {
                    speed *= 0.65f;
                }
                VirtualCursor += move * speed * dt;
                VirtualCursor.x = Mathf.Clamp(VirtualCursor.x, area.xMin, area.xMax);
                VirtualCursor.y = Mathf.Clamp(VirtualCursor.y, area.yMin, area.yMax);
                pointer = VirtualCursor;
                click = IsPressed(input.InteractAction) || IsPressed(input.GrabAction) || IsPressed(input.MenuSelect);

                // Controller equivalent of Ctrl+A: SecondaryAction1 (Interact3; Y / Triangle by
                // default on PC, follows rebinding) clears the search. Skipped if that button is
                // also bound to a select action, so one press never both clicks and clears.
                if (IsPressed(input.SecondaryAction1) && !click)
                {
                    SetSearch("");
                }

                // Edge scrolling for the stick.
                if (!OskOpen)
                {
                    Rect grid = ScreenRect(GridViewport);
                    if (grid.Contains(pointer))
                    {
                        float edge = grid.height * 0.12f;
                        if (pointer.y > grid.yMax - edge)
                        {
                            GridScroll -= 700f * dt;
                        }
                        else if (pointer.y < grid.yMin + edge)
                        {
                            GridScroll += 700f * dt;
                        }
                    }
                }
            }

            if (!SearchFocused && (IsPressed(input.MenuCancel) || IsPressed(input.MenuTrigger)))
            {
                if (OskOpen)
                {
                    SetOsk(false);
                }
                else
                {
                    RequestClose();
                    return;
                }
            }

            if (wheel != 0f && !OskOpen)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(CartViewport, pointer, null))
                {
                    CartScroll -= wheel * 0.5f;
                }
                else
                {
                    GridScroll -= wheel * 0.5f;
                }
            }
            ApplyScroll();

            UiButton hovered = FindHovered(pointer, scale, out Rect hoveredRect);
            if (hovered != LastHovered)
            {
                if (LastHovered != null)
                {
                    LastHovered.IsHovered = false;
                    LastHovered.Refresh();
                }
                if (hovered != null)
                {
                    hovered.IsHovered = true;
                    hovered.Refresh();
                }
                LastHovered = hovered;
            }

            // Aim assist: with the stick at rest, glide onto the nearest button.
            if (!UsesMouse && hovered != null && move.magnitude < 0.2f)
            {
                VirtualCursor = Vector2.Lerp(VirtualCursor, hoveredRect.center, 1f - Mathf.Exp(-14f * dt));
                pointer = VirtualCursor;
            }

            if (click)
            {
                if (SearchFocused && hovered != SearchBox)
                {
                    SetSearchFocus(false);
                }
                if (hovered != null && hovered.Interactable)
                {
                    hovered.OnClick?.Invoke();
                    if (ClosedLocally || !Built)
                    {
                        return;
                    }
                }
            }
            if (rightClick && hovered == SearchBox)
            {
                // Right-clicking the box starts a fresh search (user request).
                SetSearch("");
                SetSearchFocus(true);
            }
            else if (rightClick && !OskOpen)
            {
                Card card = CardAt(pointer);
                if (card != null)
                {
                    AddToCart(card.Entry, -1);
                }
            }

            if (Time.unscaledTime > SellConfirmUntil && SellConfirmUntil > 0f)
            {
                SellConfirmUntil = 0f;
                RefreshSellBox(force: true);
            }

            UpdateSearchLabel();
            UpdateTooltip(pointer);
            UpdateCursorImage(pointer);
            LoadSomeIcons();
        }

        private IEnumerable<List<UiButton>> ActiveGroups()
        {
            if (OskOpen)
            {
                yield return OskButtons;
                yield break;
            }
            yield return StaticButtons;
            yield return TabButtons;
            yield return CartButtons;
            yield return CardButtons;
        }

        /// <summary>
        /// The button under the pointer. Controller players also get the nearest button within
        /// SnapRadius (aim assist), so they don't have to land exactly on small targets.
        /// </summary>
        private UiButton FindHovered(Vector2 pointer, float scale, out Rect hoveredRect)
        {
            hoveredRect = default;
            UiButton nearest = null;
            float nearestDist = float.MaxValue;
            Rect nearestRect = default;
            foreach (List<UiButton> group in ActiveGroups())
            {
                foreach (UiButton b in group)
                {
                    if (b.Rect == null || !b.Rect.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    Rect r = ScreenRect(b.Rect);
                    if (b.Clip != null && !ScreenRect(b.Clip).Contains(r.center))
                    {
                        continue; // scrolled out of view
                    }
                    if (r.Contains(pointer))
                    {
                        hoveredRect = r;
                        return b;
                    }
                    if (!UsesMouse)
                    {
                        float dx = Mathf.Max(r.xMin - pointer.x, 0f, pointer.x - r.xMax);
                        float dy = Mathf.Max(r.yMin - pointer.y, 0f, pointer.y - r.yMax);
                        float d = dx * dx + dy * dy;
                        if (d < nearestDist)
                        {
                            nearestDist = d;
                            nearest = b;
                            nearestRect = r;
                        }
                    }
                }
            }
            float reach = SnapRadius * scale;
            if (nearest != null && nearestDist <= reach * reach)
            {
                hoveredRect = nearestRect;
                return nearest;
            }
            return null;
        }

        private Card CardAt(Vector2 pointer)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(GridViewport, pointer, null))
            {
                return null;
            }
            foreach (Card c in Cards)
            {
                if (c.Root.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(c.Root, pointer, null))
                {
                    return c;
                }
            }
            return null;
        }

        // ---- search typing

        private void SetSearchFocus(bool focus)
        {
            if (focus == SearchFocused)
            {
                return;
            }
            SearchFocused = focus;
            TypedChars.Clear();
            if (SubscribedKeyboard != null)
            {
                SubscribedKeyboard.onTextInput -= OnTextInput;
                SubscribedKeyboard = null;
            }
            if (focus && Keyboard.current != null)
            {
                SubscribedKeyboard = Keyboard.current;
                SubscribedKeyboard.onTextInput += OnTextInput;
            }
        }

        private void OnTextInput(char c)
        {
            if (!char.IsControl(c))
            {
                TypedChars.Enqueue(c);
            }
        }

        /// <summary>Returns true if the frame's input was used up (Esc left the search box).</summary>
        private bool HandleTyping()
        {
            Keyboard kb = Keyboard.current;
            while (TypedChars.Count > 0)
            {
                AppendSearch(TypedChars.Dequeue().ToString());
            }
            if (kb == null)
            {
                return false;
            }
            if (kb.backspaceKey.wasPressedThisFrame)
            {
                BackspaceSearch();
            }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                SetSearchFocus(false);
            }
            if (kb.escapeKey.wasPressedThisFrame)
            {
                SetSearchFocus(false);
                return true;
            }
            return false;
        }

        private void AppendSearch(string s)
        {
            if (Search.Length >= MaxSearchLength)
            {
                return;
            }
            SetSearch(Search + s);
        }

        private void BackspaceSearch()
        {
            if (Search.Length > 0)
            {
                SetSearch(Search.Substring(0, Search.Length - 1));
            }
        }

        private void SetSearch(string s)
        {
            if (s == Search)
            {
                return;
            }
            Search = s;
            if (Search.Length > 0 && ActiveCategory != Category.All)
            {
                ActiveCategory = Category.All;
                RefreshTabColours();
            }
            GridScroll = 0f;
            FilterDirty = true;
        }

        private void ApplyCursorMode()
        {
            if (UsesMouse)
            {
                if (SavedCursorVisible == null)
                {
                    SavedCursorVisible = Cursor.visible;
                    SavedCursorLock = Cursor.lockState;
                }
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            else
            {
                RestoreCursor();
            }
            if (CursorImage != null)
            {
                CursorImage.gameObject.SetActive(!UsesMouse);
            }
            if (HintText != null)
            {
                HintText.text = UsesMouse
                    ? "Click: select  •  Right-click a card: remove one  •  Wheel: scroll  •  Ctrl+F: search  •  Ctrl+A or right-click search: clear  •  Esc: close"
                    : "Stick: move (snaps to buttons)  •  Interact/Grab: select  •  Y/Triangle: clear search  •  Back: close  •  Hold the cursor at the top/bottom of the list to scroll";
            }
        }

        private void RestoreCursor()
        {
            if (SavedCursorVisible != null)
            {
                Cursor.visible = SavedCursorVisible.Value;
                Cursor.lockState = SavedCursorLock;
                SavedCursorVisible = null;
            }
        }

        private static Rect ScreenRect(RectTransform rt)
        {
            rt.GetWorldCorners(Corners);
            return new Rect(Corners[0].x, Corners[0].y, Corners[2].x - Corners[0].x, Corners[2].y - Corners[0].y);
        }

        // ================================================================= requests

        private void RequestClose()
        {
            if (ClosedLocally)
            {
                return;
            }
            ClosedLocally = true;
            CloseRequestedAt = Time.unscaledTime;
            EnqueueClose();
            // Hide right away; the host destroys the session (and this view) when it gets the request.
            TearDown();
        }

        private void EnqueueClose()
        {
            Outgoing.Enqueue(new ShopResponseData
            {
                Sequence = NextSequence++,
                Action = ShopAction.Close,
                Lines = new List<ShopOrderLine>()
            });
        }

        private void SendOrder(ShopAction action, List<ShopOrderLine> lines, string pendingText)
        {
            if (AwaitingSequence != 0)
            {
                return;
            }
            int seq = NextSequence++;
            AwaitingSequence = seq;
            AwaitingAction = action;
            Outgoing.Enqueue(new ShopResponseData { Sequence = seq, Action = action, Lines = lines });
            SetStatus(pendingText, ShopTheme.TextDim);
            UpdateCartFooter();
        }

        private void Purchase()
        {
            var lines = new List<ShopOrderLine>();
            foreach ((ShopEntryKind kind, int id) key in CartOrder)
            {
                lines.Add(new ShopOrderLine { Kind = key.kind, ID = key.id, Quantity = CartQty[key] });
            }
            if (lines.Count > 0)
            {
                SendOrder(ShopAction.Purchase, lines, "Placing order...");
            }
        }

        private void TakeBlueprint(ShopEntry entry)
        {
            SendOrder(ShopAction.TakeBlueprint, new List<ShopOrderLine>
            {
                new ShopOrderLine { Kind = entry.Kind, ID = entry.ID, Quantity = 1 }
            }, "Fetching blueprint...");
        }

        private void Sell()
        {
            if (Data.HeldKind != HeldKind.Appliance && Data.HeldKind != HeldKind.Parcel)
            {
                return;
            }
            if (Time.unscaledTime > SellConfirmUntil)
            {
                // First click arms it; selling destroys the appliance, so ask once more.
                SellConfirmUntil = Time.unscaledTime + SellConfirmSeconds;
                RefreshSellBox(force: true);
                return;
            }
            SellConfirmUntil = 0f;
            SendOrder(ShopAction.Sell, new List<ShopOrderLine>
            {
                new ShopOrderLine { Kind = ShopEntryKind.Appliance, ID = Data.HeldID, Quantity = 1 }
            }, "Selling...");
            RefreshSellBox(force: true);
        }

        // ================================================================= cart

        private void AddToCart(ShopEntry entry, int delta)
        {
            var key = (entry.Kind, entry.ID);
            CartQty.TryGetValue(key, out int qty);
            int max = (entry.Flags & ShopEntryFlags.Unique) != 0 ? 1 : ShopService.MaxQuantityPerLine;
            int next = Mathf.Clamp(qty + delta, 0, max);
            if (next == qty)
            {
                if (delta > 0 && next == max && max == 1)
                {
                    SetStatus($"Only one {EntryName(entry)} is allowed.", ShopTheme.Bad);
                }
                return;
            }
            if (next == 0)
            {
                CartQty.Remove(key);
                CartOrder.Remove(key);
            }
            else
            {
                if (!CartQty.ContainsKey(key))
                {
                    CartOrder.Add(key);
                }
                CartQty[key] = next;
            }
            CartDirty = true;
            SaveCart();
        }

        private void RemoveFromCart((ShopEntryKind, int) key)
        {
            CartQty.Remove(key);
            CartOrder.Remove(key);
            CartDirty = true;
            SaveCart();
        }

        private void ClearCart()
        {
            CartQty.Clear();
            CartOrder.Clear();
            CartDirty = true;
            SaveCart();
        }

        private int CartTotal()
        {
            int total = 0;
            foreach ((ShopEntryKind, int) key in CartOrder)
            {
                if (CatalogByKey.TryGetValue(key, out ShopEntry entry))
                {
                    total += entry.Price * CartQty[key];
                }
            }
            return total;
        }

        private void SaveCart()
        {
            if (!HasData || !IsLocalOwner || !CartRestored)
            {
                return;
            }
            var saved = new SavedCart { Day = Data.Day };
            foreach ((ShopEntryKind, int) key in CartOrder)
            {
                saved.Lines.Add((key, CartQty[key]));
            }
            SavedCarts[Data.OwnerPlayerID] = saved;
        }

        private void RestoreCart()
        {
            CartRestored = true;
            if (!SavedCarts.TryGetValue(Data.OwnerPlayerID, out SavedCart saved) || saved.Day != Data.Day)
            {
                SavedCarts.Remove(Data.OwnerPlayerID);
                return;
            }
            foreach (((ShopEntryKind, int) key, int qty) in saved.Lines)
            {
                if (!CartQty.ContainsKey(key))
                {
                    CartOrder.Add(key);
                }
                CartQty[key] = qty;
            }
            CartDirty = true;
            ShopLog.Debug(() => $"Restored {saved.Lines.Count} cart line(s) from earlier tonight");
        }

        // ================================================================= data -> UI

        private void RefreshFromData()
        {
            if (Data.CatalogHash != RenderedCatalogHash || CatalogByKey.Count == 0 && Data.Entries != null && Data.Entries.Count > 0)
            {
                RenderedCatalogHash = Data.CatalogHash;
                CatalogByKey.Clear();
                if (Data.Entries != null)
                {
                    foreach (ShopEntry e in Data.Entries)
                    {
                        CatalogByKey[(e.Kind, e.ID)] = e;
                    }
                }
                if (!CartRestored)
                {
                    RestoreCart();
                }
                // Drop cart lines that are no longer offered (e.g. a unique item now owned).
                for (int i = CartOrder.Count - 1; i >= 0; i--)
                {
                    if (!CatalogByKey.ContainsKey(CartOrder[i]))
                    {
                        CartQty.Remove(CartOrder[i]);
                        CartOrder.RemoveAt(i);
                    }
                }
                SaveCart();
                CartDirty = true;
                RebuildCards();
                RebuildTabs();
                FilterDirty = true;
                TitleText.text = Data.IsDecorNight ? "Blueprint Shop  <size=70%><color=#BFB2A3>Decoration night</color></size>" : "Blueprint Shop";
            }

            if (Data.OwnedHash != RenderedOwnedHash)
            {
                RenderedOwnedHash = Data.OwnedHash;
                OwnedById.Clear();
                if (Data.Owned != null)
                {
                    foreach (OwnedCount c in Data.Owned)
                    {
                        OwnedById[c.ID] = c.Count;
                    }
                }
                foreach (Card card in Cards)
                {
                    UpdateCardOwned(card);
                }
            }

            if (Data.Money != RenderedMoney)
            {
                RenderedMoney = Data.Money;
                MoneyText.text = Coin(Data.Money);
                foreach (Card card in Cards)
                {
                    UpdateCardPrice(card);
                }
                CartDirty = true;
            }

            if (Data.AckSequence != RenderedAck)
            {
                RenderedAck = Data.AckSequence;
                if (AwaitingSequence != 0 && Data.AckSequence >= AwaitingSequence)
                {
                    if (Data.LastSucceeded && AwaitingAction == ShopAction.Purchase)
                    {
                        ClearCart();
                    }
                    AwaitingSequence = 0;
                    SetStatus(Data.Status, Data.LastSucceeded ? ShopTheme.Good : ShopTheme.Bad);
                    CartDirty = true;
                    RefreshSellBox(force: true);
                }
            }

            RefreshSellBox(force: false);

            if (CartDirty)
            {
                CartDirty = false;
                RebuildCart();
            }
        }

        private void SetStatus(string text, Color color)
        {
            if (StatusText != null)
            {
                StatusText.text = text ?? "";
                StatusText.color = color;
            }
        }

        private static string Coin(int amount) =>
            ShopTheme.HasCoinSprite ? $"{amount} <sprite name=\"coin\" color=#FF9800>" : $"${amount}";

        private static string EntryName(ShopEntry entry)
        {
            if (entry.Kind == ShopEntryKind.Appliance && GameData.Main.TryGet(entry.ID, out Appliance a))
            {
                return a.Name;
            }
            if (entry.Kind == ShopEntryKind.Decor && GameData.Main.TryGet(entry.ID, out Decor d))
            {
                return DecorName(d);
            }
            return $"#{entry.ID}";
        }

        private static string ApplianceName(int id) =>
            GameData.Main.TryGet(id, out Appliance a) ? a.Name : $"#{id}";

        private static string DecorName(Decor decor)
        {
            // Decor has no localised name; its asset name is the closest thing.
            string name = decor.name ?? "";
            name = name.Replace('_', ' ').Replace('-', ' ').Trim();
            string kind = decor.Type.ToString();
            // Asset names already start with the type ("Floor Carpet Blue"); don't say it twice.
            if (name.StartsWith(kind + " ", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(kind.Length).Trim();
            }
            return string.IsNullOrEmpty(name) ? kind : $"{name} ({kind})";
        }

        // ---- categories

        private static int RootOf(int applianceId)
        {
            if (_upgradeBase == null)
            {
                _upgradeBase = new Dictionary<int, int>();
                foreach (Appliance a in GameData.Main.Get<Appliance>())
                {
                    if (a?.Upgrades == null)
                    {
                        continue;
                    }
                    foreach (Appliance u in a.Upgrades)
                    {
                        if (u != null && u.ID != a.ID && !_upgradeBase.ContainsKey(u.ID))
                        {
                            _upgradeBase[u.ID] = a.ID;
                        }
                    }
                }
            }
            int id = applianceId;
            for (int i = 0; i < 6 && _upgradeBase.TryGetValue(id, out int parent); i++)
            {
                id = parent;
            }
            return id;
        }

        private static Category FromTags(ShoppingTags t)
        {
            if ((t & ShoppingTags.SpecialEvent) != 0) return Category.Events;
            if ((t & ShoppingTags.Decoration) != 0) return Category.Decorations;
            if ((t & ShoppingTags.Automation) != 0) return Category.Automation;
            if ((t & ShoppingTags.Cooking) != 0) return Category.Cooking;
            if ((t & ShoppingTags.Plumbing) != 0) return Category.Plumbing;
            if ((t & ShoppingTags.FrontOfHouse) != 0) return Category.FrontOfHouse;
            if ((t & ShoppingTags.Technology) != 0) return Category.Technology;
            if ((t & (ShoppingTags.Office | ShoppingTags.BlueprintStore | ShoppingTags.BlueprintUpgrader)) != 0) return Category.Office;
            if ((t & ShoppingTags.Basic) != 0) return Category.Essentials;
            return Category.Misc;
        }

        /// <summary>
        /// Function first: upgrades sit with what they upgrade (a grabber is Automation, not
        /// "Upgrades"); "staple" and "upgrade" are shown as badges instead of categories,
        /// because they change from night to night.
        /// </summary>
        private static Category CategoryOf(ShopEntry entry)
        {
            if (entry.Kind == ShopEntryKind.Decor)
            {
                return Category.Surfaces;
            }
            if (!GameData.Main.TryGet(entry.ID, out Appliance a))
            {
                return Category.Misc;
            }
            int root = RootOf(entry.ID);
            if (root != entry.ID && GameData.Main.TryGet(root, out Appliance baseAppliance))
            {
                Category inherited = FromTags(baseAppliance.ShoppingTags);
                if (inherited != Category.Misc)
                {
                    return inherited;
                }
            }
            return FromTags(a.ShoppingTags);
        }

        // ================================================================= building

        private void Build()
        {
            ShopTheme.ResetFontSearch();
            CanvasRoot = new GameObject("BlueprintShop Canvas", typeof(RectTransform));
            CanvasRoot.transform.SetParent(transform, false);
            CanvasRoot.layer = 5;
            Canvas canvas = CanvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000;
            CanvasScaler scaler = CanvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            SlotArea = Ui.Stretch(Ui.Rect("Slot", CanvasRoot.transform));
            Ui.Stretch(Ui.Box(SlotArea, "Backdrop", ShopTheme.Backdrop, rounded: false).rectTransform);

            Panel = Ui.Stretch(Ui.Box(SlotArea, "Panel", ShopTheme.Panel).rectTransform, 40, 40, 36, 36);
            Ui.Group<VerticalLayoutGroup>(Panel, 10, new RectOffset(18, 18, 14, 14));

            BuildHeader();
            BuildSearchRow();

            // Category tabs (wrap onto more rows when narrow).
            TabsContainer = Ui.Rect("Tabs", Panel);
            GridLayoutGroup tabGrid = TabsContainer.gameObject.AddComponent<GridLayoutGroup>();
            tabGrid.cellSize = new Vector2(150f, 38f);
            tabGrid.spacing = new Vector2(6f, 6f);
            tabGrid.startAxis = GridLayoutGroup.Axis.Horizontal;

            // Body: item grid + cart.
            RectTransform body = Ui.Rect("Body", Panel);
            Ui.Layout(body, flexW: 1, flexH: 1);
            Ui.Group<HorizontalLayoutGroup>(body, 14, null).childForceExpandHeight = true;

            Image gridBg = Ui.Box(body, "GridViewport", ShopTheme.PanelInner);
            GridViewport = gridBg.rectTransform;
            GridViewport.gameObject.AddComponent<RectMask2D>();
            Ui.Layout(GridViewport, flexW: 1, flexH: 1);
            GridContent = Ui.Rect("Content", GridViewport);
            GridContent.anchorMin = new Vector2(0f, 1f);
            GridContent.anchorMax = new Vector2(1f, 1f);
            GridContent.pivot = new Vector2(0.5f, 1f);
            GridContent.offsetMin = Vector2.zero;
            GridContent.offsetMax = Vector2.zero;
            GridLayoutGroup grid = GridContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CardWidth, CardHeight);
            grid.spacing = new Vector2(10f, 10f);
            grid.padding = new RectOffset(10, 10, 10, 10);
            grid.childAlignment = TextAnchor.UpperCenter;
            GridContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            EmptyText = Ui.Text(GridViewport, "Empty", "Nothing matches.", 22, TextAlignmentOptions.Center, ShopTheme.TextDim);
            Ui.Stretch(EmptyText.rectTransform, 20, 20, 20, 20);
            EmptyText.gameObject.SetActive(false);

            BuildCartColumn(body);

            HintText = Ui.Text(Panel, "Hint", "", 18, TextAlignmentOptions.Center, ShopTheme.TextDim);
            Ui.AutoSize(HintText, 12);
            Ui.Layout(HintText, prefH: 26);

            BuildOsk();

            // Tooltip and controller cursor live on the canvas so nothing clips them.
            Image tooltipBg = Ui.Box(CanvasRoot.transform, "Tooltip", new Color32(0x1E, 0x1A, 0x17, 0xF8));
            Tooltip = tooltipBg.rectTransform;
            Tooltip.pivot = new Vector2(0f, 1f);
            Tooltip.anchorMin = Tooltip.anchorMax = Vector2.zero;
            Ui.Group<VerticalLayoutGroup>(Tooltip, 6, new RectOffset(14, 14, 12, 12));
            ContentSizeFitter tipFit = Tooltip.gameObject.AddComponent<ContentSizeFitter>();
            tipFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            tipFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            TooltipText = Ui.Text(Tooltip, "Text", "", 19);
            TooltipText.overflowMode = TextOverflowModes.Overflow;
            Ui.Layout(TooltipText, prefW: 420);
            Tooltip.gameObject.SetActive(false);

            Image cursor = Ui.Box(CanvasRoot.transform, "Cursor", ShopTheme.Accent);
            CursorImage = cursor.rectTransform;
            CursorImage.sizeDelta = new Vector2(26f, 26f);
            CursorImage.anchorMin = CursorImage.anchorMax = Vector2.zero;
            CursorImage.pivot = new Vector2(0.5f, 0.5f);
            Image cursorDot = Ui.Box(CursorImage, "Dot", ShopTheme.Panel);
            Ui.Stretch(cursorDot.rectTransform, 7, 7, 7, 7);

            Built = true;
            OpenLocal.Add(this);
            LocalInputSourceConsumers.Register(this);
            AcquirePlayerLock();
            ApplyCursorMode();
            SetStatus("Add items to your cart, or take a blueprint for a cabinet or desk.", ShopTheme.TextDim);
            ShopLog.Debug(() => $"Built shop menu for player {Data.OwnerPlayerID}");
        }

        private void BuildHeader()
        {
            RectTransform header = Ui.Rect("Header", Panel);
            Ui.Layout(header, minH: 64, prefH: 64, flexH: 0);
            Ui.Group<HorizontalLayoutGroup>(header, 12, null, TextAnchor.MiddleLeft);
            TitleText = Ui.Text(header, "Title", "Blueprint Shop", 34);
            Ui.AutoSize(TitleText, 22);
            Ui.Layout(TitleText, flexW: 1, prefH: 64);

            // Balance, and under it what the cart will leave you with.
            RectTransform money = Ui.Rect("Money", header);
            Ui.Layout(money, minW: 300, prefW: 300, prefH: 64);
            Ui.Group<VerticalLayoutGroup>(money, 0, null, TextAnchor.MiddleRight).childForceExpandWidth = true;
            MoneyText = Ui.Text(money, "Balance", "", 30, TextAlignmentOptions.Right, ShopTheme.Accent);
            Ui.Layout(MoneyText, prefH: 38);
            SubtotalText = Ui.Text(money, "Subtotal", "", 18, TextAlignmentOptions.Right, ShopTheme.TextDim);
            Ui.AutoSize(SubtotalText, 12);
            Ui.Layout(SubtotalText, prefH: 24);

            UiButton close = Ui.Button(header, "Close", "X", 26, RequestClose);
            close.Normal = ShopTheme.Bad;
            close.Hover = new Color(1f, 0.45f, 0.35f);
            close.Refresh();
            Ui.Layout(close.Rect, minW: 56, prefW: 56, prefH: 48);
            StaticButtons.Add(close);
        }

        private void BuildSearchRow()
        {
            RectTransform row = Ui.Rect("SearchRow", Panel);
            Ui.Layout(row, minH: 42, prefH: 42, flexH: 0);
            Ui.Group<HorizontalLayoutGroup>(row, 8, null, TextAnchor.MiddleLeft).childForceExpandHeight = true;

            SearchBox = Ui.Button(row, "Search", "", 20, OnSearchBoxClicked);
            SearchBox.Normal = ShopTheme.PanelInner;
            SearchBox.Hover = ShopTheme.CardHover;
            SearchBox.Refresh();
            Ui.Layout(SearchBox.Rect, flexW: 1, prefH: 42);
            SearchLabel = SearchBox.Label;
            SearchLabel.alignment = TextAlignmentOptions.Left;
            Ui.Stretch(SearchLabel.rectTransform, 14, 14, 2, 2);
            StaticButtons.Add(SearchBox);

            SearchClear = Ui.Button(row, "SearchClear", "Clear", 18, () => SetSearch(""));
            Ui.Layout(SearchClear.Rect, minW: 90, prefW: 90, prefH: 42);
            StaticButtons.Add(SearchClear);
        }

        private void OnSearchBoxClicked()
        {
            if (UsesMouse)
            {
                SetSearchFocus(true);
            }
            else
            {
                SetOsk(!OskOpen);
            }
        }

        private void UpdateSearchLabel()
        {
            bool active = SearchFocused || OskOpen;
            string caret = active && (Time.unscaledTime % 1f) < 0.5f ? "|" : "";
            if (Search.Length == 0)
            {
                SearchLabel.text = active ? caret : (UsesMouse ? "<color=#BFB2A3>Search...  (click or Ctrl+F)</color>" : "<color=#BFB2A3>Search...</color>");
            }
            else
            {
                SearchLabel.text = Search + caret;
            }
            SearchClear.Interactable = Search.Length > 0;
            SearchClear.Refresh();
        }

        private void BuildCartColumn(RectTransform body)
        {
            Image cartBg = Ui.Box(body, "Cart", ShopTheme.PanelInner);
            // flexW 0: otherwise the cart inherits flexible width from its stretchy line list
            // and splits the body with the item grid (v0.1.0 bug).
            Ui.Layout(cartBg, minW: 360, prefW: 400, flexW: 0, flexH: 1);
            Ui.Group<VerticalLayoutGroup>(cartBg, 8, new RectOffset(14, 14, 12, 14));

            // Sell what you're holding.
            Image sell = Ui.Box(cartBg.transform, "Sell", ShopTheme.Card);
            SellBox = sell.rectTransform;
            Ui.Layout(SellBox, minH: 104, prefH: 104, flexH: 0);
            Ui.Group<VerticalLayoutGroup>(SellBox, 6, new RectOffset(10, 10, 8, 8)).childForceExpandWidth = true;
            SellText = Ui.Text(SellBox, "Text", "", 18);
            Ui.AutoSize(SellText, 12);
            Ui.Layout(SellText, prefH: 44);
            SellButton = Ui.Button(SellBox, "SellButton", "Sell", 20, Sell);
            SellButton.Normal = ShopTheme.Accent;
            SellButton.Hover = new Color(1f, 0.72f, 0.3f);
            Ui.Layout(SellButton.Rect, prefH: 38);
            StaticButtons.Add(SellButton);
            SellBox.gameObject.SetActive(false);

            TextMeshProUGUI cartTitle = Ui.Text(cartBg.transform, "Title", "Cart", 28);
            Ui.Layout(cartTitle, prefH: 36, flexH: 0);

            CartViewport = Ui.Rect("Lines", cartBg.transform);
            CartViewport.gameObject.AddComponent<RectMask2D>();
            Ui.Layout(CartViewport, flexH: 1, flexW: 1);
            CartContent = Ui.Rect("Content", CartViewport);
            CartContent.anchorMin = new Vector2(0f, 1f);
            CartContent.anchorMax = new Vector2(1f, 1f);
            CartContent.pivot = new Vector2(0.5f, 1f);
            CartContent.offsetMin = CartContent.offsetMax = Vector2.zero;
            Ui.Group<VerticalLayoutGroup>(CartContent, 6, null).childForceExpandWidth = true;
            CartContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TotalText = Ui.Text(cartBg.transform, "Total", "", 26, TextAlignmentOptions.Right);
            Ui.Layout(TotalText, prefH: 36, flexH: 0);

            StatusText = Ui.Text(cartBg.transform, "Status", "", 19, TextAlignmentOptions.TopLeft, ShopTheme.TextDim);
            Ui.AutoSize(StatusText, 13);
            Ui.Layout(StatusText, prefH: 70, flexH: 0);

            RectTransform buttons = Ui.Rect("Buttons", cartBg.transform);
            // flexH 0: childForceExpandHeight below makes the row report itself as flexible,
            // which made it grab half the cart's height (v0.1.0 bug).
            Ui.Layout(buttons, minH: 54, prefH: 54, flexH: 0);
            Ui.Group<HorizontalLayoutGroup>(buttons, 10, null).childForceExpandHeight = true;
            ClearButton = Ui.Button(buttons, "Clear", "Clear", 22, ClearCart);
            Ui.Layout(ClearButton.Rect, prefW: 110);
            BuyButton = Ui.Button(buttons, "Buy", "Purchase", 26, Purchase);
            BuyButton.Normal = ShopTheme.Good;
            BuyButton.Hover = ShopTheme.GoodHover;
            Ui.Layout(BuyButton.Rect, flexW: 1);
            StaticButtons.Add(ClearButton);
            StaticButtons.Add(BuyButton);
        }

        private void BuildOsk()
        {
            Image bg = Ui.Box(SlotArea, "Keyboard", new Color32(0x1E, 0x1A, 0x17, 0xFA));
            Osk = bg.rectTransform;
            Osk.anchorMin = Osk.anchorMax = new Vector2(0.5f, 0f);
            Osk.pivot = new Vector2(0.5f, 0f);
            Osk.anchoredPosition = new Vector2(0f, 70f);
            Ui.Group<VerticalLayoutGroup>(Osk, 6, new RectOffset(14, 14, 12, 12), TextAnchor.UpperCenter);
            ContentSizeFitter fit = Osk.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (string keys in new[] { "1234567890", "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" })
            {
                RectTransform row = Ui.Rect("Row", Osk);
                Ui.Group<HorizontalLayoutGroup>(row, 6, null, TextAnchor.MiddleCenter);
                foreach (char c in keys)
                {
                    string s = c.ToString();
                    UiButton key = Ui.Button(row, "Key " + s, s, 24, () => AppendSearch(s.ToLowerInvariant()));
                    Ui.Layout(key.Rect, minW: 58, prefW: 58, minH: 52, prefH: 52);
                    OskButtons.Add(key);
                }
            }
            RectTransform bottom = Ui.Rect("Row", Osk);
            Ui.Group<HorizontalLayoutGroup>(bottom, 6, null, TextAnchor.MiddleCenter);
            foreach ((string label, float width, Action act) in new (string, float, Action)[]
            {
                ("Space", 220f, () => AppendSearch(" ")),
                ("Back", 110f, BackspaceSearch),
                ("Clear", 110f, () => SetSearch("")),
                ("Done", 130f, () => SetOsk(false))
            })
            {
                UiButton key = Ui.Button(bottom, label, label, 22, act);
                Ui.Layout(key.Rect, minW: width, prefW: width, minH: 52, prefH: 52);
                if (label == "Done")
                {
                    key.Normal = ShopTheme.Good;
                    key.Hover = ShopTheme.GoodHover;
                    key.Refresh();
                }
                OskButtons.Add(key);
            }
            Osk.gameObject.SetActive(false);
        }

        private void SetOsk(bool open)
        {
            OskOpen = open && Osk != null;
            if (Osk != null)
            {
                Osk.gameObject.SetActive(OskOpen);
                if (OskOpen)
                {
                    Osk.SetAsLastSibling();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(Osk);
                }
            }
        }

        private void RebuildTabs()
        {
            Ui.DestroyChildren(TabsContainer);
            TabButtons.Clear();
            var present = new HashSet<Category>();
            foreach (Card c in Cards)
            {
                present.Add(c.Category);
            }
            if (ActiveCategory != Category.All && !present.Contains(ActiveCategory))
            {
                ActiveCategory = Category.All;
            }
            foreach (Category c in (Category[])Enum.GetValues(typeof(Category)))
            {
                if (c != Category.All && !present.Contains(c))
                {
                    continue;
                }
                Category captured = c;
                UiButton tab = Ui.Button(TabsContainer, "Tab " + c, CategoryNames[c], 19, () =>
                {
                    if (ActiveCategory != captured)
                    {
                        ActiveCategory = captured;
                        if (Search.Length > 0)
                        {
                            Search = "";
                        }
                        GridScroll = 0f;
                        RefreshTabColours();
                        FilterDirty = true;
                    }
                });
                tab.OnClickTag = c;
                TabButtons.Add(tab);
            }
            RefreshTabColours();
        }

        private void RefreshTabColours()
        {
            foreach (UiButton tab in TabButtons)
            {
                bool active = tab.OnClickTag is Category c && c == ActiveCategory;
                tab.Normal = active ? ShopTheme.TabActive : ShopTheme.Button;
                tab.Hover = active ? ShopTheme.TabActive : ShopTheme.ButtonHover;
                tab.Refresh();
            }
        }

        private void RebuildCards()
        {
            Ui.DestroyChildren(GridContent);
            CardButtons.Clear();
            Cards.Clear();
            PendingIcons.Clear();

            var entries = new List<ShopEntry>();
            if (Data.Entries != null)
            {
                entries.AddRange(Data.Entries);
            }
            var cats = new Dictionary<(ShopEntryKind, int), Category>();
            foreach (ShopEntry e in entries)
            {
                cats[(e.Kind, e.ID)] = CategoryOf(e);
            }
            entries.Sort((x, y) =>
            {
                int c = cats[(x.Kind, x.ID)].CompareTo(cats[(y.Kind, y.ID)]);
                return c != 0 ? c : string.Compare(EntryName(x), EntryName(y), StringComparison.CurrentCultureIgnoreCase);
            });
            foreach (ShopEntry entry in entries)
            {
                Cards.Add(BuildCard(entry, cats[(entry.Kind, entry.ID)]));
            }
        }

        private void ApplyFilter()
        {
            string query = Search.Trim().ToLowerInvariant();
            int visible = 0;
            foreach (Card card in Cards)
            {
                bool show = query.Length > 0
                    ? card.SearchText.Contains(query)
                    : ActiveCategory == Category.All || card.Category == ActiveCategory;
                card.Root.gameObject.SetActive(show);
                if (show)
                {
                    visible++;
                }
            }
            EmptyText.text = query.Length > 0 ? $"Nothing matches \"{Search}\"." : "Nothing to show here.";
            EmptyText.gameObject.SetActive(visible == 0);
            LayoutRebuilder.ForceRebuildLayoutImmediate(GridContent);
            ApplyScroll();
        }

        private Card BuildCard(ShopEntry entry, Category category)
        {
            string name = EntryName(entry);
            var card = new Card
            {
                Entry = entry,
                Category = category,
                Name = name,
                SearchText = (name + " " + CategoryNames[category]).ToLowerInvariant()
            };
            Image bg = Ui.Box(GridContent, "Card " + entry.ID, ShopTheme.Card);
            RectTransform rt = bg.rectTransform;
            card.Root = rt;

            RectTransform iconRt = Ui.Rect("Icon", rt);
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.sizeDelta = new Vector2(116f, 116f);
            iconRt.anchoredPosition = new Vector2(0f, -10f);
            RawImage icon = iconRt.gameObject.AddComponent<RawImage>();
            icon.raycastTarget = false;
            icon.color = new Color(1f, 1f, 1f, 0f);
            PendingIcons.Enqueue((icon, entry));

            // Badges: staple/upgrade/limit (left) and owned count (right).
            string badge = "";
            if ((entry.Flags & ShopEntryFlags.Staple) != 0) badge = "Staple";
            else if ((entry.Flags & ShopEntryFlags.Upgrade) != 0) badge = "Upgrade";
            if ((entry.Flags & ShopEntryFlags.Unique) != 0) badge += badge.Length > 0 ? " • One only" : "One only";
            if (badge.Length > 0)
            {
                TextMeshProUGUI badgeText = Ui.Text(rt, "Badge", badge, 13, TextAlignmentOptions.TopLeft, ShopTheme.Accent);
                Ui.AutoSize(badgeText, 10);
                Place(badgeText.rectTransform, 8f, -6f, -60f, 20f);
            }
            card.Owned = Ui.Text(rt, "Owned", "", 13, TextAlignmentOptions.TopRight, ShopTheme.TextDim);
            Place(card.Owned.rectTransform, 110f, -6f, -8f, 20f);

            // Names shrink to fit two lines; the tooltip always has the full name.
            TextMeshProUGUI nameText = Ui.Text(rt, "Name", name, 18, TextAlignmentOptions.Center);
            Ui.AutoSize(nameText, 11);
            nameText.rectTransform.anchorMin = new Vector2(0f, 1f);
            nameText.rectTransform.anchorMax = new Vector2(1f, 1f);
            nameText.rectTransform.pivot = new Vector2(0.5f, 1f);
            nameText.rectTransform.offsetMin = new Vector2(6f, -178f);
            nameText.rectTransform.offsetMax = new Vector2(-6f, -128f);

            card.Price = Ui.Text(rt, "Price", "", 20, TextAlignmentOptions.Center, ShopTheme.Accent);
            card.Price.rectTransform.anchorMin = new Vector2(0f, 0f);
            card.Price.rectTransform.anchorMax = new Vector2(1f, 0f);
            card.Price.rectTransform.pivot = new Vector2(0.5f, 0f);
            card.Price.rectTransform.offsetMin = new Vector2(6f, 46f);
            card.Price.rectTransform.offsetMax = new Vector2(-6f, 70f);
            UpdateCardPrice(card);
            UpdateCardOwned(card);

            bool canBlueprint = entry.Kind == ShopEntryKind.Appliance;
            card.Add = Ui.Button(rt, "Add", canBlueprint ? "Add" : "Add to cart", 18, () => AddToCart(entry, 1));
            card.Add.Normal = ShopTheme.Good;
            card.Add.Hover = ShopTheme.GoodHover;
            card.Add.Clip = GridViewport;
            card.Add.Rect.anchorMin = new Vector2(0f, 0f);
            card.Add.Rect.anchorMax = new Vector2(canBlueprint ? 0.5f : 1f, 0f);
            card.Add.Rect.pivot = new Vector2(0.5f, 0f);
            card.Add.Rect.offsetMin = new Vector2(6f, 6f);
            card.Add.Rect.offsetMax = new Vector2(canBlueprint ? -3f : -6f, 40f);
            card.Add.Refresh();
            CardButtons.Add(card.Add);

            if (canBlueprint)
            {
                card.Blueprint = Ui.Button(rt, "Blueprint", "Blueprint", 15, () => TakeBlueprint(entry));
                card.Blueprint.Clip = GridViewport;
                card.Blueprint.Rect.anchorMin = new Vector2(0.5f, 0f);
                card.Blueprint.Rect.anchorMax = new Vector2(1f, 0f);
                card.Blueprint.Rect.pivot = new Vector2(0.5f, 0f);
                card.Blueprint.Rect.offsetMin = new Vector2(3f, 6f);
                card.Blueprint.Rect.offsetMax = new Vector2(-6f, 40f);
                card.Blueprint.Refresh();
                CardButtons.Add(card.Blueprint);
            }
            return card;
        }

        private static void Place(RectTransform rt, float left, float top, float right, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, top - height);
            rt.offsetMax = new Vector2(right, top);
        }

        private void UpdateCardPrice(Card card)
        {
            int price = card.Entry.Price;
            card.Price.text = price > 0 ? Coin(price) : "Free";
            card.Price.color = price > Data.Money ? ShopTheme.Bad : ShopTheme.Accent;
        }

        private void UpdateCardOwned(Card card)
        {
            int owned = card.Entry.Kind == ShopEntryKind.Appliance && OwnedById.TryGetValue(card.Entry.ID, out int n) ? n : 0;
            card.Owned.text = owned > 0 ? $"Owned {owned}" : "";
        }

        private void RebuildCart()
        {
            Ui.DestroyChildren(CartContent);
            CartButtons.Clear();
            CartLineRects.Clear();
            if (CartOrder.Count == 0)
            {
                TextMeshProUGUI empty = Ui.Text(CartContent, "Empty", "Your cart is empty.", 20, TextAlignmentOptions.Center, ShopTheme.TextDim);
                Ui.Layout(empty, prefH: 40);
            }
            foreach ((ShopEntryKind, int) key in CartOrder)
            {
                if (!CatalogByKey.TryGetValue(key, out ShopEntry entry))
                {
                    continue;
                }
                int qty = CartQty[key];
                Image line = Ui.Box(CartContent, "Line", ShopTheme.Card);
                Ui.Layout(line, prefH: 46);
                Ui.Group<HorizontalLayoutGroup>(line, 6, new RectOffset(10, 6, 4, 4), TextAnchor.MiddleLeft);
                CartLineRects.Add((line.rectTransform, entry));

                TextMeshProUGUI label = Ui.Text(line.transform, "Name", $"{qty}x {EntryName(entry)}", 18);
                label.enableWordWrapping = false;
                Ui.AutoSize(label, 12);
                Ui.Layout(label, flexW: 1, prefH: 38);
                TextMeshProUGUI cost = Ui.Text(line.transform, "Cost", entry.Price > 0 ? Coin(entry.Price * qty) : "Free", 18, TextAlignmentOptions.Right, ShopTheme.Accent);
                Ui.AutoSize(cost, 12);
                Ui.Layout(cost, prefW: 80, prefH: 38);

                ShopEntry captured = entry;
                var capturedKey = key;
                foreach ((string text, Action act) in new (string, Action)[]
                {
                    ("-", () => AddToCart(captured, -1)),
                    ("+", () => AddToCart(captured, 1)),
                    ("x", () => RemoveFromCart(capturedKey))
                })
                {
                    UiButton b = Ui.Button(line.transform, text, text, 20, act);
                    Ui.Layout(b.Rect, minW: 34, prefW: 34, prefH: 34);
                    b.Clip = CartViewport;
                    if (text == "x")
                    {
                        b.Normal = new Color(0.55f, 0.27f, 0.22f);
                        b.Hover = ShopTheme.Bad;
                        b.Refresh();
                    }
                    CartButtons.Add(b);
                }
            }
            UpdateCartFooter();
        }

        private void UpdateCartFooter()
        {
            int total = CartTotal();
            bool affordable = total <= Data.Money;
            TotalText.text = $"Total: <color=#{(affordable ? "FF9800" : "E05A47")}>{Coin(total)}</color>";
            SubtotalText.text = total > 0
                ? $"Cart -{total}  ->  <color=#{(affordable ? "BFB2A3" : "E05A47")}>{Coin(Data.Money - total)} left</color>"
                : "";
            BuyButton.Interactable = CartOrder.Count > 0 && affordable && AwaitingSequence == 0;
            BuyButton.Refresh();
            ClearButton.Interactable = CartOrder.Count > 0;
            ClearButton.Refresh();
            foreach (Card card in Cards)
            {
                if (card.Blueprint != null)
                {
                    card.Blueprint.Interactable = AwaitingSequence == 0;
                    card.Blueprint.Refresh();
                }
            }
        }

        private void RefreshSellBox(bool force)
        {
            var state = (Data.HeldKind, Data.HeldID, Data.HeldRefund, Data.StateFlags);
            if (!force && state == RenderedHeld)
            {
                return;
            }
            if (state != RenderedHeld)
            {
                SellConfirmUntil = 0f; // holding something else now: re-arm
            }
            RenderedHeld = state;
            bool sellable = Data.HeldKind == HeldKind.Appliance || Data.HeldKind == HeldKind.Parcel;
            SellBox.gameObject.SetActive(Data.HeldKind != HeldKind.None);
            if (!sellable)
            {
                SellText.text = "<color=#BFB2A3>You can only sell appliances and unopened parcels.</color>";
                SellButton.Rect.gameObject.SetActive(false);
                return;
            }
            SellButton.Rect.gameObject.SetActive(true);
            string what = Data.HeldKind == HeldKind.Parcel ? $"a parcel ({ApplianceName(Data.HeldID)})" : ApplianceName(Data.HeldID);
            string note = (Data.StateFlags & ShopStateFlags.UninstallPrep) != 0
                ? "Uninstall prep is on, so refunds are $0."
                : Data.HeldRefund > 0 ? "Refund is what you paid." : "It was free, so there's no refund.";
            SellText.text = $"Holding <b>{what}</b>\n<color=#BFB2A3>{note}</color>";
            bool confirming = Time.unscaledTime <= SellConfirmUntil;
            SellButton.Label.text = confirming
                ? "Click again to confirm"
                : Data.HeldRefund > 0 ? $"Sell for {Coin(Data.HeldRefund)}" : "Get rid of it (no refund)";
            SellButton.Interactable = AwaitingSequence == 0;
            SellButton.Refresh();
        }

        // ================================================================= per-frame UI

        private void LayoutSlot()
        {
            int index = Mathf.Max(0, OpenLocal.IndexOf(this));
            int count = Mathf.Max(1, OpenLocal.Count);
            if (count == 1)
            {
                SlotArea.anchorMin = Vector2.zero;
                SlotArea.anchorMax = Vector2.one;
            }
            else if (count == 2)
            {
                SlotArea.anchorMin = new Vector2(index * 0.5f, 0f);
                SlotArea.anchorMax = new Vector2(index * 0.5f + 0.5f, 1f);
            }
            else
            {
                int col = index % 2;
                int row = index / 2;
                SlotArea.anchorMin = new Vector2(col * 0.5f, 0.5f - row * 0.5f);
                SlotArea.anchorMax = new Vector2(col * 0.5f + 0.5f, 1f - row * 0.5f);
            }
            SlotArea.offsetMin = SlotArea.offsetMax = Vector2.zero;
        }

        private void ApplyScroll()
        {
            float gridMax = Mathf.Max(0f, GridContent.rect.height - GridViewport.rect.height);
            GridScroll = Mathf.Clamp(GridScroll, 0f, gridMax);
            GridContent.anchoredPosition = new Vector2(0f, GridScroll);

            float cartMax = Mathf.Max(0f, CartContent.rect.height - CartViewport.rect.height);
            CartScroll = Mathf.Clamp(CartScroll, 0f, cartMax);
            CartContent.anchoredPosition = new Vector2(0f, CartScroll);
        }

        private void UpdateCursorImage(Vector2 pointer)
        {
            if (CursorImage == null || UsesMouse)
            {
                return;
            }
            CursorImage.SetAsLastSibling();
            CursorImage.position = new Vector3(pointer.x, pointer.y, 0f);
        }

        private ShopEntry? TooltipEntry;

        private void UpdateTooltip(Vector2 pointer)
        {
            ShopEntry? hovered = null;
            if (!OskOpen)
            {
                Card card = CardAt(pointer);
                if (card != null)
                {
                    hovered = card.Entry;
                }
                else if (RectTransformUtility.RectangleContainsScreenPoint(CartViewport, pointer, null))
                {
                    foreach ((RectTransform rect, ShopEntry entry) in CartLineRects)
                    {
                        if (RectTransformUtility.RectangleContainsScreenPoint(rect, pointer, null))
                        {
                            hovered = entry;
                            break;
                        }
                    }
                }
            }
            if (hovered == null)
            {
                Tooltip.gameObject.SetActive(false);
                TooltipEntry = null;
                return;
            }
            if (TooltipEntry == null || !TooltipEntry.Value.Equals(hovered.Value))
            {
                TooltipEntry = hovered;
                TooltipText.text = TooltipFor(hovered.Value, OwnedById);
                LayoutRebuilder.ForceRebuildLayoutImmediate(Tooltip);
            }
            Tooltip.gameObject.SetActive(true);
            Tooltip.SetAsLastSibling();

            // Place beside the pointer, flipping to stay on screen.
            Rect size = ScreenRect(Tooltip);
            float scale = Screen.height / 1080f;
            Vector2 pos = pointer + new Vector2(24f, -12f) * scale;
            if (pos.x + size.width > Screen.width)
            {
                pos.x = pointer.x - size.width - 24f * scale;
            }
            if (pos.y - size.height < 0f)
            {
                pos.y = size.height;
            }
            Tooltip.position = new Vector3(pos.x, pos.y, 0f);
            if (CursorImage != null)
            {
                CursorImage.SetAsLastSibling();
            }
        }

        /// <summary>The same information as the vanilla blueprint tooltip (ApplianceInfoView).</summary>
        private static string TooltipFor(ShopEntry entry, Dictionary<int, int> owned)
        {
            var sb = new StringBuilder();
            GlobalLocalisation loc = GameData.Main.GlobalLocalisation;
            if (entry.Kind == ShopEntryKind.Decor)
            {
                sb.Append("<size=125%><b>").Append(EntryName(entry)).Append("</b></size>\n");
                sb.Append("<color=#BFB2A3>Free wall or floor covering. Use it on a room to apply.</color>");
                return sb.ToString();
            }
            if (!GameData.Main.TryGet(entry.ID, out Appliance a))
            {
                return $"#{entry.ID}";
            }
            sb.Append("<size=125%><b>").Append(a.Name).Append("</b></size>\n");
            int root = RootOf(entry.ID);
            if (root != entry.ID)
            {
                sb.Append("<color=#BFB2A3>Upgrade of ").Append(ApplianceName(root)).Append("</color>\n");
            }
            if (!string.IsNullOrEmpty(a.Description))
            {
                sb.Append("<color=#D9CFC2>").Append(a.Description).Append("</color>\n");
            }

            var tags = new List<string>();
            if (a.Properties != null)
            {
                foreach (IApplianceProperty property in a.Properties)
                {
                    if (property is CGivesDecoration gives)
                    {
                        var icons = new StringBuilder();
                        foreach (DecorationType type in DecorationValues.Types)
                        {
                            for (int j = 0; j < gives.DecorationValues[type]; j++)
                            {
                                icons.Append(loc.GetIcon(type)).Append(' ');
                            }
                        }
                        sb.Append("\n<b>").Append(loc["ADDS_DECORATION"]).Append("</b>  ").Append(icons).Append('\n');
                    }
                    else if (property is CHighlyFlammable)
                    {
                        tags.Add(loc["CHighlyFlammable"]);
                    }
                }
            }
            if (a.Tags != null)
            {
                tags.AddRange(a.Tags);
            }
            if (a.HasUpgrades)
            {
                tags.Add(loc["Upgradable"]);
            }
            if (tags.Count > 0)
            {
                sb.Append("\n<color=#FF9800>").Append(string.Join("  •  ", tags)).Append("</color>\n");
            }
            if (a.Sections != null)
            {
                foreach (Appliance.Section section in a.Sections)
                {
                    sb.Append("\n<b>").Append(section.Title).Append("</b>");
                    if (!string.IsNullOrEmpty(section.RangeDescription))
                    {
                        sb.Append("  <color=#BFB2A3>").Append(section.RangeDescription).Append("</color>");
                    }
                    sb.Append('\n').Append(section.Description).Append('\n');
                }
            }
            sb.Append("\n<b>Price:</b> ").Append(entry.Price > 0 ? Coin(entry.Price) : "Free");
            if (owned.TryGetValue(entry.ID, out int count) && count > 0)
            {
                sb.Append("   <color=#BFB2A3>Owned: ").Append(count).Append("</color>");
            }
            if ((entry.Flags & ShopEntryFlags.Staple) != 0)
            {
                sb.Append("   <color=#BFB2A3>Staple tonight</color>");
            }
            if ((entry.Flags & ShopEntryFlags.Unique) != 0)
            {
                sb.Append("   <color=#BFB2A3>Limit one</color>");
            }
            return sb.ToString().TrimEnd();
        }

        private void LoadSomeIcons()
        {
            for (int i = 0; i < SnapshotsPerFrame && PendingIcons.Count > 0; i++)
            {
                (RawImage image, ShopEntry entry) = PendingIcons.Dequeue();
                if (image == null)
                {
                    continue;
                }
                try
                {
                    if (entry.Kind == ShopEntryKind.Appliance)
                    {
                        if (GameData.Main.TryGet(entry.ID, out Appliance a) && a.Prefab != null)
                        {
                            image.texture = PrefabSnapshot.GetApplianceSnapshot(a.Prefab);
                            image.color = Color.white;
                        }
                    }
                    else if (GameData.Main.TryGet(entry.ID, out Decor d))
                    {
                        // Decor materials have no plain texture or colour to show directly. Use
                        // what vanilla draws for a decor item (ApplianceView with DrawUsing): its
                        // applicator appliance painted with the decor material. Each decor has its
                        // own prefab, so PrefabSnapshot caches them separately.
                        GameObject prefab = GameData.Main.DecoratorPrefabView?.GetPrefab(d);
                        if (prefab != null)
                        {
                            image.texture = PrefabSnapshot.GetApplianceSnapshot(prefab);
                            image.color = Color.white;
                        }
                    }
                }
                catch (Exception e)
                {
                    ShopLog.Debug(() => $"No icon for {entry.Kind} {entry.ID}: {e.Message}");
                }
            }
        }
    }
}

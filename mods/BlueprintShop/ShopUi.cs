using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Kitchen;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlueprintShop
{
    /// <summary>Colours and shared assets for the shop menu.</summary>
    internal static class ShopTheme
    {
        public static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color Panel = new Color32(0x2B, 0x26, 0x22, 0xF5);
        public static readonly Color PanelInner = new Color32(0x36, 0x2F, 0x2A, 0xFF);
        public static readonly Color Card = new Color32(0x44, 0x3B, 0x34, 0xFF);
        public static readonly Color CardHover = new Color32(0x58, 0x4C, 0x42, 0xFF);
        public static readonly Color Text = new Color32(0xF4, 0xEB, 0xDD, 0xFF);
        public static readonly Color TextDim = new Color32(0xBF, 0xB2, 0xA3, 0xFF);
        public static readonly Color Accent = new Color32(0xFF, 0x98, 0x00, 0xFF);
        public static readonly Color Good = new Color32(0x4C, 0xAF, 0x50, 0xFF);
        public static readonly Color GoodHover = new Color32(0x66, 0xC8, 0x6A, 0xFF);
        public static readonly Color Bad = new Color32(0xE0, 0x5A, 0x47, 0xFF);
        public static readonly Color Button = new Color32(0x5D, 0x50, 0x45, 0xFF);
        public static readonly Color ButtonHover = new Color32(0x75, 0x66, 0x58, 0xFF);
        public static readonly Color Disabled = new Color32(0x3E, 0x38, 0x33, 0xFF);
        public static readonly Color TabActive = new Color32(0xFF, 0x98, 0x00, 0xFF);

        private static Sprite _rounded;
        private static TMP_FontAsset _font;
        private static TMP_SpriteAsset _sprites;
        private static bool _fontSearched;

        /// <summary>A 9-sliced rounded rectangle generated at runtime (no asset bundle needed).</summary>
        public static Sprite Rounded
        {
            get
            {
                if (_rounded != null)
                {
                    return _rounded;
                }
                const int size = 32;
                const int radius = 10;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    name = "BlueprintShop Rounded"
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                        float dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        byte a = (byte)(Mathf.Clamp01(radius - dist + 0.5f) * 255f);
                        pixels[y * size + x] = new Color32(255, 255, 255, a);
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                _rounded.name = "BlueprintShop Rounded";
                return _rounded;
            }
        }

        /// <summary>The game's own TMP font (and sprite sheet, for coin/decoration icons).</summary>
        public static TMP_FontAsset Font
        {
            get
            {
                FindFont();
                return _font;
            }
        }

        public static TMP_SpriteAsset Sprites
        {
            get
            {
                FindFont();
                return _sprites;
            }
        }

        private static bool? _hasCoin;

        public static bool HasCoinSprite
        {
            get
            {
                if (_hasCoin == null)
                {
                    _hasCoin = Sprites != null && Sprites.GetSpriteIndexFromName("coin") >= 0;
                }
                return _hasCoin.Value;
            }
        }

        /// <summary>Forget the chosen font so the next menu re-picks it (called when a menu is built).</summary>
        public static void ResetFontSearch()
        {
            _fontSearched = false;
            _font = null;
            _sprites = null;
            _hasCoin = null;
        }

        private static void FindFont()
        {
            // Search once per menu: FindObjectsOfTypeAll is expensive and this runs for every text element.
            if (_fontSearched)
            {
                return;
            }
            _fontSearched = true;
            try
            {
                // Not every game font is a text font: some map letters to icons. Picking the first
                // TextMeshPro found produced scrambled text (v0.1.0). Choose deliberately instead.
                string how = null;

                // 1. Explicit override from BlueprintShop.cfg ("font=<asset name>").
                if (!string.IsNullOrEmpty(ShopLog.FontOverride))
                {
                    foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                    {
                        if (f != null && string.Equals(f.name, ShopLog.FontOverride, StringComparison.OrdinalIgnoreCase))
                        {
                            _font = f;
                            how = "config override";
                            break;
                        }
                    }
                    if (_font == null)
                    {
                        ShopLog.Warn($"Font '{ShopLog.FontOverride}' from the config wasn't found; choosing automatically.");
                    }
                }

                // 2. The shop terminal's own label: on screen whenever a menu opens, and plain text.
                if (_font == null)
                {
                    FieldInfo titleField = AccessTools.Field(typeof(RerollBlueprintView), "Title");
                    foreach (RerollBlueprintView view in UnityEngine.Object.FindObjectsOfType<RerollBlueprintView>())
                    {
                        if (titleField?.GetValue(view) is TextMeshPro title && title.font != null)
                        {
                            _font = title.font;
                            _sprites = title.spriteAsset;
                            how = "shop terminal label";
                            break;
                        }
                    }
                }

                // 3. The font most text in the loaded scenes uses, skipping icon/symbol fonts.
                var counts = new Dictionary<TMP_FontAsset, int>();
                foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
                {
                    if (text == null || text.font == null || !text.gameObject.scene.IsValid())
                    {
                        continue;
                    }
                    counts.TryGetValue(text.font, out int n);
                    counts[text.font] = n + 1;
                    if (_sprites == null && text.spriteAsset != null && text.spriteAsset.GetSpriteIndexFromName("coin") >= 0)
                    {
                        _sprites = text.spriteAsset;
                    }
                }
                if (_font == null)
                {
                    int best = 0;
                    foreach (KeyValuePair<TMP_FontAsset, int> kv in counts)
                    {
                        if (kv.Value > best && !LooksLikeIconFont(kv.Key.name))
                        {
                            best = kv.Value;
                            _font = kv.Key;
                            how = "most used in scene";
                        }
                    }
                }
                if (_font == null)
                {
                    _font = TMP_Settings.defaultFontAsset;
                    how = "TMP default";
                }

                // Coin sprite for prices: any loaded sprite sheet that has one.
                if (_sprites == null || _sprites.GetSpriteIndexFromName("coin") < 0)
                {
                    TMP_SpriteAsset fallback = _sprites;
                    _sprites = null;
                    foreach (TMP_SpriteAsset s in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>())
                    {
                        if (s != null && s.GetSpriteIndexFromName("coin") >= 0)
                        {
                            _sprites = s;
                            break;
                        }
                    }
                    _sprites = _sprites ?? fallback ?? TMP_Settings.defaultSpriteAsset;
                }

                string chosen = $"UI font: {(_font != null ? _font.name : "none")} ({how}), sprites: {(_sprites != null ? _sprites.name : "none")}, coin sprite: {HasCoinSprite}";
                ShopLog.Debug(() => chosen);
                ShopLog.Debug(() =>
                {
                    var sb = new StringBuilder("Fonts in use (set font=<name> in BlueprintShop.cfg to force one):");
                    foreach (KeyValuePair<TMP_FontAsset, int> kv in counts)
                    {
                        sb.Append($"\n  {kv.Key.name} x{kv.Value}");
                    }
                    return sb.ToString();
                });
            }
            catch (Exception e)
            {
                ShopLog.Error("Couldn't find a UI font", e);
            }
        }

        private static bool LooksLikeIconFont(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            return n.Contains("icon") || n.Contains("symbol") || n.Contains("emoji") || n.Contains("glyph") || n.Contains("dingbat");
        }
    }

    /// <summary>
    /// A clickable rectangle. The menu does its own hit testing (mouse position or the
    /// controller's virtual cursor), so no uGUI EventSystem/GraphicRaycaster is involved.
    /// </summary>
    internal class UiButton
    {
        public RectTransform Rect;
        public Image Background;
        public TextMeshProUGUI Label;
        public Action OnClick;
        public bool Interactable = true;
        public Color Normal = ShopTheme.Button;
        public Color Hover = ShopTheme.ButtonHover;
        public Color DisabledColor = ShopTheme.Disabled;
        /// <summary>If set, the button only counts as hit inside this rect (scroll viewports).</summary>
        public RectTransform Clip;
        public bool IsHovered;
        /// <summary>Free slot for the owner's bookkeeping (e.g. which category a tab selects).</summary>
        public object OnClickTag;

        public bool Contains(Vector2 screenPoint)
        {
            if (Rect == null || !Rect.gameObject.activeInHierarchy)
            {
                return false;
            }
            if (Clip != null && !RectTransformUtility.RectangleContainsScreenPoint(Clip, screenPoint, null))
            {
                return false;
            }
            return RectTransformUtility.RectangleContainsScreenPoint(Rect, screenPoint, null);
        }

        public void Refresh()
        {
            if (Background != null)
            {
                Background.color = !Interactable ? DisabledColor : IsHovered ? Hover : Normal;
            }
            if (Label != null)
            {
                Label.color = Interactable ? ShopTheme.Text : ShopTheme.TextDim;
            }
        }
    }

    /// <summary>Small helpers for building uGUI hierarchies from code.</summary>
    internal static class Ui
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Image Box(Transform parent, string name, Color color, bool rounded = true)
        {
            RectTransform rt = Rect(name, parent);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (rounded)
            {
                img.sprite = ShopTheme.Rounded;
                img.type = Image.Type.Sliced;
            }
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size,
            TextAlignmentOptions align = TextAlignmentOptions.Left, Color? color = null)
        {
            RectTransform rt = Rect(name, parent);
            TextMeshProUGUI tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (ShopTheme.Font != null)
            {
                tmp.font = ShopTheme.Font;
            }
            if (ShopTheme.Sprites != null)
            {
                tmp.spriteAsset = ShopTheme.Sprites;
            }
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = color ?? ShopTheme.Text;
            tmp.richText = true;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        /// <summary>Shrink text to fit its box (down to <paramref name="min"/>) instead of cutting it off.</summary>
        public static TextMeshProUGUI AutoSize(TextMeshProUGUI text, float min)
        {
            text.enableAutoSizing = true;
            text.fontSizeMax = text.fontSize;
            text.fontSizeMin = Mathf.Min(min, text.fontSize);
            return text;
        }

        public static UiButton Button(Transform parent, string name, string label, float fontSize, Action onClick)
        {
            Image bg = Box(parent, name, ShopTheme.Button);
            TextMeshProUGUI text = Text(bg.transform, "Label", label, fontSize, TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 6, 6, 2, 2);
            text.enableWordWrapping = false;
            AutoSize(text, Mathf.Max(9f, fontSize * 0.6f));
            var button = new UiButton
            {
                Rect = bg.rectTransform,
                Background = bg,
                Label = text,
                OnClick = onClick
            };
            button.Refresh();
            return button;
        }

        public static LayoutElement Layout(Component c, float minW = -1, float minH = -1, float prefW = -1, float prefH = -1, float flexW = -1, float flexH = -1)
        {
            // Explicit == null: Unity objects override ==, which the ?? operator bypasses.
            LayoutElement le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = c.gameObject.AddComponent<LayoutElement>();
            }
            le.minWidth = minW;
            le.minHeight = minH;
            le.preferredWidth = prefW;
            le.preferredHeight = prefH;
            le.flexibleWidth = flexW;
            le.flexibleHeight = flexH;
            return le;
        }

        public static T Group<T>(Component c, float spacing, RectOffset padding, TextAnchor align = TextAnchor.UpperLeft) where T : HorizontalOrVerticalLayoutGroup
        {
            T g = c.gameObject.AddComponent<T>();
            g.spacing = spacing;
            g.padding = padding ?? new RectOffset();
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            return g;
        }

        public static void DestroyChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
            }
        }
    }
}

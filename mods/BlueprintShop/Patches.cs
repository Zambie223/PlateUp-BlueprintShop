using HarmonyLib;
using Kitchen;
using TMPro;
using UnityEngine;

namespace BlueprintShop
{
    /// <summary>
    /// Supplies the prefab for the shop's custom ViewType. The game looks view prefabs up in
    /// AssetDirectory.ViewPrefabs, which only knows vanilla view types.
    /// </summary>
    [HarmonyPatch(typeof(LocalViewRouter), "GetPrefab")]
    internal static class ShopViewPrefabPatch
    {
        private static GameObject _template;

        private static GameObject _labelTemplate;

        private static GameObject _queueTemplate;

        private static bool Prefix(ViewType view_type, ref GameObject __result)
        {
            if (view_type == ShopIds.LabelViewType)
            {
                if (_labelTemplate == null)
                {
                    _labelTemplate = new GameObject("BlueprintShop Sale Label Template");
                    _labelTemplate.SetActive(false);
                    _labelTemplate.AddComponent<SaleLabelView>();
                    Object.DontDestroyOnLoad(_labelTemplate);
                    ShopLog.Debug(() => "Created sale label template");
                }
                __result = _labelTemplate;
                return false;
            }
            if (view_type == ShopIds.QueueViewType)
            {
                if (_queueTemplate == null)
                {
                    _queueTemplate = new GameObject("BlueprintShop Build Queue Template");
                    _queueTemplate.SetActive(false);
                    _queueTemplate.AddComponent<BuildQueueView>();
                    Object.DontDestroyOnLoad(_queueTemplate);
                    ShopLog.Debug(() => "Created build queue template");
                }
                __result = _queueTemplate;
                return false;
            }
            if (view_type != ShopIds.ViewType)
            {
                return true;
            }
            if (_template == null)
            {
                // Inactive so the template itself never runs; ShopView.Initialise activates each
                // instance the router creates from it.
                _template = new GameObject("BlueprintShop View Template");
                _template.SetActive(false);
                _template.AddComponent<ShopView>();
                Object.DontDestroyOnLoad(_template);
                ShopLog.Debug(() => "Created shop view template");
            }
            __result = _template;
            return false;
        }
    }

    /// <summary>
    /// The shop terminal reuses the reroll trigger's model. The host sends the reroll view a
    /// price of -1 for terminals (see TerminalLabelViewSystem); show "Shop" instead of
    /// "Reroll -1", and swap the dice hologram for a shopping cart (ShopHologram).
    /// </summary>
    [HarmonyPatch(typeof(RerollBlueprintView), "UpdateData", typeof(RerollBlueprintView.ViewData))]
    internal static class TerminalLabelPatch
    {
        private static void Postfix(RerollBlueprintView __instance, RerollBlueprintView.ViewData data, TextMeshPro ___Title)
        {
            if (data.Price == ShopIds.TerminalPriceSentinel)
            {
                if (___Title != null)
                {
                    ___Title.text = "Shop";
                }
                try
                {
                    ShopHologram.Apply(__instance);
                }
                catch (System.Exception e)
                {
                    ShopLog.Error("Couldn't build the shop terminal's hologram; it keeps the dice.", e);
                }
            }
            else
            {
                // Not (or no longer) a shop terminal: make sure it shows its own dice.
                ShopHologram.Remove(__instance);
            }
        }
    }

    /// <summary>
    /// In build mode with a mouse, the crane steers toward the mouse pointer. It reads the
    /// pointer directly (GetMousePosition), so neither consuming input nor the shop's input
    /// lock stops it, and it chased the cursor around behind the shop window. While the
    /// player's shop is open locally, report "no new mouse target", and keep the crane's last
    /// seen pointer position current so it doesn't jump to the pointer when the shop closes
    /// (it follows again once the mouse moves).
    /// </summary>
    [HarmonyPatch(typeof(PlayerCraneMovementComponent), "IsIssuingMouseTarget")]
    internal static class CraneMouseTargetPatch
    {
        private static bool Prefix(int player_id, ref Vector2 ___RecentMousePosition, ref bool __result)
        {
            if (!ShopView.IsOpenLocallyFor(player_id))
            {
                return true;
            }
            if (Controllers.InputSourceIdentifier.DefaultInputSource != null)
            {
                ___RecentMousePosition = Controllers.InputSourceIdentifier.DefaultInputSource.GetMousePosition();
            }
            __result = false;
            return false;
        }
    }
}

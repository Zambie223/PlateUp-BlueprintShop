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

        private static bool Prefix(ViewType view_type, ref GameObject __result)
        {
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
    /// "Reroll -1".
    /// </summary>
    [HarmonyPatch(typeof(RerollBlueprintView), "UpdateData", typeof(RerollBlueprintView.ViewData))]
    internal static class TerminalLabelPatch
    {
        private static void Postfix(RerollBlueprintView.ViewData data, TextMeshPro ___Title)
        {
            if (data.Price == ShopIds.TerminalPriceSentinel && ___Title != null)
            {
                ___Title.text = "Shop";
            }
        }
    }
}

using System;

namespace BlueprintShop
{
    /// <summary>
    /// Base-game appliances the shop sells although the game's letters can't offer them.
    /// Checked in the game's data (all 439 appliances).
    /// </summary>
    internal static class ShopAdditions
    {
        /// <summary>
        /// Upgrades with neither IsPurchasable nor IsPurchasableAsUpgrade, so they're never in the
        /// pool letters draw from, and a research desk is the only way to get them. Sold with
        /// sell_desk_upgrades, once upgrades are unlocked. These are all of them; enchanting desk
        /// results are left out on purpose. Their base appliances have no shop requirements, so
        /// there's nothing else to check.
        /// </summary>
        public static readonly int[] DeskUpgrades =
        {
            Teleporters.ApplianceID, // from the Dumbwaiter, 1250
            524986663,               // Buffet - Heated, from the Buffet, 120
            -1228506595,             // Buffet - Platter, from the Buffet, 120
        };

        /// <summary>
        /// Woks. In the game's pool (purchasable, once you own one, like the Pot Stack), but with
        /// no shopping tags, so no letter ever matches it. Sold with the normal appliances when
        /// the game's own filters leave it in, which means once you own one.
        /// </summary>
        public const int Woks = 314862254;

        public static bool IsDeskUpgrade(int applianceId) => Array.IndexOf(DeskUpgrades, applianceId) >= 0;
    }
}

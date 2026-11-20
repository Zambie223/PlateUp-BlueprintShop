using Kitchen;
using KitchenMods;
using Unity.Entities;

namespace BlueprintShop
{
    internal static class ShopIds
    {
        // Arbitrary values that don't collide with vanilla ViewType values or union keys.
        // "BPSH" in ASCII, plus offsets.
        public const int Base = 0x42505348;

        public static readonly ViewType ViewType = (ViewType)Base;

        public const int ViewDataUnionKey = Base + 1;

        public const int ResponseUnionKey = Base + 2;

        /// <summary>Floating refund label over binned appliances (see SaleLabels.cs).</summary>
        public static readonly ViewType LabelViewType = (ViewType)(Base + 3);

        public const int LabelUnionKey = Base + 4;

        /// <summary>On-screen list of a player's items still to place (see BuildQueue.cs).</summary>
        public static readonly ViewType QueueViewType = (ViewType)(Base + 5);

        public const int QueueUnionKey = Base + 6;

        /// <summary>Price sent to the reroll view of a shop terminal so clients relabel it.</summary>
        public const int TerminalPriceSentinel = -1;
    }

    /// <summary>
    /// Marks a reroll trigger that has been converted into a shop terminal. The entity also
    /// gets CDoNotPersist, so neither the marker nor the converted trigger is ever saved; the
    /// game recreates a vanilla trigger after loading (and the mod converts it again).
    /// </summary>
    public struct CShopTerminal : IModComponent
    {
    }

    /// <summary>
    /// A terminal the mod spawned itself on day 0 (vanilla only makes a reroll trigger from
    /// day 1). Destroyed at the start of the day so it doesn't overlap vanilla's trigger.
    /// </summary>
    public struct CShopTerminalSpawned : IModComponent
    {
    }

    /// <summary>
    /// What the player paid for this appliance (or the parcel it's in), so selling refunds
    /// exactly that. No tag means it was free (starting kit, card rewards): refund 0.
    ///
    /// ONE OF THE TWO COMPONENTS THE MOD WRITES TO SAVE FILES (the other is SUpgradeUnlock).
    /// Unity identifies saved component types by a hash of their name and layout, so:
    ///   - never rename, move (namespace) or change the fields of this struct, or existing
    ///     saves stop loading;
    ///   - a run saved with this tag can't be loaded without the mod. Players who want to
    ///     uninstall mid-run set prepare_uninstall=true first (see ShopLog / README).
    /// </summary>
    public struct CPaidPrice : IModComponent
    {
        public int Price;
    }

    /// <summary>
    /// Singleton: whether upgraded appliances have been unlocked in this run, and the salt for
    /// the "roll to unlock upgrades" rolls (see UpgradeUnlock.cs). Saved with the run, so
    /// upgrades stay unlocked once unlocked and reloading a save doesn't re-roll.
    ///
    /// SAVED TO SAVE FILES, like CPaidPrice: never rename, move or change its fields, and
    /// prepare_uninstall removes it so the run loads without the mod.
    /// </summary>
    public struct SUpgradeUnlock : IModComponent
    {
        public bool Unlocked;
        /// <summary>The night upgrades were unlocked on.</summary>
        public int UnlockedDay;
        public int Salt;
    }

    /// <summary>
    /// A refund label entity floating over one binned appliance (Source). Host-only bookkeeping,
    /// created with CRequiresView (LabelViewType) and CDoNotPersist, so it's never saved.
    /// Nothing is added to the appliance itself, to keep saves free of extra mod data.
    /// </summary>
    public struct CSaleLabel : IModComponent
    {
        public Entity Source;
        public int Refund;
        public bool Required;
    }

    /// <summary>
    /// The view entity of one player's build queue ("place items myself" purchases). Created
    /// with CRequiresView (QueueViewType) and CDoNotPersist; the queue's contents are host-side
    /// state in BuildQueueSystem.
    /// </summary>
    public struct CBuildQueue : IModComponent
    {
        public Entity Player;
        public int PlayerID;
        public int InputSource;
    }

    /// <summary>
    /// One open shop menu, owned by one player. Created with CRequiresView (our ViewType) and
    /// CDoNotPersist; destroyed when the menu closes, the day starts, or the player leaves.
    /// </summary>
    public struct CShopSession : IModComponent
    {
        public Entity Player;

        public int PlayerID;

        public int InputSource;
    }
}

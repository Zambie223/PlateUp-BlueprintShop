using Kitchen;
using KitchenMods;
using Unity.Entities;

namespace BlueprintShop
{
    /// <summary>What the shop's status line says about upgrades (sent to every shop menu).</summary>
    public enum UpgradeState : byte
    {
        /// <summary>Upgrades are on sale; nothing to say.</summary>
        Available = 0,
        /// <summary>Fixed-night mode, not reached yet: "Upgrades unlock on night N".</summary>
        LockedUntilNight = 1,
        /// <summary>Roll mode, not unlocked yet: shows tonight's and next night's chance.</summary>
        LockedRoll = 2,
        /// <summary>Roll mode: tonight's roll unlocked them.</summary>
        UnlockedTonight = 3
    }

    /// <summary>
    /// When upgraded appliances are on sale (host).
    ///
    /// Vanilla (HandleNewShop) rolls floor(day / 5) * 10% per letter, plus every
    /// CUpgradedShopChance card and the red envelopes, to let upgrades into that letter's
    /// draw. The shop has no letters, so it offers upgrades on any night that chance is above
    /// zero: night 5 onward, or earlier with cards. Two modes:
    ///   - Fixed night (default): from upgrade_night (default 5 = vanilla), or earlier with cards.
    ///   - Roll (upgrade_roll): night N has an N x upgrade_chance_per_night % chance, combined
    ///     with the cards' chance the way vanilla combines them.
    /// Either way, once upgrades have been on sale they stay unlocked for the rest of the run
    /// (SUpgradeUnlock, saved with the run), even if the settings change later.
    ///
    /// Rolls are deterministic per (run, night): a hash of the run's salt and the day. So
    /// reopening the shop or reloading the night's autosave can't re-roll. The salt comes from
    /// the run's seed (SFixedSeed, which every run has), so a seeded run unlocks on the same
    /// night for everyone with the same settings.
    /// </summary>
    internal static class UpgradeUnlock
    {
        /// <summary>vanilla's way of adding chances: a + (1 - a) * b.</summary>
        public static float Combine(float a, float b) => a + (1f - a) * b;

        /// <summary>A stable number in [0, 1) for this run and night.</summary>
        public static float Roll(int salt, int day)
        {
            unchecked
            {
                uint h = (uint)salt ^ ((uint)day * 0x9E3779B9u);
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return (h >> 8) / 16777216f;
            }
        }

        /// <summary>A salt from the run's seed (FNV-1a of its text), or 0 without one.</summary>
        public static int SaltFrom(SFixedSeed seed)
        {
            if (!seed.Seed.IsSet)
            {
                return 0;
            }
            unchecked
            {
                uint h = 2166136261u;
                foreach (char c in seed.Seed.Value.ToString())
                {
                    h = (h ^ c) * 16777619u;
                }
                return (int)h;
            }
        }

        /// <summary>Fills in ctx's upgrade fields. extraChance is the cards' and events' chance (0 to 1).</summary>
        public static void Evaluate(ref ShopService.CatalogContext ctx, float extraChance, bool unlocked, int unlockedDay, int salt)
        {
            int day = ctx.Day;
            if (ShopSettings.UpgradeRoll)
            {
                float tonight = Combine(ShopSettings.UpgradeRollChance(day), extraChance);
                ctx.UpgradeChanceTonight = tonight;
                ctx.UpgradeChanceNext = Combine(ShopSettings.UpgradeRollChance(day + 1), extraChance);
                bool rolledTonight = tonight > 0f && Roll(salt, day) < tonight;
                ctx.UpgradesAvailable = unlocked || rolledTonight;
                if (!ctx.UpgradesAvailable)
                {
                    ctx.UpgradeState = UpgradeState.LockedRoll;
                }
                else if (!unlocked || unlockedDay == day)
                {
                    ctx.UpgradeState = UpgradeState.UnlockedTonight;
                }
                else
                {
                    ctx.UpgradeState = UpgradeState.Available;
                }
                return;
            }
            ctx.UpgradesAvailable = unlocked || day >= ShopSettings.UpgradeNight || extraChance > 0f;
            ctx.UpgradeState = ctx.UpgradesAvailable ? UpgradeState.Available : UpgradeState.LockedUntilNight;
        }
    }

    /// <summary>
    /// Host: remembers, in the save, that upgrades have been unlocked, and creates the
    /// SUpgradeUnlock singleton (with the run's salt) at night. prepare_uninstall removes it,
    /// like the price records, so the run loads without the mod.
    /// </summary>
    public class UpgradeUnlockSystem : RestaurantSystem, IModSystem
    {
        /// <summary>The check builds the shop context (a few queries); a few times a second is plenty.</summary>
        private const int CheckEveryFrames = 10;

        private int Frame;

        protected override void OnUpdate()
        {
            if (ShopSettings.PrepareUninstall)
            {
                if (Has<SUpgradeUnlock>())
                {
                    Clear<SUpgradeUnlock>();
                    ShopLog.Info("prepare_uninstall: removed the upgrade-unlock record from this run.");
                }
                return;
            }
            if (!Has<SIsNightTime>() || ++Frame % CheckEveryFrames != 0)
            {
                return;
            }
            if (!Require(out SUpgradeUnlock state))
            {
                int salt = Require(out SFixedSeed seed) && seed.Seed.IsSet
                    ? UpgradeUnlock.SaltFrom(seed)
                    : UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                Set(new SUpgradeUnlock { Salt = salt });
                return;
            }
            if (state.Unlocked)
            {
                return;
            }
            ShopService service = World.GetExistingSystem<ShopService>();
            if (service == null)
            {
                return;
            }
            ShopService.CatalogContext ctx = service.GetContext();
            if (ctx.UpgradesAvailable)
            {
                Set(new SUpgradeUnlock { Unlocked = true, UnlockedDay = ctx.Day, Salt = state.Salt });
                ShopLog.Info(ShopSettings.UpgradeRoll
                    ? $"Upgrades unlocked on night {ctx.Day} by the nightly roll ({ctx.UpgradeChanceTonight:P0} chance)."
                    : $"Upgrades unlocked on night {ctx.Day}.");
            }
        }
    }
}

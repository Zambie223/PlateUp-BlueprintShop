using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace BlueprintShop
{
    /// <summary>
    /// In-game options page for the Mod List mod (Pause > Mods > gear), options API v1. Mod List
    /// finds this class by name; there's no reference to Mod List, and without it this class
    /// is simply never called. Values live in ShopSettings and BlueprintShop.cfg, which stay the
    /// source of truth; the page only reads and changes them, and the file is saved when the
    /// page closes.
    ///
    /// Shop rules and balance settings only matter on the host (the host builds the catalog and
    /// does every purchase and refund), so they're locked when playing in someone else's game.
    ///
    /// Menu text must be plain characters: the game's menu font lacks some (such as ';' and '|'),
    /// and rich-text tags aren't supported.
    /// </summary>
    internal static class ModListOptions
    {
        public static IEnumerable GetOptions()
        {
            Func<bool> notClient = () => Kitchen.Session.NetworkedPlayState != Platforms.NetworkedPlayState.Client;

            var decorChoices = new List<string>();
            var decorLabels = new List<string>();
            for (int i = ShopSettings.MinDecorInterval; i <= ShopSettings.MaxDecorInterval; i++)
            {
                decorChoices.Add(i.ToString(CultureInfo.InvariantCulture));
                decorLabels.Add(i == 1 ? "Every night" : $"Every {i} nights");
            }

            return new List<Dictionary<string, object>>
            {
                Heading("Shop rules"),
                new Dictionary<string, object>
                {
                    ["id"] = "decor_interval",
                    ["type"] = "choice",
                    ["label"] = "Decoration nights",
                    ["description"] = "How often decorations, wallpapers and floors are on sale. Base game: every 5 nights. Host's setting applies.",
                    ["choices"] = decorChoices,
                    ["labels"] = decorLabels,
                    ["get"] = (Func<string>)(() => ShopSettings.DecorInterval.ToString(CultureInfo.InvariantCulture)),
                    ["set"] = (Action<string>)(v =>
                    {
                        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                        {
                            ShopSettings.DecorInterval = n;
                        }
                    }),
                    ["enabled"] = notClient,
                },
                new Dictionary<string, object>
                {
                    ["id"] = "normal_items_on_decor_nights",
                    ["type"] = "bool",
                    ["label"] = "Normal items on decoration nights",
                    ["description"] = "Also sell normal appliances on decoration nights. Base game: Off. Host's setting applies.",
                    ["get"] = (Func<bool>)(() => ShopSettings.NormalItemsOnDecorNights),
                    ["set"] = (Action<bool>)(v => ShopSettings.NormalItemsOnDecorNights = v),
                    ["enabled"] = notClient,
                },
                new Dictionary<string, object>
                {
                    ["id"] = "refund_outside_at_day_start",
                    ["type"] = "bool",
                    ["label"] = "Refund items left outside",
                    ["description"] = "Bought items still outside when the day starts are sold automatically instead of thrown away. Base game: Off. Host's setting applies.",
                    ["get"] = (Func<bool>)(() => ShopSettings.RefundOutsideAtDayStart),
                    ["set"] = (Action<bool>)(v => ShopSettings.RefundOutsideAtDayStart = v),
                    ["enabled"] = notClient,
                },

                Heading("Balance"),
                new Dictionary<string, object>
                {
                    ["id"] = "price_percent",
                    ["type"] = "int",
                    ["label"] = "Price multiplier",
                    ["description"] = "Applies to every shop price, on top of the game's own discounts. 100% is the base game, 0% makes everything free. Click the number to type one. Host's setting applies.",
                    ["min"] = ShopSettings.MinPricePercent,
                    ["max"] = ShopSettings.MaxPricePercent,
                    ["format"] = "{0}%",
                    // Mod List 0.2.0: a draggable bar, and typed entry (click the number or press
                    // confirm), clamped and snapped before set is called. 0.2.0 also raised the
                    // per-picker cap from 1000 values to 1,000,000, so step 1 over 0 to 1000 is fine.
                    ["slider"] = true,
                    ["input"] = true,
                    ["get"] = (Func<int>)(() => ShopSettings.PricePercent),
                    ["set"] = (Action<int>)(v => ShopSettings.PricePercent = v),
                    ["enabled"] = notClient,
                },
                new Dictionary<string, object>
                {
                    ["id"] = "sale_loss_percent",
                    ["type"] = "int",
                    ["label"] = "Sale loss",
                    ["description"] = "How much of the price paid you lose when selling, including the day-start refund. 0% gives it all back. Click the number to type one. Host's setting applies.",
                    ["min"] = ShopSettings.MinSaleLossPercent,
                    ["max"] = ShopSettings.MaxSaleLossPercent,
                    ["format"] = "{0}%",
                    ["slider"] = true,
                    ["input"] = true,
                    ["get"] = (Func<int>)(() => ShopSettings.SaleLossPercent),
                    ["set"] = (Action<int>)(v => ShopSettings.SaleLossPercent = v),
                    ["enabled"] = notClient,
                },

                new Dictionary<string, object>
                {
                    ["id"] = "upgrade_roll",
                    ["type"] = "bool",
                    ["label"] = "Roll to unlock upgrades",
                    ["description"] = "Instead of a fixed night, each night has a growing chance to unlock upgraded appliances. Once unlocked, they stay unlocked. Host's setting applies.",
                    ["get"] = (Func<bool>)(() => ShopSettings.UpgradeRoll),
                    ["set"] = (Action<bool>)(v => ShopSettings.UpgradeRoll = v),
                    ["enabled"] = notClient,
                },
                new Dictionary<string, object>
                {
                    ["id"] = "upgrade_night",
                    ["type"] = "int",
                    ["label"] = "Upgrades unlock on",
                    ["description"] = "The first night upgraded appliances are on sale. Base game: night 5. Cards that raise the upgrade chance still unlock them earlier. Host's setting applies.",
                    ["min"] = ShopSettings.MinUpgradeNight,
                    ["max"] = ShopSettings.MaxUpgradeNight,
                    ["format"] = "Night {0}",
                    ["get"] = (Func<int>)(() => ShopSettings.UpgradeNight),
                    ["set"] = (Action<int>)(v => ShopSettings.UpgradeNight = v),
                    ["enabled"] = notClient,
                    // Only one of the two upgrade options is shown, depending on the roll toggle.
                    ["visible"] = (Func<bool>)(() => !ShopSettings.UpgradeRoll),
                },
                new Dictionary<string, object>
                {
                    ["id"] = "upgrade_chance_per_night",
                    ["type"] = "int",
                    ["label"] = "Upgrade chance per night",
                    ["description"] = "How much the unlock chance grows each night: at 10%, night 1 has a 10% chance, night 2 has 20% and so on. Cards add to it. Host's setting applies.",
                    ["min"] = ShopSettings.MinUpgradeChancePerNight,
                    ["max"] = ShopSettings.MaxUpgradeChancePerNight,
                    ["format"] = "+{0}% per night",
                    ["slider"] = true,
                    ["input"] = true,
                    ["get"] = (Func<int>)(() => ShopSettings.UpgradeChancePerNight),
                    ["set"] = (Action<int>)(v => ShopSettings.UpgradeChancePerNight = v),
                    ["enabled"] = notClient,
                    ["visible"] = (Func<bool>)(() => ShopSettings.UpgradeRoll),
                },

                Heading("Your preferences"),
                new Dictionary<string, object>
                {
                    ["id"] = "place_items_myself",
                    ["type"] = "bool",
                    ["label"] = "Place items myself",
                    ["description"] = "Whether the shop's Place items myself box starts ticked. Only affects this computer.",
                    ["get"] = (Func<bool>)(() => ShopSettings.PlaceMyselfByDefault),
                    ["set"] = (Action<bool>)(v =>
                    {
                        ShopSettings.PlaceMyselfByDefault = v;
                        ShopView.ResetPlaceMyself();
                    }),
                },
                new Dictionary<string, object>
                {
                    ["id"] = "show_sale_labels",
                    ["type"] = "bool",
                    ["label"] = "Floating sale prices",
                    ["description"] = "Show refund prices above bought items placed outside. Only on your screen.",
                    ["get"] = (Func<bool>)(() => ShopSettings.ShowSaleLabels),
                    ["set"] = (Action<bool>)(v => ShopSettings.ShowSaleLabels = v),
                },
                new Dictionary<string, object>
                {
                    ["id"] = "shop_aim_assist",
                    ["type"] = "choice",
                    ["label"] = "Shop menu aim assist",
                    ["description"] = "Controller only. Snaps the shop menu's cursor to nearby buttons. It doesn't affect anything outside the shop menu.",
                    ["choices"] = new[] { "Off", "Low", "Normal", "High" },
                    ["get"] = (Func<string>)(() => ShopSettings.AimAssist.ToString()),
                    ["set"] = (Action<string>)(v =>
                    {
                        if (Enum.TryParse(v, true, out ShopAimAssist a))
                        {
                            ShopSettings.AimAssist = a;
                        }
                    }),
                },
                new Dictionary<string, object>
                {
                    ["id"] = "shop_cursor_speed_percent",
                    ["type"] = "int",
                    ["label"] = "Shop menu cursor speed",
                    ["description"] = "Controller only. How fast the cursor moves in the shop menu.",
                    ["min"] = ShopSettings.MinCursorSpeedPercent,
                    ["max"] = ShopSettings.MaxCursorSpeedPercent,
                    ["step"] = 10,
                    ["format"] = "{0}%",
                    ["get"] = (Func<int>)(() => ShopSettings.CursorSpeedPercent),
                    ["set"] = (Action<int>)(v => ShopSettings.CursorSpeedPercent = v),
                },

                Heading("Wishlists"),
                new Dictionary<string, object>
                {
                    ["id"] = "wishlists_export",
                    ["type"] = "button",
                    ["label"] = "Export wishlists",
                    ["description"] = "Copies all your wishlists to the clipboard as text, to back them up or send to a friend.",
                    ["action"] = (Action)Wishlists.ExportToClipboard,
                },
                new Dictionary<string, object>
                {
                    ["id"] = "wishlists_import",
                    ["type"] = "button",
                    ["label"] = "Import wishlists",
                    ["description"] = "Adds the wishlists copied to the clipboard to yours. A list whose name you already use gets a number added.",
                    ["action"] = (Action)Wishlists.ImportFromClipboard,
                },
                // What the last export or import did. Mod List buttons can't change their text,
                // so each outcome is its own dimmed line, shown by "visible" until the page closes.
                WishResult("wishlists_exported", "Copied to the clipboard", Wishlists.IoResult.Exported),
                WishResult("wishlists_nothing_to_export", "No wishlists to export yet", Wishlists.IoResult.NothingToExport),
                WishResult("wishlists_imported", "Imported from the clipboard", Wishlists.IoResult.Imported),
                WishResult("wishlists_nothing_new", "You already have those lists", Wishlists.IoResult.ImportedNothingNew),
                WishResult("wishlists_nothing_to_import", "No wishlists in the clipboard", Wishlists.IoResult.NothingToImport),
                WishResult("wishlists_failed", "That didn't work: see Player.log", Wishlists.IoResult.Failed),

                Heading("Troubleshooting"),
                new Dictionary<string, object>
                {
                    ["id"] = "verbose",
                    ["type"] = "bool",
                    ["label"] = "Verbose logging",
                    ["description"] = ShopSettings.LaunchVerbose
                        ? "Forced on by the " + ShopSettings.LaunchFlag + " launch option."
                        : "Writes extra detail to Player.log, for bug reports.",
                    ["get"] = (Func<bool>)(() => ShopSettings.Verbose),
                    ["set"] = (Action<bool>)(v => ShopSettings.VerboseSetting = v),
                    ["enabled"] = !ShopSettings.LaunchVerbose,
                },
                new Dictionary<string, object>
                {
                    ["id"] = "prepare_uninstall",
                    ["type"] = "bool",
                    ["label"] = "Prepare to uninstall",
                    ["description"] = "Removes this run's purchase records now, so refunds are 0 even if you turn it off again. After the next night's autosave the run loads without the mod.",
                    ["get"] = (Func<bool>)(() => ShopSettings.PrepareUninstall),
                    ["set"] = (Action<bool>)(v => ShopSettings.PrepareUninstall = v),
                    ["enabled"] = notClient,
                },
                new Dictionary<string, object>
                {
                    ["id"] = "reset",
                    ["type"] = "button",
                    ["label"] = "Reset to defaults",
                    ["description"] = "Puts the shop rules, balance and your preferences back to the defaults. In someone else's game, only your preferences.",
                    ["action"] = (Action)ResetToDefaults,
                },
            };
        }

        public static void OnOptionsClosed()
        {
            ShopSettings.Save();
            Wishlists.LastResult = Wishlists.IoResult.None;
        }

        private static Dictionary<string, object> WishResult(string id, string label, Wishlists.IoResult result) => new Dictionary<string, object>
        {
            ["id"] = id,
            ["type"] = "button",
            ["label"] = label,
            ["action"] = (Action)(() => { }),
            ["enabled"] = false,
            ["visible"] = (Func<bool>)(() => Wishlists.LastResult == result),
        };

        private static Dictionary<string, object> Heading(string label) => new Dictionary<string, object>
        {
            ["type"] = "heading",
            ["label"] = label,
        };

        private static void ResetToDefaults()
        {
            if (Kitchen.Session.NetworkedPlayState != Platforms.NetworkedPlayState.Client)
            {
                ShopSettings.ResetHostSettings();
            }
            ShopSettings.ResetPreferences();
            ShopView.ResetPlaceMyself();
            ShopSettings.Save();
        }
    }
}

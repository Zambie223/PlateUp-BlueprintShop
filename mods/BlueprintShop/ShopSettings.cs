using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace BlueprintShop
{
    /// <summary>How strongly the shop menu's controller cursor snaps to nearby buttons.</summary>
    public enum ShopAimAssist
    {
        Off,
        Low,
        Normal,
        High
    }

    /// <summary>
    /// Every setting, kept in BlueprintShop.cfg next to Player.log
    /// (%USERPROFILE%\AppData\LocalLow\It's Happening\PlateUp\). The file lives in the game's
    /// data folder rather than the mod folder so that updates don't overwrite it. It is the one
    /// source of truth: the in-game options page (Mod List, see ModListOptions) reads and
    /// changes these values and saves them back here.
    ///
    /// Shop rules and balance settings are host settings: only the host's values are used,
    /// because the host builds the catalog and does every purchase and refund. The rest are
    /// per machine and only change what that machine shows.
    /// </summary>
    internal static class ShopSettings
    {
        private const string FileName = "BlueprintShop.cfg";
        public const string LaunchFlag = "-blueprintshop-verbose";

        // Defaults. Every shop rule and balance setting defaults to the base game's behaviour.
        public const int DefaultDecorInterval = 5;
        public const bool DefaultNormalItemsOnDecorNights = false;
        public const bool DefaultRefundOutsideAtDayStart = true;
        public const int DefaultUpgradeNight = 5;
        public const bool DefaultUpgradeRoll = false;
        public const int DefaultUpgradeChancePerNight = 10;
        public const int DefaultPricePercent = 100;
        public const int DefaultSaleLossPercent = 0;
        public const int DefaultCursorSpeedPercent = 100;

        public const int MinDecorInterval = 1, MaxDecorInterval = 20;
        public const int MinUpgradeNight = 1, MaxUpgradeNight = 30;
        public const int MinUpgradeChancePerNight = 1, MaxUpgradeChancePerNight = 100;
        public const int MinPricePercent = 0, MaxPricePercent = 1000;
        public const int MinSaleLossPercent = 0, MaxSaleLossPercent = 100;
        public const int MinCursorSpeedPercent = 50, MaxCursorSpeedPercent = 200;

        public static string ConfigPath { get; private set; } = "(not loaded)";

        // ------------------------------------------------------------- troubleshooting

        /// <summary>The launch option forces verbose logging on, whatever the file says.</summary>
        public static bool LaunchVerbose { get; private set; }

        /// <summary>"verbose" as saved in the file.</summary>
        public static bool VerboseSetting { get; set; }

        public static bool Verbose => LaunchVerbose || VerboseSetting;

        /// <summary>Optional "font=": TMP font asset name for the menu. Config file only.</summary>
        public static string FontOverride { get; private set; }

        /// <summary>
        /// "prepare_uninstall": stop tagging purchases and strip existing CPaidPrice tags, so the
        /// next autosave (start of the next night) produces a save that loads without the mod.
        /// Refunds are $0 while this is on. Host setting.
        /// </summary>
        public static bool PrepareUninstall { get; set; }

        // ------------------------------------------------------------- shop rules (host)

        private static int _decorInterval = DefaultDecorInterval;
        private static int _upgradeNight = DefaultUpgradeNight;
        private static int _upgradeChancePerNight = DefaultUpgradeChancePerNight;
        private static int _pricePercent = DefaultPricePercent;
        private static int _saleLossPercent = DefaultSaleLossPercent;
        private static int _cursorSpeedPercent = DefaultCursorSpeedPercent;

        /// <summary>Decorations, wallpapers and floors are in the shop every Nth night (vanilla 5).</summary>
        public static int DecorInterval
        {
            get => _decorInterval;
            set => _decorInterval = Mathf.Clamp(value, MinDecorInterval, MaxDecorInterval);
        }

        /// <summary>Decoration nights also list the normal appliances (vanilla: decorations only).</summary>
        public static bool NormalItemsOnDecorNights { get; set; } = DefaultNormalItemsOnDecorNights;

        /// <summary>Refund bought items still outside at the start of the day (vanilla deletes them for $0).</summary>
        public static bool RefundOutsideAtDayStart { get; set; } = DefaultRefundOutsideAtDayStart;

        /// <summary>
        /// First night upgraded appliances are on sale. Vanilla's base upgrade chance is
        /// floor(day / 5) * 10%, which is above zero from night 5. Cards that add an upgrade
        /// chance still unlock them earlier, as in vanilla.
        /// </summary>
        public static int UpgradeNight
        {
            get => _upgradeNight;
            set => _upgradeNight = Mathf.Clamp(value, MinUpgradeNight, MaxUpgradeNight);
        }

        /// <summary>
        /// Instead of a fixed night, roll each night to unlock upgrades: night N has an
        /// N x UpgradeChancePerNight % chance (capped at 100%). Once unlocked they stay unlocked
        /// for the run (SUpgradeUnlock). UpgradeNight isn't used while this is on.
        /// </summary>
        public static bool UpgradeRoll { get; set; } = DefaultUpgradeRoll;

        public static int UpgradeChancePerNight
        {
            get => _upgradeChancePerNight;
            set => _upgradeChancePerNight = Mathf.Clamp(value, MinUpgradeChancePerNight, MaxUpgradeChancePerNight);
        }

        /// <summary>The roll chance on a night, from the setting alone (0 to 1), before cards.</summary>
        public static float UpgradeRollChance(int day) =>
            day <= 0 ? 0f : Mathf.Clamp01(day * UpgradeChancePerNight / 100f);

        // ------------------------------------------------------------- balance (host)

        /// <summary>Applied on top of the game's own price and discounts. 100 = vanilla; 0 makes everything free.</summary>
        public static int PricePercent
        {
            get => _pricePercent;
            set => _pricePercent = Mathf.Clamp(value, MinPricePercent, MaxPricePercent);
        }

        /// <summary>Share of the price paid that's lost when selling. 0 = full refund.</summary>
        public static int SaleLossPercent
        {
            get => _saleLossPercent;
            set => _saleLossPercent = Mathf.Clamp(value, MinSaleLossPercent, MaxSaleLossPercent);
        }

        /// <summary>What selling something bought for <paramref name="paid"/> gives back (rounded down).</summary>
        public static int RefundFor(int paid)
        {
            if (paid <= 0)
            {
                return 0;
            }
            return (int)((long)paid * (100 - SaleLossPercent) / 100);
        }

        // ------------------------------------------------------------- this machine only

        /// <summary>Whether "Place items myself" starts ticked. The tick box can still be changed per visit.</summary>
        public static bool PlaceMyselfByDefault { get; set; }

        /// <summary>Show the floating refund labels on items outside.</summary>
        public static bool ShowSaleLabels { get; set; } = true;

        public static ShopAimAssist AimAssist { get; set; } = ShopAimAssist.Normal;

        /// <summary>The shop menu's controller cursor speed. 100 = the original speed.</summary>
        public static int CursorSpeedPercent
        {
            get => _cursorSpeedPercent;
            set => _cursorSpeedPercent = Mathf.Clamp(value, MinCursorSpeedPercent, MaxCursorSpeedPercent);
        }

        /// <summary>Reference pixels within which the controller cursor snaps to a button.</summary>
        public static float SnapRadius
        {
            get
            {
                switch (AimAssist)
                {
                    case ShopAimAssist.Off: return 0f;
                    case ShopAimAssist.Low: return 26f;
                    case ShopAimAssist.High: return 66f;
                    default: return 44f;
                }
            }
        }

        /// <summary>Shop rules and balance back to their defaults (the base game's behaviour).</summary>
        public static void ResetHostSettings()
        {
            DecorInterval = DefaultDecorInterval;
            NormalItemsOnDecorNights = DefaultNormalItemsOnDecorNights;
            UpgradeNight = DefaultUpgradeNight;
            UpgradeRoll = DefaultUpgradeRoll;
            UpgradeChancePerNight = DefaultUpgradeChancePerNight;
            RefundOutsideAtDayStart = DefaultRefundOutsideAtDayStart;
            PricePercent = DefaultPricePercent;
            SaleLossPercent = DefaultSaleLossPercent;
        }

        /// <summary>This computer's preferences back to their defaults.</summary>
        public static void ResetPreferences()
        {
            PlaceMyselfByDefault = false;
            ShowSaleLabels = true;
            AimAssist = ShopAimAssist.Normal;
            CursorSpeedPercent = DefaultCursorSpeedPercent;
        }

        // ------------------------------------------------------------- the file

        /// <summary>One key in the file: how to write it, read it, and the comment written above it.</summary>
        private sealed class Entry
        {
            public string Key;
            public string Comment;
            public Func<string> Write;
            /// <summary>Returns false when the text isn't a valid value (the old value is kept).</summary>
            public Func<string, bool> Read;
        }

        private static readonly Entry[] Entries =
        {
            Bool("verbose", "verbose=true writes detailed diagnostics to Player.log (for bug reports).\n" +
                 "The launch option " + LaunchFlag + " does the same without editing this file.",
                 () => VerboseSetting, v => VerboseSetting = v),
            Bool("prepare_uninstall", "prepare_uninstall=true: set this, play into the next night (it autosaves), then the run\n" +
                 "can be loaded without the mod. Purchase records are removed straight away; turning it off\n" +
                 "again doesn't bring them back. Refunds are $0 while it's on.",
                 () => PrepareUninstall, v => PrepareUninstall = v),
            Int("decor_interval", "decor_interval=N: decorations, wallpapers and floors are in the shop every Nth night\n" +
                "(" + MinDecorInterval + " to " + MaxDecorInterval + "; vanilla: 5).",
                () => DecorInterval, v => DecorInterval = v, MinDecorInterval, MaxDecorInterval),
            Bool("normal_items_on_decor_nights", "normal_items_on_decor_nights=true: decoration nights also sell normal appliances (vanilla: false).",
                () => NormalItemsOnDecorNights, v => NormalItemsOnDecorNights = v),
            Int("upgrade_night", "upgrade_night=N: upgraded appliances are on sale from night N (" + MinUpgradeNight + " to " + MaxUpgradeNight + "; vanilla: 5).\n" +
                "Cards that raise the upgrade chance can still unlock them earlier.",
                () => UpgradeNight, v => UpgradeNight = v, MinUpgradeNight, MaxUpgradeNight),
            Bool("upgrade_roll", "upgrade_roll=true: instead of upgrade_night, roll each night to unlock upgrades. Night N has an\n" +
                 "N x upgrade_chance_per_night % chance. Once unlocked, they stay unlocked for the run.",
                 () => UpgradeRoll, v => UpgradeRoll = v),
            Int("upgrade_chance_per_night", "upgrade_chance_per_night=N: with upgrade_roll, how much the chance grows each night (" +
                MinUpgradeChancePerNight + " to " + MaxUpgradeChancePerNight + "; 10 = 10% on night 1, 20% on night 2...).",
                () => UpgradeChancePerNight, v => UpgradeChancePerNight = v, MinUpgradeChancePerNight, MaxUpgradeChancePerNight),
            Bool("refund_outside_at_day_start", "refund_outside_at_day_start=true: bought items still outside when the day starts are refunded\n" +
                 "(at the sale_loss_percent rate) instead of being thrown away for nothing as in vanilla.",
                 () => RefundOutsideAtDayStart, v => RefundOutsideAtDayStart = v),
            Int("price_percent", "price_percent=N: every shop price is multiplied by N% (" + MinPricePercent + " to " + MaxPricePercent + "; vanilla: 100),\n" +
                "on top of the game's own discounts. 0 makes everything free.",
                () => PricePercent, v => PricePercent = v, MinPricePercent, MaxPricePercent),
            Int("sale_loss_percent", "sale_loss_percent=N: selling gives back the price paid minus N% (" + MinSaleLossPercent + " to " + MaxSaleLossPercent + "; 0 = full refund).",
                () => SaleLossPercent, v => SaleLossPercent = v, MinSaleLossPercent, MaxSaleLossPercent),
            Bool("place_items_myself", "The settings below only affect this computer.\n" +
                 "place_items_myself=true: the shop's \"Place items myself\" box starts ticked.",
                 () => PlaceMyselfByDefault, v => PlaceMyselfByDefault = v),
            Bool("show_sale_labels", "show_sale_labels=false hides the floating refund prices on items outside.",
                 () => ShowSaleLabels, v => ShowSaleLabels = v),
            new Entry
            {
                Key = "shop_aim_assist",
                Comment = "shop_aim_assist=Off/Low/Normal/High: how strongly the shop menu's controller cursor snaps to buttons.\n" +
                          "It only affects the shop menu, not gameplay.",
                Write = () => AimAssist.ToString(),
                Read = s =>
                {
                    foreach (ShopAimAssist a in (ShopAimAssist[])Enum.GetValues(typeof(ShopAimAssist)))
                    {
                        if (string.Equals(a.ToString(), s, StringComparison.OrdinalIgnoreCase))
                        {
                            AimAssist = a;
                            return true;
                        }
                    }
                    return false;
                }
            },
            Int("shop_cursor_speed_percent", "shop_cursor_speed_percent=N: the shop menu's controller cursor speed (" + MinCursorSpeedPercent + " to " + MaxCursorSpeedPercent + ").",
                () => CursorSpeedPercent, v => CursorSpeedPercent = v, MinCursorSpeedPercent, MaxCursorSpeedPercent),
        };

        private const string Header =
            "# Blueprint Shop mod settings. These can also be changed in game with the Mod List mod\n" +
            "# (Pause > Mods > the gear next to Blueprint Shop).\n" +
            "# font=<name> forces the menu font (verbose mode logs the available names).\n" +
            "# decor_interval to sale_loss_percent are host settings: in online games the host's values apply.\n";

        private static Entry Bool(string key, string comment, Func<bool> get, Action<bool> set) => new Entry
        {
            Key = key,
            Comment = comment,
            Write = () => get() ? "true" : "false",
            Read = s =>
            {
                if (!bool.TryParse(s, out bool v))
                {
                    return false;
                }
                set(v);
                return true;
            }
        };

        private static Entry Int(string key, string comment, Func<int> get, Action<int> set, int min, int max) => new Entry
        {
            Key = key,
            Comment = comment,
            Write = () => get().ToString(CultureInfo.InvariantCulture),
            Read = s =>
            {
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || v < min || v > max)
                {
                    return false;
                }
                set(v);
                return true;
            }
        };

        private static Entry Find(string key)
        {
            foreach (Entry e in Entries)
            {
                if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return e;
                }
            }
            return null;
        }

        private static bool TrySplit(string raw, out string key, out string value)
        {
            key = value = null;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
            {
                return false;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                return false;
            }
            key = line.Substring(0, eq).Trim();
            value = line.Substring(eq + 1).Trim();
            return true;
        }

        /// <summary>Reads the launch options and the file, then adds any setting the file doesn't have yet.</summary>
        public static void Load()
        {
            try
            {
                foreach (string arg in Environment.GetCommandLineArgs())
                {
                    if (string.Equals(arg, LaunchFlag, StringComparison.OrdinalIgnoreCase))
                    {
                        LaunchVerbose = true;
                    }
                }
            }
            catch (Exception e)
            {
                ShopLog.Warn($"Could not read launch arguments: {e.Message}");
            }

            try
            {
                ConfigPath = Path.Combine(Application.persistentDataPath, FileName);
                if (File.Exists(ConfigPath))
                {
                    foreach (string raw in File.ReadAllLines(ConfigPath))
                    {
                        if (!TrySplit(raw, out string key, out string value))
                        {
                            continue;
                        }
                        if (string.Equals(key, "font", StringComparison.OrdinalIgnoreCase))
                        {
                            FontOverride = value;
                            continue;
                        }
                        Entry entry = Find(key);
                        if (entry != null && !entry.Read(value))
                        {
                            ShopLog.Warn($"{entry.Key}='{value}' in {FileName} isn't a valid value; using {entry.Write()}.");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ShopLog.Warn($"Could not read config '{ConfigPath}': {e.Message}");
            }
            // Creates the file, or adds settings that are newer than the file.
            Save();
        }

        /// <summary>
        /// Writes the current values back. Lines for known settings are updated in place, so the
        /// player's comments, the font line and anything unknown are kept; settings missing from
        /// the file are added with their comment. The file is only written when it changes.
        /// </summary>
        public static void Save()
        {
            if (ConfigPath == "(not loaded)")
            {
                return;
            }
            try
            {
                bool exists = File.Exists(ConfigPath);
                string old = exists ? File.ReadAllText(ConfigPath) : null;
                var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sb = new StringBuilder();
                if (exists)
                {
                    string[] lines = old.Replace("\r\n", "\n").Split('\n');
                    // Split leaves one empty item after a final newline; don't double it up.
                    int count = lines.Length > 0 && lines[lines.Length - 1].Length == 0 ? lines.Length - 1 : lines.Length;
                    for (int i = 0; i < count; i++)
                    {
                        Entry entry = TrySplit(lines[i], out string key, out _) ? Find(key) : null;
                        if (entry != null)
                        {
                            sb.Append(entry.Key).Append('=').Append(entry.Write()).Append('\n');
                            written.Add(entry.Key);
                        }
                        else
                        {
                            sb.Append(lines[i]).Append('\n');
                        }
                    }
                }
                else
                {
                    sb.Append(Header);
                }
                foreach (Entry entry in Entries)
                {
                    if (written.Contains(entry.Key))
                    {
                        continue;
                    }
                    foreach (string c in entry.Comment.Split('\n'))
                    {
                        sb.Append("# ").Append(c).Append('\n');
                    }
                    sb.Append(entry.Key).Append('=').Append(entry.Write()).Append('\n');
                }

                string text = sb.ToString();
                if (exists && string.Equals(text.Replace("\r\n", "\n"), old.Replace("\r\n", "\n"), StringComparison.Ordinal))
                {
                    return;
                }
                // Write a temporary file first, so a failed write can't leave a half-written config.
                string temp = ConfigPath + ".tmp";
                File.WriteAllText(temp, text);
                File.Copy(temp, ConfigPath, overwrite: true);
                File.Delete(temp);
            }
            catch (Exception e)
            {
                ShopLog.Warn($"Could not save config '{ConfigPath}': {e.Message}");
            }
        }

        /// <summary>One line for the startup log.</summary>
        public static string Summary() =>
            $"verbose logging {(Verbose ? "ON" : "off")}, decorations every {DecorInterval} night(s), " +
            $"normal items on decoration nights {(NormalItemsOnDecorNights ? "on" : "off")}, " +
            (UpgradeRoll ? $"upgrades by nightly roll (+{UpgradeChancePerNight}% per night), " : $"upgrades from night {UpgradeNight}, ") +
            $"prices {PricePercent}%, sale loss {SaleLossPercent}%, day-start refund {(RefundOutsideAtDayStart ? "on" : "off")}" +
            (PrepareUninstall ? ", PREPARE UNINSTALL ON" : "") + $", config: {ConfigPath}";
    }
}

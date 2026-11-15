using System;
using System.IO;
using UnityEngine;

namespace BlueprintShop
{
    /// <summary>
    /// Logging for the mod. Everything goes to Player.log
    /// (%USERPROFILE%\AppData\LocalLow\It's Happening\PlateUp\Player.log).
    ///
    /// Verbose logging is OFF by default. Turn it on with either:
    ///   - the launch option  -blueprintshop-verbose   (Steam: Properties > Launch Options), or
    ///   - "verbose=true" in BlueprintShop.cfg, next to Player.log (created on first run).
    /// The config lives in the game's data folder rather than the mod folder so that
    /// Workshop updates don't overwrite it.
    /// </summary>
    internal static class ShopLog
    {
        private const string Prefix = "[" + ModMain.Name + "] ";
        private const string LaunchFlag = "-blueprintshop-verbose";
        private const string ConfigFileName = "BlueprintShop.cfg";

        public static bool Verbose { get; private set; }

        public static string ConfigPath { get; private set; } = "(not loaded)";

        /// <summary>Optional "font=" from the config: TMP font asset name to use for the menu.</summary>
        public static string FontOverride { get; private set; }

        /// <summary>
        /// "prepare_uninstall=true": stop tagging purchases and strip existing CPaidPrice tags,
        /// so the next autosave (start of the next night) produces a save that loads without
        /// the mod. Refunds are $0 while this is on.
        /// </summary>
        public static bool PrepareUninstall { get; private set; }

        /// <summary>
        /// "decor_interval=N": decorations, wallpapers and floors are in the shop every Nth night
        /// (night N, 2N, ...). Vanilla's decoration night is every 5th. Host's setting applies.
        /// </summary>
        public static int DecorInterval { get; private set; } = 3;

        /// <summary>
        /// "normal_items_on_decor_nights=true": decoration nights also list the normal
        /// appliances. Vanilla offers only decorations then. Host's setting applies.
        /// </summary>
        public static bool NormalItemsOnDecorNights { get; private set; } = true;

        private static readonly string DefaultShopRulesText =
            "# decor_interval=N: decorations, wallpapers and floors are in the shop every Nth night (vanilla: 5).\n" +
            "decor_interval=3\n" +
            "# normal_items_on_decor_nights=true: decoration nights also sell normal appliances (vanilla: false).\n" +
            "# In online games the host's settings apply.\n" +
            "normal_items_on_decor_nights=true\n";

        public static void LoadConfig()
        {
            bool verbose = false;
            try
            {
                foreach (string arg in Environment.GetCommandLineArgs())
                {
                    if (string.Equals(arg, LaunchFlag, StringComparison.OrdinalIgnoreCase))
                    {
                        verbose = true;
                    }
                }
            }
            catch (Exception e)
            {
                Warn($"Could not read launch arguments: {e.Message}");
            }

            try
            {
                ConfigPath = Path.Combine(Application.persistentDataPath, ConfigFileName);
                if (File.Exists(ConfigPath))
                {
                    bool hasShopRules = false;
                    foreach (string raw in File.ReadAllLines(ConfigPath))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#"))
                        {
                            continue;
                        }
                        int eq = line.IndexOf('=');
                        if (eq <= 0)
                        {
                            continue;
                        }
                        string key = line.Substring(0, eq).Trim();
                        string value = line.Substring(eq + 1).Trim();
                        if (string.Equals(key, "verbose", StringComparison.OrdinalIgnoreCase)
                            && bool.TryParse(value, out bool parsed))
                        {
                            verbose |= parsed;
                        }
                        else if (string.Equals(key, "font", StringComparison.OrdinalIgnoreCase))
                        {
                            FontOverride = value;
                        }
                        else if (string.Equals(key, "prepare_uninstall", StringComparison.OrdinalIgnoreCase)
                            && bool.TryParse(value, out bool uninstall))
                        {
                            PrepareUninstall = uninstall;
                        }
                        else if (string.Equals(key, "decor_interval", StringComparison.OrdinalIgnoreCase))
                        {
                            hasShopRules = true;
                            if (int.TryParse(value, out int interval) && interval >= 1)
                            {
                                DecorInterval = interval;
                            }
                            else
                            {
                                Warn($"decor_interval '{value}' isn't a whole number of 1 or more; using {DecorInterval}.");
                            }
                        }
                        else if (string.Equals(key, "normal_items_on_decor_nights", StringComparison.OrdinalIgnoreCase))
                        {
                            hasShopRules = true;
                            if (bool.TryParse(value, out bool normal))
                            {
                                NormalItemsOnDecorNights = normal;
                            }
                        }
                    }
                    if (!hasShopRules)
                    {
                        // Config from an older version: add the newer settings so they can be found.
                        File.AppendAllText(ConfigPath, "\n" + DefaultShopRulesText);
                    }
                }
                else
                {
                    File.WriteAllText(ConfigPath,
                        "# Blueprint Shop mod settings\n" +
                        "# verbose=true writes detailed diagnostics to Player.log (for bug reports).\n" +
                        "# The launch option " + LaunchFlag + " does the same without editing this file.\n" +
                        "verbose=false\n" +
                        "# font=<name> forces the menu font (verbose mode logs the available names).\n" +
                        "# prepare_uninstall=true: set this, play into the next night (it autosaves), then the run\n" +
                        "# can be loaded without the mod. Refunds are $0 while it's on.\n" +
                        "prepare_uninstall=false\n" +
                        DefaultShopRulesText);
                }
            }
            catch (Exception e)
            {
                Warn($"Could not read or create config '{ConfigPath}': {e.Message}");
            }

            Verbose = verbose;
        }

        public static void Info(string message) => UnityEngine.Debug.Log(Prefix + message);

        public static void Warn(string message) => UnityEngine.Debug.LogWarning(Prefix + message);

        public static void Error(string message) => UnityEngine.Debug.LogError(Prefix + message);

        public static void Error(string message, Exception e)
        {
            UnityEngine.Debug.LogError(Prefix + message);
            UnityEngine.Debug.LogException(e);
        }

        /// <summary>Verbose-only. Takes a lambda so the string isn't built when logging is off.</summary>
        public static void Debug(Func<string> message)
        {
            if (!Verbose)
            {
                return;
            }
            try
            {
                UnityEngine.Debug.Log(Prefix + "[verbose] " + message());
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(Prefix + "[verbose] log message threw: " + e.Message);
            }
        }
    }
}

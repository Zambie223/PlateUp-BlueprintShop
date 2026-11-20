using System;
using UnityEngine;

namespace BlueprintShop
{
    /// <summary>
    /// Logging for the mod. Everything goes to Player.log
    /// (%USERPROFILE%\AppData\LocalLow\It's Happening\PlateUp\Player.log).
    ///
    /// Verbose logging is OFF by default. Turn it on with either:
    ///   - the launch option  -blueprintshop-verbose   (Steam: Properties > Launch Options), or
    ///   - "verbose=true" in BlueprintShop.cfg (see ShopSettings), or the in-game options page.
    /// </summary>
    internal static class ShopLog
    {
        private const string Prefix = "[" + ModMain.Name + "] ";

        /// <summary>See ShopSettings: the launch option or "verbose=true" in BlueprintShop.cfg.</summary>
        public static bool Verbose => ShopSettings.Verbose;

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

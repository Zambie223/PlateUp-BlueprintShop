using System;
using System.Reflection;
using HarmonyLib;
using KitchenMods;

namespace BlueprintShop
{
    /// <summary>
    /// Entry point. Replaces PlateUp's random nightly blueprint letters with a shop menu that
    /// lists every blueprint the game would currently be able to offer, in unlimited quantity.
    ///
    /// Overview (see README "Blueprint Shop" for the player-facing description):
    ///   Host:    InterceptShopRequests removes the vanilla random letter/decor requests,
    ///            ShopService turns the reroll trigger into a shop terminal, builds the catalog
    ///            and executes orders, ShopViewSystem talks to the menu views.
    ///   Network: ShopNetworkPatches teaches the game's MessagePack unions about our two
    ///            message types; ShopViewPrefabPatch supplies the view prefab for our ViewType.
    ///   Client:  ShopView builds the uGUI menu for the local player who opened it.
    /// </summary>
    public class ModMain : IModInitializer
    {
        public const string Id = "zambie.blueprintshop";
        public const string Name = "Blueprint Shop";
        public const string Version = "0.4.0";

        private static Harmony _harmony;
        private static bool _resolverRegistered;

        public void PostActivate(KitchenMods.Mod mod)
        {
            ShopLog.LoadConfig();
            ShopLog.Info($"v{Version} loaded (verbose logging {(ShopLog.Verbose ? "ON" : "off")}, decorations every {ShopLog.DecorInterval} night(s), normal items on decoration nights {(ShopLog.NormalItemsOnDecorNights ? "on" : "off")}, config: {ShopLog.ConfigPath})");

            RegisterAssemblyResolver();

            if (_harmony == null)
            {
                _harmony = new Harmony(Id);
                _harmony.PatchAll(typeof(ModMain).Assembly);
                foreach (MethodBase method in _harmony.GetPatchedMethods())
                {
                    ShopLog.Debug(() => $"Patched {method.DeclaringType?.FullName}.{method.Name}");
                }
            }
        }

        public void PreInject() { }

        public void PostInject() { }

        /// <summary>
        /// Client -> host responses carry the response's System.Type, serialized by name.
        /// Mod assemblies are loaded from bytes, so Type.GetType(name) on the receiving side
        /// can't find them by itself. Point the runtime at this (already loaded) assembly.
        /// </summary>
        private static void RegisterAssemblyResolver()
        {
            if (_resolverRegistered)
            {
                return;
            }
            _resolverRegistered = true;
            Assembly self = typeof(ModMain).Assembly;
            string selfName = self.GetName().Name;
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    if (new AssemblyName(args.Name).Name == selfName)
                    {
                        ShopLog.Debug(() => $"Resolved assembly request '{args.Name}'");
                        return self;
                    }
                }
                catch (Exception)
                {
                    // Malformed names are not ours.
                }
                return null;
            };
        }
    }
}

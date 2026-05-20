using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BnlPlugins.Launcher
{
    /// <summary>
    /// BepInEx plugin for Block N Load community server support.
    /// Uses Harmony runtime patches only — NO files are modified on disk,
    /// so Steam never detects changes and never triggers verification.
    ///
    /// Patches applied:
    ///   - EACToolInitializer.IsEACRuntime() → always returns true
    ///   - EACToolInitializer.Initialize()/KickPlayer()/leave hooks → disabled
    ///   - Writes servers.txt for community server selection
    /// </summary>
    [BepInPlugin("bnl.community.launcher", "BNL Launcher Patches", "1.1.0")]
    public class LauncherPlugin : BaseUnityPlugin
    {
        internal static string DefaultServerHost = "v310.blocknload.pauldh.nl";
        internal static int DefaultServerPort = 28100;
        internal static ManualLogSource Log = null!;
        internal static string CardTexturesDir = string.Empty;
        internal static string TextureMapPath = string.Empty;
        private static readonly Dictionary<string, string> ShopOverrideMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> ShopOverrideSprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> LoggedMissingIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _mapLoaded;

        private void Awake()
        {
            Log = Logger;
            LoadConfig();
            InitializeImageOverridePaths();

            string gameRoot = Paths.GameRootPath;
            Logger.Log(BepInEx.Logging.LogLevel.Info, "[BNL Launcher] Game root: " + gameRoot);

            WriteServersFile(gameRoot);
            ApplyHarmonyPatches();

            Logger.Log(BepInEx.Logging.LogLevel.Info,
                string.Format("[BNL Launcher] Ready. Server: {0}:{1}",
                    DefaultServerHost, DefaultServerPort));
        }

        private void LoadConfig()
        {
            string cfgPath = Path.Combine(
                Path.Combine(Paths.GameRootPath, "BepInEx"),
                Path.Combine("config", "BnlPlugins.Launcher.cfg"));

            if (!File.Exists(cfgPath))
            {
                File.WriteAllText(cfgPath,
                    "[Server]\r\n" +
                    "host=v310.blocknload.pauldh.nl\r\n" +
                    "port=28100\r\n");
                return;
            }

            foreach (string line in File.ReadAllLines(cfgPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#") || trimmed.StartsWith("[") || !trimmed.Contains("="))
                    continue;

                int eq = trimmed.IndexOf('=');
                string key = trimmed.Substring(0, eq).Trim().ToLowerInvariant();
                string val = trimmed.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "host":
                        if (!string.IsNullOrEmpty(val) && val.Trim().Length > 0)
                            DefaultServerHost = val;
                        break;
                    case "port":
                        if (int.TryParse(val, out int p))
                            DefaultServerPort = p;
                        break;
                }
            }
        }

        private void WriteServersFile(string gameRoot)
        {
            string parentRoot = Path.GetDirectoryName(gameRoot) ?? gameRoot;
            string serversPath = Path.Combine(parentRoot, "servers.txt");
            string content = string.Format("public {0} {1}", DefaultServerHost, DefaultServerPort);

            try
            {
                File.WriteAllText(serversPath, content + Environment.NewLine, new UTF8Encoding(false));
                Logger.Log(BepInEx.Logging.LogLevel.Info,
                    "[BNL Launcher] Wrote servers.txt: " + content);
            }
            catch (Exception ex)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Error,
                    "[BNL Launcher] Failed to write servers.txt: " + ex.Message);
            }
        }

        private void InitializeImageOverridePaths()
        {
            CardTexturesDir = Path.Combine(Paths.GameRootPath, @"BepInEx\plugins\CardTextures");
            Directory.CreateDirectory(CardTexturesDir);
            TextureMapPath = Path.Combine(CardTexturesDir, "texture-map.txt");
        }

        private void ApplyHarmonyPatches()
        {
            try
            {
                var harmony = new Harmony("bnl.community.launcher");

                // Patch 1: Make IsEACRuntime always return true so the game
                // never kicks for missing EAC.
                var eacType = Type.GetType("EACToolInitializer, Assembly-CSharp");
                if (eacType != null)
                {
                    var isEacRuntime = FindInstanceMethod(eacType, "IsEACRuntime", 0);
                    var initialize = FindInstanceMethod(eacType, "Initialize", 0);
                    var onStartInitialize = FindInstanceMethod(eacType, "OnStartInitialize", 0);
                    var onStateChanged = FindInstanceMethod(eacType, "OnStateChanged", 2);
                    var onLoadCompleted = FindInstanceMethod(eacType, "OnLoadCompleted", 2);
                    var kickPlayer = FindInstanceMethod(eacType, "KickPlayer", 1);
                    var onLeaveServer = FindInstanceMethod(eacType, "OnLeaveServer", 0);
                    var onLeaveApp = FindInstanceMethod(eacType, "OnLeaveApp", 0);
                    if (isEacRuntime != null)
                    {
                        harmony.Patch(isEacRuntime,
                            postfix: new HarmonyMethod(typeof(EacPatches).GetMethod("IsEACRuntime_Postfix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: EACToolInitializer.IsEACRuntime → always true");
                    }
                    if (initialize != null)
                    {
                        harmony.Patch(initialize,
                            prefix: new HarmonyMethod(typeof(EacPatches).GetMethod("Initialize_Prefix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: EACToolInitializer.Initialize → skipped");
                    }
                    else
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Warning,
                            "[BNL Launcher] Could not find EACToolInitializer.Initialize");
                    }
                    if (onStartInitialize != null)
                    {
                        harmony.Patch(onStartInitialize,
                            prefix: new HarmonyMethod(typeof(EacPatches).GetMethod("OnStartInitialize_Prefix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: EACToolInitializer.OnStartInitialize → skipped");
                    }
                    if (onStateChanged != null)
                    {
                        harmony.Patch(onStateChanged,
                            prefix: new HarmonyMethod(typeof(EacPatches).GetMethod("OnStateChanged_Prefix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: EACToolInitializer.OnStateChanged → skipped");
                    }
                    if (onLoadCompleted != null)
                    {
                        harmony.Patch(onLoadCompleted,
                            prefix: new HarmonyMethod(typeof(EacPatches).GetMethod("OnLoadCompleted_Prefix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: EACToolInitializer.OnLoadCompleted → skipped");
                    }
                    if (kickPlayer != null)
                    {
                        harmony.Patch(kickPlayer,
                            prefix: new HarmonyMethod(typeof(EacPatches).GetMethod("KickPlayer_Prefix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: EACToolInitializer.KickPlayer → blocked");
                    }
                    if (onLeaveServer != null)
                    {
                        harmony.Patch(onLeaveServer,
                            prefix: new HarmonyMethod(typeof(EacPatches).GetMethod("OnLeaveServer_Prefix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: EACToolInitializer.OnLeaveServer → blocked");
                    }
                    if (onLeaveApp != null)
                    {
                        harmony.Patch(onLeaveApp,
                            prefix: new HarmonyMethod(typeof(EacPatches).GetMethod("OnLeaveApp_Prefix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: EACToolInitializer.OnLeaveApp → blocked");
                    }
                }

                var guiSpriteResourcesType = Type.GetType("GuiSpriteResources, Assembly-CSharp");
                if (guiSpriteResourcesType != null)
                {
                    var getShopImage = guiSpriteResourcesType.GetMethod("GetShopImage",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (getShopImage != null)
                    {
                        harmony.Patch(getShopImage,
                            prefix: new HarmonyMethod(typeof(ShopImagePatches).GetMethod("GetShopImage_Prefix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: GuiSpriteResources.GetShopImage → external override support");
                    }
                }

                var steamLoginType = Type.GetType("SteamLogin, Assembly-CSharp");
                if (steamLoginType != null)
                {
                    var initServerMethod = FindMethod(steamLoginType, "Init", 0);
                    if (initServerMethod != null)
                    {
                        harmony.Patch(initServerMethod,
                            postfix: new HarmonyMethod(typeof(LauncherParityPatches).GetMethod("SteamInit_Postfix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: SteamLogin.Init → selected server override");
                    }
                    else
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Warning,
                            "[BNL Launcher] Could not find SteamLogin.Init");
                    }
                }

                var playerDataType = Type.GetType("PlayerData, Assembly-CSharp");
                if (playerDataType != null)
                {
                    var isNoobGetter = FindInstanceMethod(playerDataType, "get_IsNoob", 0);
                    if (isNoobGetter != null)
                    {
                        harmony.Patch(isNoobGetter,
                            postfix: new HarmonyMethod(typeof(LauncherParityPatches).GetMethod("PlayerDataIsNoob_Postfix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: PlayerData.IsNoob → false");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Error,
                    "[BNL Launcher] Harmony error: " + ex.Message);
            }
        }

        internal static Sprite? TryResolveShopOverride(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
                return null;

            EnsureShopOverrideMapLoaded();

            string mappedPath;
            if (!ShopOverrideMap.TryGetValue(iconName, out mappedPath))
                return null;

            Sprite cachedSprite;
            if (ShopOverrideSprites.TryGetValue(iconName, out cachedSprite) && cachedSprite != null)
                return cachedSprite;

            string fullPath = Path.IsPathRooted(mappedPath)
                ? mappedPath
                : Path.Combine(CardTexturesDir, mappedPath);

            if (!File.Exists(fullPath))
            {
                if (LoggedMissingIcons.Add(iconName))
                    Log.LogWarning("[BNL Launcher] Shop override file missing for " + iconName + ": " + fullPath);
                return null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                texture.name = "shop_override_" + iconName;
                if (!texture.LoadImage(bytes))
                {
                    Log.LogWarning("[BNL Launcher] Failed to decode shop override for " + iconName + ": " + fullPath);
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                var sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                sprite.name = "shop_override_" + iconName;
                ShopOverrideSprites[iconName] = sprite;
                Log.LogInfo("[BNL Launcher] Loaded external shop sprite " + iconName + " from " + fullPath);
                return sprite;
            }
            catch (Exception ex)
            {
                Log.LogError("[BNL Launcher] Failed loading shop override for " + iconName + ": " + ex.Message);
                return null;
            }
        }

        private static void EnsureShopOverrideMapLoaded()
        {
            if (_mapLoaded)
                return;

            _mapLoaded = true;
            ShopOverrideMap.Clear();

            if (!File.Exists(TextureMapPath))
            {
                Log.LogInfo("[BNL Launcher] No texture-map.txt found at " + TextureMapPath);
                return;
            }

            foreach (string rawLine in File.ReadAllLines(TextureMapPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || !line.Contains("="))
                    continue;

                int eqIndex = line.IndexOf('=');
                if (eqIndex <= 0 || eqIndex >= line.Length - 1)
                    continue;

                string key = line.Substring(0, eqIndex).Trim();
                string value = line.Substring(eqIndex + 1).Trim();
                if (key.Length == 0 || value.Length == 0)
                    continue;

                ShopOverrideMap[key] = value;
            }

            Log.LogInfo("[BNL Launcher] Loaded " + ShopOverrideMap.Count + " shop image override mappings.");
        }

        private static MethodInfo? FindInstanceMethod(Type type, string name, int parameterCount)
        {
            return type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == parameterCount);
        }

        private static MethodInfo? FindMethod(Type type, string name, int parameterCount)
        {
            return type
                .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == parameterCount);
        }
    }

    /// <summary>
    /// Harmony patch methods for EAC bypass.
    /// </summary>
    public static class EacPatches
    {
        /// <summary>
        /// Forces IsEACRuntime to always return true. The game thinks EAC is
        /// running and never kicks the player.
        /// </summary>
        public static void IsEACRuntime_Postfix(ref bool __result)
        {
            __result = true;
        }

        public static bool Initialize_Prefix()
        {
            LauncherPlugin.Log.LogInfo("[BNL Launcher] Skipping EACToolInitializer.Initialize");
            return false;
        }

        public static bool OnStartInitialize_Prefix()
        {
            LauncherPlugin.Log.LogInfo("[BNL Launcher] Skipping EACToolInitializer.OnStartInitialize");
            return false;
        }

        public static bool OnStateChanged_Prefix()
        {
            return false;
        }

        public static bool OnLoadCompleted_Prefix()
        {
            return false;
        }

        public static bool KickPlayer_Prefix(object reason)
        {
            LauncherPlugin.Log.LogWarning("[BNL Launcher] Blocked KickPlayer reason=" + reason);
            return false;
        }

        public static bool OnLeaveServer_Prefix()
        {
            LauncherPlugin.Log.LogInfo("[BNL Launcher] Blocked EACToolInitializer.OnLeaveServer");
            return false;
        }

        public static bool OnLeaveApp_Prefix()
        {
            LauncherPlugin.Log.LogInfo("[BNL Launcher] Blocked EACToolInitializer.OnLeaveApp");
            return false;
        }
    }

    public static class ShopImagePatches
    {
        public static bool GetShopImage_Prefix(string sprite, ref Sprite __result)
        {
            Sprite overrideSprite = LauncherPlugin.TryResolveShopOverride(sprite);
            if (overrideSprite == null)
                return true;

            __result = overrideSprite;
            return false;
        }
    }

    public static class LauncherParityPatches
    {
        public static void SteamInit_Postfix()
        {
            try
            {
                object loginLogic = GetSingletonInstance("LoginLogic");
                object playerData = GetSingletonInstance("PlayerData");
                if (loginLogic == null || playerData == null)
                    return;

                FieldInfo selectorField = loginLogic.GetType().GetField("ServerSelector",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo masterServerField = playerData.GetType().GetField("MasterServer",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Type helperType = Type.GetType("ServerSelector.ServerSelectorHelper, Assembly-CSharp");
                if (selectorField == null || masterServerField == null || helperType == null)
                    return;

                object selector = selectorField.GetValue(loginLogic);
                if (selector == null)
                    return;

                MethodInfo getSelectedServer = helperType.GetMethod("GetSelectedServer",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (getSelectedServer == null)
                    return;

                object selectedServer = getSelectedServer.Invoke(null, new[] { selector });
                if (selectedServer == null)
                    return;

                masterServerField.SetValue(playerData, selectedServer);
                LauncherPlugin.Log.LogInfo("[BNL Launcher] Overrode PlayerData.MasterServer from server selector");
            }
            catch (Exception ex)
            {
                LauncherPlugin.Log.LogError("[BNL Launcher] Failed to override MasterServer: " + ex.Message);
            }
        }

        public static void PlayerDataIsNoob_Postfix(ref bool __result)
        {
            __result = false;
        }

        private static object GetSingletonInstance(string typeName)
        {
            Type targetType = Type.GetType(typeName + ", Assembly-CSharp");
            Type singletonOpenType = Type.GetType("Singleton`1, Assembly-CSharp");
            if (targetType == null || singletonOpenType == null)
                return null;

            Type singletonType = singletonOpenType.MakeGenericType(targetType);
            PropertyInfo instanceProperty = singletonType.GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public);
            return instanceProperty != null ? instanceProperty.GetValue(null, null) : null;
        }
    }
}

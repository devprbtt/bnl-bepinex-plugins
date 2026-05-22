using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
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
    [BepInPlugin("bnl.community.launcher", "! BNL Launcher Patches", "1.4.0")]
    public class LauncherPlugin : BaseUnityPlugin
    {
        internal const string CurrentVersion = "1.4.0";
        private const string GitHubRepo = "devprbtt/bnl-bepinex-plugins";
        private const string LatestVersionUrl = "https://raw.githubusercontent.com/devprbtt/bnl-bepinex-plugins/master/latest-version.txt";

        internal static string DefaultServerHost = "v310.blocknload.pauldh.nl";
        internal static int DefaultServerPort = 28100;
        internal static ManualLogSource Log = null!;
        internal static string CardTexturesDir = string.Empty;
        internal static string PluginDir = string.Empty;
        private static readonly Dictionary<string, string> ShopOverrideFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> ShopOverrideSprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> LoggedMissingIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _filesIndexed;
        private static bool _updateCheckDone;
        private static string _versionFilePath = string.Empty;
        private static string _lastCheckFilePath = string.Empty;
        private static bool _checkRequested;
        private static int _lastManualCheckFrame = -1;
        private ConfigEntry<bool>? _checkForUpdatesNowEntry;

        private void Awake()
        {
            Log = Logger;

            // Unity 5 Mono may not support TLS 1.2. We try, but if it fails,
            // the update check will use Unity's WWW class which handles TLS independently.
            try
            {
                System.Net.ServicePointManager.SecurityProtocol =
                    (System.Net.SecurityProtocolType)4080; // 0xFF0 = all protocols
            }
            catch { }

            LoadConfig();
            InitializeImageOverridePaths();

            string gameRoot = Paths.GameRootPath;
            Logger.Log(BepInEx.Logging.LogLevel.Info, "[BNL Launcher] Game root: " + gameRoot);

            WriteServersFile(gameRoot);
            ApplyHarmonyPatches();
            StartUpdateCheck();

            Logger.Log(BepInEx.Logging.LogLevel.Info,
                string.Format("[BNL Launcher] Ready v{0}. Server: {1}:{2}",
                    CurrentVersion, DefaultServerHost, DefaultServerPort));
        }

        private void Update()
        {
            if (_checkForUpdatesNowEntry != null && _checkForUpdatesNowEntry.Value)
            {
                _checkForUpdatesNowEntry.Value = false;
                if (!_checkRequested)
                    QueueManualUpdateCheck("ConfigurationManager");
            }

            if (!_checkRequested)
                return;

            _checkRequested = false;
            StartCoroutine(CheckForUpdatesCoroutine(true));
        }

        private void QueueManualUpdateCheck(string source)
        {
            if (_lastManualCheckFrame == Time.frameCount)
                return;

            _lastManualCheckFrame = Time.frameCount;
            Logger.Log(BepInEx.Logging.LogLevel.Info,
                "[BNL Launcher] Manual update check requested via " + source);
            _checkRequested = true;
        }

        private void LoadConfig()
        {
            _checkForUpdatesNowEntry = Config.Bind(
                "Updates",
                "Check For Updates Now",
                false,
                new ConfigDescription(
                    "Run an update check immediately.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        Order = 100,
                        HideDefaultButton = true,
                        HideSettingName = true,
                        CustomDrawer = _ =>
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("Run an update check now.", GUILayout.ExpandWidth(true));
                            bool clicked = GUILayout.Button("Check Now", GUILayout.Width(140f));
                            GUILayout.EndHorizontal();

                            if (clicked && !_checkRequested)
                            {
                                _checkForUpdatesNowEntry.Value = false;
                                QueueManualUpdateCheck("ConfigurationManager");
                            }
                        }
                    }));

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
            PluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            CardTexturesDir = Path.Combine(Path.Combine(PluginDir, "Launcher"), "CardTextures");
            Directory.CreateDirectory(CardTexturesDir);
            _versionFilePath = Path.Combine(Path.Combine(PluginDir, "Launcher"), "version.txt");
            _lastCheckFilePath = Path.Combine(Path.Combine(PluginDir, "Launcher"), "last_check.txt");
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
                            "[BNL Launcher] Patched: SteamLogin.Init → server override");
                    }
                    else
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Warning,
                            "[BNL Launcher] Could not find SteamLogin.Init");
                    }

                    var initFinalizeMethod = steamLoginType.GetMethod("<Init>m__641",
                        BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (initFinalizeMethod != null)
                    {
                        harmony.Patch(initFinalizeMethod,
                            postfix: new HarmonyMethod(typeof(LauncherParityPatches).GetMethod("SteamInitFinalize_Postfix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: SteamLogin.<Init>m__641 → late server override");
                    }
                    else
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Warning,
                            "[BNL Launcher] Could not find SteamLogin.<Init>m__641");
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

                var threadNetworkClientType = Type.GetType("ThreadNetworkClient, Assembly-CSharp");
                if (threadNetworkClientType != null)
                {
                    var connectMethod = threadNetworkClientType.GetMethod("Connect",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(string), typeof(int) },
                        null);
                    if (connectMethod != null)
                    {
                        harmony.Patch(connectMethod,
                            prefix: new HarmonyMethod(typeof(LauncherParityPatches).GetMethod("ThreadNetworkClientConnect_Prefix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: ThreadNetworkClient.Connect → master endpoint override");
                    }
                    else
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Warning,
                            "[BNL Launcher] Could not find ThreadNetworkClient.Connect(string,int)");
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

            EnsureShopOverrideFilesIndexed();

            string fullPath;
            if (!TryResolveOverridePath(iconName, out fullPath))
                return null;

            Sprite cachedSprite;
            if (ShopOverrideSprites.TryGetValue(iconName, out cachedSprite) && cachedSprite != null)
                return cachedSprite;

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

        private static void EnsureShopOverrideFilesIndexed()
        {
            if (_filesIndexed)
                return;

            _filesIndexed = true;
            ShopOverrideFiles.Clear();

            if (!Directory.Exists(CardTexturesDir))
            {
                Log.LogInfo("[BNL Launcher] No CardTextures directory found at " + CardTexturesDir);
                return;
            }

            string[] allowedExtensions = { ".png", ".jpg", ".jpeg" };
            foreach (string path in Directory.GetFiles(CardTexturesDir))
            {
                string extension = Path.GetExtension(path);
                if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    continue;

                string key = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(key))
                    continue;

                ShopOverrideFiles[key] = path;
            }

            Log.LogInfo("[BNL Launcher] Indexed " + ShopOverrideFiles.Count + " card override image files from " + CardTexturesDir);
        }

        private static bool TryResolveOverridePath(string iconName, out string fullPath)
        {
            foreach (string candidate in GetOverrideCandidates(iconName))
            {
                if (ShopOverrideFiles.TryGetValue(candidate, out fullPath))
                    return true;
            }

            fullPath = string.Empty;
            return false;
        }

        private static IEnumerable<string> GetOverrideCandidates(string iconName)
        {
            yield return iconName;

            if (iconName.StartsWith("shop_item_", StringComparison.OrdinalIgnoreCase))
            {
                yield return iconName.Substring("shop_item_".Length);
            }

            if (iconName.StartsWith("shop_", StringComparison.OrdinalIgnoreCase))
            {
                yield return iconName.Substring("shop_".Length);
            }
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

        // ── Update Check ────────────────────────────────────────────

        private void StartUpdateCheck()
        {
            if (_updateCheckDone)
                return;

            // Read installed version from file
            string installedVersion = CurrentVersion;
            if (File.Exists(_versionFilePath))
            {
                try
                {
                    string fileVersion = File.ReadAllText(_versionFilePath).Trim();
                    if (!string.IsNullOrEmpty(fileVersion))
                        installedVersion = fileVersion;
                }
                catch { }
            }

            // Write version.txt only if it doesn't exist yet — the installer is
            // the authority on what release is installed; we must not overwrite it.
            if (!File.Exists(_versionFilePath))
            {
                try { File.WriteAllText(_versionFilePath, CurrentVersion); }
                catch { }
            }

            // Check rate limit — only auto-check once per 24 hours
            bool shouldCheck = true;
            if (File.Exists(_lastCheckFilePath))
            {
                try
                {
                    string lastCheckStr = File.ReadAllText(_lastCheckFilePath).Trim();
                    if (long.TryParse(lastCheckStr, out long lastCheckTicks))
                    {
                        var lastCheck = new DateTime(lastCheckTicks, DateTimeKind.Utc);
                        if ((DateTime.UtcNow - lastCheck).TotalHours < 24)
                        {
                            shouldCheck = false;
                            Logger.Log(BepInEx.Logging.LogLevel.Info,
                                "[BNL Launcher] Skipping update check (last check: " +
                                lastCheck.ToLocalTime().ToString("g") + ")");
                        }
                    }
                }
                catch { }
            }

            if (shouldCheck)
            {
                StartCoroutine(CheckForUpdatesCoroutine(false));
            }
        }

        /// <summary>Force an update check, bypassing rate limit. Call from UI or config.</summary>
        internal static void RequestUpdateCheck()
        {
            _checkRequested = true;
        }

        private System.Collections.IEnumerator CheckForUpdatesCoroutine(bool force)
        {
            yield return new WaitForSeconds(10f);

            if (!force && _updateCheckDone)
                yield break;

            _updateCheckDone = true;

            string installedVersion = CurrentVersion;
            if (File.Exists(_versionFilePath))
            {
                try
                {
                    string fileVersion = File.ReadAllText(_versionFilePath).Trim();
                    if (!string.IsNullOrEmpty(fileVersion))
                        installedVersion = fileVersion;
                }
                catch { }
            }

            Logger.Log(BepInEx.Logging.LogLevel.Info,
                "[BNL Launcher] Update check backend: local installer");

            try
            {
                File.WriteAllText(_lastCheckFilePath, DateTime.UtcNow.Ticks.ToString());
            }
            catch { }

            string installerPath = Path.Combine(Path.Combine(PluginDir, "Launcher"), "BNL-Installer.exe");

            // Always refresh the local installer from the latest release so the
            // spawned exe has the full up-to-date plugin list in its manifest.
            string installerUrl =
                "https://github.com/" + GitHubRepo + "/releases/latest/download/BNL-Installer.exe";
            string tempInstaller = installerPath + ".tmp";
            string? downloadError = null;
            yield return DownloadUrlToFile(installerUrl, tempInstaller, err => downloadError = err);
            if (downloadError == null && File.Exists(tempInstaller))
            {
                try
                {
                    if (File.Exists(installerPath)) File.Delete(installerPath);
                    File.Move(tempInstaller, installerPath);
                    Logger.Log(BepInEx.Logging.LogLevel.Info,
                        "[BNL Launcher] Refreshed local installer from latest release");
                }
                catch (Exception ex)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Warning,
                        "[BNL Launcher] Could not replace local installer: " + ex.Message);
                    try { if (File.Exists(tempInstaller)) File.Delete(tempInstaller); } catch { }
                }
            }
            else
            {
                Logger.Log(BepInEx.Logging.LogLevel.Warning,
                    "[BNL Launcher] Could not refresh installer (" + (downloadError ?? "no file") +
                    "), using existing copy");
                try { if (File.Exists(tempInstaller)) File.Delete(tempInstaller); } catch { }
            }

            if (!File.Exists(installerPath))
            {
                Logger.Log(BepInEx.Logging.LogLevel.Warning,
                    "[BNL Launcher] Update check skipped: installer not found at " + installerPath);
                yield break;
            }

            string gameRoot = Paths.GameRootPath;
            string args = "--check-updates --game-path=\"" + gameRoot + "\" --current-version=" + installedVersion;
            if (!force)
                args += " --silent-no-update";

            try
            {
                Logger.Log(BepInEx.Logging.LogLevel.Info,
                    "[BNL Launcher] Launching installer update check: " + installerPath);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(installerPath) ?? PluginDir
                });
            }
            catch (Exception ex)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Warning,
                    "[BNL Launcher] Failed to launch installer update check: " + ex.Message);
            }
        }

        private static string ExtractJsonString(string json, string key)
        {
            string search = "\"" + key + "\":\"";
            int start = json.IndexOf(search, StringComparison.Ordinal);
            if (start < 0)
            {
                // Try with "key": " (with space)
                search = "\"" + key + "\": \"";
                start = json.IndexOf(search, StringComparison.Ordinal);
            }
            if (start < 0)
                return null;

            start += search.Length;
            int end = json.IndexOf("\"", start, StringComparison.Ordinal);
            if (end < 0)
                return null;

            return UnescapeJson(json.Substring(start, end - start));
        }

        // Extract the "body" field which may contain escaped characters and newlines
        private static string ExtractJsonBody(string json)
        {
            string search = "\"body\":\"";
            int start = json.IndexOf(search, StringComparison.Ordinal);
            if (start < 0)
            {
                search = "\"body\": \"";
                start = json.IndexOf(search, StringComparison.Ordinal);
            }
            if (start < 0)
                return null;

            start += search.Length;

            // Scan forward handling escape sequences to find the real end
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    if (next == 'n') { sb.Append('\n'); i++; }
                    else if (next == 'r') { sb.Append('\r'); i++; }
                    else if (next == 't') { sb.Append('\t'); i++; }
                    else if (next == '"') { sb.Append('"'); i++; }
                    else if (next == '\\') { sb.Append('\\'); i++; }
                    else { sb.Append(c); }
                }
                else if (c == '"')
                {
                    break; // End of string
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static string UnescapeJson(string str)
        {
            if (str.IndexOf('\\') < 0)
                return str;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == '\\' && i + 1 < str.Length)
                {
                    char next = str[i + 1];
                    if (next == 'n') { sb.Append('\n'); i++; }
                    else if (next == 'r') { sb.Append('\r'); i++; }
                    else if (next == 't') { sb.Append('\t'); i++; }
                    else if (next == '"') { sb.Append('"'); i++; }
                    else if (next == '\\') { sb.Append('\\'); i++; }
                    else { sb.Append(str[i]); }
                }
                else
                {
                    sb.Append(str[i]);
                }
            }
            return sb.ToString();
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            try
            {
                string[] latestParts = latest.Split('.');
                string[] currentParts = current.Split('.');
                int maxLen = Math.Max(latestParts.Length, currentParts.Length);

                for (int i = 0; i < maxLen; i++)
                {
                    int l = i < latestParts.Length ? int.Parse(latestParts[i]) : 0;
                    int c = i < currentParts.Length ? int.Parse(currentParts[i]) : 0;
                    if (l > c) return true;
                    if (l < c) return false;
                }

                return false;
            }
            catch
            {
                // Fallback: string comparison
                return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0;
            }
        }

        private void ShowUpdateNotification(string newVersion, string releaseUrl,
            string currentVersion, string releaseName, string releaseBody, string? installerDownloadUrl)
        {
            var go = new GameObject("BNL_UpdateNotifier");
            DontDestroyOnLoad(go);
            go.AddComponent<UpdateNotifier>().Initialize(
                newVersion, releaseUrl, currentVersion, releaseName, releaseBody, installerDownloadUrl);
        }

        private System.Collections.IEnumerator FetchUrlWithPowerShell(string url, Action<string> onSuccess, Action<string> onError)
        {
            string stdout = "";
            string? fetchError = null;

            yield return FetchUrlWithWww(url, result =>
            {
                stdout = result ?? "";
            }, error =>
            {
                fetchError = error;
            });

            if (!string.IsNullOrEmpty(fetchError))
            {
                Log.Log(BepInEx.Logging.LogLevel.Warning,
                    "[BNL Launcher] WWW fetch failed, falling back to curl.exe: " + fetchError);

                fetchError = null;
                stdout = "";

                yield return FetchUrlWithCurl(url, result =>
                {
                    stdout = result ?? "";
                }, error =>
                {
                    fetchError = error;
                });
            }

            if (!string.IsNullOrEmpty(fetchError))
            {
                Log.Log(BepInEx.Logging.LogLevel.Warning,
                    "[BNL Launcher] curl.exe fetch failed, falling back to PowerShell: " + fetchError);

                fetchError = null;
                stdout = "";

                yield return FetchUrlWithPowerShellFallback(url, result =>
                {
                    stdout = result ?? "";
                }, error =>
                {
                    fetchError = error;
                });

                if (!string.IsNullOrEmpty(fetchError))
                {
                    onError(fetchError);
                    yield break;
                }
            }

            Logger.Log(BepInEx.Logging.LogLevel.Info,
                "[BNL Launcher] Update fetch completed successfully");
            onSuccess(stdout);
        }

        private static System.Collections.IEnumerator FetchUrlWithWww(string url, Action<string> onSuccess, Action<string> onError)
        {
            Log.Log(BepInEx.Logging.LogLevel.Info,
                "[BNL Launcher] Launching WWW fetch for " + url);

            using (var www = new WWW(url))
            {
                while (!www.isDone)
                    yield return null;

                if (!string.IsNullOrEmpty(www.error))
                {
                    onError(www.error);
                    yield break;
                }

                string text = www.text ?? "";
                Log.Log(BepInEx.Logging.LogLevel.Info,
                    "[BNL Launcher] WWW fetch completed successfully. Bytes=" + text.Length);
                onSuccess(text);
            }
        }

        private static System.Collections.IEnumerator FetchUrlWithCurl(string url, Action<string> onSuccess, Action<string> onError)
        {
            string tempOutput = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bnl-update-check-curl.txt");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = "--fail --location --silent --show-error -A \"BNL-Launcher\" -o \"" + tempOutput + "\" \"" + url + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            System.Diagnostics.Process? proc = null;
            try
            {
                Log.Log(BepInEx.Logging.LogLevel.Info,
                    "[BNL Launcher] Launching curl fetch for " + url);
                proc = System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                onError("could not start curl.exe: " + ex.Message);
                yield break;
            }

            if (proc == null)
            {
                onError("could not start curl.exe");
                yield break;
            }

            while (!proc.HasExited)
                yield return null;

            string stderr = proc.StandardError.ReadToEnd();
            if (proc.ExitCode != 0)
            {
                onError(string.IsNullOrEmpty(stderr) ? "curl.exe exited with code " + proc.ExitCode : stderr.Trim());
                yield break;
            }

            string stdout = "";
            try
            {
                if (System.IO.File.Exists(tempOutput))
                {
                    long size = new System.IO.FileInfo(tempOutput).Length;
                    Log.Log(BepInEx.Logging.LogLevel.Info,
                        "[BNL Launcher] curl fetch temp file size=" + size);
                    stdout = System.IO.File.ReadAllText(tempOutput, Encoding.UTF8);
                }
                else
                {
                    Log.Log(BepInEx.Logging.LogLevel.Warning,
                        "[BNL Launcher] curl fetch temp file was not created");
                }
            }
            catch (Exception ex)
            {
                onError("could not read curl response: " + ex.Message);
                yield break;
            }
            finally
            {
                try { if (System.IO.File.Exists(tempOutput)) System.IO.File.Delete(tempOutput); } catch { }
            }

            Log.Log(BepInEx.Logging.LogLevel.Info,
                "[BNL Launcher] curl fetch completed successfully");
            onSuccess(stdout);
        }

        private static System.Collections.IEnumerator FetchUrlWithPowerShellFallback(string url, Action<string> onSuccess, Action<string> onError)
        {
            string escapedUrl = EscapePowerShellSingleQuoted(url);
            string command =
                "$ErrorActionPreference='Stop'; " +
                "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]3072 -bor [Net.SecurityProtocolType]768 -bor [Net.SecurityProtocolType]192; " +
                "$ProgressPreference='SilentlyContinue'; " +
                "(Invoke-WebRequest -UseBasicParsing -Headers @{'User-Agent'='BNL-Launcher'} -Uri '" + escapedUrl + "').Content";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + EncodePowerShellCommand(command),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            System.Diagnostics.Process? proc = null;
            try
            {
                Log.Log(BepInEx.Logging.LogLevel.Info,
                    "[BNL Launcher] Launching PowerShell fetch for " + url);
                proc = System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                onError("could not start PowerShell: " + ex.Message);
                yield break;
            }

            if (proc == null)
            {
                onError("could not start PowerShell");
                yield break;
            }

            while (!proc.HasExited)
                yield return null;

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            if (proc.ExitCode != 0)
            {
                onError(string.IsNullOrEmpty(stderr) ? "PowerShell exited with code " + proc.ExitCode : stderr.Trim());
                yield break;
            }

            onSuccess(stdout);
        }

        internal static System.Collections.IEnumerator DownloadUrlToFile(string url, string destinationPath, Action<string?> onComplete)
        {
            string? curlError = null;
            yield return RunCurlDownload(url, destinationPath, error =>
            {
                curlError = error;
            });

            if (string.IsNullOrEmpty(curlError))
            {
                onComplete(null);
                yield break;
            }

            Log.Log(BepInEx.Logging.LogLevel.Warning,
                "[BNL Launcher] curl.exe download failed, falling back to PowerShell: " + curlError);

            yield return RunPowerShellDownload(url, destinationPath, onComplete);
        }

        private static System.Collections.IEnumerator RunCurlDownload(string url, string destinationPath, Action<string?> onComplete)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = "--fail --location --silent --show-error -A \"BNL-Launcher\" -o \"" + destinationPath + "\" \"" + url + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            System.Diagnostics.Process? proc = null;
            try
            {
                Log.Log(BepInEx.Logging.LogLevel.Info,
                    "[BNL Launcher] Launching curl download for " + url);
                proc = System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                onComplete("could not start curl.exe: " + ex.Message);
                yield break;
            }

            if (proc == null)
            {
                onComplete("could not start curl.exe");
                yield break;
            }

            while (!proc.HasExited)
                yield return null;

            string stderr = proc.StandardError.ReadToEnd();
            if (proc.ExitCode != 0)
            {
                onComplete(string.IsNullOrEmpty(stderr) ? "curl.exe exited with code " + proc.ExitCode : stderr.Trim());
                yield break;
            }

            onComplete(null);
        }

        private static System.Collections.IEnumerator RunPowerShellDownload(string url, string destinationPath, Action<string?> onComplete)
        {
            string escapedUrl = EscapePowerShellSingleQuoted(url);
            string escapedDest = EscapePowerShellSingleQuoted(destinationPath);
            string command =
                "$ErrorActionPreference='Stop'; " +
                "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]3072 -bor [Net.SecurityProtocolType]768 -bor [Net.SecurityProtocolType]192; " +
                "$ProgressPreference='SilentlyContinue'; " +
                "Invoke-WebRequest -UseBasicParsing -Headers @{'User-Agent'='BNL-Launcher'} -Uri '" + escapedUrl + "' -OutFile '" + escapedDest + "'";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + EncodePowerShellCommand(command),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            System.Diagnostics.Process? proc = null;
            try
            {
                Log.Log(BepInEx.Logging.LogLevel.Info,
                    "[BNL Launcher] Launching PowerShell download for " + url);
                proc = System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                onComplete("could not start PowerShell: " + ex.Message);
                yield break;
            }

            if (proc == null)
            {
                onComplete("could not start PowerShell");
                yield break;
            }

            while (!proc.HasExited)
                yield return null;

            string stderr = proc.StandardError.ReadToEnd();

            if (proc.ExitCode != 0)
            {
                onComplete(string.IsNullOrEmpty(stderr) ? "PowerShell exited with code " + proc.ExitCode : stderr.Trim());
                yield break;
            }

            onComplete(null);
        }

        private static string EscapePowerShellSingleQuoted(string value)
        {
            return value.Replace("'", "''");
        }

        private static string EncodePowerShellCommand(string command)
        {
            return Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        }
    }

    /// <summary>
    /// In-game popup for update notification. Downloads the installer exe from
    /// GitHub and launches it so the user can pick plugins and install.
    /// </summary>
    public class UpdateNotifier : MonoBehaviour
    {
        private const string RepoOwner = "devprbtt";
        private const string RepoName = "bnl-bepinex-plugins";
        private string _newVersion = "";
        private string _releaseUrl = "";
        private string _currentVersion = "";
        private string _releaseName = "";
        private string _releaseBody = "";
        private string? _installerUrl;
        private bool _visible = true;
        private bool _downloading;
        private bool _upToDate;
        private string _downloadStatus = "";
        private string _errorMessage = "";
        private Rect _windowRect;
        private Vector2 _scrollPos;

        public void Initialize(string newVersion, string releaseUrl, string currentVersion,
            string releaseName, string releaseBody, string? installerDownloadUrl)
        {
            _newVersion = newVersion;
            _releaseUrl = releaseUrl;
            _currentVersion = currentVersion;
            _releaseName = releaseName ?? "";
            _releaseBody = releaseBody ?? "";
            _installerUrl = installerDownloadUrl;
            _windowRect = new Rect(Screen.width / 2f - 230f, Screen.height / 2f - 170f, 460f, 340f);
        }

        /// <summary>Shown when user manually checks and is already up to date.</summary>
        public void InitializeUpToDate(string currentVersion)
        {
            _upToDate = true;
            _currentVersion = currentVersion;
            _windowRect = new Rect(Screen.width / 2f - 180f, Screen.height / 2f - 40f, 360f, 80f);
        }

        private void Update()
        {
        }

        private void OnGUI()
        {
            if (!_visible)
                return;

            if (_upToDate)
            {
                _windowRect = GUI.Window(GetInstanceID() + 1, _windowRect, DrawUpToDateWindow,
                    "BNL Launcher - Up to Date");
                return;
            }

            _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawWindow,
                "BNL Launcher - Update v" + _newVersion + " Available");
        }

        private void DrawUpToDateWindow(int windowId)
        {
            GUILayout.Label("You're up to date! v" + _currentVersion + " is the latest version.",
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(8);
            if (GUILayout.Button("OK"))
            {
                _visible = false;
                Destroy(gameObject);
            }
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawWindow(int windowId)
        {
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(220));

            GUILayout.Label("A new version is available!", new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                normal = { textColor = Color.yellow }
            });

            GUILayout.Space(6);

            GUILayout.Label("Installed: v" + _currentVersion + "  ->  Latest: v" + _newVersion);

            if (!string.IsNullOrEmpty(_releaseName) &&
                !_releaseName.Equals("v" + _newVersion, StringComparison.OrdinalIgnoreCase) &&
                !_releaseName.Equals(_newVersion, StringComparison.OrdinalIgnoreCase))
            {
                GUILayout.Space(4);
                GUILayout.Label(_releaseName, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            }

            if (!string.IsNullOrEmpty(_releaseBody))
            {
                GUILayout.Space(8);

                string[] lines = _releaseBody.Split('\n');
                int shown = 0;
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 && shown == 0)
                        continue;
                    if (shown >= 20)
                        break;

                    GUIStyle style = GUI.skin.label;
                    if (trimmed.StartsWith("## "))
                        { trimmed = trimmed.Substring(3); style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold }; }
                    else if (trimmed.StartsWith("# "))
                        { trimmed = trimmed.Substring(2); style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 11 }; }
                    else if (trimmed.StartsWith("- "))
                        { trimmed = "  " + trimmed; }

                    GUILayout.Label(trimmed.Length > 0 ? trimmed : " ", style);
                    shown++;
                }

                if (shown >= 20)
                    GUILayout.Label("... (see release page for full details)");
            }

            GUILayout.EndScrollView();

            GUILayout.Space(6);

            if (_downloading)
            {
                GUILayout.Label(_downloadStatus, new GUIStyle(GUI.skin.label)
                {
                    normal = { textColor = Color.cyan },
                    alignment = TextAnchor.MiddleCenter
                });

                if (GUILayout.Button("Cancel", GUILayout.Width(100)))
                {
                    _downloading = false;
                    _downloadStatus = "";
                }
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Open Installer", GUILayout.Width(150), GUILayout.Height(34)))
                {
                    StartCoroutine(DownloadAndLaunchInstaller());
                }

                GUILayout.Space(12);

                if (GUILayout.Button("Remind Later", GUILayout.Width(120), GUILayout.Height(34)))
                {
                    _visible = false;
                    Destroy(gameObject);
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.Label("Tip: Open the config menu to manually re-check for updates",
                    new GUIStyle(GUI.skin.label) { fontSize = 8, alignment = TextAnchor.MiddleCenter });
            }

            GUILayout.Space(6);

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private System.Collections.IEnumerator DownloadAndLaunchInstaller()
        {
            _downloading = true;
            _downloadStatus = "Preparing download...";
            _errorMessage = "";
            yield return null;

            string? installerUrl = _installerUrl;
            if (string.IsNullOrEmpty(installerUrl) && !string.IsNullOrEmpty(_releaseUrl))
            {
                string tag = _releaseUrl.Substring(_releaseUrl.LastIndexOf('/') + 1);
                installerUrl = "https://github.com/" + RepoOwner + "/" + RepoName +
                    "/releases/download/" + tag + "/BNL-Installer.exe";
            }

            if (string.IsNullOrEmpty(installerUrl))
            {
                _errorMessage = "Could not find installer download URL.";
                _downloading = false;
                yield break;
            }

            _downloadStatus = "Downloading installer...";
            yield return null;

            string tempInstaller = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BNL-Installer.exe");
            string? downloadError = null;
            yield return DownloadFileWithPowerShell(installerUrl, tempInstaller, error =>
            {
                downloadError = error;
            });

            if (downloadError != null)
            {
                _errorMessage = "Download failed: " + downloadError;
                _downloading = false;
                yield break;
            }

            _downloadStatus = "Launching installer...";
            yield return null;

            string? launchError = null;
            try
            {
                string gameRoot = GetGameRoot();
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempInstaller,
                    Arguments = "\"" + gameRoot + "\"",
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(tempInstaller) ?? "."
                };

                if (System.Diagnostics.Process.Start(psi) == null)
                    throw new Exception("Could not launch installer.");
            }
            catch (Exception ex) { launchError = ex.Message; }

            if (launchError != null)
            {
                _errorMessage = "Installer launch failed: " + launchError;
                _downloading = false;
                yield break;
            }

            _downloadStatus = "Installer opened. Close the game and finish the update there.";
            yield return new WaitForSeconds(4f);

            _visible = false;
            Destroy(gameObject);
        }

        private System.Collections.IEnumerator DownloadFileWithPowerShell(string url, string destinationPath, Action<string?> onComplete)
        {
            yield return LauncherPlugin.DownloadUrlToFile(url, destinationPath, onComplete);
        }

        private static string EscapePowerShellSingleQuoted(string value)
        {
            return value.Replace("'", "''");
        }

        private static string EncodePowerShellCommand(string command)
        {
            return Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        }

        private static string GetGameRoot()
        {
            string pluginPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string pluginsDir = System.IO.Path.GetDirectoryName(pluginPath) ?? ".";
            string bepinexDir = System.IO.Path.GetDirectoryName(pluginsDir) ?? ".";
            string win64Dir = System.IO.Path.GetDirectoryName(bepinexDir) ?? ".";
            string gameRoot = System.IO.Path.GetDirectoryName(win64Dir) ?? win64Dir;
            return gameRoot;
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
                object playerData = GetSingletonInstance("PlayerData");
                if (playerData != null)
                    SetMasterServerDirect(playerData);
            }
            catch (Exception ex)
            {
                LauncherPlugin.Log.LogError("[BNL Launcher] SteamInit override failed: " + ex.Message);
            }
        }

        public static void SteamInitFinalize_Postfix()
        {
            try
            {
                object playerData = GetSingletonInstance("PlayerData");
                if (playerData != null)
                {
                    SetMasterServerDirect(playerData);
                    LauncherPlugin.Log.LogInfo("[BNL Launcher] Reapplied MasterServer after SteamLogin.<Init>m__641");
                }
            }
            catch (Exception ex)
            {
                LauncherPlugin.Log.LogError("[BNL Launcher] Late SteamLogin override failed: " + ex.Message);
            }
        }

        private static void SetMasterServerDirect(object playerData)
        {
            FieldInfo msField = playerData.GetType().GetField("MasterServer",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (msField == null)
            {
                LauncherPlugin.Log.LogWarning("[BNL Launcher] Could not find PlayerData.MasterServer field");
                return;
            }

            object selectedServer = TryGetSelectedServer();
            if (selectedServer == null)
            {
                selectedServer = CreateCommunityServer(msField.FieldType);
            }
            if (selectedServer == null)
            {
                LauncherPlugin.Log.LogWarning("[BNL Launcher] Could not create a community server instance");
                return;
            }

            msField.SetValue(playerData, selectedServer);
            LauncherPlugin.Log.LogInfo("[BNL Launcher] Set PlayerData.MasterServer to " +
                DescribeServer(selectedServer));
        }

        public static void PlayerDataIsNoob_Postfix(ref bool __result)
        {
            __result = false;
        }

        public static void ThreadNetworkClientConnect_Prefix(ref string host, ref int port)
        {
            if (!string.Equals(host, "162.55.251.122", StringComparison.OrdinalIgnoreCase) || port != 28100)
                return;

            LauncherPlugin.Log.LogInfo("[BNL Launcher] Rewriting master connect " + host + ":" + port +
                                       " -> " + LauncherPlugin.DefaultServerHost + ":" + LauncherPlugin.DefaultServerPort);
            host = LauncherPlugin.DefaultServerHost;
            port = LauncherPlugin.DefaultServerPort;
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

        private static object? TryGetSelectedServer()
        {
            try
            {
                object loginLogic = GetSingletonInstance("LoginLogic");
                if (loginLogic == null)
                    return null;

                FieldInfo serverSelectorField = loginLogic.GetType().GetField("ServerSelector",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object serverSelector = serverSelectorField != null ? serverSelectorField.GetValue(loginLogic) : null;
                if (serverSelector == null)
                    return null;

                Type helperType = Type.GetType("ServerSelector.ServerSelectorHelper, Assembly-CSharp");
                MethodInfo getSelectedServer = helperType != null
                    ? helperType.GetMethod("GetSelectedServer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;

                object selectedServer = getSelectedServer != null
                    ? getSelectedServer.Invoke(null, new[] { serverSelector })
                    : null;
                if (selectedServer != null)
                {
                    LauncherPlugin.Log.LogInfo("[BNL Launcher] Selected server from selector: " +
                        DescribeServer(selectedServer));
                }

                return selectedServer;
            }
            catch (Exception ex)
            {
                LauncherPlugin.Log.LogWarning("[BNL Launcher] Failed resolving selected server: " + ex.Message);
                return null;
            }
        }

        private static object? CreateCommunityServer(Type serverType)
        {
            try
            {
                ConstructorInfo ctor = serverType.GetConstructor(new[] { typeof(string), typeof(string), typeof(short) });
                if (ctor != null)
                {
                    return ctor.Invoke(new object[]
                    {
                        "public",
                        LauncherPlugin.DefaultServerHost,
                        (short)LauncherPlugin.DefaultServerPort
                    });
                }

                object server = Activator.CreateInstance(serverType);
                if (server == null)
                    return null;

                SetFieldOrProperty(server, "Name", "public");
                SetFieldOrProperty(server, "Host", LauncherPlugin.DefaultServerHost);
                SetFieldOrProperty(server, "Port", (short)LauncherPlugin.DefaultServerPort);
                return server;
            }
            catch (Exception ex)
            {
                LauncherPlugin.Log.LogWarning("[BNL Launcher] Failed creating community server: " + ex.Message);
                return null;
            }
        }

        private static void SetFieldOrProperty(object target, string memberName, object value)
        {
            Type type = target.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            PropertyInfo prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
                prop.SetValue(target, value, null);
        }

        private static string DescribeServer(object server)
        {
            if (server == null)
                return "<null>";

            string name = ReadFieldOrProperty(server, "Name") as string ?? "?";
            string host = ReadFieldOrProperty(server, "Host") as string ?? "?";
            object portValue = ReadFieldOrProperty(server, "Port");
            return name + " " + host + ":" + portValue;
        }

        private static object? ReadFieldOrProperty(object target, string memberName)
        {
            Type type = target.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field.GetValue(target);

            PropertyInfo prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return prop != null ? prop.GetValue(target, null) : null;
        }
    }
}

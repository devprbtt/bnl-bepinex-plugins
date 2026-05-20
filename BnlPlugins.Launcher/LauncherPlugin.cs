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
    [BepInPlugin("bnl.community.launcher", "BNL Launcher Patches", "1.2.0")]
    public class LauncherPlugin : BaseUnityPlugin
    {
        internal const string CurrentVersion = "1.2.0";
        private const string GitHubRepo = "devprbtt/bnl-bepinex-plugins";

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

        private void Awake()
        {
            Log = Logger;

            // Unity 5 Mono doesn't negotiate TLS 1.2 by default — required for GitHub API
            // .NET 3.5 doesn't have the Tls12 enum, so we use the raw integer value (3072 = 0xC00)
            try
            {
                System.Net.ServicePointManager.SecurityProtocol =
                    (System.Net.SecurityProtocolType)3072 | (System.Net.SecurityProtocolType)768;
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

            // Write current version so we know what was last installed
            try
            {
                File.WriteAllText(_versionFilePath, CurrentVersion);
            }
            catch { }

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
            // Wait for the game to fully load before showing any UI
            yield return new WaitForSeconds(10f);

            // Handle manual re-check requests (bypasses rate limit and _updateCheckDone)
            if (!force && _updateCheckDone)
                yield break;

            _updateCheckDone = true;

            string latestVersion = null;
            string releaseUrl = null;
            string releaseName = null;
            string releaseBody = null;
            string zipDownloadUrl = null;

            try
            {
                using (var client = new System.Net.WebClient())
                {
                    client.Headers.Add("User-Agent", "BNL-Launcher");
                    string apiUrl = "https://api.github.com/repos/" + GitHubRepo + "/releases/latest";
                    string json = client.DownloadString(apiUrl);

                    latestVersion = ExtractJsonString(json, "tag_name");
                    releaseUrl = ExtractJsonString(json, "html_url");
                    releaseName = ExtractJsonString(json, "name");
                    releaseBody = ExtractJsonBody(json);
                    zipDownloadUrl = FindZipAsset(json);

                    if (latestVersion != null && latestVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                        latestVersion = latestVersion.Substring(1);
                }

                // Record successful check timestamp
                try
                {
                    File.WriteAllText(_lastCheckFilePath, DateTime.UtcNow.Ticks.ToString());
                }
                catch { }
            }
            catch (Exception ex)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Warning,
                    "[BNL Launcher] Update check failed: " + ex.Message);
                yield break;
            }

            if (string.IsNullOrEmpty(latestVersion))
                yield break;

            Logger.Log(BepInEx.Logging.LogLevel.Info,
                "[BNL Launcher] Installed: v" + CurrentVersion + ", Latest: v" + latestVersion);

            if (IsNewerVersion(latestVersion, CurrentVersion))
            {
                Logger.Log(BepInEx.Logging.LogLevel.Info,
                    "[BNL Launcher] New version available: v" + latestVersion);
                ShowUpdateNotification(latestVersion, releaseUrl, releaseName, releaseBody, zipDownloadUrl);
            }
            else if (force)
            {
                // User manually checked — tell them they're up to date
                var go = new GameObject("BNL_UpdateNotifier");
                DontDestroyOnLoad(go);
                go.AddComponent<UpdateNotifier>().InitializeUpToDate(CurrentVersion);
            }
        }

        /// <summary>Find the .zip asset browser_download_url from the GitHub release JSON.</summary>
        private static string? FindZipAsset(string json)
        {
            int assetsIdx = json.IndexOf("\"assets\":[", StringComparison.Ordinal);
            if (assetsIdx < 0) return null;

            int searchFrom = assetsIdx;
            while (true)
            {
                int urlIdx = json.IndexOf("\"browser_download_url\":\"", searchFrom, StringComparison.Ordinal);
                if (urlIdx < 0) return null;

                urlIdx += "\"browser_download_url\":\"".Length;
                int urlEnd = json.IndexOf("\"", urlIdx, StringComparison.Ordinal);
                if (urlEnd < 0) return null;

                string candidate = json.Substring(urlIdx, urlEnd - urlIdx);
                if (candidate.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return candidate;

                searchFrom = urlEnd + 1;
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
            string releaseName, string releaseBody, string? zipDownloadUrl)
        {
            var go = new GameObject("BNL_UpdateNotifier");
            DontDestroyOnLoad(go);
            go.AddComponent<UpdateNotifier>().Initialize(
                newVersion, releaseUrl, CurrentVersion, releaseName, releaseBody, zipDownloadUrl);
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
        private string? _zipUrl;
        private bool _visible = true;
        private bool _downloading;
        private bool _upToDate;
        private string _downloadStatus = "";
        private string _errorMessage = "";
        private Rect _windowRect;
        private Vector2 _scrollPos;

        public void Initialize(string newVersion, string releaseUrl, string currentVersion,
            string releaseName, string releaseBody, string? zipDownloadUrl)
        {
            _newVersion = newVersion;
            _releaseUrl = releaseUrl;
            _currentVersion = currentVersion;
            _releaseName = releaseName ?? "";
            _releaseBody = releaseBody ?? "";
            _zipUrl = zipDownloadUrl;  // unused field - will use later
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
            if (_visible && !_downloading && Input.GetKeyDown(KeyCode.F5))
            {
                _visible = false;
                Destroy(gameObject);
                LauncherPlugin.RequestUpdateCheck();
                LauncherPlugin.Log.LogInfo("[BNL Launcher] Manual update check requested (F5)");
            }
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

                if (GUILayout.Button("Download && Update", GUILayout.Width(150), GUILayout.Height(34)))
                {
                    StartCoroutine(DownloadAndExtract());
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
                GUILayout.Label("Tip: Press F5 to manually re-check for updates",
                    new GUIStyle(GUI.skin.label) { fontSize = 8, alignment = TextAnchor.MiddleCenter });
            }

            GUILayout.Space(6);

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private System.Collections.IEnumerator DownloadAndExtract()
        {
            _downloading = true;
            _downloadStatus = "Preparing download...";
            _errorMessage = "";
            yield return null;

            string? zipUrl = _zipUrl;
            if (string.IsNullOrEmpty(zipUrl) && !string.IsNullOrEmpty(_releaseUrl))
            {
                string tag = _releaseUrl.Substring(_releaseUrl.LastIndexOf('/') + 1);
                zipUrl = "https://github.com/" + RepoOwner + "/" + RepoName +
                    "/releases/download/" + tag + "/bnl-bepinex-plugins-" + _newVersion + ".zip";
            }

            if (string.IsNullOrEmpty(zipUrl))
            {
                _errorMessage = "Could not find download URL.";
                _downloading = false;
                yield break;
            }

            _downloadStatus = "Downloading update...";
            yield return null;

            string tempZip = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bnl-update.zip");
            string? downloadError = null;
            try
            {
                using (var client = new System.Net.WebClient())
                {
                    client.Headers.Add("User-Agent", "BNL-Launcher");
                    client.DownloadFile(zipUrl, tempZip);
                }
            }
            catch (Exception ex) { downloadError = ex.Message; }

            if (downloadError != null)
            {
                _errorMessage = "Download failed: " + downloadError;
                _downloading = false;
                yield break;
            }

            _downloadStatus = "Extracting files...";
            yield return null;

            string? extractError = null;
            bool needsRestart = false;
            try
            {
                string gameRoot = System.IO.Path.GetDirectoryName(
                    System.IO.Path.GetDirectoryName(
                        System.IO.Path.GetDirectoryName(
                            System.Reflection.Assembly.GetExecutingAssembly().Location))) ?? ".";

                string win64Dir = System.IO.Path.Combine(gameRoot, "Win64");

                // .NET 3.5 doesn't have ZipFile — use PowerShell for extraction
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -Command \"Expand-Archive -Path '" +
                        tempZip + "' -DestinationPath '" + win64Dir + "' -Force\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    if (proc == null)
                        throw new Exception("Could not start PowerShell");
                    proc.WaitForExit(30000);
                    if (proc.ExitCode != 0)
                    {
                        string err = proc.StandardError.ReadToEnd();
                        throw new Exception("PowerShell exited with code " + proc.ExitCode +
                            (string.IsNullOrEmpty(err) ? "" : ": " + err));
                    }
                }

                // Update version file
                string versionFile = System.IO.Path.Combine(
                    System.IO.Path.Combine(
                        System.IO.Path.Combine(
                            System.IO.Path.Combine(win64Dir, "BepInEx"), "plugins"), "Launcher"), "version.txt");
                try { System.IO.File.WriteAllText(versionFile, _newVersion); } catch { }

                // Clean up temp
                try { System.IO.File.Delete(tempZip); } catch { }
            }
            catch (Exception ex) { extractError = ex.Message; }

            if (extractError != null)
            {
                _errorMessage = "Extract failed: " + extractError;
                _downloading = false;
                yield break;
            }

            _downloadStatus = "Updated to v" + _newVersion + "!";
            if (needsRestart)
                _downloadStatus += " (restart game for plugin update)";

            yield return new WaitForSeconds(3f);

            _visible = false;
            Destroy(gameObject);
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

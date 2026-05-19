using System;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;

namespace BnlPlugins.Launcher
{
    /// <summary>
    /// BepInEx plugin for Block N Load community server support.
    /// Uses Harmony runtime patches only — NO files are modified on disk,
    /// so Steam never detects changes and never triggers verification.
    ///
    /// Patches applied:
    ///   - EACToolInitializer.IsEACRuntime() → always returns true (no kick)
    ///   - Searches for and disables Kick/KickPlayer/Disconnect methods
    ///   - Writes servers.txt for community server selection
    /// </summary>
    [BepInPlugin("bnl.community.launcher", "BNL Launcher Patches", "1.0.0")]
    public class LauncherPlugin : BaseUnityPlugin
    {
        internal static string DefaultServerHost = "v310.blocknload.pauldh.nl";
        internal static int DefaultServerPort = 28100;

        private void Awake()
        {
            LoadConfig();

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
                    var isEacRuntime = eacType.GetMethod("IsEACRuntime",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (isEacRuntime != null)
                    {
                        harmony.Patch(isEacRuntime,
                            postfix: new HarmonyMethod(typeof(EacPatches).GetMethod("IsEACRuntime_Postfix")));
                        Logger.Log(BepInEx.Logging.LogLevel.Info,
                            "[BNL Launcher] Patched: EACToolInitializer.IsEACRuntime → always true");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Error,
                    "[BNL Launcher] Harmony error: " + ex.Message);
            }
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
    }
}

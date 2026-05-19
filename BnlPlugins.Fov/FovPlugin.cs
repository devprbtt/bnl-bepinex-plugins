using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace BnlPlugins.Fov
{
    [BepInPlugin("bnl.community.fov", "BNL FOV Plugin", "1.0.0")]
    public class FovPlugin : BaseUnityPlugin
    {
        internal static float FovValue = 120f;
        internal static float WeaponModelFov = 30f;
        internal static float AdsSensitivityMultiplier = 1.0f;
        internal static bool Enabled = true;

        private void Awake()
        {
            LoadConfig();

            try
            {
                var harmony = new Harmony("bnl.community.fov");

                var camFovType = Type.GetType("CameraFov, Assembly-CSharp");
                if (camFovType != null)
                {
                    var original = camFovType.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var postfix = typeof(Patches).GetMethod("CameraFovPostfix");
                    if (original != null && postfix != null)
                        harmony.Patch(original, null, new HarmonyMethod(postfix));
                }

                var camArmsType = Type.GetType("CameraArms, Assembly-CSharp");
                if (camArmsType != null)
                {
                    var original = camArmsType.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var postfix = typeof(Patches).GetMethod("CameraArmsPostfix");
                    if (original != null && postfix != null)
                        harmony.Patch(original, null, new HarmonyMethod(postfix));
                }

                // MouseLook::RotateByMouse is void in this Unity version —
                // ADS sensitivity multiplier not available via postfix.
                // var mouseLookType = Type.GetType("MouseLook, Assembly-CSharp");
                // if (mouseLookType != null)
                // {
                //     var original = mouseLookType.GetMethod("RotateByMouse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                //     var postfix = typeof(Patches).GetMethod("MouseLookPostfix");
                //     if (original != null && postfix != null)
                //         harmony.Patch(original, null, new HarmonyMethod(postfix));
                // }
            }
            catch (Exception ex)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Error, "[BNL FOV] Harmony error: " + ex.Message);
            }

            Logger.Log(BepInEx.Logging.LogLevel.Info,
                string.Format("[BNL FOV] Loaded. Enabled={0} FOV={1} WeaponFOV={2} AdsMult={3}",
                    Enabled, FovValue, WeaponModelFov, AdsSensitivityMultiplier));
        }

        private void LoadConfig()
        {
            string cfgPath = Path.Combine(Path.Combine(Paths.GameRootPath, "BepInEx"), Path.Combine("config", "BnlPlugins.Fov.cfg"));

            if (!File.Exists(cfgPath))
            {
                File.WriteAllText(cfgPath,
                    "[FOV]\r\nenabled=true\r\nfov=120\r\nweapon_model_fov=30\r\nads_sensitivity_multiplier=1.0\r\n");
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

                float f;
                switch (key)
                {
                    case "enabled":
                        Enabled = val.ToLowerInvariant() != "false" && val != "0";
                        break;
                    case "fov":
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out f))
                            FovValue = f;
                        break;
                    case "weapon_model_fov":
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out f))
                            WeaponModelFov = f;
                        break;
                    case "ads_sensitivity_multiplier":
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out f))
                            AdsSensitivityMultiplier = f;
                        break;
                }
            }
        }
    }

    public static class Patches
    {
        public static void CameraFovPostfix(Component __instance)
        {
            if (!FovPlugin.Enabled) return;
            Camera cam = __instance.GetComponent<Camera>();
            if (cam != null)
                cam.fieldOfView = FovPlugin.FovValue;
        }

        public static void CameraArmsPostfix(Component __instance)
        {
            if (!FovPlugin.Enabled) return;
            Camera cam = __instance.GetComponent<Camera>();
            if (cam != null && cam.enabled)
                cam.fieldOfView = FovPlugin.WeaponModelFov;
        }

        public static void MouseLookPostfix(Component __instance, ref float __result)
        {
            if (!FovPlugin.Enabled) return;
            if (Math.Abs(FovPlugin.AdsSensitivityMultiplier - 1f) < 0.001f) return;
            if (__instance.GetComponent("Unit") != null)
                __result *= FovPlugin.AdsSensitivityMultiplier;
        }
    }
}

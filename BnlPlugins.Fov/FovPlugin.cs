using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BnlPlugins.Fov
{
    [BepInPlugin("bnl.community.fov", "BNL FOV Plugin", "1.0.0")]
    public class FovPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log = null!;

        internal static ConfigEntry<bool> EnableFov = null!;
        internal static ConfigEntry<float> FovValue = null!;
        internal static ConfigEntry<float> WeaponModelFov = null!;
        internal static ConfigEntry<float> AdsSensitivityMultiplier = null!;

        private void Awake()
        {
            Log = base.Logger;

            EnableFov = Config.Bind("FOV", "Enabled", true, "Enable custom FOV override.");
            FovValue = Config.Bind("FOV", "Fov", 120f, new ConfigDescription("Camera field of view.", new AcceptableValueRange<float>(60f, 170f)));
            WeaponModelFov = Config.Bind("FOV", "WeaponModelFov", 30f, new ConfigDescription("Weapon model camera FOV.", new AcceptableValueRange<float>(10f, 90f)));
            AdsSensitivityMultiplier = Config.Bind("FOV", "AdsSensitivityMultiplier", 1.0f, new ConfigDescription("Mouse sensitivity multiplier while aiming down sights.", new AcceptableValueRange<float>(0.1f, 5f)));

            var harmony = new Harmony("bnl.community.fov");
            harmony.PatchAll(typeof(CameraFovPatch));
            harmony.PatchAll(typeof(CameraArmsPatch));
            harmony.PatchAll(typeof(MouseLookPatch));

            Log.LogInfo($"FOV plugin loaded. FOV={FovValue.Value}, WeaponModelFov={WeaponModelFov.Value}, AdsSensitivity={AdsSensitivityMultiplier.Value}");
        }
    }

    // Postfix CameraFov.Update — runs after the game sets cam.fieldOfView, then overrides it.
    [HarmonyPatch(typeof(CameraFov), "Update")]
    static class CameraFovPatch
    {
        static void Postfix(CameraFov __instance)
        {
            if (!FovPlugin.EnableFov.Value) return;
            Camera cam = __instance.GetComponent<Camera>();
            if (cam != null)
                cam.fieldOfView = FovPlugin.FovValue.Value;
        }
    }

    // Postfix CameraArms.Update — overrides the weapon model camera FOV.
    [HarmonyPatch(typeof(CameraArms), "Update")]
    static class CameraArmsPatch
    {
        static void Postfix(CameraArms __instance)
        {
            if (!FovPlugin.EnableFov.Value) return;
            Camera cam = __instance.GetComponent<Camera>();
            if (cam != null && cam.enabled)
                cam.fieldOfView = FovPlugin.WeaponModelFov.Value;
        }
    }

    // Postfix MouseLook.RotateByMouse — scales the returned sensitivity when ADS.
    // The method signature: float RotateByMouse(float currentScale)
    [HarmonyPatch(typeof(MouseLook), "RotateByMouse")]
    static class MouseLookPatch
    {
        static void Postfix(MouseLook __instance, ref float __result)
        {
            if (!FovPlugin.EnableFov.Value) return;
            if (Math.Abs(FovPlugin.AdsSensitivityMultiplier.Value - 1f) < 0.001f) return;

            Unit? unit = __instance.GetComponent<Unit>();
            if (unit != null && unit.GetAimingState() != null)
                __result *= FovPlugin.AdsSensitivityMultiplier.Value;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BnlPlugins.Fov
{
    [BepInPlugin("bnl.community.fov", "BNL FOV", "0.1.0")]
    public sealed class FovPlugin : BaseUnityPlugin
    {
        private const string HarmonyId = "bnl.community.fov";
        internal static FovPlugin Instance = null!;
        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<float> ForcedFov = null!;
        internal static ConfigEntry<float> AdsSensitivityMultiplier = null!;
        internal static ConfigEntry<float> WeaponModelFov = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Instance = this;

            Enabled = Config.Bind("FOV", "Enabled", true,
                new ConfigDescription("Enable forced first-person FOV, ADS sensitivity multiplier, and weapon model FOV overrides.", null,
                    new ConfigurationManagerAttributes { Order = 100 }));
            ForcedFov = Config.Bind("FOV", "Fov", 100f,
                FloatConfig.Range("Forced first-person camera FOV.", 60f, 150f, 99));
            AdsSensitivityMultiplier = Config.Bind("FOV", "AdsSensitivityMultiplier", 1f,
                FloatConfig.Range("Multiplier applied to mouse sensitivity while aiming.", 0.1f, 4f, 98));
            WeaponModelFov = Config.Bind("FOV", "WeaponModelFov", 20f,
                FloatConfig.Range("Weapon model / arms camera FOV.", 10f, 120f, 97));

            FloatConfig.BindRound(ForcedFov, 60f, 150f);
            FloatConfig.BindRound(AdsSensitivityMultiplier, 0.1f, 4f);
            FloatConfig.BindRound(WeaponModelFov, 10f, 120f);

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(FovPlugin).Assembly);
            Logger.LogInfo("[BNL FOV] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }
    }

    internal static class FovRuntime
    {
        private sealed class SensitivityState
        {
            public float LastAppliedMultiplier = 1f;
        }

        private static readonly Dictionary<int, SensitivityState> SensitivityStates = new Dictionary<int, SensitivityState>();

        internal static void ApplyWeaponModelFov(Component component)
        {
            if (!FovPlugin.Enabled.Value || component == null)
                return;

            var targetFov = Mathf.Clamp(FovPlugin.WeaponModelFov.Value, 10f, 120f);
            var cameras = component.GetComponentsInChildren<Camera>(true);
            if (cameras == null || cameras.Length == 0)
            {
                var camera = component.GetComponent<Camera>();
                if (camera != null)
                    camera.fieldOfView = targetFov;
                return;
            }

            for (int i = 0; i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (camera == null)
                    continue;

                if (camera != Camera.main || cameras.Length == 1)
                    camera.fieldOfView = targetFov;
            }
        }

        internal static void ApplyAdsSensitivity(object mouseLookInstance)
        {
            if (mouseLookInstance == null)
                return;

            var type = mouseLookInstance.GetType();
            var id = mouseLookInstance.GetHashCode();
            if (!SensitivityStates.TryGetValue(id, out var state))
            {
                state = new SensitivityState();
                SensitivityStates[id] = state;
            }

            var sensitivityXField = type.GetField("SensitivityX", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var sensitivityYField = type.GetField("SensitivityY", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var unitField = type.GetField("unit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (sensitivityXField == null || sensitivityYField == null || unitField == null)
                return;

            float currentX = Convert.ToSingle(sensitivityXField.GetValue(mouseLookInstance));
            float currentY = Convert.ToSingle(sensitivityYField.GetValue(mouseLookInstance));
            float previousMultiplier = state.LastAppliedMultiplier <= 0f ? 1f : state.LastAppliedMultiplier;
            float baseX = currentX / previousMultiplier;
            float baseY = currentY / previousMultiplier;

            float multiplier = 1f;
            if (FovPlugin.Enabled.Value)
            {
                object unit = unitField.GetValue(mouseLookInstance);
                if (unit != null)
                {
                    var getAimingState = unit.GetType().GetMethod("GetAimingState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (getAimingState != null && getAimingState.Invoke(unit, null) != null)
                        multiplier = Mathf.Clamp(FovPlugin.AdsSensitivityMultiplier.Value, 0.1f, 4f);
                }
            }

            sensitivityXField.SetValue(mouseLookInstance, baseX * multiplier);
            sensitivityYField.SetValue(mouseLookInstance, baseY * multiplier);
            state.LastAppliedMultiplier = multiplier;
        }

        internal static float GetForcedCameraFov()
        {
            return Mathf.Clamp(FovPlugin.ForcedFov.Value, 60f, 150f);
        }
    }

    [HarmonyPatch]
    internal static class CameraFovPatch
    {
        private static MethodBase TargetMethod()
        {
            return PatchTargets.ResolveTarget("CameraFov", "Update");
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var getSettingsInstance = AccessTools.Method("Singleton`1[Settings]:get_Instance");
            var getCameraFov = AccessTools.Method(typeof(Settings), "get_CameraFov");
            var settingFloatImplicit = AccessTools.Method(typeof(SettingFloat), "op_Implicit", new[] { typeof(SettingFloat) });
            var forcedGetter = AccessTools.Method(typeof(FovRuntime), nameof(FovRuntime.GetForcedCameraFov));

            for (int i = 0; i < list.Count; i++)
            {
                if (i <= list.Count - 3 &&
                    CallsMethod(list[i], getSettingsInstance) &&
                    CallsMethod(list[i + 1], getCameraFov) &&
                    CallsMethod(list[i + 2], settingFloatImplicit))
                {
                    var replacement = new CodeInstruction(OpCodes.Call, forcedGetter);
                    replacement.labels.AddRange(list[i].labels);
                    replacement.blocks.AddRange(list[i].blocks);
                    yield return replacement;
                    i += 2;
                    continue;
                }

                yield return list[i];
            }
        }

        private static bool CallsMethod(CodeInstruction instruction, MethodInfo? method)
        {
            return method != null && instruction.opcode == OpCodes.Call && Equals(instruction.operand, method) ||
                   method != null && instruction.opcode == OpCodes.Callvirt && Equals(instruction.operand, method);
        }
    }

    [HarmonyPatch]
    internal static class CameraArmsPatch
    {
        private static MethodBase TargetMethod()
        {
            return PatchTargets.ResolveTarget("CameraArms", "Update");
        }

        [HarmonyPostfix]
        private static void Postfix(Component __instance)
        {
            FovRuntime.ApplyWeaponModelFov(__instance);
        }
    }

    [HarmonyPatch]
    internal static class MouseLookPatch
    {
        private static MethodBase TargetMethod()
        {
            return PatchTargets.ResolveTarget("MouseLook", "Update");
        }

        [HarmonyPrefix]
        private static void Prefix(object __instance)
        {
            FovRuntime.ApplyAdsSensitivity(__instance);
        }
    }

    internal static class PatchTargets
    {
        internal static MethodBase ResolveTarget(string typeName, string methodName)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null)
                throw new InvalidOperationException("Could not find type: " + typeName);

            var method = AccessTools.Method(type, methodName);
            if (method == null)
                throw new InvalidOperationException("Could not find method: " + typeName + "." + methodName);

            return method;
        }
    }
}

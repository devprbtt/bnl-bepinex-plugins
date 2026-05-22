using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace BnlPlugins.Crosshair
{
    [BepInPlugin("bnl.community.crosshair", "BNL Crosshair", "0.1.0")]
    public sealed class CrosshairPlugin : BaseUnityPlugin
    {
        internal static CrosshairPlugin Instance = null!;
        internal static BepInEx.Logging.ManualLogSource Log = null!;
        private const string HarmonyId = "bnl.community.crosshair";
        private static readonly string[] AllowedShapes = { "__DEFAULT__", "Dot", "Crosshair", "BrokenCircle", "Hashed", "HashedCrosshair", "Melee" };

        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<string> IdleColorHex = null!;
        internal static ConfigEntry<string> FullDamageColorHex = null!;
        internal static ConfigEntry<string> BelowMaxColorHex = null!;
        internal static ConfigEntry<float> BrightnessMultiplier = null!;
        internal static ConfigEntry<float> SizeMultiplier = null!;
        internal static ConfigEntry<float> SpreadMultiplier = null!;
        internal static ConfigEntry<float> Alpha = null!;
        internal static ConfigEntry<string> ForceShape = null!;
        internal static ConfigEntry<bool> ForceShowInAds = null!;
        internal static ConfigEntry<bool> HideCrosshair = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Enabled = Config.Bind("Crosshair", "Enabled", true,
                new ConfigDescription("Enable crosshair overrides.", null,
                    new ConfigurationManagerAttributes { Order = 100 }));
            IdleColorHex = Config.Bind("Crosshair", "IdleColor", "#FFFFFF",
                CreateColorDescription("Crosshair color when not targeting an enemy.", 99, "Idle Color"));
            FullDamageColorHex = Config.Bind("Crosshair", "FullDamageColor", "#FF0000",
                CreateColorDescription("Crosshair color when target is inside full damage range.", 98, "Full Damage Color"));
            BelowMaxColorHex = Config.Bind("Crosshair", "BelowMaxColor", "#FF0000",
                CreateColorDescription("Crosshair color when target is beyond full damage but still in max range.", 97, "Below Max Color"));
            BrightnessMultiplier = Config.Bind("Crosshair", "BrightnessMultiplier", 1f,
                FloatConfig.Range("Brightness multiplier applied to all crosshair colors.", 0.1f, 4f, 96));
            SizeMultiplier = Config.Bind("Crosshair", "SizeMultiplier", 1f,
                FloatConfig.Range("Crosshair size multiplier.", 0.25f, 4f, 95));
            SpreadMultiplier = Config.Bind("Crosshair", "SpreadMultiplier", 1f,
                FloatConfig.Range("Crosshair spread multiplier.", 0.25f, 4f, 94));
            Alpha = Config.Bind("Crosshair", "Alpha", 1f,
                FloatConfig.Range("Crosshair alpha multiplier.", 0.05f, 1f, 93));
            ForceShape = Config.Bind("Crosshair", "ForceShape", "__DEFAULT__",
                new ConfigDescription("Force crosshair shape.", new AcceptableValueList<string>(AllowedShapes), new ConfigurationManagerAttributes { Order = 92 }));
            ForceShowInAds = Config.Bind("Crosshair", "ForceShowInAds", false,
                new ConfigDescription("Force the crosshair to stay visible while aiming down sights.", null,
                    new ConfigurationManagerAttributes { Order = 91 }));
            HideCrosshair = Config.Bind("Crosshair", "HideCrosshair", false,
                new ConfigDescription("Hide the crosshair entirely.", null,
                    new ConfigurationManagerAttributes { Order = 90 }));

            FloatConfig.BindRound(BrightnessMultiplier, 0.1f, 4f);
            FloatConfig.BindRound(SizeMultiplier, 0.25f, 4f);
            FloatConfig.BindRound(SpreadMultiplier, 0.25f, 4f);
            FloatConfig.BindRound(Alpha, 0.05f, 1f);

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(CrosshairPlugin).Assembly);
            Logger.LogInfo("[BNL Crosshair] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }

        private static ConfigDescription CreateColorDescription(string description, int order, string displayName)
        {
            return new ConfigDescription(description, null, new ConfigurationManagerAttributes
            {
                CustomDrawer = ColorDrawer.Draw,
                Order = order,
                DispName = displayName,
                DefaultValue = "#FFFFFF"
            });
        }
    }

    internal static class CrosshairRuntime
    {
        private sealed class ControllerState
        {
            public string LastForcedShape = "__DEFAULT__";
        }

        private static readonly Dictionary<int, Vector3> OriginalPartScales = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, Vector2> OriginalPartSizes = new Dictionary<int, Vector2>();
        private static readonly Dictionary<int, Vector3> OriginalPartPositions = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, ControllerState> ControllerStates = new Dictionary<int, ControllerState>();

        internal static bool TryApplyHardHide(GuiCrosshairController? controller)
        {
            if (!CrosshairPlugin.Enabled.Value || !CrosshairPlugin.HideCrosshair.Value || controller == null)
                return false;

            if (controller.Content != null)
                controller.Content.SetActive(false);

            if (controller.NotUse != null)
                controller.NotUse.SetActive(false);

            return true;
        }

        internal static void ApplyVisibility(GuiCrosshairController? controller)
        {
            if (!CrosshairPlugin.Enabled.Value || !CrosshairPlugin.ForceShowInAds.Value || controller == null || controller.Content == null || controller.Content.activeSelf)
                return;

            var registry = Singleton<UnitsRegistry>.Instance;
            if (registry == null)
                return;

            var player = registry.GetPlayer();
            if (player == null || player.IsDeath || player.CurrentGear == null)
                return;

            if (player.IsInAimingState() && !player.IsReloading && !player.IsSwitchingGear)
                controller.Content.SetActive(true);
        }

        internal static void ApplyControllerColors(GuiCrosshairController? controller)
        {
            if (!CrosshairPlugin.Enabled.Value || controller == null)
                return;

            controller.NoTarget = GetConfiguredColor(CrosshairPlugin.IdleColorHex.Value);
            controller.FullDamage = GetConfiguredColor(CrosshairPlugin.FullDamageColorHex.Value);
            controller.BelowMaxDamage = GetConfiguredColor(CrosshairPlugin.BelowMaxColorHex.Value);
        }

        internal static void RefreshShapeIfNeeded(GuiCrosshairController? controller)
        {
            if (!CrosshairPlugin.Enabled.Value || controller == null || controller.CrosshairsPopulation == null)
                return;

            int id = controller.GetInstanceID();
            ControllerState state;
            if (!ControllerStates.TryGetValue(id, out state))
            {
                state = new ControllerState();
                ControllerStates[id] = state;
            }

            string currentShape = NormalizeShapeValue(CrosshairPlugin.ForceShape.Value);
            if (string.Equals(state.LastForcedShape, currentShape, StringComparison.Ordinal))
                return;

            state.LastForcedShape = currentShape;
            controller.CrosshairsPopulation.ClearContent();
        }

        internal static void ApplyBlank(GuiCrosshairBlank? blank)
        {
            if (!CrosshairPlugin.Enabled.Value || blank == null)
                return;

            var root = blank.transform as RectTransform;
            var rects = blank.GetComponentsInChildren<RectTransform>(true);
            if (rects == null || rects.Length == 0)
            {
                blank.transform.localScale = Vector3.one * GetClampedSizeMultiplier();
                return;
            }

            blank.transform.localScale = Vector3.one;
            for (int i = 0; i < rects.Length; i++)
            {
                var rect = rects[i];
                if (rect == null || rect == root)
                    continue;

                int id = rect.GetInstanceID();

                Vector3 baseScale;
                if (!OriginalPartScales.TryGetValue(id, out baseScale))
                {
                    baseScale = rect.localScale;
                    OriginalPartScales[id] = baseScale;
                }
                rect.localScale = baseScale * GetClampedSizeMultiplier();

                Vector2 baseSize;
                if (!OriginalPartSizes.TryGetValue(id, out baseSize))
                {
                    baseSize = rect.sizeDelta;
                    OriginalPartSizes[id] = baseSize;
                }
                rect.sizeDelta = baseSize * GetClampedSizeMultiplier();

                // Movable parts are positioned by SetAngle via Lerp — their position
                // is dynamic and already affected by the scaled angle/MaxBloom, so we
                // must not scale or cache their localPosition here.
                if (IsRuntimeCrosshairPart(blank, rect))
                    continue;

                Vector3 basePosition;
                if (!OriginalPartPositions.TryGetValue(id, out basePosition))
                {
                    basePosition = rect.localPosition;
                    OriginalPartPositions[id] = basePosition;
                }
                rect.localPosition = basePosition * GetClampedSizeMultiplier();
            }
        }

        internal static float ScaleAngle(float angle)
        {
            if (!CrosshairPlugin.Enabled.Value)
                return angle;

            return angle * Mathf.Clamp(CrosshairPlugin.SpreadMultiplier.Value, 0.25f, 4f);
        }

        internal static Vector3 ScaleSizeVector(Vector3 value)
        {
            if (!CrosshairPlugin.Enabled.Value)
                return value;

            return value * GetClampedSizeMultiplier();
        }

        internal static GameObject? GetForcedCrosshairPrefab(GuiCrosshairController? controller, ReticleInfo? reticleInfo)
        {
            if (!CrosshairPlugin.Enabled.Value || controller == null || reticleInfo == null)
                return null;

            var forcedType = GetForcedType();
            if (!forcedType.HasValue)
                return null;

            ReticleType type = reticleInfo.Type;
            if (type != ReticleType.Melee)
                type = forcedType.Value;

            switch (type)
            {
                case ReticleType.Dot:
                    return controller.PrototypeDot != null ? controller.PrototypeDot.gameObject : null;
                case ReticleType.Crosshair:
                    return controller.PrototypeCrosshair != null ? controller.PrototypeCrosshair.gameObject : null;
                case ReticleType.BrokenCircle:
                    return controller.PrototypeBrokenCircle != null ? controller.PrototypeBrokenCircle.gameObject : null;
                case ReticleType.Hashed:
                    return controller.PrototypeHashed != null ? controller.PrototypeHashed.gameObject : null;
                case ReticleType.HashedCrosshair:
                    return controller.PrototypeHashedCrosshair != null ? controller.PrototypeHashedCrosshair.gameObject : null;
                case ReticleType.Melee:
                    return controller.PrototypeMelee != null ? controller.PrototypeMelee.gameObject : null;
                default:
                    return controller.PrototypeDot != null ? controller.PrototypeDot.gameObject : null;
            }
        }

        private static bool IsRuntimeCrosshairPart(GuiCrosshairBlank blank, RectTransform rect)
        {
            var crosshair = blank as GuiCrosshair;
            if (crosshair == null || crosshair.Movable == null)
                return false;

            for (int i = 0; i < crosshair.Movable.Count; i++)
            {
                if (crosshair.Movable[i] == rect)
                    return true;
            }

            return false;
        }

        private static float GetClampedSizeMultiplier()
        {
            return Mathf.Clamp(CrosshairPlugin.SizeMultiplier.Value, 0.25f, 4f);
        }

        private static Color GetConfiguredColor(string hex)
        {
            Color baseColor;
            if (!TryParseHexColor(hex, out baseColor))
                baseColor = Color.white;

            float brightness = Mathf.Clamp(CrosshairPlugin.BrightnessMultiplier.Value, 0.1f, 4f);
            float alpha = Mathf.Clamp(CrosshairPlugin.Alpha.Value, 0.05f, 1f);

            baseColor.r = Mathf.Clamp01(baseColor.r * brightness);
            baseColor.g = Mathf.Clamp01(baseColor.g * brightness);
            baseColor.b = Mathf.Clamp01(baseColor.b * brightness);
            baseColor.a = alpha;
            return baseColor;
        }

        private static bool TryParseHexColor(string value, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(value))
                return false;

            string hex = value.Trim();
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);

            if (hex.Length != 6 && hex.Length != 8)
                return false;

            byte r;
            byte g;
            byte b;
            byte a = 255;
            if (!TryParseHexByte(hex.Substring(0, 2), out r) ||
                !TryParseHexByte(hex.Substring(2, 2), out g) ||
                !TryParseHexByte(hex.Substring(4, 2), out b))
            {
                return false;
            }

            if (hex.Length == 8 && !TryParseHexByte(hex.Substring(6, 2), out a))
                return false;

            color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            return true;
        }

        private static bool TryParseHexByte(string value, out byte result)
        {
            try
            {
                result = Convert.ToByte(value, 16);
                return true;
            }
            catch
            {
                result = 0;
                return false;
            }
        }

        private static ReticleType? GetForcedType()
        {
            switch (NormalizeShapeValue(CrosshairPlugin.ForceShape.Value))
            {
                case "Dot":
                    return ReticleType.Dot;
                case "Crosshair":
                    return ReticleType.Crosshair;
                case "BrokenCircle":
                    return ReticleType.BrokenCircle;
                case "Hashed":
                    return ReticleType.Hashed;
                case "HashedCrosshair":
                    return ReticleType.HashedCrosshair;
                case "Melee":
                    return ReticleType.Melee;
                default:
                    return null;
            }
        }

        private static string NormalizeShapeValue(string value)
        {
            return string.IsNullOrEmpty(value) ? "__DEFAULT__" : value.Trim();
        }
    }

    [HarmonyPatch(typeof(GuiCrosshairController), "Update")]
    internal static class GuiCrosshairControllerUpdatePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(GuiCrosshairController __instance)
        {
            if (CrosshairRuntime.TryApplyHardHide(__instance))
                return false;

            CrosshairRuntime.ApplyControllerColors(__instance);
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(GuiCrosshairController __instance)
        {
            CrosshairRuntime.RefreshShapeIfNeeded(__instance);
            CrosshairRuntime.ApplyVisibility(__instance);
        }
    }

    [HarmonyPatch(typeof(GuiCrosshairController), "GetAppropriateCrosshairPrefab")]
    internal static class GuiCrosshairControllerGetAppropriatePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(GuiCrosshairController __instance, ReticleInfo __0, ref GameObject __result)
        {
            if (!CrosshairPlugin.Enabled.Value)
                return true;

            var prefab = CrosshairRuntime.GetForcedCrosshairPrefab(__instance, __0);
            if (prefab == null)
                return true;

            __result = prefab;
            return false;
        }
    }

    [HarmonyPatch(typeof(GuiCrosshairBlank), "SetColor")]
    internal static class GuiCrosshairBlankSetColorPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GuiCrosshairBlank __instance)
        {
            CrosshairRuntime.ApplyBlank(__instance);
        }
    }

    [HarmonyPatch(typeof(GuiCrosshair), "SetAngle")]
    internal static class GuiCrosshairSetAnglePatch
    {
        [HarmonyPrefix]
        private static void Prefix(GuiCrosshair __instance, ref float angle, out Vector3 __state)
        {
            angle = CrosshairRuntime.ScaleAngle(angle);
            __state = __instance.MaxBloom;
            if (CrosshairPlugin.Enabled.Value)
                __instance.MaxBloom = CrosshairRuntime.ScaleSizeVector(__instance.MaxBloom);
        }

        [HarmonyPostfix]
        private static void Postfix(GuiCrosshair __instance, Vector3 __state)
        {
            __instance.MaxBloom = __state;
            CrosshairRuntime.ApplyBlank(__instance);
        }
    }

    [HarmonyPatch(typeof(GuiCrosshairCircle), "SetAngle")]
    internal static class GuiCrosshairCircleSetAnglePatch
    {
        [HarmonyPrefix]
        private static void Prefix(ref float angle)
        {
            angle = CrosshairRuntime.ScaleAngle(angle);
        }

        [HarmonyPostfix]
        private static void Postfix(GuiCrosshairCircle __instance)
        {
            CrosshairRuntime.ApplyBlank(__instance);
        }
    }

    [HarmonyPatch(typeof(GuiCrosshairMelee), "SetAngle")]
    internal static class GuiCrosshairMeleeSetAnglePatch
    {
        [HarmonyPrefix]
        private static void Prefix(ref float angle)
        {
            angle = CrosshairRuntime.ScaleAngle(angle);
        }

        [HarmonyPostfix]
        private static void Postfix(GuiCrosshairMelee __instance)
        {
            CrosshairRuntime.ApplyBlank(__instance);
        }
    }
}

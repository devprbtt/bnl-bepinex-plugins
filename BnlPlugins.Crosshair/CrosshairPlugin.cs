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
                "Enable crosshair overrides.");
            IdleColorHex = Config.Bind("Crosshair", "IdleColor", "#FFFFFF",
                CreateColorDescription("Crosshair color when not targeting an enemy.", 90, "Idle Color"));
            FullDamageColorHex = Config.Bind("Crosshair", "FullDamageColor", "#FF0000",
                CreateColorDescription("Crosshair color when target is inside full damage range.", 89, "Full Damage Color"));
            BelowMaxColorHex = Config.Bind("Crosshair", "BelowMaxColor", "#FF0000",
                CreateColorDescription("Crosshair color when target is beyond full damage but still in max range.", 88, "Below Max Color"));
            BrightnessMultiplier = Config.Bind("Crosshair", "BrightnessMultiplier", 1f,
                new ConfigDescription("Brightness multiplier applied to all crosshair colors.", new AcceptableValueRange<float>(0.1f, 4f)));
            SizeMultiplier = Config.Bind("Crosshair", "SizeMultiplier", 1f,
                new ConfigDescription("Crosshair size multiplier.", new AcceptableValueRange<float>(0.25f, 4f)));
            SpreadMultiplier = Config.Bind("Crosshair", "SpreadMultiplier", 1f,
                new ConfigDescription("Crosshair spread multiplier.", new AcceptableValueRange<float>(0.25f, 4f)));
            Alpha = Config.Bind("Crosshair", "Alpha", 1f,
                new ConfigDescription("Crosshair alpha multiplier.", new AcceptableValueRange<float>(0.05f, 1f)));
            ForceShape = Config.Bind("Crosshair", "ForceShape", "__DEFAULT__",
                new ConfigDescription("Force crosshair shape.", new AcceptableValueList<string>(AllowedShapes)));
            ForceShowInAds = Config.Bind("Crosshair", "ForceShowInAds", false,
                "Force the crosshair to stay visible while aiming down sights.");
            HideCrosshair = Config.Bind("Crosshair", "HideCrosshair", false,
                "Hide the crosshair entirely.");

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
                CustomDrawer = CrosshairColorDrawer.Draw,
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

    internal static class CrosshairColorDrawer
    {
        private sealed class PresetColor
        {
            public string Name = string.Empty;
            public Color Color;
        }

        // Per-entry text buffer so partial input isn't clobbered by other controls.
        private static readonly Dictionary<ConfigEntryBase, string> _editBuffer
            = new Dictionary<ConfigEntryBase, string>();

        private static GUIStyle? _presetButtonStyle;
        private static readonly PresetColor[] Presets =
        {
            new PresetColor { Name = "White", Color = new Color(1f, 1f, 1f, 1f) },
            new PresetColor { Name = "Black", Color = new Color(0f, 0f, 0f, 1f) },
            new PresetColor { Name = "Red", Color = new Color(1f, 0f, 0f, 1f) },
            new PresetColor { Name = "Green", Color = new Color(0f, 1f, 0f, 1f) },
            new PresetColor { Name = "Blue", Color = new Color(0f, 0.6f, 1f, 1f) },
            new PresetColor { Name = "Yellow", Color = new Color(1f, 1f, 0f, 1f) },
            new PresetColor { Name = "Orange", Color = new Color(1f, 0.5f, 0f, 1f) },
            new PresetColor { Name = "Magenta", Color = new Color(1f, 0f, 1f, 1f) },
            new PresetColor { Name = "Cyan", Color = new Color(0f, 1f, 1f, 1f) },
            new PresetColor { Name = "Purple", Color = new Color(0.6f, 0.2f, 1f, 1f) },
            new PresetColor { Name = "Pink", Color = new Color(1f, 0.75f, 0.8f, 1f) },
            new PresetColor { Name = "Gray", Color = new Color(0.6f, 0.6f, 0.6f, 1f) }
        };

        internal static void Draw(ConfigEntryBase entry)
        {
            string stored = entry.BoxedValue as string ?? "#FFFFFFFF";
            string controlName = "bnl_hex_" + entry.GetHashCode();
            bool isFocused = GUI.GetNameOfFocusedControl() == controlName;

            // While the field is focused, show the live buffer so typing is visible.
            // When not focused, sync buffer to stored so external changes (presets, slider) show up.
            string bufferValue;
            if (!_editBuffer.TryGetValue(entry, out bufferValue) || !isFocused)
            {
                bufferValue = stored;
                _editBuffer[entry] = stored;
            }

            Color color;
            if (!TryParseHexColor(bufferValue, out color) && !TryParseHexColor(stored, out color))
                color = Color.white;

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            GUI.SetNextControlName(controlName);
            string typed = GUILayout.TextField(bufferValue, GUILayout.ExpandWidth(true));
            if (!string.Equals(typed, bufferValue, StringComparison.Ordinal))
            {
                _editBuffer[entry] = typed;
                Color parsed;
                if (TryParseHexColor(typed, out parsed))
                {
                    entry.BoxedValue = ToHexString(parsed);
                    GUI.changed = true;
                }
            }

            // Preview box row.
            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            DrawPreviewBox(new Color(color.r, color.g, color.b, 1f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawPresets(entry, color);

            // Alpha slider — only applies when stored is valid; skip while typing.
            Color storedColor;
            if (!isFocused && TryParseHexColor(stored, out storedColor))
            {
                float alpha = DrawChannel("A", storedColor.a);
                string updatedHex = ToHexString(new Color(storedColor.r, storedColor.g, storedColor.b, alpha));
                if (!string.Equals(updatedHex, stored, StringComparison.OrdinalIgnoreCase))
                {
                    entry.BoxedValue = updatedHex;
                    _editBuffer[entry] = updatedHex;
                    GUI.changed = true;
                }
            }
            else if (isFocused)
            {
                // Show the slider as read-only while typing so layout stays stable.
                DrawChannel("A", color.a);
            }

            GUILayout.EndVertical();
        }

        private static void DrawPreviewBox(Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(26f, 18f, GUILayout.Width(26f), GUILayout.Height(18f));
            DrawColorRect(rect, color, true);
        }

        private static void DrawPresets(ConfigEntryBase entry, Color current)
        {
            const int columns = 3;
            GUIStyle style = GetPresetButtonStyle();
            for (int i = 0; i < Presets.Length; i += columns)
            {
                GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                for (int j = i; j < i + columns && j < Presets.Length; j++)
                {
                    var preset = Presets[j];
                    Color oldBackground = GUI.backgroundColor;
                    Color oldContent = GUI.contentColor;
                    GUI.backgroundColor = ApproximatelyEqual(current, preset.Color) ? new Color(0.22f, 0.22f, 0.22f, 1f) : GUI.backgroundColor;
                    GUI.contentColor = preset.Color;
                    if (GUILayout.Button(preset.Name, style, GUILayout.Width(72f), GUILayout.Height(20f)))
                    {
                        string newHex = ToHexString(new Color(preset.Color.r, preset.Color.g, preset.Color.b, current.a));
                        entry.BoxedValue = newHex;
                        _editBuffer[entry] = newHex;
                        GUI.FocusControl(null);
                        GUI.changed = true;
                    }
                    GUI.backgroundColor = oldBackground;
                    GUI.contentColor = oldContent;
                }
                GUILayout.EndHorizontal();
            }
        }

        private static float DrawChannel(string label, float value)
        {
            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            GUILayout.Label(label, GUILayout.Width(14f));
            float result = GUILayout.HorizontalSlider(value, 0f, 1f, GUILayout.ExpandWidth(true));
            GUILayout.Label(Mathf.RoundToInt(result * 255f).ToString(), GUILayout.Width(36f));
            GUILayout.EndHorizontal();
            return result;
        }

        private static string NormalizeHex(string text, Color fallback)
        {
            Color parsed;
            return TryParseHexColor(text, out parsed) ? ToHexString(parsed) : ToHexString(fallback);
        }

        private static string ToHexString(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255);
            return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", r, g, b, a);
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

        private static bool ApproximatelyEqual(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f &&
                   Mathf.Abs(a.g - b.g) < 0.01f &&
                   Mathf.Abs(a.b - b.b) < 0.01f;
        }

        private static void DrawColorRect(Rect rect, Color color, bool selected)
        {
            Color old = GUI.color;

            GUI.color = Color.black;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            var inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            GUI.color = color;
            GUI.DrawTexture(inner, Texture2D.whiteTexture);

            if (selected)
            {
                var mark = new Rect(rect.x + 5f, rect.y + 4f, rect.width - 10f, rect.height - 8f);
                GUI.color = color.grayscale > 0.5f ? Color.black : Color.white;
                GUI.DrawTexture(mark, Texture2D.whiteTexture);
            }

            GUI.color = old;
        }

        private static GUIStyle GetPresetButtonStyle()
        {
            if (_presetButtonStyle == null)
            {
                _presetButtonStyle = new GUIStyle(GUI.skin.button);
                _presetButtonStyle.margin = new RectOffset(1, 1, 1, 1);
                _presetButtonStyle.padding = new RectOffset(2, 2, 1, 1);
                _presetButtonStyle.fontStyle = FontStyle.Bold;
                _presetButtonStyle.alignment = TextAnchor.MiddleCenter;
            }

            return _presetButtonStyle;
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
    }

    [HarmonyPatch(typeof(GuiCrosshairController), "Update")]
    internal static class GuiCrosshairControllerUpdatePatch
    {
        private static bool _lastContent = true;

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
            bool contentNow = __instance.Content != null && __instance.Content.activeSelf;
            if (contentNow != _lastContent)
            {
                _lastContent = contentNow;
                var hud = Singleton<Hud>.Instance;
                var registry = Singleton<UnitsRegistry>.Instance;
                var player = registry != null ? registry.GetPlayer() : null;
                CrosshairPlugin.Log.LogInfo(
                    "[Crosshair] Content changed -> " + contentNow +
                    " | IsPlayerAlive=" + (hud != null ? hud.IsPlayerAlive.ToString() : "?") +
                    " IsMenu=" + (hud != null ? hud.IsMenu.ToString() : "?") +
                    " IsMapFullSize=" + (hud != null ? hud.IsMapFullSize.ToString() : "?") +
                    " ShowScores=" + (hud != null ? hud.ShowScores.ToString() : "?") +
                    " | player=" + (player != null ? "found" : "null") +
                    " CurrentGear=" + (player != null && player.CurrentGear != null ? "present" : "null") +
                    " IsReloading=" + (player != null ? player.IsReloading.ToString() : "?") +
                    " IsSwitchingGear=" + (player != null ? player.IsSwitchingGear.ToString() : "?") +
                    " IsInAimingState=" + (player != null ? player.IsInAimingState().ToString() : "?") +
                    " PopCount=" + (__instance.CrosshairsPopulation != null ? __instance.CrosshairsPopulation.Content.Count.ToString() : "?"));
            }

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

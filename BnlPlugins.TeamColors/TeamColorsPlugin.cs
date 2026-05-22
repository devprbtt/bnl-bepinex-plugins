using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace BnlPlugins.TeamColors
{
    [BepInPlugin("bnl.community.teamcolors", "BNL Team Colors", "0.1.0")]
    public sealed class TeamColorsPlugin : BaseUnityPlugin
    {
        internal static TeamColorsPlugin Instance = null!;
        internal static BepInEx.Logging.ManualLogSource Log = null!;
        private const string HarmonyId = "bnl.community.teamcolors";

        private static readonly string[] PresetNames = { "Custom", "Default", "Classic", "Beta" };

        // Preset colors: [preset index][0=friendly, 1=enemy, 2=background_friendly, 3=background_enemy]
        private static readonly string[,] PresetColors =
        {
            // Custom — populated from config at runtime
            { "#4AA3FFFF", "#FF5A5AFF", "#4AA3FF80", "#FF5A5A80" },
            // Default
            { "#4AA3FFFF", "#FF5A5AFF", "#4AA3FF80", "#FF5A5A80" },
            // Classic
            { "#B4D0FBFF", "#D7373FFF", "#B4D0FB80", "#D7373F80" },
            // Beta
            { "#0BA187FF", "#9138A5FF", "#0BA18780", "#9138A580" },
        };

        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<string> Preset = null!;
        internal static ConfigEntry<string> FriendlyColorHex = null!;
        internal static ConfigEntry<string> EnemyColorHex = null!;
        internal static ConfigEntry<string> FriendlyBgColorHex = null!;
        internal static ConfigEntry<string> EnemyBgColorHex = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Enabled = Config.Bind("TeamColors", "Enabled", true,
                new ConfigDescription("Enable team color overrides.", null,
                    new ConfigurationManagerAttributes { Order = 101 }));
            Preset = Config.Bind("TeamColors", "Preset", "Default",
                new ConfigDescription("Color preset. Set to Custom to use the colors below.",
                    new AcceptableValueList<string>(PresetNames),
                    new ConfigurationManagerAttributes { Order = 100 }));

            FriendlyColorHex = Config.Bind("TeamColors", "FriendlyColor", "#4AA3FF",
                MakeColorDesc("Friendly team color.", 90, "Friendly Color"));
            EnemyColorHex = Config.Bind("TeamColors", "EnemyColor", "#FF5A5A",
                MakeColorDesc("Enemy team color.", 89, "Enemy Color"));
            FriendlyBgColorHex = Config.Bind("TeamColors", "FriendlyBgColor", "#4AA3FF80",
                MakeColorDesc("Friendly force-field / background color.", 88, "Friendly BG Color"));
            EnemyBgColorHex = Config.Bind("TeamColors", "EnemyBgColor", "#FF5A5A80",
                MakeColorDesc("Enemy force-field / background color.", 87, "Enemy BG Color"));

            Preset.SettingChanged += (_, __) => ApplyPresetToEntries();
            FriendlyColorHex.SettingChanged += (_, __) => ApplyColorsToContainer();
            EnemyColorHex.SettingChanged += (_, __) => ApplyColorsToContainer();
            FriendlyBgColorHex.SettingChanged += (_, __) => ApplyColorsToContainer();
            EnemyBgColorHex.SettingChanged += (_, __) => ApplyColorsToContainer();
            Enabled.SettingChanged += (_, __) => ApplyColorsToContainer();

            _harmony = new Harmony(HarmonyId);
            // No patches needed — we write directly into TeamColorContainer
            Logger.LogInfo("[BNL Team Colors] Loaded");
        }

        private float _applyTimer = 0f;

        private void Update()
        {
            // Re-apply every second to survive any container resets.
            _applyTimer -= UnityEngine.Time.deltaTime;
            if (_applyTimer > 0f) return;
            _applyTimer = 1f;
            ApplyColorsToContainer();
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }

        private static ConfigDescription MakeColorDesc(string description, int order, string displayName)
        {
            return new ConfigDescription(description, null, new ConfigurationManagerAttributes
            {
                CustomDrawer = TeamColorDrawer.Draw,
                Order = order,
                DispName = displayName,
            });
        }

        private static void ApplyPresetToEntries()
        {
            int idx = GetPresetIndex();
            if (idx == 0)
                return; // Custom — leave entries as-is

            FriendlyColorHex.Value = PresetColors[idx, 0];
            EnemyColorHex.Value = PresetColors[idx, 1];
            FriendlyBgColorHex.Value = PresetColors[idx, 2];
            EnemyBgColorHex.Value = PresetColors[idx, 3];
        }

        internal static int GetPresetIndex()
        {
            string p = Preset.Value ?? "Default";
            for (int i = 0; i < PresetNames.Length; i++)
                if (string.Equals(p, PresetNames[i], StringComparison.OrdinalIgnoreCase))
                    return i;
            return 1; // Default
        }

        internal static void ApplyColorsToContainer()
        {
            var container = Singleton<TeamColorContainer>.Instance;
            if (container == null || container.Gui == null)
                return;

            if (!Enabled.Value)
                return;

            Color friendly, enemy, friendlyBg, enemyBg;
            if (!TeamColorHelper.TryParseHex(FriendlyColorHex.Value, out friendly)) return;
            if (!TeamColorHelper.TryParseHex(EnemyColorHex.Value, out enemy)) return;
            TeamColorHelper.TryParseHex(FriendlyBgColorHex.Value, out friendlyBg);
            TeamColorHelper.TryParseHex(EnemyBgColorHex.Value, out enemyBg);

            container.Gui.TeamFriendly = friendly;
            container.Gui.TeamEnemy = enemy;
            container.Gui.BackgroundTeamFriendly = friendlyBg;
            container.Gui.BackgroundTeamEnemy = enemyBg;
        }
    }

    internal static class TeamColorHelper
    {
        private static readonly FieldInfo UnitField =
            typeof(GuiHealthbar).GetField("unit", BindingFlags.NonPublic | BindingFlags.Instance)!;

        internal static bool TryGetUnit(GuiHealthbar healthbar, out Unit unit)
        {
            unit = UnitField?.GetValue(healthbar) as Unit;
            return unit != null;
        }

        internal static bool IsTeamFriendly(TeamType team)
        {
            var zoneData = Singleton<ZoneData>.Instance;
            if (zoneData == null)
                return true;
            return team == zoneData.MyTeam;
        }

        internal static bool TryParseHex(string value, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(value))
                return false;

            string hex = value.Trim();
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);

            if (hex.Length != 6 && hex.Length != 8)
                return false;

            byte r, g, b;
            byte a = 255;
            try
            {
                r = Convert.ToByte(hex.Substring(0, 2), 16);
                g = Convert.ToByte(hex.Substring(2, 2), 16);
                b = Convert.ToByte(hex.Substring(4, 2), 16);
                if (hex.Length == 8)
                    a = Convert.ToByte(hex.Substring(6, 2), 16);
            }
            catch { return false; }

            color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            return true;
        }
    }


    internal static class TeamColorDrawer
    {
        private static readonly Dictionary<ConfigEntryBase, string> _editBuffer
            = new Dictionary<ConfigEntryBase, string>();

        private static GUIStyle? _presetButtonStyle;

        private sealed class PresetColor
        {
            public string Name = string.Empty;
            public Color Color;
        }

        private static readonly PresetColor[] Presets =
        {
            new PresetColor { Name = "White",   Color = new Color(1f, 1f, 1f, 1f) },
            new PresetColor { Name = "Black",   Color = new Color(0f, 0f, 0f, 1f) },
            new PresetColor { Name = "Red",     Color = new Color(1f, 0.35f, 0.35f, 1f) },
            new PresetColor { Name = "Blue",    Color = new Color(0.29f, 0.64f, 1f, 1f) },
            new PresetColor { Name = "Green",   Color = new Color(0.04f, 0.63f, 0.53f, 1f) },
            new PresetColor { Name = "Purple",  Color = new Color(0.57f, 0.22f, 0.65f, 1f) },
        };

        internal static void Draw(ConfigEntryBase entry)
        {
            string stored = entry.BoxedValue as string ?? "#FFFFFFFF";
            string controlName = "bnl_tc_hex_" + entry.GetHashCode();
            bool isFocused = GUI.GetNameOfFocusedControl() == controlName;

            string bufferValue;
            if (!_editBuffer.TryGetValue(entry, out bufferValue) || !isFocused)
            {
                bufferValue = stored;
                _editBuffer[entry] = stored;
            }

            Color color;
            if (!TeamColorHelper.TryParseHex(bufferValue, out color) &&
                !TeamColorHelper.TryParseHex(stored, out color))
                color = Color.white;

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            GUI.SetNextControlName(controlName);
            string typed = GUILayout.TextField(bufferValue, GUILayout.ExpandWidth(true));
            if (!string.Equals(typed, bufferValue, StringComparison.Ordinal))
            {
                _editBuffer[entry] = typed;
                Color parsed;
                if (TeamColorHelper.TryParseHex(typed, out parsed))
                {
                    entry.BoxedValue = ToHexString(parsed);
                    GUI.changed = true;
                }
            }

            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            DrawColorRect(GUILayoutUtility.GetRect(26f, 18f, GUILayout.Width(26f), GUILayout.Height(18f)),
                new Color(color.r, color.g, color.b, 1f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawPresets(entry, color);

            Color storedColor;
            if (!isFocused && TeamColorHelper.TryParseHex(stored, out storedColor))
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
                DrawChannel("A", color.a);
            }

            GUILayout.EndVertical();
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
                    Color oldBg = GUI.backgroundColor;
                    Color oldFg = GUI.contentColor;
                    bool active = Mathf.Abs(current.r - preset.Color.r) < 0.01f &&
                                  Mathf.Abs(current.g - preset.Color.g) < 0.01f &&
                                  Mathf.Abs(current.b - preset.Color.b) < 0.01f;
                    if (active) GUI.backgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f);
                    GUI.contentColor = preset.Color;
                    if (GUILayout.Button(preset.Name, style, GUILayout.Width(72f), GUILayout.Height(20f)))
                    {
                        string newHex = ToHexString(new Color(preset.Color.r, preset.Color.g, preset.Color.b, current.a));
                        entry.BoxedValue = newHex;
                        _editBuffer[entry] = newHex;
                        GUI.FocusControl(null);
                        GUI.changed = true;
                    }
                    GUI.backgroundColor = oldBg;
                    GUI.contentColor = oldFg;
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

        private static void DrawColorRect(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            var inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            GUI.color = color;
            GUI.DrawTexture(inner, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static string ToHexString(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255);
            return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", r, g, b, a);
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
    }
}

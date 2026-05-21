using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace BnlPlugins.CombatNumbers
{
    internal static class ColorHelper
    {
        internal static bool TryParseHex(string value, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(value)) return false;
            string hex = value.Trim();
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            if (hex.Length != 6 && hex.Length != 8) return false;
            try
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                byte a = hex.Length == 8 ? Convert.ToByte(hex.Substring(6, 2), 16) : (byte)255;
                color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                return true;
            }
            catch { return false; }
        }

        internal static string ToHex(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255);
            return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", r, g, b, a);
        }
    }

    internal static class ColorDrawer
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
            new PresetColor { Name = "Yellow",  Color = new Color(1f, 1f, 0f, 1f) },
            new PresetColor { Name = "Orange",  Color = new Color(1f, 0.4f, 0f, 1f) },
            new PresetColor { Name = "Red",     Color = new Color(1f, 0.2f, 0.2f, 1f) },
            new PresetColor { Name = "Green",   Color = new Color(0.57f, 0.93f, 0.47f, 1f) },
            new PresetColor { Name = "Cyan",    Color = new Color(0f, 1f, 1f, 1f) },
        };

        internal static void Draw(ConfigEntryBase entry)
        {
            string stored = entry.BoxedValue as string ?? "#FFFFFFFF";
            string controlName = "bnl_cn_hex_" + entry.GetHashCode();
            bool isFocused = GUI.GetNameOfFocusedControl() == controlName;

            string bufferValue;
            if (!_editBuffer.TryGetValue(entry, out bufferValue) || !isFocused)
            {
                bufferValue = stored;
                _editBuffer[entry] = stored;
            }

            Color color;
            if (!ColorHelper.TryParseHex(bufferValue, out color) &&
                !ColorHelper.TryParseHex(stored, out color))
                color = Color.white;

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            GUI.SetNextControlName(controlName);
            string typed = GUILayout.TextField(bufferValue, GUILayout.ExpandWidth(true));
            if (!string.Equals(typed, bufferValue, StringComparison.Ordinal))
            {
                _editBuffer[entry] = typed;
                Color parsed;
                if (ColorHelper.TryParseHex(typed, out parsed))
                {
                    entry.BoxedValue = ColorHelper.ToHex(parsed);
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
            if (!isFocused && ColorHelper.TryParseHex(stored, out storedColor))
            {
                float alpha = DrawChannel("A", storedColor.a);
                string updatedHex = ColorHelper.ToHex(new Color(storedColor.r, storedColor.g, storedColor.b, alpha));
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
                        string newHex = ColorHelper.ToHex(new Color(preset.Color.r, preset.Color.g, preset.Color.b, current.a));
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

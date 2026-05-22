using System;
using BepInEx.Configuration;

namespace BnlPlugins.ShieldTimer
{
    internal static class FloatConfig
    {
        internal static ConfigDescription Range(string description, float min, float max, int order, string? displayName = null)
        {
            return new ConfigDescription(description, new AcceptableValueRange<float>(min, max), new ConfigurationManagerAttributes
            {
                Order = order,
                DispName = displayName,
                ObjToStr = value => RoundToSingleDecimal(Convert.ToSingle(value)).ToString("0.0"),
                StrToObj = text => RoundToSingleDecimal(Parse(text))
            });
        }

        internal static void BindRound(ConfigEntry<float> entry, float min, float max)
        {
            entry.Value = ClampAndRound(entry.Value, min, max);
            entry.SettingChanged += (_, __) =>
            {
                float rounded = ClampAndRound(entry.Value, min, max);
                if (Math.Abs(entry.Value - rounded) > 0.0001f)
                    entry.Value = rounded;
            };
        }

        private static float Parse(string? value)
        {
            float parsed;
            if (!float.TryParse(value ?? string.Empty, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed) &&
                !float.TryParse(value ?? string.Empty, out parsed))
            {
                parsed = 0f;
            }

            return parsed;
        }

        private static float ClampAndRound(float value, float min, float max)
        {
            return UnityEngine.Mathf.Clamp(RoundToSingleDecimal(value), min, max);
        }

        internal static float RoundToSingleDecimal(float value)
        {
            return (float)Math.Round(value, 1, MidpointRounding.AwayFromZero);
        }
    }
}

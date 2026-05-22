using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace BnlPlugins.LowHpAlert
{
    [BepInPlugin("bnl.community.lowhpalert", "BNL Low HP Alert", "0.1.0")]
    public sealed class LowHpAlertPlugin : BaseUnityPlugin
    {
        private const string HarmonyId = "bnl.community.lowhpalert";

        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<string> AlertColorHex = null!;
        internal static ConfigEntry<float> Threshold = null!;
        internal static ConfigEntry<bool> ShowDirectionIndicator = null!;
        internal static ConfigEntry<float> IndicatorSize = null!;
        internal static ConfigEntry<float> IndicatorAlpha = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Enabled = Config.Bind("Low HP Alert", "Enabled", true,
                new ConfigDescription("Changes the health bar and name color of friendly players when their health drops below the threshold.",
                    null, new ConfigurationManagerAttributes { Order = 100 }));
            AlertColorHex = Config.Bind("Low HP Alert", "AlertColor", "#FF4444FF",
                new ConfigDescription("Alert color for low-health friendlies.", null,
                    new ConfigurationManagerAttributes { CustomDrawer = ColorDrawer.Draw, Order = 99, DispName = "Alert Color" }));
            Threshold = Config.Bind("Low HP Alert", "Threshold", 0.3f,
                FloatConfig.Range("Low-health threshold as a fraction of max HP.", 0.1f, 0.9f, 98, "Threshold"));
            ShowDirectionIndicator = Config.Bind("Low HP Alert", "ShowDirectionIndicator", true,
                new ConfigDescription("Show a direction arrow when a low-health friendly is off-screen.",
                    null, new ConfigurationManagerAttributes { Order = 97, DispName = "Show Direction Indicator" }));
            IndicatorSize = Config.Bind("Low HP Alert", "IndicatorSize", 1.0f,
                FloatConfig.Range("Size of the off-screen indicator.", 0.1f, 5.0f, 96, "Indicator Size"));
            IndicatorAlpha = Config.Bind("Low HP Alert", "IndicatorAlpha", 1.0f,
                FloatConfig.Range("Alpha of the off-screen indicator.", 0.1f, 1.0f, 95, "Indicator Alpha"));

            FloatConfig.BindRound(Threshold, 0.1f, 0.9f);
            FloatConfig.BindRound(IndicatorSize, 0.1f, 5.0f);
            FloatConfig.BindRound(IndicatorAlpha, 0.1f, 1.0f);

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(LowHpAlertPlugin).Assembly);
            Logger.LogInfo("[BNL Low HP Alert] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }

        internal static bool TryGetAlertColor(out Color color)
        {
            return ColorHelper.TryParseHex(AlertColorHex.Value, out color);
        }
    }

    [HarmonyPatch(typeof(GuiHealthbar), "Start")]
    internal static class GuiHealthbarStartPatch
    {
        private static void Postfix(GuiHealthbar __instance)
        {
            FriendlyLowHealthRuntime.AttachFriendlyLowHealth(__instance);
        }
    }

    [HarmonyPatch(typeof(GuiHealthbar), "IsUnitAvailableForShow")]
    internal static class GuiHealthbarAvailablePatch
    {
        private static void Postfix(GuiHealthbar __instance, ref bool __result)
        {
            if (__result)
                return;

            if (FriendlyLowHealthRuntime.IsFriendlyLowHealth(__instance))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(GuiHealthbar), "AlphaUpdate")]
    internal static class GuiHealthbarAlphaUpdatePatch
    {
        private static readonly FieldInfo ShowTimeField =
            typeof(GuiHealthbar).GetField("showTime", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static void Prefix(GuiHealthbar __instance)
        {
            if (!FriendlyLowHealthRuntime.IsFriendlyLowHealth(__instance) || ReferenceEquals(ShowTimeField, null))
                return;

            ShowTimeField.SetValue(__instance, 1f);
        }
    }

    internal static class FriendlyLowHealthRuntime
    {
        private static readonly FieldInfo FollowField =
            typeof(GuiHealthbar).GetField("follow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo UnitField =
            typeof(GuiHealthbar).GetField("unit", BindingFlags.Instance | BindingFlags.NonPublic)!;

        internal static void AttachFriendlyLowHealth(GuiHealthbar healthbar)
        {
            if (healthbar == null || healthbar.HealthBar == null)
                return;

            FriendlyLowHealthController controller = healthbar.gameObject.GetComponent<FriendlyLowHealthController>();
            if (controller == null)
                controller = healthbar.gameObject.AddComponent<FriendlyLowHealthController>();
            controller.Init(healthbar);
        }

        internal static bool IsFriendlyLowHealth(GuiHealthbar healthbar)
        {
            if (!LowHpAlertPlugin.Enabled.Value || healthbar == null)
                return false;

            Unit unit = ReferenceEquals(UnitField, null) ? null : UnitField.GetValue(healthbar) as Unit;
            if (unit == null || unit.IsMyPlayer || !unit.PlayerId.HasValue || unit.IsDeath)
                return false;

            ZoneData zoneData = Singleton<ZoneData>.Instance;
            if (zoneData == null || unit.Team == TeamType.Neutral || unit.Team != zoneData.MyTeam)
                return false;

            GuiFollow follow = ReferenceEquals(FollowField, null) ? null : FollowField.GetValue(healthbar) as GuiFollow;
            if (follow == null || !follow.IsInFrontOfCamera)
                return false;

            CameraDeath deathCam = Singleton<CameraDeath>.Instance;
            if (deathCam != null && deathCam.Target != null)
                return true;

            float maxHp = unit.MaxHealth;
            if (maxHp <= 0f)
                return false;

            return (unit.Health / maxHp) <= LowHpAlertPlugin.Threshold.Value;
        }
    }

    public sealed class FriendlyLowHealthController : MonoBehaviour
    {
        private static readonly FieldInfo UnitField =
            typeof(GuiHealthbar).GetField("unit", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo FollowField =
            typeof(GuiHealthbar).GetField("follow", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private GuiHealthbar? _healthbar;

        public void Init(GuiHealthbar source)
        {
            _healthbar = source;
        }

        private void OnDestroy()
        {
            Unit unit = _healthbar != null && !ReferenceEquals(UnitField, null) ? UnitField.GetValue(_healthbar) as Unit : null;
            if (unit != null)
                FriendlyLowHealthIndicatorService.RemoveIndicator(unit);
        }

        private void LateUpdate()
        {
            if (!LowHpAlertPlugin.Enabled.Value || _healthbar == null || _healthbar.HealthBar == null)
                return;

            Unit unit = ReferenceEquals(UnitField, null) ? null : UnitField.GetValue(_healthbar) as Unit;
            if (unit == null || unit.IsMyPlayer || !unit.PlayerId.HasValue || unit.Health <= 0f || unit.IsDeath)
            {
                if (unit != null)
                    FriendlyLowHealthIndicatorService.RemoveIndicator(unit);
                return;
            }

            ZoneData zoneData = Singleton<ZoneData>.Instance;
            if (zoneData == null || unit.Team == TeamType.Neutral || unit.Team != zoneData.MyTeam)
            {
                FriendlyLowHealthIndicatorService.RemoveIndicator(unit);
                return;
            }

            float maxHp = unit.MaxHealth;
            bool isLow = maxHp > 0f && (unit.Health / maxHp) <= LowHpAlertPlugin.Threshold.Value;
            Color alertColor;
            if (!LowHpAlertPlugin.TryGetAlertColor(out alertColor))
                alertColor = new Color(1f, 0.27f, 0.27f, 1f);

            if (isLow)
            {
                _healthbar.HealthBar.color = alertColor;
                if (_healthbar.Title != null)
                    _healthbar.Title.color = alertColor;

                if (LowHpAlertPlugin.ShowDirectionIndicator.Value)
                {
                    GuiFollow follow = ReferenceEquals(FollowField, null) ? null : FollowField.GetValue(_healthbar) as GuiFollow;
                    bool isOffScreen = follow == null || !follow.IsInFrontOfCamera;
                    FriendlyLowHealthIndicatorService.UpdateIndicator(unit, isOffScreen, alertColor);
                }
            }
            else
            {
                FriendlyLowHealthIndicatorService.RemoveIndicator(unit);
            }
        }
    }

    internal static class FriendlyLowHealthIndicatorService
    {
        private static readonly Dictionary<Unit, GuiWorldSpaceIndicator> Indicators = new Dictionary<Unit, GuiWorldSpaceIndicator>();

        internal static void UpdateIndicator(Unit unit, bool isOffScreen, Color color)
        {
            if (!isOffScreen)
            {
                RemoveIndicator(unit);
                return;
            }

            GuiWorldSpaceIndicatorFactory factory = Singleton<GuiWorldSpaceIndicatorFactory>.Instance;
            if (factory == null)
                return;

            GuiWorldSpaceIndicator existing;
            if (Indicators.TryGetValue(unit, out existing))
            {
                if (existing == null)
                    Indicators.Remove(unit);
                return;
            }

            GuiWorldSpaceIndicator indicator = factory.AddArrow(unit);
            if (indicator == null)
                return;

            indicator.SetColor(color);
            indicator.IconMinSize = LowHpAlertPlugin.IndicatorSize.Value;
            indicator.IconMaxSize = LowHpAlertPlugin.IndicatorSize.Value;

            UiTweenAlpha tween = indicator.GetComponent<UiTweenAlpha>();
            if (tween != null)
                tween.to = LowHpAlertPlugin.IndicatorAlpha.Value;

            Indicators[unit] = indicator;
        }

        internal static void RemoveIndicator(Unit unit)
        {
            GuiWorldSpaceIndicator existing;
            if (Indicators.TryGetValue(unit, out existing))
            {
                Indicators.Remove(unit);
                if (existing != null)
                    existing.Kill();
            }
        }
    }
}

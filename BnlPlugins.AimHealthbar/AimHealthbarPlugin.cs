using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BnlPlugins.AimHealthbar
{
    [BepInPlugin("bnl.community.aimhealthbar", "BNL Aim Healthbar", "0.1.0")]
    public sealed class AimHealthbarPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> Enabled = null!;
        private Harmony? _harmony;
        private const string HarmonyId = "bnl.community.aimhealthbar";

        private void Awake()
        {
            Enabled = Config.Bind("Aim Healthbar", "Enabled", true,
                new ConfigDescription("Show a unit healthbar while your crosshair is aimed directly at that unit.",
                    null, new ConfigurationManagerAttributes { Order = 100 }));

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(AimHealthbarPlugin).Assembly);
            Logger.LogInfo("[BNL Aim Healthbar] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }
    }

    internal static class AimHealthbarRuntime
    {
        internal static bool ShouldShow(Unit healthbarUnit)
        {
            if (!AimHealthbarPlugin.Enabled.Value || healthbarUnit == null)
                return false;

            Crosshair crosshair = Singleton<Crosshair>.Instance;
            if (crosshair == null)
                return false;

            return crosshair.RaycastUnitInfo.Unit == healthbarUnit;
        }
    }

    [HarmonyPatch(typeof(GuiHealthbar), "IsUnitAvailableForShow")]
    internal static class GuiHealthbarIsUnitAvailableForShowPatch
    {
        private static readonly FieldInfo UnitField =
            typeof(GuiHealthbar).GetField("unit", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static void Postfix(GuiHealthbar __instance, ref bool __result)
        {
            if (__result)
                return;

            Unit healthbarUnit = ReferenceEquals(UnitField, null) ? null : UnitField.GetValue(__instance) as Unit;
            if (AimHealthbarRuntime.ShouldShow(healthbarUnit))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(GuiHealthbar), "AlphaUpdate")]
    internal static class GuiHealthbarAlphaUpdatePatch
    {
        private static readonly FieldInfo UnitField =
            typeof(GuiHealthbar).GetField("unit", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo ShowTimeField =
            typeof(GuiHealthbar).GetField("showTime", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static void Prefix(GuiHealthbar __instance)
        {
            Unit healthbarUnit = ReferenceEquals(UnitField, null) ? null : UnitField.GetValue(__instance) as Unit;
            if (!AimHealthbarRuntime.ShouldShow(healthbarUnit))
                return;

            if (!ReferenceEquals(ShowTimeField, null))
                ShowTimeField.SetValue(__instance, 1f);
        }
    }
}

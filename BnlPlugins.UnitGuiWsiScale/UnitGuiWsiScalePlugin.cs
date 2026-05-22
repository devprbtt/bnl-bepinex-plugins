using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BnlPlugins.UnitGuiWsiScale
{
    [BepInPlugin("bnl.community.unitguiwsiscale", "BNL Unit GUI / WSI Scale", "0.1.0")]
    public sealed class UnitGuiWsiScalePlugin : BaseUnityPlugin
    {
        private const string HarmonyId = "bnl.community.unitguiwsiscale";

        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<bool> UnitGuiScaleEnabled = null!;
        internal static ConfigEntry<float> UnitGuiScaleMultiplier = null!;
        internal static ConfigEntry<bool> WsiScaleEnabled = null!;
        internal static ConfigEntry<float> WsiScaleMultiplier = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Enabled = Config.Bind("Unit GUI / WSI Scale", "Enabled", false,
                new ConfigDescription("Enable unit GUI and world-space indicator scaling overrides.",
                    null, new ConfigurationManagerAttributes { Order = 100 }));
            UnitGuiScaleEnabled = Config.Bind("Unit GUI / WSI Scale", "UnitGuiScaleEnabled", true,
                new ConfigDescription("Scale unit GUI elements such as healthbars/follow UI.", null,
                    new ConfigurationManagerAttributes { Order = 99, DispName = "Enable Unit GUI Scale" }));
            UnitGuiScaleMultiplier = Config.Bind("Unit GUI / WSI Scale", "UnitGuiScaleMultiplier", 1.0f,
                FloatConfig.Range("Multiplier for unit GUI scale.", 0.1f, 3.0f, 98, "Unit GUI Scale"));
            WsiScaleEnabled = Config.Bind("Unit GUI / WSI Scale", "WsiScaleEnabled", true,
                new ConfigDescription("Scale world-space indicators shown above units.", null,
                    new ConfigurationManagerAttributes { Order = 97, DispName = "Enable WSI Scale" }));
            WsiScaleMultiplier = Config.Bind("Unit GUI / WSI Scale", "WsiScaleMultiplier", 1.0f,
                FloatConfig.Range("Multiplier for world-space indicator scale.", 0.1f, 3.0f, 96, "WSI Scale"));

            FloatConfig.BindRound(UnitGuiScaleMultiplier, 0.1f, 3.0f);
            FloatConfig.BindRound(WsiScaleMultiplier, 0.1f, 3.0f);

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(UnitGuiWsiScalePlugin).Assembly);
            Logger.LogInfo("[BNL Unit GUI / WSI Scale] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }
    }

    internal static class UnitGuiScaleRuntime
    {
        internal static float GetScaleMultiplier()
        {
            if (!UnitGuiWsiScalePlugin.Enabled.Value || !UnitGuiWsiScalePlugin.UnitGuiScaleEnabled.Value)
                return 1f;

            return UnitGuiWsiScalePlugin.UnitGuiScaleMultiplier.Value;
        }
    }

    internal static class WsiScaleRuntime
    {
        internal static float GetScaleMultiplier()
        {
            if (!UnitGuiWsiScalePlugin.Enabled.Value || !UnitGuiWsiScalePlugin.WsiScaleEnabled.Value)
                return 1f;

            return UnitGuiWsiScalePlugin.WsiScaleMultiplier.Value;
        }
    }

    [HarmonyPatch(typeof(GuiFollow), "UpdateScale")]
    internal static class GuiFollowUpdateScalePatch
    {
        private static void Postfix(ref float __result)
        {
            __result *= UnitGuiScaleRuntime.GetScaleMultiplier();
        }
    }

    [HarmonyPatch(typeof(GuiWorldSpaceIndicator), "Awake")]
    internal static class GuiWorldSpaceIndicatorAwakePatch
    {
        private static void Postfix(GuiWorldSpaceIndicator __instance)
        {
            if (__instance == null)
                return;

            WsiScaleApplier applier = __instance.gameObject.GetComponent<WsiScaleApplier>();
            if (applier == null)
                applier = __instance.gameObject.AddComponent<WsiScaleApplier>();
            applier.Init(__instance);
        }
    }

    public sealed class WsiScaleApplier : MonoBehaviour
    {
        private GuiWorldSpaceIndicator? _indicator;
        private float _baseMin;
        private float _baseMax;
        private bool _initialized;

        public void Init(GuiWorldSpaceIndicator indicator)
        {
            _indicator = indicator;
            _baseMin = indicator.IconMinSize;
            _baseMax = indicator.IconMaxSize;
            _initialized = true;
            Apply();
        }

        private void LateUpdate()
        {
            if (_initialized)
                Apply();
        }

        private void Apply()
        {
            if (_indicator == null)
                return;

            float scale = WsiScaleRuntime.GetScaleMultiplier();
            _indicator.IconMinSize = _baseMin * scale;
            _indicator.IconMaxSize = _baseMax * scale;
        }
    }
}

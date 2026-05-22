using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BnlPlugins.Misc
{
    [BepInPlugin("bnl.community.misc", "BNL Misc", "0.1.0")]
    public sealed class MiscPlugin : BaseUnityPlugin
    {
        private const string HarmonyId = "bnl.community.misc";

        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<bool> SkipIntro = null!;
        internal static ConfigEntry<bool> DisableMainMenuFrameCap = null!;
        internal static ConfigEntry<bool> HideObjectiveBeam = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Enabled = Config.Bind("Misc", "Enabled", false,
                new ConfigDescription("Enable miscellaneous quality-of-life tweaks.", null,
                    new ConfigurationManagerAttributes { Order = 100 }));
            SkipIntro = Config.Bind("Misc", "SkipIntro", false,
                new ConfigDescription("Skip the warning screen and intro video on game start.", null,
                    new ConfigurationManagerAttributes { Order = 99, DispName = "Skip Intro" }));
            DisableMainMenuFrameCap = Config.Bind("Misc", "DisableMainMenuFrameCap", false,
                new ConfigDescription("Uncap FPS while on the main menu and lobby screens.", null,
                    new ConfigurationManagerAttributes { Order = 98, DispName = "Disable Main Menu Frame Cap" }));
            HideObjectiveBeam = Config.Bind("Misc", "HideObjectiveBeam", false,
                new ConfigDescription("Hide the beam shown on base/objective markers.", null,
                    new ConfigurationManagerAttributes { Order = 97, DispName = "Hide Objective Beam" }));

            MiscManager.Ensure();

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(MiscPlugin).Assembly);
            Logger.LogInfo("[BNL Misc] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }
    }

    internal static class MiscRuntime
    {
        private static readonly FieldInfo? ActiveField = AccessTools.Field(typeof(BuildingBeamEffect), "Active");
        private static readonly MethodInfo? SkipMethod = AccessTools.Method(typeof(GuiLoginIntro), "Skip");
        private static readonly MethodInfo? FinishWarningMethod = AccessTools.Method(typeof(GuiLoginIntro), "FinishWarning");
        private static readonly MethodInfo? FinishIntroMethod = AccessTools.Method(typeof(GuiLoginIntro), "FinishIntro");
        private static readonly FieldInfo? IntroFinishedField = AccessTools.Field(typeof(GuiLoginIntro), "introFinished");
        private static readonly FieldInfo? WarningFinishedField = AccessTools.Field(typeof(GuiLoginIntro), "warningFinished");

        internal static bool IsEnabled()
        {
            return MiscPlugin.Enabled.Value;
        }

        internal static bool ShouldSkipIntro()
        {
            return IsEnabled() && MiscPlugin.SkipIntro.Value;
        }

        internal static bool ShouldDisableMainMenuFrameCap()
        {
            return IsEnabled() && MiscPlugin.DisableMainMenuFrameCap.Value;
        }

        internal static bool ShouldHideObjectiveBeam()
        {
            return IsEnabled() && MiscPlugin.HideObjectiveBeam.Value;
        }

        internal static void ApplyObjectiveBeam(BuildingBeamEffect effect)
        {
            if (effect == null)
                return;

            if (ShouldHideObjectiveBeam())
            {
                if (ActiveField != null)
                    ActiveField.SetValue(effect, false);

                foreach (Renderer renderer in effect.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
            }
            else
            {
                foreach (Renderer renderer in effect.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = true;
            }
        }

        internal static void SkipLoginIntro(object intro)
        {
            if (!ShouldSkipIntro() || intro == null)
                return;

            if (IntroFinishedField != null && (bool)IntroFinishedField.GetValue(intro))
                return;

            // Skip both the warning screen and the intro video in one go.
            if (FinishWarningMethod != null)
                FinishWarningMethod.Invoke(intro, null);
            if (FinishIntroMethod != null)
                FinishIntroMethod.Invoke(intro, null);
        }
    }

    internal sealed class MiscManager : MonoBehaviour
    {
        private static MiscManager? _instance;
        private string _lastIntroScene = string.Empty;
        private bool _introSkipIssued;

        internal static void Ensure()
        {
            if (_instance != null)
                return;

            GameObject go = new GameObject("BnlMiscManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MiscManager>();
        }

        private void Update()
        {
            string level = Application.loadedLevelName ?? string.Empty;
            if (!string.Equals(level, _lastIntroScene, System.StringComparison.Ordinal))
            {
                _lastIntroScene = level;
                _introSkipIssued = false;
            }

            if (!MiscRuntime.IsEnabled())
                return;

            if (MiscRuntime.ShouldDisableMainMenuFrameCap())
            {
                if (level == "MainMenu" || level == "Lobby")
                    Application.targetFrameRate = -1;
            }

            if (MiscRuntime.ShouldSkipIntro() && !_introSkipIssued)
            {
                GuiLoginIntro intro = Object.FindObjectOfType(typeof(GuiLoginIntro)) as GuiLoginIntro;
                if (intro != null)
                {
                    MiscRuntime.SkipLoginIntro(intro);
                    _introSkipIssued = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(BuildingBeamEffect), "Update")]
    internal static class BuildingBeamEffectUpdatePatch
    {
        private static void Postfix(BuildingBeamEffect __instance)
        {
            MiscRuntime.ApplyObjectiveBeam(__instance);
        }
    }

}

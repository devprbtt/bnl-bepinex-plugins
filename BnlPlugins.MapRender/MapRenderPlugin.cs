using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace BnlPlugins.MapRender
{
    internal enum MapRenderPreset
    {
        Default,
        DaytimeWarm,
        DaytimeCold,
        Sunset,
        Night
    }

    [BepInPlugin("bnl.community.maprender", "BNL Map Render", "0.1.0")]
    public sealed class MapRenderPlugin : BaseUnityPlugin
    {
        private const string HarmonyId = "bnl.community.maprender";

        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<MapRenderPreset> Preset = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Enabled = Config.Bind("Map Render", "Enabled", false,
                new ConfigDescription("Override the map's environmental lighting preset.", null,
                    new ConfigurationManagerAttributes { Order = 100 }));
            Preset = Config.Bind("Map Render", "Preset", MapRenderPreset.Default,
                new ConfigDescription("Lighting preset override. Default keeps the map's own preset.", null,
                    new ConfigurationManagerAttributes { Order = 99 }));

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(MapRenderPlugin).Assembly);
            Logger.LogInfo("[BNL Map Render] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }
    }

    internal static class MapRenderRuntime
    {
        internal static string GetOverride(string original)
        {
            if (!MapRenderPlugin.Enabled.Value)
                return original;

            MapRenderPreset preset = MapRenderPlugin.Preset.Value;
            if (preset == MapRenderPreset.Default)
                return original;

            return preset.ToString();
        }
    }

    [HarmonyPatch(typeof(MapWorld), "UpdateRender")]
    internal static class MapWorldUpdateRenderPatch
    {
        private static void Prefix(ref string prefab)
        {
            prefab = MapRenderRuntime.GetOverride(prefab);
        }
    }
}

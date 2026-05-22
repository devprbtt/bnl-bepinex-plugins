using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BnlPlugins.ImpactVfx
{
    [BepInPlugin("bnl.community.impactvfx", "BNL Impact VFX", "0.1.0")]
    public sealed class ImpactVfxPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<bool> HideImpactVfx = null!;
        internal static ConfigEntry<bool> HideLavaWaterPlane = null!;
        internal static ConfigEntry<bool> HideFallingBlocks = null!;

        private Harmony? _harmony;
        private const string HarmonyId = "bnl.community.impactvfx";

        private void Awake()
        {
            Enabled = Config.Bind("Impact VFX", "Enabled", false,
                new ConfigDescription("Suppress impact and explosion visuals without affecting gameplay.",
                    null, new ConfigurationManagerAttributes { Order = 100 }));
            HideImpactVfx = Config.Bind("Impact VFX", "HideImpactVfx", false,
                new ConfigDescription("Hide impact and explosion visual effects.", null,
                    new ConfigurationManagerAttributes { Order = 99, DispName = "Hide Impact VFX" }));
            HideLavaWaterPlane = Config.Bind("Impact VFX", "HideLavaWaterPlane", false,
                new ConfigDescription("Hide the visual lava/water plane while keeping collision.", null,
                    new ConfigurationManagerAttributes { Order = 98, DispName = "Hide Lava/Water Plane" }));
            HideFallingBlocks = Config.Bind("Impact VFX", "HideFallingBlocks", false,
                new ConfigDescription("Hide falling block visuals and their destroy embers.", null,
                    new ConfigurationManagerAttributes { Order = 97, DispName = "Hide Falling Blocks" }));

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(ImpactVfxPlugin).Assembly);
            Logger.LogInfo("[BNL Impact VFX] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }
    }

    internal static class ImpactVfxRuntime
    {
        internal static bool ShouldHideVfx()
        {
            return ImpactVfxPlugin.Enabled.Value && ImpactVfxPlugin.HideImpactVfx.Value;
        }

        internal static bool ShouldHidePlane()
        {
            return ImpactVfxPlugin.Enabled.Value && ImpactVfxPlugin.HideLavaWaterPlane.Value;
        }

        internal static bool ShouldHideFallingBlocks()
        {
            return ImpactVfxPlugin.Enabled.Value && ImpactVfxPlugin.HideFallingBlocks.Value;
        }

        internal static void HidePlane(MapPlane plane)
        {
            if (!ShouldHidePlane() || plane == null)
                return;

            DisableRenderers(plane.Ground);

            var myGroundField = AccessTools.Field(typeof(MapPlane), "myGround");
            if (myGroundField != null)
                DisableRenderers(myGroundField.GetValue(plane) as GameObject);
        }

        internal static void DestroyFallingBlock(GameObject childBlock)
        {
            if (childBlock != null)
                Object.Destroy(childBlock);
        }

        private static void DisableRenderers(GameObject target)
        {
            if (target == null)
                return;

            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
        }
    }

    [HarmonyPatch(typeof(GlobalEffects), "MakeImpactEffect", typeof(Vector3), typeof(Vector3), typeof(Key), typeof(uint?))]
    internal static class GlobalEffectsImpactPatch1
    {
        private static bool Prefix() => !ImpactVfxRuntime.ShouldHideVfx();
    }

    [HarmonyPatch(typeof(GlobalEffects), "MakeImpactEffect", typeof(Vector3), typeof(Vector3), typeof(Key), typeof(bool?), typeof(bool?), typeof(uint?))]
    internal static class GlobalEffectsImpactPatch2
    {
        private static bool Prefix() => !ImpactVfxRuntime.ShouldHideVfx();
    }

    [HarmonyPatch(typeof(MapPlane), "Awake")]
    internal static class MapPlaneAwakePatch
    {
        private static void Postfix(MapPlane __instance)
        {
            ImpactVfxRuntime.HidePlane(__instance);
        }
    }

    [HarmonyPatch(typeof(RolyTankBallCannon), "OnGearToolFire")]
    internal static class RolyTankBallCannonPatch
    {
        private static bool Prefix() => !ImpactVfxRuntime.ShouldHideVfx();
    }

    [HarmonyPatch(typeof(RolyTankBallRocketEffect), "OnGearToolFire")]
    internal static class RolyTankBallRocketEffectPatch
    {
        private static bool Prefix() => !ImpactVfxRuntime.ShouldHideVfx();
    }

    [HarmonyPatch(typeof(GearModelFireEffect), "ShotEffect")]
    internal static class GearModelFireEffectPatch
    {
        private static bool Prefix() => !ImpactVfxRuntime.ShouldHideVfx();
    }

    [HarmonyPatch(typeof(GearModelShotEffect), "ShotEffect")]
    internal static class GearModelShotEffectPatch
    {
        private static bool Prefix() => !ImpactVfxRuntime.ShouldHideVfx();
    }

    [HarmonyPatch(typeof(BlockFalling), "Create")]
    internal static class BlockFallingCreatePatch
    {
        private static bool Prefix(GameObject childBlock)
        {
            if (!ImpactVfxRuntime.ShouldHideFallingBlocks())
                return true;

            ImpactVfxRuntime.DestroyFallingBlock(childBlock);
            return false;
        }
    }

    [HarmonyPatch(typeof(BlockFalling), "OnDestroy")]
    internal static class BlockFallingOnDestroyPatch
    {
        private static bool Prefix() => !ImpactVfxRuntime.ShouldHideFallingBlocks();
    }
}

using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BnlPlugins.DeathCamHp
{
    [BepInPlugin("bnl.community.deathcamhp", "BNL Death Cam HP", "0.1.0")]
    public sealed class DeathCamHpPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> Enabled = null!;
        private Harmony? _harmony;
        private const string HarmonyId = "bnl.community.deathcamhp";

        private void Awake()
        {
            Enabled = Config.Bind("Death Cam HP", "Enabled", true,
                new ConfigDescription("Show death-cam target HP in the nickname text and keep friendly healthbars visible while spectating after death.",
                    null, new ConfigurationManagerAttributes { Order = 100 }));

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(DeathCamHpPlugin).Assembly);
            Logger.LogInfo("[BNL Death Cam HP] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }
    }

    internal static class DeathCamRuntime
    {
        internal static bool IsDeathCamFriendly(Unit healthbarUnit)
        {
            if (!DeathCamHpPlugin.Enabled.Value || healthbarUnit == null)
                return false;

            try
            {
                if (healthbarUnit.IsMyPlayer || !healthbarUnit.PlayerId.HasValue)
                    return false;

                UnitsRegistry registry = Singleton<UnitsRegistry>.Instance;
                if (registry == null)
                    return false;

                Unit player = registry.GetPlayer();
                if (player == null || !player.IsDeath)
                    return false;

                return healthbarUnit.Team == player.Team;
            }
            catch
            {
                return false;
            }
        }

        internal static void AttachDeathCamController(GuiHealthbar healthbar)
        {
            if (!DeathCamHpPlugin.Enabled.Value || healthbar == null)
                return;

            DeathCamHealthbarController controller = healthbar.gameObject.GetComponent<DeathCamHealthbarController>();
            if (controller == null)
                controller = healthbar.gameObject.AddComponent<DeathCamHealthbarController>();
            controller.Init(healthbar);
        }

        internal static void UpdateDeathCamHpText(Text nicknameText)
        {
            if (!DeathCamHpPlugin.Enabled.Value || nicknameText == null)
                return;

            try
            {
                CameraDeath deathCam = Singleton<CameraDeath>.Instance;
                if (deathCam == null || deathCam.Target == null)
                    return;

                Unit targetUnit = deathCam.Target.GetComponent<Unit>();
                if (targetUnit == null)
                    return;

                float health = targetUnit.Health;
                float maxHealth = targetUnit.MaxHealth;
                float pct = maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;
                int filled = Mathf.RoundToInt(pct * 10f);

                string bar = string.Empty;
                for (int i = 0; i < 10; i++)
                    bar += i < filled ? "\u2588" : "\u2591";

                string playerName = targetUnit.name;
                if (targetUnit.PlayerId.HasValue && PlayerData.Instance != null)
                {
                    object friend = PlayerData.Instance.FindFriend(targetUnit.PlayerId.Value);
                    if (friend != null)
                    {
                        PropertyInfo nicknameProperty = friend.GetType().GetProperty("Nickname", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        string nickname = nicknameProperty != null ? nicknameProperty.GetValue(friend, null) as string : null;
                        if (!string.IsNullOrEmpty(nickname))
                            playerName = nickname;
                    }
                }

                nicknameText.text = playerName + string.Format(" {0} {1:F0}/{2:F0}", bar, health, maxHealth);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(GuiHealthbar), "IsUnitAvailableForShow")]
    internal static class GuiHealthbarIsUnitAvailableForShowPatch
    {
        private static readonly FieldInfo UnitField =
            typeof(GuiHealthbar).GetField("unit", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static bool Prefix(GuiHealthbar __instance, ref bool __result)
        {
            if (ReferenceEquals(UnitField, null))
                return true;

            Unit healthbarUnit = UnitField.GetValue(__instance) as Unit;
            if (DeathCamRuntime.IsDeathCamFriendly(healthbarUnit))
            {
                __result = true;
                return false;
            }

            return true;
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
            if (ReferenceEquals(UnitField, null) || ReferenceEquals(ShowTimeField, null))
                return;

            Unit healthbarUnit = UnitField.GetValue(__instance) as Unit;
            if (!DeathCamRuntime.IsDeathCamFriendly(healthbarUnit))
                return;

            ShowTimeField.SetValue(__instance, 1f);
        }
    }

    public sealed class DeathCamHealthbarController : MonoBehaviour
    {
        private static readonly FieldInfo UnitField =
            typeof(GuiHealthbar).GetField("unit", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private GuiHealthbar? _healthbar;

        public void Init(GuiHealthbar healthbar)
        {
            _healthbar = healthbar;
        }

        private void LateUpdate()
        {
            if (!DeathCamHpPlugin.Enabled.Value || _healthbar == null || _healthbar.HealthBar == null)
                return;

            Unit healthbarUnit = ReferenceEquals(UnitField, null) ? null : UnitField.GetValue(_healthbar) as Unit;
            if (!DeathCamRuntime.IsDeathCamFriendly(healthbarUnit))
                return;

            _healthbar.HealthBar.enabled = true;
            if (_healthbar.Title != null)
                _healthbar.Title.enabled = true;
        }
    }

    [HarmonyPatch(typeof(GuiHealthbar), "Start")]
    internal static class GuiHealthbarStartPatch
    {
        private static void Postfix(GuiHealthbar __instance)
        {
            DeathCamRuntime.AttachDeathCamController(__instance);
        }
    }

    [HarmonyPatch(typeof(GuiDeathCameraTargets), "Update")]
    internal static class GuiDeathCameraTargetsUpdatePatch
    {
        private static void Postfix(GuiDeathCameraTargets __instance)
        {
            DeathCamRuntime.UpdateDeathCamHpText(__instance.Nickname);
        }
    }
}

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BnlPlugins.TeammateHp
{
    [BepInPlugin("bnl.community.teammatehp", "BNL Teammate HP", "0.1.0")]
    public sealed class TeammateHpPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> Enabled = null!;
        private Harmony? _harmony;
        private const string HarmonyId = "bnl.community.teammatehp";

        private void Awake()
        {
            Enabled = Config.Bind("Teammate HP", "Enabled", false,
                new ConfigDescription("Show each teammate's current HP percentage next to their name in the team panel while alive.",
                    null, new ConfigurationManagerAttributes { Order = 100 }));

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(TeammateHpPlugin).Assembly);
            Logger.LogInfo("[BNL Teammate HP] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }
    }

    internal static class TeammateHpRuntime
    {
        private static readonly System.Reflection.FieldInfo PlayerIdField =
            typeof(GuiTeammate).GetField("playerId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;

        internal static void UpdateTeammateHpText(GuiTeammate gui)
        {
            if (gui == null || !TeammateHpPlugin.Enabled.Value)
                return;

            try
            {
                if (ReferenceEquals(PlayerIdField, null))
                    return;

                uint playerId = (uint)PlayerIdField.GetValue(gui);
                UnitsRegistry registry = Singleton<UnitsRegistry>.Instance;
                if (registry == null)
                    return;

                Unit unit = registry.GetByPlayerId(playerId);
                if (unit == null || unit.IsDeath)
                    return;

                float health = unit.Health;
                float maxHealth = unit.MaxHealth;
                if (maxHealth <= 0f)
                    return;

                int pct = Mathf.RoundToInt((health / maxHealth) * 100f);
                string hpText = pct + "%";
                string playerName = Singleton<ZonePlayersCache>.Instance != null
                    ? Singleton<ZonePlayersCache>.Instance.GetPlayerName(playerId)
                    : (gui.PlayerName != null ? gui.PlayerName.text : string.Empty);

                if (gui.PlayerName != null)
                    gui.PlayerName.text = playerName + " " + hpText;
                if (gui.RespawnTime != null)
                    gui.RespawnTime.text = hpText;
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(GuiTeammate), "Update")]
    internal static class GuiTeammateUpdatePatch
    {
        private static void Postfix(GuiTeammate __instance)
        {
            TeammateHpRuntime.UpdateTeammateHpText(__instance);
        }
    }
}

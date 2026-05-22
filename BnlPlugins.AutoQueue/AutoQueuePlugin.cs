using System;
using BepInEx;
using BepInEx.Configuration;
using Protocol;
using UnityEngine;

namespace BnlPlugins.AutoQueue
{
    [BepInPlugin("bnl.community.autoqueue", "BNL Auto Queue", "0.1.0")]
    public sealed class AutoQueuePlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> Enabled = null!;
        private static AutoQueueRuntime? _runtime;

        private void Awake()
        {
            Enabled = Config.Bind("Auto Queue", "Enabled", false,
                new ConfigDescription("Enter casual queue automatically while you are in a custom game, and leave the custom game when a casual match is found.",
                    null, new ConfigurationManagerAttributes { Order = 100 }));

            EnsureRuntime();
            Logger.LogInfo("[BNL Auto Queue] Loaded");
        }

        private static void EnsureRuntime()
        {
            if (_runtime != null)
                return;

            GameObject go = GameObject.Find("BNL_AUTO_CASUAL_QUEUE");
            if (go == null)
            {
                go = new GameObject("BNL_AUTO_CASUAL_QUEUE");
                UnityEngine.Object.DontDestroyOnLoad(go);
            }

            _runtime = go.GetComponent<AutoQueueRuntime>();
            if (_runtime == null)
                _runtime = go.AddComponent<AutoQueueRuntime>();
        }
    }

    public sealed class AutoQueueRuntime : MonoBehaviour
    {
        private bool _wasInCustomGame;
        private bool _leaveRequestedForMatch;
        private MatchmakerStateType _lastLoggedState = MatchmakerStateType.None;

        private void Update()
        {
            if (!AutoQueuePlugin.Enabled.Value)
            {
                _wasInCustomGame = false;
                _leaveRequestedForMatch = false;
                _lastLoggedState = MatchmakerStateType.None;
                return;
            }

            try
            {
                CustomGameData customGameData = Singleton<CustomGameData>.Instance;
                MatchmakerData matchmakerData = Singleton<MatchmakerData>.Instance;
                NetworkDispatcher dispatcher = Singleton<NetworkDispatcher>.Instance;
                if (customGameData == null || matchmakerData == null || dispatcher == null)
                    return;

                bool isInCustomGame = customGameData.IsCustomGame;
                MatchmakerStateType currentState = matchmakerData.State != null
                    ? matchmakerData.State.State
                    : MatchmakerStateType.None;

                if (currentState != _lastLoggedState)
                {
                    Debug.Log("[BNL Auto Queue] state=" + currentState + " inCustom=" + isInCustomGame);
                    _lastLoggedState = currentState;
                }

                if (currentState != MatchmakerStateType.Confirming)
                    _leaveRequestedForMatch = false;

                if (isInCustomGame && !_wasInCustomGame && currentState == MatchmakerStateType.None)
                {
                    Debug.Log("[BNL Auto Queue] entering casual queue from custom game");
                    dispatcher.ServiceMatchmaker.EnterQueue(CatalogueHelper.ModeFriendly.Key);
                }

                _wasInCustomGame = isInCustomGame;

                if (isInCustomGame && currentState == MatchmakerStateType.Confirming && !_leaveRequestedForMatch)
                {
                    Debug.Log("[BNL Auto Queue] leaving custom game after match found");
                    ZoneData zoneData = Singleton<ZoneData>.Instance;
                    if (zoneData != null && zoneData.IsCustomGame)
                        dispatcher.ServiceZone.ExitMatch();
                    else
                        customGameData.LeaveGame();
                    _leaveRequestedForMatch = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BNL Auto Queue] Update failed: " + ex.Message);
            }
        }
    }
}

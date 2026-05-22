using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Protocol;
using UnityEngine;

namespace BnlPlugins.BuildPreview
{
    [BepInPlugin("bnl.community.buildpreview", "BNL Build Preview", "0.1.0")]
    public sealed class BuildPreviewPlugin : BaseUnityPlugin
    {
        private const string HarmonyId = "bnl.community.buildpreview";

        internal static ConfigEntry<bool> Enabled = null!;
        private Harmony? _harmony;

        private void Awake()
        {
            Enabled = Config.Bind("Build Preview", "Enabled", false,
                new ConfigDescription(
                    "Blocks and devices appear immediately without waiting for server confirmation. Recommended only for high ping, because placement and switching feel different.",
                    null,
                    new ConfigurationManagerAttributes { Order = 100 }));

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(BuildPreviewPlugin).Assembly);
            Logger.LogInfo("[BNL Build Preview] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }
    }

    internal static class LocalBuildPredictionRuntime
    {
        private static readonly float InstantCrateChainWindowSeconds = 5.0f;
        private const float PredictionTimeoutSeconds = 3f;

        private static PredictionManager? _manager;
        private static float _instantCrateChainUntil;
        private static readonly Dictionary<ushort, Vector3s> PendingRpcBlockPos = new Dictionary<ushort, Vector3s>();

        private static PredictionManager? Manager
        {
            get
            {
                if (!BuildPreviewPlugin.Enabled.Value)
                    return null;

                if (_manager == null)
                {
                    GameObject go = GameObject.Find("BNL_LOCAL_BUILD_PREDICTION");
                    if (go == null)
                    {
                        go = new GameObject("BNL_LOCAL_BUILD_PREDICTION");
                        UnityEngine.Object.DontDestroyOnLoad(go);
                    }

                    _manager = go.GetComponent<PredictionManager>();
                    if (_manager == null)
                        _manager = go.AddComponent<PredictionManager>();
                }

                return _manager;
            }
        }

        internal static bool IsBlockBackedDeviceKey(Key deviceKey)
        {
            if (!BuildPreviewPlugin.Enabled.Value)
                return false;

            CardDevice deviceCard = Singleton<Catalogue>.Instance.GetCard<CardDevice>(deviceKey);
            if (deviceCard == null)
                return false;

            CardBlock blockCard = Singleton<Catalogue>.Instance.GetCard<CardBlock>(deviceCard.DeviceKey);
            return blockCard != null && blockCard.BlockId != 0;
        }

        internal static bool IsInstantPlacementDeviceKey(Key deviceKey)
        {
            if (!BuildPreviewPlugin.Enabled.Value)
                return false;

            CardDevice deviceCard = Singleton<Catalogue>.Instance.GetCard<CardDevice>(deviceKey);
            if (deviceCard == null)
                return false;

            CardBlock blockCard = Singleton<Catalogue>.Instance.GetCard<CardBlock>(deviceCard.DeviceKey);
            if (blockCard == null || blockCard.BlockId == 0)
                return false;
            if (deviceCard.BuildTime.GetValueOrDefault(0f) > 0f)
                return false;
            if (blockCard.Special is BlockSpecialBounce)
                return false;
            if (blockCard.Special is BlockSpecialFastMovement)
                return false;
            return true;
        }

        internal static bool IsCratePlacementDeviceKey(Key deviceKey)
        {
            if (!BuildPreviewPlugin.Enabled.Value)
                return false;

            CardDevice deviceCard = Singleton<Catalogue>.Instance.GetCard<CardDevice>(deviceKey);
            if (deviceCard == null)
                return false;

            CardBlock blockCard = Singleton<Catalogue>.Instance.GetCard<CardBlock>(deviceCard.DeviceKey);
            return blockCard != null && blockCard.BlockId == 58;
        }

        private static void ActivateInstantCrateChainWindow()
        {
            _instantCrateChainUntil = Mathf.Max(_instantCrateChainUntil, Time.time + InstantCrateChainWindowSeconds);
        }

        private static bool IsInstantCrateChainWindowActive()
        {
            return BuildPreviewPlugin.Enabled.Value && Time.time < _instantCrateChainUntil;
        }

        internal static bool ShouldBypassBuildValidate(ToolLogicBuild tool)
        {
            return BuildPreviewPlugin.Enabled.Value &&
                   tool != null &&
                   tool.Unit != null &&
                   tool.Unit.IsMyPlayer &&
                   tool.Unit.CurrentDevice != null &&
                   (IsInstantPlacementDeviceKey(tool.Unit.CurrentDevice.DeviceKey) ||
                    (IsCratePlacementDeviceKey(tool.Unit.CurrentDevice.DeviceKey) && IsInstantCrateChainWindowActive()));
        }

        internal static bool ShouldZeroBuildTime(Unit unit, Key deviceKey)
        {
            return BuildPreviewPlugin.Enabled.Value &&
                   unit != null &&
                   unit.IsMyPlayer &&
                   (IsInstantPlacementDeviceKey(deviceKey) ||
                    (IsCratePlacementDeviceKey(deviceKey) && IsInstantCrateChainWindowActive()));
        }

        internal static bool ShouldZeroBuildTiming()
        {
            if (!BuildPreviewPlugin.Enabled.Value || Singleton<UnitsRegistry>.Instance == null)
                return false;

            Unit player = Singleton<UnitsRegistry>.Instance.GetPlayer();
            return player != null &&
                   player.IsMyPlayer &&
                   player.CurrentDevice != null &&
                   (IsInstantPlacementDeviceKey(player.CurrentDevice.DeviceKey) ||
                    (IsCratePlacementDeviceKey(player.CurrentDevice.DeviceKey) && IsInstantCrateChainWindowActive()));
        }

        internal static float GetBuildPrecastTime(Protocol.ToolTiming timing)
        {
            return ShouldZeroBuildTiming() ? 0f : ToolTimingHelper.GetPrecastTime(timing);
        }

        internal static float GetBuildTotalCastTime(Protocol.ToolTiming timing)
        {
            return ShouldZeroBuildTiming() ? 0f : ToolTimingHelper.GetTotalCastTime(timing);
        }

        internal static void TryInstantAcceptStartBuild(BuildInfo info, ServiceZone.Rpc_StartBuild rpc)
        {
            if (!BuildPreviewPlugin.Enabled.Value || info == null || rpc == null)
                return;

            if (IsInstantPlacementDeviceKey(info.DeviceKey) ||
                (IsCratePlacementDeviceKey(info.DeviceKey) && IsInstantCrateChainWindowActive()))
            {
                if (IsCratePlacementDeviceKey(info.DeviceKey))
                    ActivateInstantCrateChainWindow();

                PendingRpcBlockPos[rpc._Id] = info.BuildInsidePosition;
                rpc._Success(true);
            }
        }

        internal static void OnStartBuildResult(ServiceZone.Rpc_StartBuild rpc, bool accepted)
        {
            if (!BuildPreviewPlugin.Enabled.Value || rpc == null)
                return;

            Vector3s blockPos;
            if (!PendingRpcBlockPos.TryGetValue(rpc._Id, out blockPos))
                return;
            PendingRpcBlockPos.Remove(rpc._Id);

            PredictionManager manager = Manager;
            if (manager == null)
                return;

            if (accepted)
                manager.ResolveBlock(blockPos);
            else
                manager.RollbackBlock(blockPos);
        }

        internal static void TryInstantAcceptSwitchGear(Key gearKey, ServiceZone.Rpc_SwitchGear rpc)
        {
            if (!BuildPreviewPlugin.Enabled.Value || rpc == null || Singleton<UnitsRegistry>.Instance == null)
                return;

            Unit player = Singleton<UnitsRegistry>.Instance.GetPlayer();
            if (player != null && player.IsMyPlayer)
                rpc._Success(true);
        }

        internal static void OnLocalPlace(BuildGhostController controller)
        {
            if (!BuildPreviewPlugin.Enabled.Value || controller == null)
                return;

            try
            {
                BuildHelper.BuildData buildData = controller.TryPlaceDevice();
                if (buildData == null || buildData.Result != BuildHelper.BuildResultType.Success || buildData.Ri == null)
                    return;

                Unit unit = controller.GetComponent<Unit>();
                if (unit == null || unit.CurrentDevice == null)
                    return;

                PredictionManager manager = Manager;
                if (manager == null)
                    return;

                RaycastInfo ri = buildData.Ri.Value;
                CardDevice deviceCard = Singleton<Catalogue>.Instance.GetCard<CardDevice>(unit.CurrentDevice.DeviceKey);
                if (deviceCard == null)
                    return;

                Card objectCard = Singleton<Catalogue>.Instance.GetCard<Card>(deviceCard.DeviceKey);
                if (objectCard == null)
                    return;

                CardBlock blockCard = objectCard as CardBlock;
                if (blockCard != null && (blockCard.Special is BlockSpecialBounce || blockCard.Special is BlockSpecialFastMovement))
                    return;

                BuildGhostObject preview = BuildGhostObject.Create(unit.CurrentDevice.DeviceKey, false, unit.Team);
                PredictionEntry entry = new PredictionEntry
                {
                    DeviceKey = unit.CurrentDevice.DeviceKey,
                    SpawnCardKey = deviceCard.DeviceKey,
                    BlockPos = ri.BlockPosBuildIn,
                    WorldPos = preview.transform.position,
                    PreviousBlock = Singleton<ZoneManager>.Instance.Map.Blocks[ri.BlockPosBuildIn],
                    IsUnit = objectCard.Category == CardCategory.Unit,
                    ExpireTime = Time.time + Mathf.Max(0.25f, PredictionTimeoutSeconds)
                };

                if (blockCard != null && blockCard.BlockId != 0)
                {
                    if (blockCard.BlockId == 58)
                        ActivateInstantCrateChainWindow();

                    Block newBlock = new Block(blockCard.BlockId);
                    newBlock.Team = unit.Team;
                    Dictionary<Vector3s, BlockUpdate> updates = new Dictionary<Vector3s, BlockUpdate>();
                    updates[ri.BlockPosBuildIn] = newBlock.ToUpdate();
                    Singleton<ZoneManager>.Instance.UpdateBlocks(updates);
                    entry.WorldPos = ri.BlockPosBuildIn.ToVector3();
                    entry.IsRealLocalBlock = true;
                    UnityEngine.Object.Destroy(preview.gameObject);
                    preview = null;
                }
                else
                {
                    preview.SetValid(true);
                    preview.SetBlockPosition(ri.BlockPosBuildIn, ri.BlockPosBuildOn, ri.Direction);
                    preview.SetVisible(true);
                    preview.SetValue(1f);
                    entry.PreviewObject = preview.gameObject;
                    entry.WorldPos = preview.transform.position;
                }

                manager.AddPrediction(entry);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        internal static void OnBlockUpdates(Dictionary<Vector3s, BlockUpdate> updates)
        {
            if (!BuildPreviewPlugin.Enabled.Value || updates == null)
                return;

            PredictionManager manager = Manager;
            if (manager == null)
                return;

            foreach (KeyValuePair<Vector3s, BlockUpdate> pair in updates)
                manager.ResolveBlock(pair.Key);
        }

        internal static void OnDeviceBuilt(uint builderPlayerId, Key deviceKey, Vector3 position)
        {
            if (!BuildPreviewPlugin.Enabled.Value)
                return;

            PredictionManager manager = Manager;
            if (manager == null || Singleton<PlayerData>.Instance == null)
                return;
            if (builderPlayerId != Singleton<PlayerData>.Instance.Id)
                return;

            manager.ResolveDevice(deviceKey, position);
        }

        internal static void OnUnitCreate(UnitInit data)
        {
            if (!BuildPreviewPlugin.Enabled.Value || data == null || data.OwnerId == null)
                return;

            PredictionManager manager = Manager;
            if (manager == null || Singleton<PlayerData>.Instance == null)
                return;
            if (data.OwnerId.Value != Singleton<PlayerData>.Instance.Id)
                return;

            manager.ResolveUnit(data.Key, data.Transform.GetPosition());
        }
    }

    internal sealed class PredictionEntry
    {
        internal Key DeviceKey;
        internal Key SpawnCardKey;
        internal Vector3s BlockPos;
        internal Vector3 WorldPos;
        internal Block PreviousBlock;
        internal bool IsUnit;
        internal bool IsRealLocalBlock;
        internal GameObject PreviewObject;
        internal float ExpireTime;
    }

    internal sealed class PredictionManager : MonoBehaviour
    {
        private readonly List<PredictionEntry> _entries = new List<PredictionEntry>();
        private readonly List<PredictionEntry> _pendingRollbacks = new List<PredictionEntry>();

        internal void AddPrediction(PredictionEntry entry)
        {
            if (entry == null)
                return;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                PredictionEntry current = _entries[i];
                bool sameBlock = !current.IsUnit && !entry.IsUnit && current.BlockPos.Equals(entry.BlockPos);
                bool sameUnitSpot = current.IsUnit == entry.IsUnit &&
                                    current.DeviceKey.Equals(entry.DeviceKey) &&
                                    Vector3.Distance(current.WorldPos, entry.WorldPos) <= 0.75f;
                if (sameBlock || sameUnitSpot)
                    RemoveAt(i, false);
            }

            _entries.Add(entry);
        }

        internal void ResolveBlock(Vector3s blockPos)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (!_entries[i].IsUnit && _entries[i].BlockPos.Equals(blockPos))
                    RemoveAt(i, false);
            }
        }

        internal void RollbackBlock(Vector3s blockPos)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (!_entries[i].IsUnit && _entries[i].BlockPos.Equals(blockPos))
                    RemoveAt(i, true);
            }
        }

        internal void ResolveDevice(Key deviceKey, Vector3 position)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].DeviceKey.Equals(deviceKey) &&
                    Vector3.Distance(_entries[i].WorldPos, position) <= 1.5f)
                {
                    RemoveAt(i, false);
                }
            }
        }

        internal void ResolveUnit(Key spawnCardKey, Vector3 position)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].IsUnit &&
                    _entries[i].SpawnCardKey.Equals(spawnCardKey) &&
                    Vector3.Distance(_entries[i].WorldPos, position) <= 1.5f)
                {
                    RemoveAt(i, false);
                }
            }
        }

        private void Update()
        {
            if (_pendingRollbacks.Count > 0 &&
                Singleton<ZoneManager>.Instance != null &&
                Singleton<ZoneManager>.Instance.MapCreated)
            {
                for (int i = _pendingRollbacks.Count - 1; i >= 0; i--)
                {
                    PredictionEntry rb = _pendingRollbacks[i];
                    _pendingRollbacks.RemoveAt(i);
                    Dictionary<Vector3s, BlockUpdate> updates = new Dictionary<Vector3s, BlockUpdate>();
                    updates[rb.BlockPos] = rb.PreviousBlock.ToUpdate();
                    Singleton<ZoneManager>.Instance.UpdateBlocks(updates);
                }
            }

            float now = Time.time;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                PredictionEntry entry = _entries[i];
                if (entry == null)
                {
                    RemoveAt(i, false);
                    continue;
                }

                if (!entry.IsRealLocalBlock && entry.PreviewObject == null)
                {
                    RemoveAt(i, false);
                    continue;
                }

                if (now >= entry.ExpireTime)
                    RemoveAt(i, true);
            }
        }

        private void RemoveAt(int index, bool rollbackRealLocalBlock)
        {
            PredictionEntry entry = _entries[index];
            _entries.RemoveAt(index);
            if (rollbackRealLocalBlock && entry != null && entry.IsRealLocalBlock)
            {
                if (Singleton<ZoneManager>.Instance != null && Singleton<ZoneManager>.Instance.MapCreated)
                {
                    Dictionary<Vector3s, BlockUpdate> updates = new Dictionary<Vector3s, BlockUpdate>();
                    updates[entry.BlockPos] = entry.PreviousBlock.ToUpdate();
                    Singleton<ZoneManager>.Instance.UpdateBlocks(updates);
                }
                else
                {
                    _pendingRollbacks.Add(entry);
                }
            }

            if (entry != null && entry.PreviewObject != null)
                UnityEngine.Object.Destroy(entry.PreviewObject);
        }
    }

    [HarmonyPatch(typeof(BuffHelper), "BuildTime")]
    internal static class BuffHelperBuildTimePatch
    {
        private static bool Prefix(Unit unit, ToolBuild toolBuild, Key deviceKey, Key matchModeKey, ref float __result)
        {
            if (!LocalBuildPredictionRuntime.ShouldZeroBuildTime(unit, deviceKey))
                return true;

            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(ToolLogicBuild), "ValidateUse")]
    internal static class ToolLogicBuildValidateUsePatch
    {
        private static bool Prefix(ToolLogicBuild __instance, ref bool __result)
        {
            if (!LocalBuildPredictionRuntime.ShouldBypassBuildValidate(__instance))
                return true;

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(ServiceZone), "StartBuild")]
    internal static class ServiceZoneStartBuildPatch
    {
        private static void Postfix(BuildInfo info, ServiceZone.Rpc_StartBuild __result)
        {
            LocalBuildPredictionRuntime.TryInstantAcceptStartBuild(info, __result);
        }
    }

    [HarmonyPatch(typeof(ServiceZone.Rpc_StartBuild), "_Success")]
    internal static class RpcStartBuildSuccessPatch
    {
        private static void Postfix(ServiceZone.Rpc_StartBuild __instance, bool accepted)
        {
            LocalBuildPredictionRuntime.OnStartBuildResult(__instance, accepted);
        }
    }

    [HarmonyPatch(typeof(ServiceZone), "SwitchGear")]
    internal static class ServiceZoneSwitchGearPatch
    {
        private static void Postfix(Key gearKey, ServiceZone.Rpc_SwitchGear __result)
        {
            LocalBuildPredictionRuntime.TryInstantAcceptSwitchGear(gearKey, __result);
        }
    }

    [HarmonyPatch(typeof(BuildGhostController), "Place")]
    internal static class BuildGhostControllerPlacePatch
    {
        private static void Postfix(BuildGhostController __instance, CastData __result)
        {
            if (__result != null)
                LocalBuildPredictionRuntime.OnLocalPlace(__instance);
        }
    }

    [HarmonyPatch(typeof(ZoneServiceListener), "BlockUpdates")]
    internal static class ZoneServiceListenerBlockUpdatesPatch
    {
        private static void Postfix(Dictionary<Vector3s, BlockUpdate> updates)
        {
            LocalBuildPredictionRuntime.OnBlockUpdates(updates);
        }
    }

    [HarmonyPatch(typeof(ZoneServiceListener), "DeviceBuilt")]
    internal static class ZoneServiceListenerDeviceBuiltPatch
    {
        private static void Postfix(uint builderPlayerId, Key deviceKey, Vector3 position)
        {
            LocalBuildPredictionRuntime.OnDeviceBuilt(builderPlayerId, deviceKey, position);
        }
    }

    [HarmonyPatch(typeof(ZoneServiceListener), "UnitCreate")]
    internal static class ZoneServiceListenerUnitCreatePatch
    {
        private static void Postfix(uint id, UnitInit data)
        {
            LocalBuildPredictionRuntime.OnUnitCreate(data);
        }
    }

    internal static class BuildPreviewTranspilers
    {
        private static readonly MethodInfo GetTotalCastTime = AccessTools.Method(typeof(ToolTimingHelper), "GetTotalCastTime");
        private static readonly MethodInfo GetPrecastTime = AccessTools.Method(typeof(ToolTimingHelper), "GetPrecastTime");
        private static readonly MethodInfo ReplacementTotal = AccessTools.Method(typeof(LocalBuildPredictionRuntime), "GetBuildTotalCastTime");
        private static readonly MethodInfo ReplacementPrecast = AccessTools.Method(typeof(LocalBuildPredictionRuntime), "GetBuildPrecastTime");

        private static IEnumerable<CodeInstruction> ReplaceTimingCalls(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction code in instructions)
            {
                if (code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt)
                {
                    MethodInfo operand = code.operand as MethodInfo;
                    if (operand == GetTotalCastTime)
                    {
                        code.operand = ReplacementTotal;
                    }
                    else if (operand == GetPrecastTime)
                    {
                        code.operand = ReplacementPrecast;
                    }
                }

                yield return code;
            }
        }

        [HarmonyPatch]
        internal static class ProjectileMoveNextPatch
        {
            private static MethodBase TargetMethod()
            {
                Type nested = AccessTools.Inner(typeof(ToolLogicBuild), "<DoUseProjectile>c__IteratorC2");
                return AccessTools.Method(nested, "MoveNext");
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ReplaceTimingCalls(instructions);
            }
        }

        [HarmonyPatch]
        internal static class RotationLockMoveNextPatch
        {
            private static MethodBase TargetMethod()
            {
                Type nested = AccessTools.Inner(typeof(ToolLogicBuild), "<DoUseRotationLock>c__IteratorC3");
                return AccessTools.Method(nested, "MoveNext");
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ReplaceTimingCalls(instructions);
            }
        }

        [HarmonyPatch]
        internal static class RotationFreeMoveNextPatch
        {
            private static MethodBase TargetMethod()
            {
                Type nested = AccessTools.Inner(typeof(ToolLogicBuild), "<DoUseRotationFree>c__IteratorC4");
                return AccessTools.Method(nested, "MoveNext");
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ReplaceTimingCalls(instructions);
            }
        }
    }
}

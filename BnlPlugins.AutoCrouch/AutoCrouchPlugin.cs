using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace BnlPlugins.AutoCrouch
{
    [BepInPlugin("bnl.community.autocrouch", "BNL Auto Crouch", "0.1.0")]
    public sealed class AutoCrouchPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> Enabled = null!;
        private Harmony? _harmony;
        private const string HarmonyId = "bnl.community.autocrouch";

        private void Awake()
        {
            Enabled = Config.Bind("Auto Crouch", "Enabled", false,
                new ConfigDescription("Disable the forced-crouch behaviour that triggers when the ceiling is too low to stand.",
                    null, new ConfigurationManagerAttributes { Order = 100 }));

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(AutoCrouchPlugin).Assembly);
            Logger.LogInfo("[BNL Auto Crouch] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }
    }

    internal static class AutoCrouchRuntime
    {
        internal static bool IsPossibleToStayForAutoCrouch(MovementController controller)
        {
            if (AutoCrouchPlugin.Enabled.Value)
                return true;

            return controller.IsPossibleToStay();
        }
    }

    [HarmonyPatch(typeof(PlayerMovementGroundMove), "Update")]
    internal static class PlayerMovementGroundMoveUpdatePatch
    {
        private static readonly MethodInfo? IsPossibleToStayMethod =
            AccessTools.Method(typeof(MovementController), "IsPossibleToStay");
        private static readonly MethodInfo? ReplacementMethod =
            AccessTools.Method(typeof(AutoCrouchRuntime), "IsPossibleToStayForAutoCrouch");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (IsPossibleToStayMethod != null &&
                    ReplacementMethod != null &&
                    (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    Equals(instruction.operand, IsPossibleToStayMethod))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = ReplacementMethod;
                }

                yield return instruction;
            }
        }
    }
}

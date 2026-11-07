using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SkeletonChoice
{
    [HarmonyPatch(typeof(BaseAI), "Follow")]
    internal static class SpiritFollowDistance
    {
        private const float VanillaDistance = 3f;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int index = code.FindIndex(IsStopDistance);
            if (index < 0 || index != code.FindLastIndex(IsStopDistance))
            {
                Plugin.Instance.Log.LogWarning("Spirit follow distance unavailable: the game's follow method has changed.");
                return code;
            }

            // Keep vanilla pathfinding, catch-up speed and the combat decision intact.
            CodeInstruction previous = code[index];
            var loadAI = new CodeInstruction(OpCodes.Ldarg_0);
            loadAI.labels.AddRange(previous.labels);
            loadAI.blocks.AddRange(previous.blocks);
            code[index] = loadAI;
            code.Insert(index + 1, new CodeInstruction(OpCodes.Ldarg_1));
            code.Insert(index + 2, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SpiritFollowDistance), nameof(GetStopDistance))));
            return code;
        }

        private static bool IsStopDistance(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float distance && distance == VanillaDistance;
        }

        private static float GetStopDistance(BaseAI ai, GameObject followTarget)
        {
            Plugin plugin = Plugin.Instance;
            Player player = Player.m_localPlayer;
            if (plugin == null || !plugin.ModEnabled || player == null || followTarget != player.gameObject || !(ai is MonsterAI))
                return VanillaDistance;

            int distance = plugin.SpiritFollowDistanceMeters;
            if (distance <= VanillaDistance) return VanillaDistance;
            ZNetView view = ai.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return VanillaDistance;
            ZDO zdo = view.GetZDO();
            if (zdo == null || !SpiritCaller.TryGetSpecies(zdo.GetPrefab(), out _)) return VanillaDistance;
            long playerId = player.GetPlayerID();
            return playerId != 0 && zdo.GetLong(SummonIndicator.SummonerKey) == playerId ? distance : VanillaDistance;
        }
    }
}

namespace VBNetTweaks
{
    [HarmonyPatch]
    public static class MonsterAiPatches
    {
        [HarmonyPatch(typeof(RandEventSystem), "FixedUpdate")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> RandEventSystem_FixedUpdate_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var m = new CodeMatcher(instructions);
            m.MatchForward(false,
                new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(Player), "m_localPlayer")),
                new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(UnityEngine.Object), "op_Implicit"))
            );
            if (m.IsInvalid) return instructions;

            m.RemoveInstructions(2);
            m.Insert(
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Player), "GetAllPlayers")),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MonsterAiPatches), nameof(HasAnyPlayerNearby)))
            );
            return m.InstructionEnumeration();
        }

        [HarmonyPatch(typeof(SpawnSystem), "UpdateSpawning")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> SpawnSystem_UpdateSpawning_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var m = new CodeMatcher(instructions);
            m.MatchForward(false,
                new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(Player), "m_localPlayer")),
                new CodeMatch(OpCodes.Ldnull),
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(UnityEngine.Object), "op_Equality")),
                new CodeMatch(OpCodes.Brfalse)
            );
            if (m.IsInvalid) return instructions;

            m.RemoveInstructions(3);
            m.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Player), "GetAllPlayers")));
            m.InsertAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MonsterAiPatches), nameof(HasAnyPlayerNearby))));
            m.SetOpcodeAndAdvance(OpCodes.Brfalse);
            return m.InstructionEnumeration();
        }

        private static bool HasAnyPlayerNearby(List<Player> all)
        {
            if (all == null || all.Count == 0) return false;
            int activeArea = ZoneSystem.instance?.m_activeArea ?? 3;

            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null) continue;
                var pZone = ZoneSystem.GetZone(p.transform.position);
                // Простая проверка "в зоне активности" вокруг игрока
                if (!ZNetScene.OutsideActiveArea(p.transform.position, pZone, activeArea)) return true;
            }
            return false;
        }
    }
}

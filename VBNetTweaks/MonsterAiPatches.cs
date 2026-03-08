namespace VBNetTweaks
{
    [HarmonyPatch]
    public static class MonsterAiPatches
    {
        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.FixedUpdate))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> DisableEventPause(IEnumerable<CodeInstruction> instructions)
        {
            var m = new CodeMatcher(instructions);

            m.MatchForward(false, new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(RandEventSystem), nameof(RandEventSystem.IsAnyPlayerInEventArea))));

            if (m.IsInvalid) return instructions;

            m.SetInstruction(new CodeInstruction(OpCodes.Ldc_I4_1));

            return m.InstructionEnumeration();
        }

        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.UpdateSpawning))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> SpawnSystem_UpdateSpawning_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var m = new CodeMatcher(instructions);
            m.MatchForward(false,
                new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer))),
                new CodeMatch(OpCodes.Ldnull),
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(UnityEngine.Object), "op_Equality")),
                new CodeMatch(OpCodes.Brfalse)
            );
            if (m.IsInvalid) return instructions;

            m.RemoveInstructions(3);

            m.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Player), nameof(Player.GetAllPlayers))));
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
                if (!p) continue;
                var pZone = ZoneSystem.GetZone(p.transform.position);
                if (!ZNetScene.OutsideActiveArea(p.transform.position, pZone, activeArea)) return true;
            }
            return false;
        }

        private static readonly List<ActiveEvent> _events = new();

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.SetRandomEvent))]
        [HarmonyPrefix]
        static bool MultiEvent_Start(RandEventSystem __instance, RandomEvent ev, Vector3 pos)
        {
            if (ev == null) return false;

            var clone = ev.Clone();
            clone.m_pos = pos;
            clone.OnStart();

            _events.Add(new ActiveEvent(clone));

            return false;
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.FixedUpdate))]
        [HarmonyPostfix]
        static void MultiEvent_Update()
        {
            float dt = Time.fixedDeltaTime;

            for (int i = _events.Count - 1; i >= 0; i--)
            {
                if (!_events[i].Update(dt)) _events.RemoveAt(i);
            }
        }

        private class ActiveEvent
        {
            private readonly RandomEvent _ev;

            public ActiveEvent(RandomEvent ev) => _ev = ev;

            public bool Update(float dt)
            {
                bool finished = _ev.Update(server: true, active: false, playerInArea: true, dt);

                if (finished)
                {
                    _ev.OnStop();
                    return false;
                }
                return true;
            }
        }
    }
}

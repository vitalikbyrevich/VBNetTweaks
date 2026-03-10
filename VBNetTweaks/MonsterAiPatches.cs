namespace VBNetTweaks
{
    [HarmonyPatch]
    public static class MonsterAiPatches
    {
        // ============================================================
        // 1. ОТКЛЮЧАЕМ ПАУЗУ ЭВЕНТОВ
        // ============================================================
        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.FixedUpdate))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> DisableEventPause(IEnumerable<CodeInstruction> instructions)
        {
            var m = new CodeMatcher(instructions);
            
            m.MatchForward(false,
                new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(RandEventSystem), nameof(RandEventSystem.IsAnyPlayerInEventArea)))
            );

            if (m.IsInvalid)
            {
                VBNetTweaks.LogDebug("IsAnyPlayerInEventArea not found, skipping patch");
                return instructions;
            }

            // Просто заменяем вызов на true
            m.SetInstruction(new CodeInstruction(OpCodes.Ldc_I4_1));
            
            return m.InstructionEnumeration();
        }

        // ============================================================
        // 2. ОПТИМИЗИРУЕМ СПАВН - Упрощенная версия без сложных манипуляций
        // ============================================================
        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.UpdateSpawning))]
        [HarmonyPrefix]
        static bool SpawnSystem_UpdateSpawning_Prefix(SpawnSystem __instance)
        {
            if (!VBNetTweaks.ModuleMonsterAI.Value)
                return true;
                
            // Получаем всех игроков
            var allPlayers = Player.GetAllPlayers();
            
            // Проверяем, есть ли игроки поблизости
            if (!HasAnyPlayerNearby(allPlayers))
            {
                return false; // Не спавним, если нет игроков
            }
            
            // Разрешаем оригинальному методу работать
            return true;
        }

        private static bool HasAnyPlayerNearby(List<Player> all)
        {
            if (all == null || all.Count == 0) 
                return false;
                
            int activeArea = ZoneSystem.instance?.m_activeArea ?? 3;

            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (!p) continue;
                
                var pZone = ZoneSystem.GetZone(p.transform.position);
                if (!ZNetScene.OutsideActiveArea(p.transform.position, pZone, activeArea)) 
                    return true;
            }
            return false;
        }

        // ============================================================
        // 3. МУЛЬТИ-ЭВЕНТЫ
        // ============================================================
        private static readonly List<ActiveEvent> _events = new();

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.SetRandomEvent))]
        [HarmonyPrefix]
        static bool MultiEvent_Start(RandEventSystem __instance, RandomEvent ev, Vector3 pos)
        {
            if (!VBNetTweaks.ModuleMonsterAI.Value)
                return true;
                
            if (ev == null) 
                return false;

            var clone = ev.Clone();
            clone.m_pos = pos;
            clone.OnStart();

            _events.Add(new ActiveEvent(clone));
            
            if (VBNetTweaks.DebugEnabled.Value)
                VBNetTweaks.LogDebug($"MultiEvent started: {ev.m_name} at {pos}");

            return false;
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.FixedUpdate))]
        [HarmonyPostfix]
        static void MultiEvent_Update()
        {
            if (!VBNetTweaks.ModuleMonsterAI.Value)
                return;
                
            float dt = Time.fixedDeltaTime;

            for (int i = _events.Count - 1; i >= 0; i--)
            {
                if (!_events[i].Update(dt))
                {
                    if (VBNetTweaks.DebugEnabled.Value)
                        VBNetTweaks.LogDebug($"MultiEvent finished: {_events[i].GetName()}");
                    _events.RemoveAt(i);
                }
            }
        }

        private class ActiveEvent
        {
            private readonly RandomEvent _ev;

            public ActiveEvent(RandomEvent ev) 
            { 
                _ev = ev; 
            }
            
            public string GetName() => _ev?.m_name ?? "Unknown";

            public bool Update(float dt)
            {
                if (_ev == null) return false;
                
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
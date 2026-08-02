namespace VBNetTweaks.ZDOUtills
{
    [HarmonyPatch]
    public static class ZDONetworkOptimizer
    {
        private static readonly int PLAYER_PREFAB = "Player".GetStableHashCode();

        public static int GetSafeQueueLimit() => Mathf.Clamp(VBNetTweaks.c_ZDOQueueLimit.Value, 8192, 1048576);
        private static Vector3 _currentRefPos;

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2))]
        [HarmonyPrefix]
        static bool SendZDOToPeers2_Prefix(ZDOMan __instance, float dt)
        {
            if (!Helper.IsServer()) return true;
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return true;

            OptimizedSendZDOToPeers(__instance, dt);
            return false;
        }

        /*   [HarmonyTranspiler]
            [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update))]
            private static IEnumerable<CodeInstruction> ZDOManUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
            {
                var matcher = new CodeMatcher(instructions).Start();
                matcher.MatchStartForward(new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2))));
                matcher.SetOperandAndAdvance(AccessTools.Method(typeof(ZDONetworkOptimizer), nameof(OptimizedSendZDOToPeers)));
                return matcher.InstructionEnumeration();
            }*/

        public static void OptimizedSendZDOToPeers(ZDOMan man, float dt)
        {
            try
            {
                int count = man.m_peers.Count;
                if (count == 0) return;

                man.m_sendTimer += dt;

                float interval = Mathf.Clamp(VBNetTweaks.c_SendInterval_S.Value, 0.01f, 0.2f);
                if (man.m_sendTimer < interval) return;

                man.m_sendTimer = 0f;

                int maxPeers = Mathf.Clamp(VBNetTweaks.c_PeersPerUpdate_S.Value, 1, count);
                int start = man.m_nextSendPeer < 0 ? 0 : man.m_nextSendPeer;

                int processed = 0;

                for (int i = 0; i < maxPeers; i++)
                {
                    int idx = (start + i) % count;
                    processed++;

                    var peer = man.m_peers[idx];

                    if (peer?.m_peer?.m_socket?.IsConnected() != true) continue;

                    int queue = peer.m_peer.m_socket.GetSendQueueSize();
                    int limit = GetSafeQueueLimit();

                    if (queue > limit) continue;

                    float flushThreshold = Mathf.Clamp01(VBNetTweaks.c_FlushThresholdPercent_S.Value) * limit;
                    bool flush = queue <= flushThreshold;

                    man.SendZDOs(peer, flush);
                }

                man.m_nextSendPeer = (start + processed) % count;
            }
            catch (Exception ex)
            {
                Helper.LogDebug($" Error in OptimizedSendZDOToPeers: {ex}");
            }
            /* try
             {
                 int count = zdoManager.m_peers.Count;
                 if (count <= 0) return;

                 zdoManager.m_sendTimer += dt;
                 float interval = VBNetTweaks.SendInterval.Value;

                 if (zdoManager.m_sendTimer < interval) return;
                 zdoManager.m_sendTimer = 0f;

                 int startPeer = Math.Max(zdoManager.m_nextSendPeer, 0);
                 int peersPerUpdate = VBNetTweaks.PeersPerUpdate.Value;
                 int queueLimit = VBNetTweaks.ZDOQueueLimit.Value;

                 float flushThresholdPercent = VBNetTweaks.FlushThresholdPercent.Value;
                 int flushThreshold = Mathf.RoundToInt(queueLimit * flushThresholdPercent);

                 int sent = 0;
                 for (int i = 0; i < Math.Min(peersPerUpdate, count); i++)
                 {
                     int idx = (startPeer + i) % count;
                     var peer = zdoManager.m_peers[idx];
                     if (peer?.m_peer?.m_socket?.IsConnected() != true) continue;

                     int queueSize = peer.m_peer.m_socket.GetSendQueueSize();

                     if (queueSize > queueLimit)
                     {
                         sent++;
                         continue;
                     }

                     bool flush = queueSize <= flushThreshold;

                     zdoManager.SendZDOs(peer, flush: flush);
                     sent++;
                 }
                 zdoManager.m_nextSendPeer = (startPeer + sent) % count;
             }
             catch (Exception ex)
             {
                 Helper.LogDebug($"[VBNetTweaks] Error in OptimizedSendZDOToPeers: {ex}");
             }*/
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSortSendZDOS))]
        [HarmonyPrefix]
        static void ServerSortSendZDOS_Prefix(Vector3 refPos)
        {
            _currentRefPos = refPos;
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSendCompare))]
        [HarmonyPrefix]
        static bool CustomServerSendCompare(ZDO x, ZDO y, ref int __result)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return true;
            if (!ZNet.instance.IsServer()) return true;

            __result = CustomCompare(x, y);
            return false;
        }

        private static int CustomCompare(ZDO x, ZDO y)
        {
            if (x == null || y == null) return 0;

            // 1. Ванильная логика для Prioritized объектов
            bool xPrioritized = x.Type == ZDO.ObjectType.Prioritized && x.HasOwner() && x.GetOwner() != ZDOMan.s_compareReceiver;
            bool yPrioritized = y.Type == ZDO.ObjectType.Prioritized && y.HasOwner() && y.GetOwner() != ZDOMan.s_compareReceiver;

            if (xPrioritized && yPrioritized) return CompareWithBias(x, y);
            if (xPrioritized != yPrioritized) return xPrioritized ? -1 : 1;

            // 2. Приоритеты: игроки > мобы рядом > важные объекты > мобы далеко > остальное
            float xBias = GetPriorityBias(x);
            float yBias = GetPriorityBias(y);

            if (Mathf.Abs(xBias - yBias) > 0.01f)
            {
                // Чем меньше bias — тем выше приоритет
                if (xBias < yBias) return -1;
                if (xBias > yBias) return 1;
                return 0;
            }

            // 3. По типу объекта (как в ваниле)
            if (x.Type == y.Type) return CompareFloats(x.m_tempSortValue, y.m_tempSortValue);

            return ((int)y.Type).CompareTo((int)x.Type);
        }

        private static float GetPriorityBias(ZDO zdo)
        {
            if (zdo == null) return 0f;

            int prefab = zdo.GetPrefab();

            // Игроки — максимальный приоритет (всегда в топе)
            if (prefab == PLAYER_PREFAB) return -500f;

            if (Helper.IsShip(zdo)) return -450f;

            // Мобы
            if (Helper.IsCreature(zdo))
            {
                float dist = Vector3.Distance(zdo.GetPosition(), _currentRefPos); // Уже посчитано расстояние

                // Мобы рядом с игроком — высокий приоритет
                if (dist < 30f) return -400f;
                if (dist < 60f) return -200f;
                if (dist < 100f) return -100f;
                if (dist < 200f) return -50f;
                return -25f; // Далекие мобы чуть выше стандартных объектов
            }

            // Остальное — без приоритета
            return 0f;
        }

        private static int CompareFloats(float a, float b)
        {
            if (a < b) return -1;
            if (a > b) return 1;
            return 0;
        }

        private static int CompareWithBias(ZDO x, ZDO y)
        {
            // Добавляем bias для Prioritized объектов
            float xBias = GetPriorityBias(x);
            float yBias = GetPriorityBias(y);

            float xVal = x.m_tempSortValue + xBias;
            float yVal = y.m_tempSortValue + yBias;

            return CompareFloats(xVal, yVal);
        }
    }
}
namespace VBNetTweaks.ZDOUtills
{
    [HarmonyPatch]
    public static class ZDONetworkOptimizer
    {

        public static int GetSafeQueueLimit() => Mathf.Clamp(VBNetTweaks.c_ZDOQueueLimit.Value, 8192, 1048576);
        private static Vector3 _currentRefPos;

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2))]
        [HarmonyPrefix]
        static bool SendZDOToPeers2_Prefix(ZDOMan __instance, float dt)
        {
         //   if (!Helper.IsServer()) return true;
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return true;

            OptimizedSendZDOToPeers(__instance, dt);
            return false;
        }

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

            // Сначала ванильная логика Prioritized
            bool xPrioritized = x.Type == ZDO.ObjectType.Prioritized && x.HasOwner() && x.GetOwner() != ZDOMan.s_compareReceiver;
            bool yPrioritized = y.Type == ZDO.ObjectType.Prioritized && y.HasOwner() && y.GetOwner() != ZDOMan.s_compareReceiver;

            if (xPrioritized && yPrioritized) return CompareFloats(x.m_tempSortValue, y.m_tempSortValue);
            if (xPrioritized != yPrioritized) return xPrioritized ? -1 : 1;

            // Сначала ванильный тип
            if (x.Type != y.Type) return ((int)y.Type).CompareTo((int)x.Type);

            // Потом дешёвый приоритет по prefab
            var px = GetPriority(x);
            var py = GetPriority(y);

            if (px != py) return ((byte)py).CompareTo((byte)px);

            // Потом обычная sort value
            return CompareFloats(x.m_tempSortValue, y.m_tempSortValue);
        }

        private static int CompareFloats(float a, float b)
        {
            if (a < b) return -1;
            if (a > b) return 1;
            return 0;
        }
        
        private enum SyncPriority : byte
        {
            Default = 0,
            Piece = 1,
            Creature = 2,
            Ship = 3,
            Player = 4
        }

        private static readonly Dictionary<int, SyncPriority> _priorityCache = new();

        private static SyncPriority GetPriority(ZDO zdo)
        {
            int prefab = zdo.GetPrefab();

            if (_priorityCache.TryGetValue(prefab, out var cached)) return cached;

            var priority = SyncPriority.Default;

            if (ZNetScene.instance)
            {
                GameObject go = ZNetScene.instance.GetPrefab(prefab);

                if (go)
                {
                    if (go.GetComponent<Player>()) priority = SyncPriority.Player;
                    else if (go.GetComponent<Ship>()) priority = SyncPriority.Ship;
                    else if (go.GetComponent<Character>()) priority = SyncPriority.Creature;
                    else if (go.GetComponent<Piece>()) priority = SyncPriority.Piece;
                }
            }

            _priorityCache[prefab] = priority;
            return priority;
        }
    }
}
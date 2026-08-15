namespace VBNetTweaks.ZDOUtills
{
    [HarmonyPatch]
    public static class ZDONetworkOptimizer
    {
        private enum SyncPriority : byte
        {
            Default = 0,
            Piece = 1,
            Creature = 2,
            Ship = 3,
            Player = 4
        }

        private static readonly Dictionary<int, SyncPriority> _priorityCache = new Dictionary<int, SyncPriority>();
        
        private static bool _cacheWarmed;

        public static int GetSafeQueueLimit()
        {
            if (Helper.IsServer()) return Mathf.Clamp(VBNetTweaks.c_ZDOQueueLimit_S.Value, 8192, 1048576);
            return Mathf.Clamp(VBNetTweaks.c_ZDOQueueLimit.Value, 8192, 1048576);
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
                int start = (man.m_nextSendPeer >= 0) ? man.m_nextSendPeer : 0;
                int processed = 0;

                for (int i = 0; i < maxPeers; i++)
                {
                    int idx = (start + i) % count;
                    processed++;

                    ZDOMan.ZDOPeer peer = man.m_peers[idx];
                    if (peer?.m_peer?.m_socket?.IsConnected() != true) continue;

                    int queue = peer.m_peer.m_socket.GetSendQueueSize();
                    int limit = GetSafeQueueLimit();

                    if (queue <= limit)
                    {
                        float flushThreshold = Mathf.Clamp01(VBNetTweaks.c_FlushThresholdPercent_S.Value) * limit;
                        bool flush = queue <= flushThreshold;
                        man.SendZDOs(peer, flush);
                    }
                }

                man.m_nextSendPeer = (start + processed) % count;
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"Error in OptimizedSendZDOToPeers: {ex}");
            }
        }

        private static int SafeCustomCompare(ZDO x, ZDO y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            long receiver = ZDOMan.s_compareReceiver;
            bool xPrio = x.Type == ZDO.ObjectType.Prioritized && x.HasOwner() && x.GetOwner() != receiver;
            bool yPrio = y.Type == ZDO.ObjectType.Prioritized && y.HasOwner() && y.GetOwner() != receiver;

            if (xPrio && yPrio) return CompareFloats(x.m_tempSortValue, y.m_tempSortValue);
            if (xPrio) return -1;
            if (yPrio) return 1;

            if (x.Type != y.Type) return ((int)y.Type).CompareTo((int)x.Type);

            SyncPriority px = GetCachedPriority(x);
            SyncPriority py = GetCachedPriority(y);

            if (px != py) return ((byte)py).CompareTo((byte)px);

            return CompareFloats(x.m_tempSortValue, y.m_tempSortValue);
        }

        private static int CompareFloats(float a, float b)
        {
            if (a < b) return -1;
            if (a > b) return 1;
            return 0;
        }

        private static SyncPriority GetCachedPriority(ZDO zdo)
        {
            int prefab = zdo.GetPrefab();
            if (_priorityCache.TryGetValue(prefab, out SyncPriority cached)) return cached;

            SyncPriority priority = SyncPriority.Default;
            
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

        public static void WarmUpCache()
        {
            if (_cacheWarmed || !ZNetScene.instance) return;
            
            Helper.LogDebug("[ZDONetworkOptimizer] Warming up priority cache...");
            int count = 0;
            
            _cacheWarmed = true;
            Helper.LogDebug($"[ZDONetworkOptimizer] Cache ready. Entries: {_priorityCache.Count}");
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        [HarmonyPostfix]
        private static void ZNetSceneAwake_Postfix()
        {
            WarmUpCache();
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2))]
        [HarmonyPrefix]
        private static bool SendZDOToPeers2_Prefix(ZDOMan __instance, float dt)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return true;

            OptimizedSendZDOToPeers(__instance, dt);
            return false;
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSendCompare))]
        [HarmonyPrefix]
        private static bool CustomServerSendCompare(ZDO x, ZDO y, ref int __result)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value || !ZNet.instance.IsServer()) return true;

            __result = SafeCustomCompare(x, y);
            return false;
        }
    }
}
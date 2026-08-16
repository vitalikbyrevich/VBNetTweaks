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
        private static float _accum;

        public static void OptimizedSendZDOToPeers(ZDOMan man, float dt)
        {
            int count = man.m_peers.Count;
            if (count == 0) { _accum = 0f; return; }

            float interval = Mathf.Clamp(VBNetTweaks.c_SendInterval_S.Value, 0.01f, 0.2f);
            int perBatch   = Mathf.Clamp(VBNetTweaks.c_PeersPerUpdate_S.Value, 1, count);

            _accum += (perBatch / interval) * dt;        // отправок/сек, независимо от FPS
            int budget = (int)_accum;
            if (budget <= 0) return;
            _accum -= budget;
            budget = Mathf.Min(budget, count);           // не догоняем после хикапа

            int idx = (man.m_nextSendPeer >= 0) ? man.m_nextSendPeer : 0;
            int sent = 0, scanned = 0;
            while (sent < budget && scanned < count)
            {
                var peer = man.m_peers[idx % count];
                idx++; scanned++;
                if (peer?.m_peer?.m_socket?.IsConnected() != true) continue;
                man.SendZDOs(peer, false);               // очередь проверит сам
                sent++;
            }
            man.m_nextSendPeer = idx % count;
        }

     /*   private static int SafeCustomCompare(ZDO x, ZDO y)
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
        }*/

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
    
            foreach (var go in ZNetScene.instance.m_prefabs)
            {
                if (!go) continue;
                int hash = go.name.GetStableHashCode();
                if (_priorityCache.ContainsKey(hash)) continue;
        
                SyncPriority p = SyncPriority.Default;
                if (go.GetComponent<Player>()) p = SyncPriority.Player;
                else if (go.GetComponent<Ship>()) p = SyncPriority.Ship;
                else if (go.GetComponent<Character>()) p = SyncPriority.Creature;
                else if (go.GetComponent<Piece>()) p = SyncPriority.Piece;
        
                _priorityCache[hash] = p;
                count++;
            }
    
            _cacheWarmed = true;
            Helper.LogDebug($"[ZDONetworkOptimizer] Cache warmed: {count} prefabs cached");
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

     /*   [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSendCompare))]
        [HarmonyPrefix]
        private static bool CustomServerSendCompare(ZDO x, ZDO y, ref int __result)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value || !ZNet.instance.IsServer()) return true;

            __result = SafeCustomCompare(x, y);
            return false;
        }*/
        
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSortSendZDOS))]
        [HarmonyPrefix]
        private static bool ServerSort_Prefix(List<ZDO> objects, Vector3 refPos, ZDOMan.ZDOPeer peer)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return true;

            float time = Time.time;
            float playerBonus = VBNetTweaks.c_PlayerSortBonus.Value;
            float staticPenalty = VBNetTweaks.c_StaticSortPenalty.Value;

            for (int i = 0; i < objects.Count; i++)
            {
                ZDO z = objects[i];
                float v = Vector3.Distance(z.GetPosition(), refPos);
                float age = 100f;
                if (peer.m_zdos.TryGetValue(z.m_uid, out var info))
                    age = Mathf.Clamp(time - info.m_syncTime, 0f, 100f);
                v -= age * 1.5f;

                // один лукап на ОБЪЕКТ, а не на сравнение
                switch (GetCachedPriority(z))
                {
                    case SyncPriority.Player: v -= playerBonus; break;
                    case SyncPriority.Ship:   v -= playerBonus * 0.6f; break;
                    case SyncPriority.Piece:  v += staticPenalty; break;
                }
                z.m_tempSortValue = v;
            }
            ZDOMan.s_compareReceiver = peer.m_peer.m_uid;
            objects.Sort(ZDOMan.ServerSendCompare);   // ванильный компаратор, без патча
            return false;
        }
    }
}
namespace VBNetTweaks.ZDOUtills
{
    [HarmonyPatch]
    public static class ZDONetworkOptimizer
    {
        private static readonly int PLAYER_PREFAB = "Player".GetStableHashCode();
        private static readonly int HASH_AI = "ai".GetStableHashCode();

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update))]
        private static IEnumerable<CodeInstruction> ZDOManUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions).Start();
            matcher.MatchStartForward(new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2))));
            matcher.SetOperandAndAdvance(AccessTools.Method(typeof(ZDONetworkOptimizer), nameof(OptimizedSendZDOToPeers)));
            return matcher.InstructionEnumeration();
        }

        public static void OptimizedSendZDOToPeers(ZDOMan zdoManager, float dt)
        {
            try
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
            }
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSortSendZDOS))]
        public static void ApplyWeights(List<ZDO> objects, Vector3 refPos)
        {
            if (!ZNet.instance.IsServer()) return;

            foreach (var zdo in objects)
            {
                if (zdo == null) continue;

                int prefab = zdo.GetPrefab();

                if (prefab == PLAYER_PREFAB)
                {
                    zdo.m_tempSortValue -= 500f;
                    continue;
                }

                if (zdo.GetInt(HASH_AI, -1) != -1)
                {
                    // Чем ближе моб к игроку, тем выше приоритет
                    float dist = Vector3.Distance(zdo.GetPosition(), refPos);
                    if (dist < 40f) zdo.m_tempSortValue -= 300f;
                    else if (dist < 80f) zdo.m_tempSortValue -= 150f;
                    // Дальше 80м — стандартный приоритет
                }
            }
        }
    }
}
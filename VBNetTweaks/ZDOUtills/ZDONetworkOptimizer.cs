namespace VBNetTweaks.ZDOUtills
{
    [HarmonyPatch]
    public static class ZDONetworkOptimizer
    {
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update))]
        private static IEnumerable<CodeInstruction> ZDOManUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions).Start();
            matcher.MatchStartForward(new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2))));

            matcher.SetOperandAndAdvance(AccessTools.Method(typeof(ZDONetworkOptimizer), nameof(ZDONetworkOptimizer.OptimizedSendZDOToPeers)));
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
        
                int sent = 0;
                for (int i = 0; i < Math.Min(peersPerUpdate, count); i++)
                {
                    int idx = (startPeer + i) % count;
                    var peer = zdoManager.m_peers[idx];
            
                    if (peer?.m_peer?.m_socket?.IsConnected() != true) continue;
            
                    int queueSize = peer.m_peer.m_socket.GetSendQueueSize();
            
                    if (queueSize > VBNetTweaks.ZDOQueueLimit.Value)
                    {
                        sent++;
                        continue;
                    }
            
                    bool flush = queueSize <= 10240;
            
                    zdoManager.SendZDOs(peer, flush: flush);
                    sent++;
                }
        
                zdoManager.m_nextSendPeer = (startPeer + sent) % count;
            }
            catch (Exception ex) { }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.RemoveObjects))]
        public static class SafeRemoveObjectsPatch
        {
            static bool Prefix(ZNetScene __instance, List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects)
            {
                if (!VBNetTweaks.ModuleSteamOptimizations.Value) return true;

                byte mark = (byte)(Time.frameCount & 255);

                if (currentNearObjects != null)
                    foreach (var z in currentNearObjects)
                        if (z != null) z.TempRemoveEarmark = mark;

                if (currentDistantObjects != null)
                    foreach (var z in currentDistantObjects)
                        if (z != null) z.TempRemoveEarmark = mark;

                __instance.m_tempRemoved.Clear();
                foreach (var kvp in __instance.m_instances)
                {
                    if (kvp.Key == null || !kvp.Value) continue;
                    if (kvp.Key.TempRemoveEarmark != mark) __instance.m_tempRemoved.Add(kvp.Value);
                }
                
                return true;
            }
        }
    }
}
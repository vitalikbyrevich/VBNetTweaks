namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZDOMan_Patch
    {
        public static void OptimizedSendZDOToPeers(ZDOMan man, float dt)
        {
            try
            {
                int count = man.m_peers.Count;
                if (count == 0) return;
        
                man.m_sendTimer += dt;
                float interval = Helper.GetSendInterval();
                if (man.m_sendTimer < interval) return;
        
                man.m_sendTimer -= interval;
        
                int maxPeers = Mathf.Clamp(VBNetTweaks.c_PeersPerUpdate_S.Value, 1, count);
                int start = (man.m_nextSendPeer >= 0) ? man.m_nextSendPeer : 0;
        
                int processed = 0;
                for (int i = 0; i < maxPeers; i++)
                {
                    int idx = (start + i) % count;
                    processed++;
            
                    ZDOMan.ZDOPeer peer = man.m_peers[idx];
                    if (peer?.m_peer?.m_socket?.IsConnected() != true) continue;
            
                    man.SendZDOs(peer, false);
                }
                man.m_nextSendPeer = (start + processed) % count; 
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"Error in OptimizedSendZDOToPeers: {ex.Message}");
                man.SendZDOToPeers2(dt);
            }
        }
        
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update)), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> ZDOManUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher codeMatcher = new CodeMatcher(instructions).Start();
            codeMatcher.MatchStartForward(new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2))));
            if (codeMatcher.IsInvalid)
            {
                Helper.LogDebug("WARNING: SendZDOToPeers2 not found");
                return instructions;
            }
            else Helper.LogDebug("SendZDOToPeers2 success replace to OptimizedSendZDOToPeers");
            codeMatcher.SetOperandAndAdvance(AccessTools.Method(typeof(ZDOMan_Patch), nameof(ZDOMan_Patch.OptimizedSendZDOToPeers)));
            return codeMatcher.InstructionEnumeration();
        }
    }
}
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
        
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update))]
        private static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return instructions;

            var methodToReplace = AccessTools.Method(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2));
            var replacementMethod = AccessTools.Method(typeof(ZDOMan_Patch), nameof(OptimizedSendZDOToPeers));

            var matcher = new CodeMatcher(instructions).MatchForward(false, new CodeMatch(OpCodes.Call, methodToReplace), new CodeMatch(OpCodes.Callvirt, methodToReplace));

            if (!matcher.IsValid)
            {
                Helper.LogDebug("Failed to find SendZDOToPeers2 call in ZDOMan.Update!");
                return instructions;
            }

            matcher.SetInstruction(new CodeInstruction(OpCodes.Call, replacementMethod));
            return matcher.InstructionEnumeration();
        }
    }
}
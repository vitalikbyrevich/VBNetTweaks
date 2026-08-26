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

                GetAdaptiveParams(count, out float interval, out int maxPeers);

                if (man.m_sendTimer < interval) return;
                man.m_sendTimer -= interval;

                int start = (man.m_nextSendPeer >= 0) ? man.m_nextSendPeer : 0;
                int processed = 0;

                for (int i = 0; i < maxPeers; i++)
                {
                    int idx = (start + i) % count;
                    processed++;
                    ZDOMan.ZDOPeer peer = man.m_peers[idx];
                    if (peer?.m_peer?.m_socket?.IsConnected() != true) continue;
                    man.SendZDOs(peer, flush: false);
                }

                man.m_nextSendPeer = (start + processed) % count;
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"Error in OptimizedSendZDOToPeers: {ex.Message}");
                man.SendZDOToPeers2(dt);
            }
        }

        private static void GetAdaptiveParams(int peerCount, out float interval, out int maxPeers)
        {
            interval = Helper.GetSendInterval();

            int divisor = VBNetTweaks.c_PeerCycleDivisor.Value;
            maxPeers = Mathf.CeilToInt((float)peerCount / divisor);

            maxPeers = Mathf.Clamp(maxPeers, 1, peerCount);
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

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs)), HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SendZDOs_QueueLimitFix(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int replacedCount = 0;

            var getQueueLimitMethod = AccessTools.Method(typeof(Helper), nameof(Helper.GetQueueLimit));

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == 10240)
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = getQueueLimitMethod;
                    replacedCount++;
                }
            }

            if (replacedCount < 2) Helper.LogDebug("ZDOQueueLimit patch failed: found less than 2 instances of 10240!");
            else if (replacedCount == 2) Helper.LogDebug($"ZDOQueueLimit patch to: {VBNetTweaks.c_ZDOQueueLimit.Value}");

            return codes;
        }
    }
}
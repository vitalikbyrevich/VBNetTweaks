namespace VBNetTweaks.RPCUtills
{
    [HarmonyPatch(typeof(ZNet))]
    public static class SmartRpcCleanupPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ZNet.Disconnect))]
        public static void OnDisconnect(ZNetPeer peer)
        {
            if (peer != null) SmartRpcFilter.ClearPeerCache(peer.m_uid);
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ZNet.Shutdown))]
        public static void OnShutdown() => SmartRpcFilter.ClearAllCaches();
    }
}
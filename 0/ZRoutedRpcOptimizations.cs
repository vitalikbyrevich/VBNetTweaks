namespace VBNetTweaks;

[HarmonyPatch(typeof(ZRoutedRpc))]
public static class ZRoutedRpcOptimizations
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(ZRoutedRpc.RouteRPC))]
    public static bool RouteRPC_Prefix(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData rpcData)
    {
        // Оптимизация маршрутизации для большого количества пиров
        if (__instance.m_peers.Count > 20) return OptimizedRouteRPC(__instance, rpcData);
        return true;
    }
    
    private static bool OptimizedRouteRPC(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData rpcData)
    {
        // Упрощенная логика маршрутизации
        ZPackage pkg = new ZPackage();
        rpcData.Serialize(pkg);
        foreach (var peer in __instance.m_peers) if (peer.IsReady() && rpcData.m_senderPeerID != peer.m_uid) peer.m_rpc.Invoke("RoutedRPC", pkg);
        return false;
    }
}
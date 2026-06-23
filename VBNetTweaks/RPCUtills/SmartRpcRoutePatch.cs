namespace VBNetTweaks.RPCUtills
{
    [HarmonyPatch(typeof(ZRoutedRpc))]
    internal static class SmartRpcRoutePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(ZRoutedRpc.RouteRPC))]
        private static bool Prefix(ZRoutedRpc __instance, object rpcData)
        {
            if (!VBNetTweaks.ModuleRPCRadiusFiltering.Value) return true;

            if (!__instance.m_server) return true;

            try
            {
                // Получаем хеш метода
                Type type = rpcData.GetType();
                FieldInfo methodHashField = type.GetField("m_methodHash");
                if (methodHashField == null) return true;
                
                int methodHash = (int)methodHashField.GetValue(rpcData);
                
                // Получаем информацию о RPC
                string methodName = ZRoutedRpcRegisterPatch.GetMethodName(methodHash);
                if (string.IsNullOrEmpty(methodName)) return true;
                
                // Если RPC глобальный - пропускаем
                if (SmartRpcFilter.ShouldBroadcastToPeer(__instance, rpcData, null, out int _)) return true;

                // === Фильтруем рассылку ===
                var peersField = typeof(ZRoutedRpc).GetField("m_peers", BindingFlags.Instance | BindingFlags.NonPublic);
                if (peersField == null) return true;
                
                var peers = peersField.GetValue(__instance) as List<ZNetPeer>;
                if (peers == null || peers.Count == 0) return true;
                
                // Сериализуем пакет один раз
                var serializeMethod = type.GetMethod("Serialize");
                if (serializeMethod == null) return true;
                
                ZPackage pkg = new ZPackage();
                serializeMethod.Invoke(rpcData, new object[] { pkg });
                
                int sentCount = 0;
                foreach (var peer in peers)
                {
                    if (peer == null || !peer.IsReady()) continue;
                    
                    int radius;
                    if (SmartRpcFilter.ShouldBroadcastToPeer(__instance, rpcData, peer, out radius))
                    {
                        peer.m_rpc.Invoke("RoutedRPC", pkg);
                        sentCount++;
                    }
                }
                
                if (VBNetTweaks.VerboseLogging.Value)
                {
                    Helper.LogVerbose($"[SmartRpcFilter] {methodName}: sent to {sentCount}/{peers.Count} peers");
                }
                
                return false; // Запрещаем стандартную рассылку
            }
            catch (Exception ex)
            {
                if (VBNetTweaks.VerboseLogging.Value) Helper.LogVerbose($"[SmartRpcFilter] Error: {ex.Message}");
                return true;
            }
        }
    }
}
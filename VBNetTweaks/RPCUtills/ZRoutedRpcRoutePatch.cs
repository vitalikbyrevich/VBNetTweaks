namespace VBNetTweaks.RPCUtills;

[HarmonyPatch(typeof(ZRoutedRpc))]
static class ZRoutedRpcRoutePatch
{
    private static Dictionary<string, int> _rpcSectorRadius = new()
    {
        // Глобальные RPC (весь мир) - радиус -1 означает "без фильтрации"
        { "ChatMessage", -1 },
        { "ServerHandshake", -1 },
        { "PlayerList", -1 },
        { "Save", -1 },
        { "Kick", -1 },
        { "Ban", -1 },
        
        // VEGVISIR RPC (БЕЗ фильтрации!)
        { "RPC_DiscoverClosestLocation", -1 },        // Запрос от клиента
        { "RPC_DiscoverClosestLocationResponse", -1 }, // Ответ сервера
        { "RPC_DiscoverLocationResponse", -1 },
        { "RPC_LocationResponse", -1 },
        
        // ZDO синхронизация (по секторам)
        { "ZDOData", 2 },      // 2 сектора = ~160м
        { "DestroyZDO", 2 },
        { "RequestZDO", 2 },
        
        // Боевые RPC (3 сектора)
        { "RPC_Damage", 3 },    // ~240м
        { "RPC_Heal", 3 },
        { "RPC_Stagger", 3 },
        { "RPC_AddNoise", 3 },
        { "RPC_OnTargeted", 3 },
        { "RPC_OnDeath", 3 },
        
        // Локальные RPC (1 сектор)
        { "RPC_UseStamina", 1 },  // ~80м
        { "RPC_UseEitr", 1 },
        { "RPC_Message", 1 },
        
        // Корабли (2 сектора)
        { "RequestControl", 2 },
        { "ReleaseControl", 2 },
        { "Rudder", 2 },
    };

    [HarmonyPrefix]
    [HarmonyPatch(nameof(ZRoutedRpc.RouteRPC))]
    static bool Prefix(ZRoutedRpc __instance, object rpcData)
    {
        if (!VBNetTweaks.ModuleRPCRadiusFiltering.Value) return true;
        if (!__instance.m_server) return true;
        
        try
        {
            var type = rpcData.GetType();
            
            // Получаем хеш метода
            var methodHashField = type.GetField("m_methodHash");
            if (methodHashField == null) return true;
            
            int methodHash = (int)methodHashField.GetValue(rpcData);
            
            // Получаем имя из нашего регистратора
            string methodName = ZRoutedRpcRegisterPatch.GetMethodName(methodHash);
            
            // Определяем радиус
            int sectorRadius = GetSectorRadiusForMethod(methodName);
            
            // Получаем позицию источника (установленную в InvokePatch)
            Vector3 origin = RPCPositionContext.GetCurrentPosition();
            
            // ЛОГИРОВАНИЕ
            if (VBNetTweaks.VerboseLogging.Value)
            {
                if (sectorRadius < 0 || origin == Vector3.zero)
                {
                    Helper.LogVerbose($"[RPCRadius] PASS-THROUGH: {methodName} ({methodHash}) - No filter applied");
                }
                else
                {
                    Helper.LogVerbose($"[RPCRadius] FILTERING: {methodName} ({methodHash}) with radius {sectorRadius} sectors from {origin}");
                }
            }

            // Если фильтрация не нужна или нет позиции - пропускаем
            if (sectorRadius < 0) return true;
            if (origin == Vector3.zero) return true;
            
            // Применяем фильтрацию
            return FilterRPCBySectors(__instance, rpcData, origin, sectorRadius);
        }
        catch (Exception ex)
        {
            Helper.LogVerbose($"[RPCRadius] Error in RouteRPC: {ex.Message}\n{ex.StackTrace}");
            return true;
        }
        finally
        {
            // Очищаем контекст позиции после обработки
            RPCPositionContext.Clear();
        }
    }

    private static bool FilterRPCBySectors(ZRoutedRpc __instance, object rpcData, Vector3 origin, int sectorRadius)
    {
        var type = rpcData.GetType();
        
        Vector2i sourceSector = ZoneSystem.GetZone(origin);
        
        var peersField = typeof(ZRoutedRpc).GetField("m_peers", BindingFlags.Instance | BindingFlags.NonPublic);
        if (peersField == null) return true;
        
        var peers = peersField.GetValue(__instance) as List<ZNetPeer>;
        if (peers == null || peers.Count == 0) return true;
        
        var senderField = type.GetField("m_senderPeerID");
        long senderPeerID = senderField != null ? (long)senderField.GetValue(rpcData) : 0;
        
        var serializeMethod = type.GetMethod("Serialize");
        if (serializeMethod == null) return true;
        
        var pkg = new ZPackage();
        serializeMethod.Invoke(rpcData, new object[] { pkg });
        
        int sentCount = 0;
        
        foreach (var peer in peers)
        {
            if (peer == null || peer.m_uid == senderPeerID || !peer.IsReady()) continue;
            
            Vector2i peerSector = ZoneSystem.GetZone(peer.GetRefPos());
            int sectorDistance = Math.Max(Math.Abs(sourceSector.x - peerSector.x), Math.Abs(sourceSector.y - peerSector.y));
            
            if (sectorDistance <= sectorRadius)
            {
                peer.m_rpc.Invoke("RoutedRPC", pkg);
                sentCount++;
            }
        }
        
        if (VBNetTweaks.VerboseLogging.Value)
        {
             Helper.LogVerbose($"[RPCRadius] Sent to {sentCount}/{peers.Count} peers.");
        }

        // Возвращаем false, чтобы оригинальный метод RouteRPC не выполнялся (мы уже разослали пакеты сами)
        return false;
    }

    private static int GetSectorRadiusForMethod(string methodName)
    {
        if (string.IsNullOrEmpty(methodName)) return VBNetTweaks.DefaultRPCRadiusSectors.Value;
        
        if (_rpcSectorRadius.TryGetValue(methodName, out var radius)) 
        {
            return radius;
        }
        
        return VBNetTweaks.DefaultRPCRadiusSectors.Value;
    }
}
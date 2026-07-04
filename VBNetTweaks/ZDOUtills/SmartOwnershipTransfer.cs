namespace VBNetTweaks.ZDOUtills;

[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ReleaseNearbyZDOS))]
public static class SmartOwnershipTransfer
{
    private static Dictionary<long, float> _playerPings = new();
    private static float _lastPingUpdate;
    private const float PING_CACHE_TIME = 2f;

    [HarmonyPrefix]
    public static bool Prefix(ZDOMan __instance, Vector3 refPosition, long uid)
    {
        if (!VBNetTweaks.ModuleSmartOwnership.Value) return true;

        if (__instance.m_tempNearObjects == null || __instance.m_tempNearObjects.Count == 0) return true;

        UpdatePlayerPings();

        long currentUid = uid;
        float currentPing = GetPlayerPing(currentUid);
        
        // Находим лучшего кандидата
        long bestOwner = FindBestOwner(__instance, refPosition, currentUid, out float bestScore);
        float currentScore = CalculateScore(refPosition, currentUid, currentPing);
        
        // Гистерезис: не меняем владельца, если разница незначительна
        float threshold = VBNetTweaks.OwnershipPingThreshold.Value;
        if (bestOwner == currentUid || bestScore >= currentScore - threshold * 0.5f) return true;

        // Логируем передачу
        if (VBNetTweaks.VerboseLogging.Value)
        {
            float bestPing = GetPlayerPing(bestOwner);
            Helper.LogVerbose($"[SmartOwnership] Transfer: {currentUid} (ping={currentPing:F0}ms, score={currentScore:F1}) -> {bestOwner} (ping={bestPing:F0}ms, score={bestScore:F1})");
        }

        // Передаем владение ВСЕМ объектам в зоне
        int transferred = 0;
        foreach (ZDO zdo in __instance.m_tempNearObjects)
        {
            if (zdo == null || !zdo.Persistent) continue;
            
            // Проверяем, что объект действительно в зоне лучшего игрока
            // (дополнительная безопасность, хотя bestOwner уже прошел проверку)
            if (IsObjectInPlayerZone(zdo, bestOwner))
            {
                zdo.SetOwner(bestOwner);
                transferred++;
            }
        }

        if (VBNetTweaks.VerboseLogging.Value && transferred > 0)
        {
            Helper.LogVerbose($"[SmartOwnership] Transferred {transferred}/{__instance.m_tempNearObjects.Count} objects to {bestOwner}");
        }

        return false; // Отменяем ванильную логику
    }

    private static long FindBestOwner(ZDOMan zdoMan, Vector3 refPos, long currentUid, out float bestScore)
    {
        float currentPing = GetPlayerPing(currentUid);
        bestScore = CalculateScore(refPos, currentUid, currentPing);
        long bestOwner = currentUid;

        foreach (var peerWrapper in zdoMan.m_peers)
        {
            if (peerWrapper?.m_peer == null || !peerWrapper.m_peer.IsReady()) continue;
            
            long playerId = peerWrapper.m_peer.m_uid;
            if (playerId == currentUid) continue;

            // Проверяем, что игрок в активной зоне
            if (!ZNetScene.InActiveArea(ZoneSystem.GetZone(refPos), peerWrapper.m_peer.GetRefPos())) continue;

            float ping = GetPlayerPing(playerId);
            float score = CalculateScore(refPos, playerId, ping);

            if (score < bestScore)
            {
                bestScore = score;
                bestOwner = playerId;
            }
        }

        return bestOwner;
    }

    private static float CalculateScore(Vector3 refPos, long playerId, float ping)
    {
        Vector3 playerPos = GetPlayerPosition(playerId);
        if (playerPos == Vector3.zero) return float.MaxValue;
            
        float distance = Vector3.Distance(refPos, playerPos);
        float pingWeight = VBNetTweaks.OwnershipPingWeight.Value;
        
        return distance + (ping * pingWeight);
    }

    private static Vector3 GetPlayerPosition(long playerId)
    {
        if (playerId == ZDOMan.GetSessionID()) return ZNet.instance?.GetReferencePosition() ?? Vector3.zero;
            
        ZNetPeer peer = ZNet.instance?.GetPeer(playerId);
        return peer?.GetRefPos() ?? Vector3.zero;
    }

    private static bool IsObjectInPlayerZone(ZDO zdo, long playerId)
    {
        Vector3 playerPos = GetPlayerPosition(playerId);
        if (playerPos == Vector3.zero) return false;
            
        return ZNetScene.InActiveArea(zdo.GetSector(), playerPos);
    }

    private static void UpdatePlayerPings()
    {
        float now = Time.time;
        if (now - _lastPingUpdate < PING_CACHE_TIME) return;
        
        _lastPingUpdate = now;
        _playerPings.Clear();
        
        _playerPings[ZDOMan.GetSessionID()] = 0f;

        if (ZNet.instance == null) return;

        foreach (var peer in ZNet.instance.GetPeers())
        {
            if (peer.IsReady()) _playerPings[peer.m_uid] = peer.m_rpc.GetTimeSinceLastPing();
        }
        
        if (VBNetTweaks.VerboseLogging.Value)
        {
            foreach (var kvp in _playerPings)
            {
                Helper.LogVerbose($"[SmartOwnership] Player {kvp.Key}: {kvp.Value:F0}ms");
            }
        }
    }

    private static float GetPlayerPing(long playerId)
    {
        return _playerPings.TryGetValue(playerId, out float ping) ? ping : 300f;
    }
}
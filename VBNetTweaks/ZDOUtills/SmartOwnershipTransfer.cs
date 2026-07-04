namespace VBNetTweaks.ZDOUtills
{
    [HarmonyPatch(typeof(ZDOMan))]
    public static class SmartOwnershipTransfer
    {
        // Кеш пингов игроков
        private static Dictionary<long, float> _playerPings = new Dictionary<long, float>();
        private static float _lastPingUpdate = 0f;
        private const float PING_UPDATE_INTERVAL = 2f;
        
        // Порог разницы пинга для передачи владения (в миллисекундах)
        private static float PingThreshold => VBNetTweaks.OwnershipPingThreshold.Value;
        
        // ============================================================
        // 1. ОСНОВНОЙ ПАТЧ - передача владения с учетом пинга
        // ============================================================
        [HarmonyPrefix]
        [HarmonyPatch(nameof(ZDOMan.ReleaseNearbyZDOS))]
        static bool Prefix(ZDOMan __instance, Vector3 refPosition, long uid)
        {
            if (!VBNetTweaks.ModuleSmartOwnership.Value) 
                return true; // Пропускаем оригинал
            
            if (__instance.m_tempNearObjects.Count == 0) 
                return true;
            
            // Обновляем кеш пингов
            UpdatePlayerPings();
            
            // Получаем пинг текущего игрока
            float currentPing = GetPlayerPing(uid);
            
            // Находим игрока с лучшим пингом в этой области
            long bestOwner = FindBestOwner(__instance, refPosition, uid, out float bestPing);
            
            // Если текущий игрок не лучший - передаем владение лучшему
            if (bestOwner != 0 && bestOwner != uid && bestPing < currentPing - PingThreshold)
            {
                if (VBNetTweaks.VerboseLogging.Value)
                {
                    Helper.LogVerbose($"[ZDO Ownership] Transferring ownership: current={uid} ({currentPing}ms) -> best={bestOwner} ({bestPing}ms)");
                }
                
                // Передаем владение лучшему игроку
                foreach (ZDO tempNearObject in __instance.m_tempNearObjects)
                {
                    if (tempNearObject != null && tempNearObject.Persistent)
                    {
                        // Проверяем, что объект в зоне лучшего игрока
                        if (IsObjectInPlayerZone(tempNearObject, bestOwner))
                        {
                            tempNearObject.SetOwner(bestOwner);
                        }
                    }
                }
                
                // Возвращаем false, чтобы оригинал не выполнялся
                // (мы уже обработали все объекты)
                return false;
            }
            
            // Если мы лучший - пропускаем оригинал (он обработает как обычно)
            return true;
        }
        
        // ============================================================
        // 2. НАХОДИМ ЛУЧШЕГО ВЛАДЕЛЬЦА
        // ============================================================
        private static long FindBestOwner(ZDOMan zdoMan, Vector3 refPos, long currentUid, out float bestPing)
        {
            bestPing = float.MaxValue;
            long bestOwner = currentUid;
            
            // Собираем всех игроков в активной зоне
            HashSet<long> playersInZone = GetPlayersInZone(zdoMan, refPos);
            
            foreach (long playerId in playersInZone)
            {
                float ping = GetPlayerPing(playerId);
                if (ping < bestPing)
                {
                    bestPing = ping;
                    bestOwner = playerId;
                }
            }
            
            return bestOwner;
        }
        
        // ============================================================
        // 3. ПОЛУЧАЕМ ИГРОКОВ В АКТИВНОЙ ЗОНЕ
        // ============================================================
        private static HashSet<long> GetPlayersInZone(ZDOMan zdoMan, Vector3 refPos)
        {
            HashSet<long> players = new HashSet<long>();
            players.Add(ZDOMan.GetSessionID()); // Сервер/хост
            
            foreach (var peerWrapper in zdoMan.m_peers)
            {
                if (peerWrapper?.m_peer == null) continue;
                if (!peerWrapper.m_peer.IsReady()) continue;
                
                // Проверяем, находится ли игрок в активной зоне
                if (ZNetScene.InActiveArea(ZoneSystem.GetZone(refPos), peerWrapper.m_peer.GetRefPos()))
                {
                    players.Add(peerWrapper.m_peer.m_uid);
                }
            }
            
            return players;
        }
        
        // ============================================================
        // 4. ПРОВЕРКА, ЧТО ОБЪЕКТ В ЗОНЕ ИГРОКА
        // ============================================================
        private static bool IsObjectInPlayerZone(ZDO zdo, long playerId)
        {
            if (playerId == ZDOMan.GetSessionID())
            {
                return ZNetScene.InActiveArea(zdo.GetSector(), ZNet.instance.GetReferencePosition());
            }
            
            ZNetPeer peer = ZNet.instance.GetPeer(playerId);
            if (peer == null) return false;
            
            return ZNetScene.InActiveArea(zdo.GetSector(), peer.GetRefPos());
        }
        
        // ============================================================
        // 5. ОБНОВЛЕНИЕ КЕША ПИНГОВ
        // ============================================================
        private static void UpdatePlayerPings()
        {
            float now = Time.time;
            if (now - _lastPingUpdate < PING_UPDATE_INTERVAL) 
                return;
                
            _lastPingUpdate = now;
            _playerPings.Clear();
            
            if (ZNet.instance == null) return;
            
            // Пинг хоста/сервера
            _playerPings[ZDOMan.GetSessionID()] = 0f;
            
            // Пинги игроков
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer.IsReady())
                {
                    _playerPings[peer.m_uid] = peer.m_rpc.GetTimeSinceLastPing();
                }
            }
        }
        
        // ============================================================
        // 6. ПОЛУЧЕНИЕ ПИНГА ИГРОКА
        // ============================================================
        private static float GetPlayerPing(long playerId)
        {
            if (_playerPings.TryGetValue(playerId, out float ping))
                return ping;
                
            // Если пинг неизвестен - считаем его плохим (300ms)
            return 300f;
        }
        
        // ============================================================
        // 7. ПЕРИОДИЧЕСКАЯ ПРОВЕРКА ВЛАДЕНИЯ (для уже существующих объектов)
        // ============================================================
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ZDOMan.Update))]
        static void Postfix_Update(ZDOMan __instance, float dt)
        {
            if (!VBNetTweaks.ModuleSmartOwnership.Value) 
                return;
                
            // Проверяем владение каждые 10 секунд
            if (Time.frameCount % 600 != 0) 
                return;
                
            // Перепроверяем владение для всех объектов
            RecheckOwnership(__instance);
        }
        
        private static void RecheckOwnership(ZDOMan zdoMan)
        {
            UpdatePlayerPings();
            
            foreach (var kvp in zdoMan.m_objectsByID)
            {
                ZDO zdo = kvp.Value;
                if (zdo == null || !zdo.Persistent) 
                    continue;
                    
                long currentOwner = zdo.GetOwner();
                if (currentOwner == 0) 
                    continue;
                    
                // Проверяем, есть ли игрок с лучшим пингом в зоне
                Vector2i sector = zdo.GetSector();
                long bestOwner = FindBestOwnerInSector(zdoMan, sector, currentOwner, out float bestPing);
                float currentPing = GetPlayerPing(currentOwner);
                
                if (bestOwner != 0 && bestOwner != currentOwner && bestPing < currentPing - PingThreshold)
                {
                    if (VBNetTweaks.VerboseLogging.Value)
                    {
                        Helper.LogVerbose($"[ZDO Ownership] Recheck: {zdo.m_uid} owner {currentOwner} ({currentPing}ms) -> {bestOwner} ({bestPing}ms)");
                    }
                    zdo.SetOwner(bestOwner);
                }
            }
        }
        
        private static long FindBestOwnerInSector(ZDOMan zdoMan, Vector2i sector, long currentOwner, out float bestPing)
        {
            bestPing = GetPlayerPing(currentOwner);
            long bestOwner = currentOwner;
            
            foreach (var peerWrapper in zdoMan.m_peers)
            {
                if (peerWrapper?.m_peer == null) continue;
                if (!peerWrapper.m_peer.IsReady()) continue;
                
                long playerId = peerWrapper.m_peer.m_uid;
                
                if (ZNetScene.InActiveArea(sector, peerWrapper.m_peer.GetRefPos()))
                {
                    float ping = GetPlayerPing(playerId);
                    if (ping < bestPing)
                    {
                        bestPing = ping;
                        bestOwner = playerId;
                    }
                }
            }
            
            return bestOwner;
        }
    }
}
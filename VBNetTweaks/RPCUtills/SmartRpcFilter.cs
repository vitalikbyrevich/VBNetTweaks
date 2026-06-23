namespace VBNetTweaks.RPCUtills
{
    public static class SmartRpcFilter
    {
        // ============================================================
        // 1. БЕЛЫЙ СПИСОК - RPC, которые НЕ ФИЛЬТРУЕМ (глобальные)
        // ============================================================
        private static readonly HashSet<string> GlobalRpcWhitelist = new HashSet<string>
        {
            // Системные (должны доходить до всех)
            "ChatMessage",
            "ServerHandshake",
            "PlayerList",
            "AdminList",
            "Save",
            "Kick",
            "Ban",
            "Unban",
            "PrintBanned",
            "RPC_RemoteCommand",
            "RPC_RemotePrint",
            "RPC_Kicked",
            "RPC_Error",
            
            // Обнаружение локаций (глобальные запросы)
            "RPC_DiscoverClosestLocation",
            "RPC_DiscoverClosestLocationResponse",
            "RPC_DiscoverLocationResponse",
            "RPC_LocationResponse",
            
            // Синхронизация времени и статуса
            "NetTime",
            "ServerSyncedPlayerData",
            "RPC_ServerSyncedPlayerData",
            
            // Игроки (должны быть глобальными для списка игроков)
            "CharacterID",
            "RPC_CharacterID",
            "SavePlayerProfile",
            "RPC_SavePlayerProfile",
        };

        // ============================================================
        // 2. УМНЫЙ СЛОВАРЬ - RPC с динамическим радиусом в зависимости от типа
        // ============================================================
        private static readonly Dictionary<string, (int radius, string priority)> RpcSectorRules = new Dictionary<string, (int radius, string priority)>
        {
            // === ВЫСОКИЙ ПРИОРИТЕТ (синхронизация движения) ===
            // Игроки и звери — максимальный радиус, чтобы не было телепортов
            { "RPC_PlayerSync", (4, "high") },      // 256м, приоритетная доставка
            { "RPC_CreatureSync", (4, "high") },
            { "RPC_SyncTransform", (4, "high") },
            { "RPC_UpdateCharacter", (4, "high") },
            
            // === СРЕДНИЙ ПРИОРИТЕТ (бой, взаимодействие) ===
            { "RPC_Damage", (3, "normal") },        // 192м
            { "RPC_Heal", (3, "normal") },
            { "RPC_Stagger", (3, "normal") },
            { "RPC_OnDeath", (3, "normal") },
            { "RPC_OnTargeted", (3, "normal") },
            { "RPC_AddNoise", (3, "normal") },
            { "RPC_Attack", (3, "normal") },
            { "RPC_Block", (3, "normal") },
            { "RPC_Parry", (3, "normal") },
            { "RPC_UseStamina", (3, "normal") },
            { "RPC_UseEitr", (3, "normal") },
            
            // === НИЗКИЙ ПРИОРИТЕТ (звуки, эффекты) ===
            { "RPC_DamageText", (2, "low") },       // 128м
            { "RPC_HealthChanged", (2, "low") },
            { "RPC_WNTHealthChanged", (2, "low") },
            { "RPC_Message", (2, "low") },
            { "RPC_Emote", (2, "low") },
            { "RPC_TriggerAnimation", (2, "low") },
            
            // === МИНИМАЛЬНЫЙ (шум, который можно резать) ===
            { "RPC_Say", (1, "low") },              // 64м - только рядом стоящие
            { "RPC_TalkerSay", (1, "low") },
            { "RPC_OnChat", (1, "low") },
            
            // === ЛОКАЛЬНЫЙ (только для владельца) ===
            { "RPC_SetTarget", (0, "local") },      // 0 = только целевой игрок
            { "RPC_Target", (0, "local") },
            
            // === КОРАБЛИ И ТРАНСПОРТ (важно для плавности) ===
            { "Rudder", (3, "high") },
            { "RequestControl", (3, "high") },
            { "ReleaseControl", (3, "high") },
            { "RPC_ShipSync", (4, "high") },
            
            // === СТРОИТЕЛЬСТВО И ОБЪЕКТЫ (средний радиус) ===
            { "RPC_OpenContainer", (2, "normal") },
            { "RPC_UseItem", (2, "normal") },
            { "RPC_Interact", (2, "normal") },
            { "RPC_Pickup", (2, "normal") },
            
            // === ZDO СИНХРОНИЗАЦИЯ (критично, НЕ трогаем) ===
            // Эти RPC идут через ZDOMan, их фильтрация сломает синхронизацию
            // { "ZDOData", -1 },      // -1 = пропускать без фильтрации
            // { "DestroyZDO", -1 },
            // { "RequestZDO", -1 },
        };

        // ============================================================
        // 3. КЕШ ДЛЯ РЕФЛЕКСИИ
        // ============================================================
        private static readonly Dictionary<int, (string name, int radius, string priority)> _methodCache = new Dictionary<int, (string, int, string)>();
        private static readonly object _cacheLock = new object();
        
        // ============================================================
        // 4. КЕШ ПОЗИЦИЙ ИГРОКОВ (уменьшает вычисления)
        // ============================================================
        private static readonly Dictionary<long, (Vector3 pos, float time)> _peerPositionCache = new Dictionary<long, (Vector3, float)>();
        private const float CACHE_TTL = 0.5f;

        // ============================================================
        // 5. ОСНОВНОЙ МЕТОД ФИЛЬТРАЦИИ
        // ============================================================
        public static bool ShouldBroadcastToPeer(ZRoutedRpc __instance, object rpcData, ZNetPeer peer, out int sectorRadius)
        {
            sectorRadius = -1;
            
            if (!VBNetTweaks.ModuleRPCRadiusFiltering.Value) return true;

            if (!__instance.m_server) return true;

            try
            {
                // === ШАГ 1: Получаем хеш метода (быстро, без десериализации) ===
                Type type = rpcData.GetType();
                FieldInfo methodHashField = type.GetField("m_methodHash");
                if (methodHashField == null) return true;
                
                int methodHash = (int)methodHashField.GetValue(rpcData);
                
                // === ШАГ 2: Проверяем кеш ===
                string methodName;
                int radius;
                string priority;
                
                lock (_cacheLock)
                {
                    if (_methodCache.TryGetValue(methodHash, out var cached))
                    {
                        methodName = cached.name;
                        radius = cached.radius;
                        priority = cached.priority;
                    }
                    else
                    {
                        // Получаем имя метода из зарегистрированных RPC
                        methodName = ZRoutedRpcRegisterPatch.GetMethodName(methodHash);
                        
                        // Определяем радиус
                        if (GlobalRpcWhitelist.Contains(methodName))
                        {
                            radius = -1; // Не фильтруем
                            priority = "global";
                        }
                        else if (RpcSectorRules.TryGetValue(methodName, out var rule))
                        {
                            radius = rule.radius;
                            priority = rule.priority;
                        }
                        else
                        {
                            // По умолчанию - средний радиус (НО НЕ ДЛЯ ВСЕХ!)
                            // Используем настройку конфига, но с умным умолчанием
                            radius = VBNetTweaks.DefaultRPCRadiusSectors.Value;
                            // Если в конфиге -1, то не фильтруем неизвестные RPC
                            if (radius < 0) radius = -1;
                            priority = "unknown";
                        }
                        
                        _methodCache[methodHash] = (methodName, radius, priority);
                    }
                }

                // === ШАГ 3: Если RPC глобальный - пропускаем ===
                if (radius < 0) return true;
                
                // === ШАГ 4: Получаем позицию источника RPC ===
                Vector3 origin = GetOriginFromRpcData(rpcData);
                if (origin == Vector3.zero) return true; // Если не знаем позицию - пропускаем (безопаснее)
                
                // === ШАГ 5: Проверяем расстояние до пира ===
                Vector3 peerPos = GetPeerPosition(peer);
                int peerSectorDist = RPCSectorHelper.CalculateSectorDistance(origin, peerPos);
                
                sectorRadius = radius;
                return peerSectorDist <= radius;
            }
            catch (Exception ex)
            {
                if (VBNetTweaks.VerboseLogging.Value) Helper.LogVerbose($"[SmartRpcFilter] Error: {ex.Message}");
                return true; // При ошибке пропускаем (безопасное поведение)
            }
        }

        // ============================================================
        // 6. ПОЛУЧЕНИЕ ПОЗИЦИИ ИСТОЧНИКА RPC (оптимизировано)
        // ============================================================
        private static Vector3 GetOriginFromRpcData(object rpcData)
        {
            Type type = rpcData.GetType();
            
            // === Быстрый путь: если есть targetZDO ===
            FieldInfo targetZDOField = type.GetField("m_targetZDO");
            if (targetZDOField != null)
            {
                ZDOID targetZDO = (ZDOID)targetZDOField.GetValue(rpcData);
                if (!targetZDO.IsNone())
                {
                    ZDO zdo = ZDOMan.instance?.GetZDO(targetZDO);
                    if (zdo != null) return zdo.GetPosition();
                }
            }
            
            // === Средний путь: парсим параметры (но только если это реально нужно) ===
            // Для тяжелых RPC (типа RPC_Damage) это может быть дорого,
            // но мы уже отфильтровали большинство RPC на предыдущем шаге.
            // Поэтому здесь можно оставить рефлексию параметров.
            
            // Если мы дошли сюда, RPC не имеет targetZDO, значит это "глобальный" RPC
            // с параметрами. Таких RPC не должно быть много, поэтому можно безопасно
            // перебрать параметры в поисках позиции.
            
            // Пропускаем - для глобальных RPC позиция не критична
            return Vector3.zero;
        }

        // ============================================================
        // 7. ПОЛУЧЕНИЕ ПОЗИЦИИ ПИРА (с кешированием)
        // ============================================================
        private static Vector3 GetPeerPosition(ZNetPeer peer)
        {
            if (peer == null) return Vector3.zero;
            
            float now = Time.time;
            long uid = peer.m_uid;
            
            lock (_peerPositionCache)
            {
                if (_peerPositionCache.TryGetValue(uid, out var cached) && (now - cached.time) < CACHE_TTL) return cached.pos;
                
                Vector3 pos = peer.GetRefPos();
                _peerPositionCache[uid] = (pos, now);
                return pos;
            }
        }

        // ============================================================
        // 8. ОЧИСТКА КЕША ПРИ ОТКЛЮЧЕНИИ ИГРОКА
        // ============================================================
        public static void ClearPeerCache(long uid)
        {
            lock (_peerPositionCache)
            {
                _peerPositionCache.Remove(uid);
            }
        }
        
        public static void ClearAllCaches()
        {
            lock (_methodCache)
            {
                _methodCache.Clear();
            }
            lock (_peerPositionCache)
            {
                _peerPositionCache.Clear();
            }
        }
    }
}
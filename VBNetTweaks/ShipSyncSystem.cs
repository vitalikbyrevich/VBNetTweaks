namespace VBNetTweaks
{
    [HarmonyPatch]
    public static class ShipSyncSystem
    {
        private class ShipData
        {
            public Vector3 pos;
            public Quaternion rot;
            public Vector3 vel;
            public float t;
            public bool ok;
            public long lastOwner;
            public float lastUpdateTime;
        }
        
        private class PlayerShipState
        {
            public Ship ship;
            public Vector3 localPos;
            public Quaternion localRot;
            public float lastUpdate;
        }
        
        private static readonly Dictionary<long, ShipData> _shipData = new();
        private static readonly Dictionary<long, PlayerShipState> _playerStates = new();
        private static readonly Dictionary<ZDOID, int> _playersOnShip = new();
        private static readonly Dictionary<long, ZDOID> _playerShipMap = new();
        
        // Настройки сглаживания
        private const float SMOOTH_POS_ON_SHIP = 0.35f;   // быстрее, когда на корабле
        private const float SMOOTH_ROT_ON_SHIP = 0.25f;
        private const float SMOOTH_POS_OFF_SHIP = 0.25f;
        private const float SMOOTH_ROT_OFF_SHIP = 0.15f;
        private const float CORRECTION_THRESHOLD = 0.5f;  // коррекция при ошибке >0.5м (было 2м)
        private const float ROT_CORRECTION_THRESHOLD = 10f; // коррекция поворота при >10 градусов
        
        private const string RPC_SYNC_SHIP = "VBNT.SyncShip";

        static ShipSyncSystem()
        {
            if (ZRoutedRpc.instance != null) 
                ZRoutedRpc.instance.Register<long, ZDOID>(RPC_SYNC_SHIP, RPC_SyncShip);
        }

        private static void SyncShip(long playerId, ZDOID shipId)
        {
            if (!ZNet.instance || ZRoutedRpc.instance == null) return;
            if (playerId == 0) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RPC_SYNC_SHIP, playerId, shipId);
            PlayerCache.UpdatePlayerState(playerId, PlayerCache.IsPlayerAttached(playerId), shipId);
        }

        private static void RPC_SyncShip(long sender, long playerId, ZDOID shipId)
        {
            if (sender == ZNet.GetUID()) return;
            PlayerCache.UpdatePlayerState(playerId, PlayerCache.IsPlayerAttached(playerId), shipId);
        }
        
        public static void CleanupPeer(long uid)
        {
            lock (_shipData) _shipData.Remove(uid);
            lock (_playerStates)
            {
                if (_playerShipMap.TryGetValue(uid, out var shipId))
                {
                    if (_playersOnShip.TryGetValue(shipId, out int count))
                    {
                        if (count <= 1) _playersOnShip.Remove(shipId);
                        else _playersOnShip[shipId] = count - 1;
                    }
                    _playerShipMap.Remove(uid);
                }
                _playerStates.Remove(uid);
            }
            PlayerCache.RemovePlayer(uid);
        }

        private static Ship GetShipUnderPlayer(Player p)
        {
            if (p == null) return null;
            RaycastHit hit;
            Vector3 origin = p.transform.position + Vector3.up * 0.2f;
            if (Physics.Raycast(origin, Vector3.down, out hit, 2f)) 
                return hit.collider.GetComponentInParent<Ship>();
            return null;
        }

        [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.Deserialize))]
        [HarmonyPostfix]
        public static void CaptureShipState(ZNetView __instance)
        {
            if (Helper.IsServer()) return;
            if (__instance == null) return;

            Ship ship = __instance.GetComponent<Ship>();
            if (ship == null) return;

            var zdo = __instance.GetZDO();
            if (zdo == null || !zdo.IsValid()) return;

            long owner = zdo.GetOwner();
            if (owner == 0 || owner == ZNet.GetUID()) return;

            Vector3 pos = zdo.GetPosition();
            Quaternion rot = zdo.GetRotation();
            float currentTime = Time.time;

            if (!_shipData.TryGetValue(owner, out var d))
            {
                d = new ShipData 
                { 
                    pos = pos, 
                    rot = rot, 
                    t = currentTime, 
                    ok = true, 
                    lastOwner = owner,
                    lastUpdateTime = currentTime
                };
                _shipData[owner] = d;
                return;
            }

            // ✅ Проверка смены владельца — если владелец сменился, сбрасываем скорость
            if (d.lastOwner != owner)
            {
                d.vel = Vector3.zero;
                d.lastOwner = owner;
                d.lastUpdateTime = currentTime;
            }

            float dt = currentTime - d.t;
            if (dt > 0.01f && dt < 0.5f) // ✅ ограничиваем максимальный dt
            {
                // ✅ Экспоненциальное сглаживание скорости (более стабильно)
                Vector3 newVel = (pos - d.pos) / dt;
                d.vel = Vector3.Lerp(d.vel, newVel, 0.3f);
            }
            else if (dt >= 0.5f)
            {
                // При большой задержке — сбрасываем скорость, чтобы не улететь
                d.vel = Vector3.zero;
            }

            d.pos = pos;
            d.rot = rot;
            d.t = currentTime;
            d.ok = true;
        }

        [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
        [HarmonyPostfix]
        public static void SmoothShip(Ship __instance)
        {
            if (Helper.IsServer()) return;
            
            var zdo = __instance.m_nview?.GetZDO();
            if (zdo == null) return;
    
            long owner = zdo.GetOwner();
            if (owner == 0 || owner == ZNet.GetUID()) return;
            
            if (!_shipData.TryGetValue(owner, out var d) || !d.ok) return;
    
            var t = __instance.transform;
            float currentTime = Time.time;
            float dt = Mathf.Min(Time.deltaTime, 0.033f); // ✅ ограничиваем dt для стабильности
            
            // ✅ Предсказание позиции с ограничением максимальной скорости
            Vector3 predicted = d.pos + d.vel * dt;
            
            // ✅ Ограничиваем максимальное предсказание (не больше 5 метров за кадр)
            Vector3 delta = predicted - t.position;
            if (delta.magnitude > 5f)
            {
                predicted = t.position + delta.normalized * 5f;
            }
            
            // ✅ Выбираем скорость сглаживания в зависимости от того, на корабле ли локальный игрок
            bool localPlayerOnShip = PlayerCache.IsPlayerOnShip(Player.m_localPlayer?.GetPlayerID() ?? 0);
            float lerpPos = localPlayerOnShip ? SMOOTH_POS_ON_SHIP : SMOOTH_POS_OFF_SHIP;
            float lerpRot = localPlayerOnShip ? SMOOTH_ROT_ON_SHIP : SMOOTH_ROT_OFF_SHIP;
            
            float error = Vector3.Distance(t.position, predicted);
            
            // ✅ Более низкий порог коррекции — 0.5 метра вместо 2
            if (error > CORRECTION_THRESHOLD)
            {
                t.position = predicted;
                t.rotation = d.rot;
            }
            else
            {
                float rotError = Quaternion.Angle(t.rotation, d.rot);
                if (rotError > ROT_CORRECTION_THRESHOLD)
                {
                    t.rotation = d.rot;
                }
                else
                {
                    t.rotation = Quaternion.Slerp(t.rotation, d.rot, lerpRot);
                }
                // ✅ Используем MoveTowards для более плавного движения
                t.position = Vector3.Lerp(t.position, predicted, lerpPos);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        [HarmonyPostfix]
        public static void TrackLocalPlayer(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;

            long uid = __instance.GetPlayerID();
            Ship oldShip = _playerStates.TryGetValue(uid, out var oldState) ? oldState.ship : null;
            Ship newShip = GetShipUnderPlayer(__instance);

            if (oldShip == newShip && newShip)
            {
                if (_playerStates.TryGetValue(uid, out var currentState))
                {
                    currentState.localPos = newShip.transform.InverseTransformPoint(__instance.transform.position);
                    currentState.localRot = Quaternion.Inverse(newShip.transform.rotation) * __instance.transform.rotation;
                    currentState.lastUpdate = Time.time;
                }
                return;
            }

            // Выход с корабля
            if (oldShip)
            {
                var oldShipZDO = oldShip.m_nview?.GetZDO();
                if (oldShipZDO != null)
                {
                    var shipId = oldShipZDO.m_uid;
                    if (_playersOnShip.TryGetValue(shipId, out int count))
                    {
                        if (count <= 1) _playersOnShip.Remove(shipId);
                        else _playersOnShip[shipId] = count - 1;
                    }
                }
            }

            // Заход на корабль
            if (newShip)
            {
                var newShipZDO = newShip.m_nview?.GetZDO();
                if (newShipZDO != null)
                {
                    var shipId = newShipZDO.m_uid;
                    _playersOnShip[shipId] = _playersOnShip.TryGetValue(shipId, out int count) ? count + 1 : 1;
                    _playerShipMap[uid] = shipId;
                }
            }

            if (!_playerStates.TryGetValue(uid, out var newState))
            {
                newState = new PlayerShipState();
                _playerStates[uid] = newState;
            }

            newState.ship = newShip;
            if (newShip)
            {
                newState.localPos = newShip.transform.InverseTransformPoint(__instance.transform.position);
                newState.localRot = Quaternion.Inverse(newShip.transform.rotation) * __instance.transform.rotation;
            }
            newState.lastUpdate = Time.time;

            if (oldShip != newShip)
            {
                ZDOID shipId = newShip?.m_nview?.GetZDO()?.m_uid ?? new ZDOID();
                SyncShip(uid, shipId);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.LateUpdate))]
        [HarmonyPostfix]
        public static void LateUpdate_PlayerSync(Player __instance)
        {
            if (__instance == Player.m_localPlayer) return;
            long uid = __instance.GetPlayerID();
            if (!_playerStates.TryGetValue(uid, out var st) || st.ship == null) return;
            if (__instance.IsAttached()) return;

            float speed = 20f; // ✅ чуть выше для лучшей отзывчивости
            Vector3 targetPos = st.ship.transform.TransformPoint(st.localPos);
            Quaternion targetRot = st.ship.transform.rotation * st.localRot;

            __instance.transform.position = Vector3.Lerp(__instance.transform.position, targetPos, Time.deltaTime * speed);
            __instance.transform.rotation = Quaternion.Slerp(__instance.transform.rotation, targetRot, Time.deltaTime * speed);
        }
    }
}
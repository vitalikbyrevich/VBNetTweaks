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
        }
        
        private class PlayerShipState
        {
            public Ship ship;
            public Vector3 localPos;
            public Quaternion localRot;
            public float lastUpdate;
        }
        private static readonly object _shipDataLock = new object();
        private static readonly object _playerStatesLock = new object();
        private static readonly Dictionary<long, ShipData> _shipData = new();
        private static readonly Dictionary<long, PlayerShipState> _playerStates = new();
        
        private static readonly Dictionary<ZDOID, int> _playersOnShip = new();
        private static readonly Dictionary<long, ZDOID> _playerShipMap = new();

        public static bool ShipHasPlayers(ZDOID shipId) 
        {
            lock (_shipDataLock)
                return _playersOnShip.ContainsKey(shipId);
        }

        public static int GetPlayersOnShipCount(ZDOID shipId) 
        {
            lock (_shipDataLock)
                return _playersOnShip.TryGetValue(shipId, out int count) ? count : 0;
        }

        public static bool IsPlayerOnShip(long playerId) => PlayerCache.IsPlayerOnShip(playerId);
        public static bool IsPlayerAttached(long playerId) => PlayerCache.IsPlayerAttached(playerId);

        private const string RPC_SYNC_SHIP = "VBNT.SyncShip";

        static ShipSyncSystem()
        {
            if (ZRoutedRpc.instance != null) ZRoutedRpc.instance.Register<long, ZDOID>(RPC_SYNC_SHIP, RPC_SyncShip);
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
            lock (_shipDataLock) _shipData.Remove(uid);

            lock (_playerStatesLock)
            {
                if (_playerShipMap.TryGetValue(uid, out var shipId))
                {
                    lock (_shipDataLock)
                    {
                        if (_playersOnShip.TryGetValue(shipId, out int count))
                        {
                            if (count <= 1) _playersOnShip.Remove(shipId);
                            else _playersOnShip[shipId] = count - 1;
                        }
                    }
                    _playerShipMap.Remove(uid);
                }
                _playerStates.Remove(uid);
            }
    
            PlayerCache.RemovePlayer(uid);
        }

        private static Ship GetShipUnderPlayer(Player p)
        {
            RaycastHit hit;
            Vector3 origin = p.transform.position + Vector3.up * 0.2f;

            if (Physics.Raycast(origin, Vector3.down, out hit, 2f)) return hit.collider.GetComponentInParent<Ship>();

            return null;
        }

        [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.Deserialize))]
        [HarmonyPostfix]
        public static void CaptureShipState(ZNetView __instance)
        {
            if (Helper.IsServer()) return;
            if (!__instance) return;

            Ship ship = __instance.GetComponent<Ship>();
            if (!ship) return;

            var zdo = __instance.GetZDO();
            if (zdo == null) return;
            if (!zdo.IsValid()) return;

            long owner = zdo.GetOwner();
            if (owner == 0) return;
            if (owner == ZNet.GetUID()) return;

            Vector3 pos = zdo.GetPosition();
            Quaternion rot = zdo.GetRotation();

            if (!_shipData.TryGetValue(owner, out var d))
            {
                d = new ShipData { pos = pos, rot = rot, t = Time.time, ok = true };
                _shipData[owner] = d;
                return;
            }

            float dt = Time.time - d.t;
            if (dt > 0f) d.vel = (pos - d.pos) / dt;

            d.pos = pos;
            d.rot = rot;
            d.t = Time.time;
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
            Vector3 predicted = d.pos + d.vel * Time.deltaTime;
    
            float lerpPos = 0.25f;
            float lerpRot = 0.15f;
    
            if (ShipSyncSystem.IsPlayerOnShip(Player.m_localPlayer?.GetPlayerID() ?? 0))
            {
                lerpPos = 0.15f;
                lerpRot = 0.1f;
            }
            float error = Vector3.Distance(__instance.transform.position, predicted);
            if (error > 2f)
            {
                t.position = predicted;
                t.rotation = d.rot;
            }
            else
            {
                float rotError = Quaternion.Angle(__instance.transform.rotation, d.rot);
                if (rotError > 15f)
                {
                    t.rotation = d.rot;
                }
                else
                {
                    t.rotation = Quaternion.Slerp(t.rotation, d.rot, lerpRot);
                }
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

            bool shipChanged = oldShip != newShip;

            newState.ship = newShip;

            if (newShip)
            {
                newState.localPos = newShip.transform.InverseTransformPoint(__instance.transform.position);
                newState.localRot = Quaternion.Inverse(newShip.transform.rotation) * __instance.transform.rotation;
            }

            newState.lastUpdate = Time.time;

            if (shipChanged)
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

            if (!_playerStates.TryGetValue(uid, out var st) || !st.ship) return;

            if (__instance.IsAttached()) return;

            float speed = 15f;

            Vector3 targetPos = st.ship.transform.TransformPoint(st.localPos);
            Quaternion targetRot = st.ship.transform.rotation * st.localRot;

            __instance.transform.position = Vector3.Lerp(__instance.transform.position, targetPos, Time.deltaTime * speed);
            __instance.transform.rotation = Quaternion.Slerp(__instance.transform.rotation, targetRot, Time.deltaTime * speed);
        }
    }
}

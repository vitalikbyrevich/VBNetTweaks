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
            public bool isAttached;
        }

        private static readonly Dictionary<long, ShipData> _shipData = new();
        private static readonly Dictionary<long, PlayerShipState> _playerStates = new();
        
        private static readonly Dictionary<ZDOID, int> _playersOnShip = new();
        private static readonly Dictionary<long, ZDOID> _playerShipMap = new();

        public static bool ShipHasPlayers(ZDOID shipId) => _playersOnShip.ContainsKey(shipId);
        public static int GetPlayersOnShipCount(ZDOID shipId) => _playersOnShip.TryGetValue(shipId, out int count) ? count : 0;

        public static bool IsPlayerOnShip(long playerId) => PlayerCache.IsPlayerOnShip(playerId);
        public static bool IsPlayerAttached(long playerId) => PlayerCache.IsPlayerAttached(playerId);

        private const string RPC_SYNC_ATTACHMENT = "VBNT.SyncAttachment";

        static ShipSyncSystem()
        {
            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.Register<long, bool, ZDOID, string>(RPC_SYNC_ATTACHMENT, RPC_SyncAttachment);
            }
        }

        private static void SyncAttachment(long playerId, bool attached, ZDOID shipId, string attachPointName = "")
        {
            if (!ZNet.instance) return;

            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.Everybody,
                RPC_SYNC_ATTACHMENT,
                playerId,
                attached,
                shipId,
                attachPointName
            );

            PlayerCache.UpdatePlayerState(playerId, attached, shipId);
        }

        private static void RPC_SyncAttachment(long sender, long playerId, bool attached, ZDOID shipId, string attachPointName)
        {
            if (sender == ZNet.GetUID()) return;

            Player player = PlayerCache.GetById(playerId);
            if (!player)
            {
                VBNetTweaks.LogDebug($"RPC_SyncAttachment: player {playerId} not found");
                return;
            }

            PlayerCache.UpdatePlayerState(playerId, attached, shipId);

            if (attached)
            {
                GameObject shipView = ZNetScene.instance.FindInstance(shipId);
                if (!shipView) return;

                Ship ship = shipView.GetComponent<Ship>();
                if (!ship) return;

                Transform attachPoint = FindAttachPoint(ship, attachPointName);
                if (attachPoint && !player.IsAttached())
                {
                    player.AttachStart(attachPoint, null, false, false, true, "attach_chair", Vector3.zero);
                }
            }
            else
            {
                if (player.IsAttached()) player.AttachStop();
            }
        }
        
        public static void CleanupPeer(long uid)
        {
            _shipData.Remove(uid);

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
            
            PlayerCache.RemovePlayer(uid);
        }

        private static Transform FindAttachPoint(Ship ship, string pointName)
        {
            if (!ship) return null;

            var controls = ship.GetComponentInChildren<ShipControlls>();
            if (controls && controls.m_attachPoint) return controls.m_attachPoint;

            var chairs = ship.GetComponentsInChildren<Chair>();
            foreach (var chair in chairs)
                if (chair.m_attachPoint) return chair.m_attachPoint;

            if (!string.IsNullOrEmpty(pointName))
            {
                var found = ship.transform.Find(pointName);
                if (found) return found;
            }
            return null;
        }

        private static Ship GetShipUnderPlayer(Player p)
        {
            RaycastHit hit;
            Vector3 origin = p.transform.position + Vector3.up * 0.2f;

            if (Physics.Raycast(origin, Vector3.down, out hit, 2f)) return hit.collider.GetComponentInParent<Ship>();

            return null;
        }

        private static bool IsPlayerAttached(Player p) => p && p.IsAttached();

        private static bool IsShipZDO(ZDO zdo)
        {
            int p = zdo.GetPrefab();
            return p == "Karve".GetStableHashCode() || p == "VikingShip".GetStableHashCode() || p == "Raft".GetStableHashCode() || p == "VikingShip_Ashlands".GetStableHashCode();
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
            if (zdo == null || !IsShipZDO(zdo)) return;

            long owner = zdo.GetOwner();
            if (!_shipData.TryGetValue(owner, out var d) || !d.ok) return;

            var t = __instance.transform;
            Vector3 predicted = d.pos + d.vel * Time.deltaTime;

            t.position = Vector3.Lerp(t.position, predicted, 0.25f);
            t.rotation = Quaternion.Slerp(t.rotation, d.rot, 0.15f);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        [HarmonyPostfix]
        public static void TrackLocalPlayer(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;

            long uid = __instance.GetPlayerID();
            Ship oldShip = _playerStates.TryGetValue(uid, out var oldState) ? oldState.ship : null;
            Ship newShip = GetShipUnderPlayer(__instance);
            bool isAttached = IsPlayerAttached(__instance);

            if (oldShip == newShip && newShip)
            {
                if (_playerStates.TryGetValue(uid, out var currentState))
                {
                    currentState.localPos = newShip.transform.InverseTransformPoint(__instance.transform.position);
                    currentState.localRot = Quaternion.Inverse(newShip.transform.rotation) * __instance.transform.rotation;
                    currentState.isAttached = isAttached;
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

            bool wasAttached = newState.isAttached;
            bool shipChanged = oldShip != newShip;

            newState.ship = newShip;
            newState.isAttached = isAttached;

            if (newShip)
            {
                newState.localPos = newShip.transform.InverseTransformPoint(__instance.transform.position);
                newState.localRot = Quaternion.Inverse(newShip.transform.rotation) * __instance.transform.rotation;
            }

            newState.lastUpdate = Time.time;

            if (shipChanged || wasAttached != isAttached)
            {
                ZDOID shipId = newShip?.m_nview?.GetZDO()?.m_uid ?? new ZDOID();
                SyncAttachment(uid, isAttached, shipId, "");
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.LateUpdate))]
        [HarmonyPostfix]
        public static void ApplyShipCompensation(Player __instance)
        {
            if (__instance == Player.m_localPlayer) return;

            long uid = __instance.GetPlayerID();
            if (!_playerStates.TryGetValue(uid, out var st)) return;
            if (!st.ship) return;

          /*  bool shouldBeAttached = st.isAttached;

            if (shouldBeAttached)
            {
                var attachPoint = __instance.GetAttachPoint();

                if (!attachPoint)
                {
                    VBNetTweaks.LogDebug($"Player {uid} should be attached but isn't");

                    return;
                }

                Ship parentShip = attachPoint.GetComponentInParent<Ship>();
                if (parentShip != st.ship)
                {
                    VBNetTweaks.LogDebug($"Player {uid} attached to wrong ship");
                }
                return;
            }*/

            float lerpSpeed = 15f;
            Vector3 targetPos = st.ship.transform.TransformPoint(st.localPos);
            Quaternion targetRot = st.ship.transform.rotation * st.localRot;

            __instance.transform.position = Vector3.Lerp(__instance.transform.position, targetPos, Time.deltaTime * lerpSpeed);
            __instance.transform.rotation = Quaternion.Slerp(__instance.transform.rotation, targetRot, Time.deltaTime * lerpSpeed);
        }

     /*   [HarmonyPatch(typeof(Chair), nameof(Chair.Interact))]
        [HarmonyPostfix]
        public static void AfterChairInteract(Chair __instance, Humanoid human, bool hold, bool alt)
        {
            if (hold) return;
            if (human != Player.m_localPlayer) return;

            TrackLocalPlayer(Player.m_localPlayer);
        }

        [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.RPC_RequestRespons))]
        [HarmonyPostfix]
        public static void AfterControlGranted(ShipControlls __instance, long sender, bool granted)
        {
            if (!granted) return;
            if (!Player.m_localPlayer) return;
            if (sender != ZNet.GetUID()) return;

            TrackLocalPlayer(Player.m_localPlayer);
        }

        [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.OnUseStop))]
        [HarmonyPostfix]
        public static void AfterControlReleased(ShipControlls __instance, Player player)
        {
            if (player != Player.m_localPlayer) return;

            TrackLocalPlayer(Player.m_localPlayer);
        }*/
    }
}
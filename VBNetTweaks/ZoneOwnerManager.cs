namespace VBNetTweaks
{
    public static class ZoneOwnerManager
    {
        private class ZoneOwnerInfo
        {
            public long OwnerPeerId;
            public float LastTransferTime;
            public int LastKnownPing = -1;
            public Vector2i ZoneId;
        }

        private static readonly Dictionary<Vector2i, ZoneOwnerInfo> _owners = new();
        private static readonly Dictionary<long, List<Vector2i>> _playerZones = new();
        private static readonly object _lock = new object();

        public static ConfigEntry<bool> Enabled { get; set; }
        public static ConfigEntry<int> PingThreshold { get; set; }
        public static ConfigEntry<int> Hysteresis { get; set; }
        public static ConfigEntry<float> TransferCooldown { get; set; }
        public static ConfigEntry<float> OwnerUpdateInterval { get; set; }

        private static float _lastOwnerUpdateTime;
        private static bool _isInitialized;

        public static void Initialize()
        {
          //  if (!ZNet.instance?.IsServer() ?? true) return;

            lock (_lock)
            {
                if (_isInitialized) return;

                if (Enabled == null)
                {
                    ZLog.LogWarning("[ZoneOwnerManager] Config not initialized, using defaults");
                    return;
                }

                if (!Enabled.Value)
                {
                    ZLog.Log("[ZoneOwnerManager] Disabled via config");
                    return;
                }

                _owners.Clear();
                _playerZones.Clear();

                ZLog.Log($"[ZoneOwnerManager] Initialized with config: " +
                         $"PingThreshold={PingThreshold?.Value ?? 150}ms, " +
                         $"Hysteresis={Hysteresis?.Value ?? 20}ms, " +
                         $"TransferCooldown={TransferCooldown?.Value ?? 5f}s, " +
                         $"OwnerUpdateInterval={OwnerUpdateInterval?.Value ?? 2f}s");

                _isInitialized = true;
            }
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                _owners.Clear();
                _playerZones.Clear();
                _isInitialized = false;
            }
        }

        private static int GetPlayerPing(long peerUid)
        {
            return AdaptiveThrottler.GetPlayerPingMs(peerUid);
        }

        public static void RemovePlayer(long peerUid)
        {
            if (!_isInitialized) return;

            lock (_lock)
            {
                if (!_playerZones.TryGetValue(peerUid, out var zones)) return;

                ZLog.Log($"[ZoneOwnerManager] Player {peerUid} left, clearing {zones.Count} zones");

                foreach (var zone in zones)
                {
                    _owners.Remove(zone);
                }

                _playerZones.Remove(peerUid);
            }
        }

        public static void UpdateZoneOwnership(Vector2i zone)
        {
            if (!_isInitialized) return;

            if (!_owners.TryGetValue(zone, out var info))
            {
                AssignInitialOwner(zone);
                return;
            }

            var ownerPeer = GetPeerById(info.OwnerPeerId);
            if (ownerPeer == null || !ownerPeer.IsReady())
            {
                lock (_lock)
                {
                    _owners.Remove(zone);
                    _playerZones[info.OwnerPeerId]?.Remove(zone);
                }
                AssignInitialOwner(zone);
                return;
            }

            int ownerPing = GetPlayerPing(ownerPeer.m_uid);
            if (ownerPing <= 0)
            {
                info.LastKnownPing = ownerPing;
                return;
            }

            info.LastKnownPing = ownerPing;

            int pingThreshold = PingThreshold?.Value ?? 150;
            if (ownerPing <= pingThreshold) return;

            var players = GetPlayersInZone(zone);
            if (players.Count == 0)
            {
                lock (_lock)
                {
                    _owners.Remove(zone);
                    _playerZones[info.OwnerPeerId]?.Remove(zone);
                }
                return;
            }

            var bestCandidate = FindBestCandidate(players, info.OwnerPeerId);
            if (bestCandidate == null) return;

            int bestPing = GetPlayerPing(bestCandidate.m_uid);
            if (bestPing <= 0) return;

            int hysteresis = Hysteresis?.Value ?? 20;
            if (bestPing + hysteresis >= ownerPing) return;

            float transferCooldown = TransferCooldown?.Value ?? 5f;
            if (Time.time - info.LastTransferTime < transferCooldown) return;

            TransferZoneOwnership(zone, ownerPeer, bestCandidate, ownerPing, bestPing, info);
        }

        private static void AssignInitialOwner(Vector2i zone)
        {
            var players = GetPlayersInZone(zone);
            if (players.Count == 0) return;

            var bestCandidate = FindBestCandidate(players, 0);
            if (bestCandidate == null) return;

            int ping = GetPlayerPing(bestCandidate.m_uid);
            if (ping <= 0) return;

            lock (_lock)
            {
                var info = new ZoneOwnerInfo
                {
                    OwnerPeerId = bestCandidate.m_uid,
                    LastTransferTime = Time.time,
                    LastKnownPing = ping,
                    ZoneId = zone
                };

                _owners[zone] = info;

                if (!_playerZones.TryGetValue(bestCandidate.m_uid, out var zones))
                {
                    zones = new List<Vector2i>();
                    _playerZones[bestCandidate.m_uid] = zones;
                }
                zones.Add(zone);

                if (ModConfig.DebugEnabled.Value)
                {
                    ZLog.Log($"[ZoneOwnerManager] Initial owner for zone {zone}: {GetPeerName(bestCandidate)} (ping: {ping}ms)");
                }
            }
        }

        private static void TransferZoneOwnership(Vector2i zone, ZNetPeer oldOwner, ZNetPeer newOwner, int oldPing, int newPing, ZoneOwnerInfo info)
        {
            ZLog.Log($"[ZoneOwnerManager] Transfer zone {zone} from {GetPeerName(oldOwner)}({oldPing}ms) → {GetPeerName(newOwner)}({newPing}ms)");

            lock (_lock)
            {
                if (_playerZones.TryGetValue(oldOwner.m_uid, out var oldZones))
                {
                    oldZones.Remove(zone);
                }

                info.OwnerPeerId = newOwner.m_uid;
                info.LastTransferTime = Time.time;
                info.LastKnownPing = newPing;

                if (!_playerZones.TryGetValue(newOwner.m_uid, out var newZones))
                {
                    newZones = new List<Vector2i>();
                    _playerZones[newOwner.m_uid] = newZones;
                }
                newZones.Add(zone);
            }
        }

        private static List<ZNetPeer> GetPlayersInZone(Vector2i zone)
        {
            var list = ObjectPool.RentList<ZNetPeer>();
            var peers = ZNet.instance.GetPeers();

            foreach (var peer in peers)
            {
                if (peer?.m_socket == null || !peer.IsReady()) continue;

                Vector2i pZone = ZoneSystem.GetZone(peer.GetRefPos());
                if (pZone == zone) list.Add(peer);
            }

            return list;
        }

        private static ZNetPeer GetPeerById(long peerId)
        {
            var peers = ZNet.instance.GetPeers();
            foreach (var peer in peers)
            {
                if (peer?.m_uid == peerId && peer.IsReady()) return peer;
            }

            return null;
        }

        private static ZNetPeer FindBestCandidate(List<ZNetPeer> players, long excludePeerId)
        {
            ZNetPeer best = null;
            int bestPing = int.MaxValue;

            foreach (var p in players)
            {
                if (p.m_uid == excludePeerId) continue;

                int ping = GetPlayerPing(p.m_uid);
                if (ping > 0 && ping < bestPing)
                {
                    best = p;
                    bestPing = ping;
                }
            }

            return best;
        }

        private static string GetPeerName(ZNetPeer peer) => !string.IsNullOrEmpty(peer.m_playerName) ? peer.m_playerName : $"Peer_{peer.m_uid}";

        public static float LastOwnerUpdateTime
        {
            get => _lastOwnerUpdateTime;
            set => _lastOwnerUpdateTime = value;
        }

        public static float GetOwnerUpdateInterval() => OwnerUpdateInterval?.Value ?? 2f;

        private static ZNetPeer GetPeerByRpc(ZRpc rpc)
        {
            var peers = ZNet.instance.GetPeers();
            foreach (var peer in peers)
            {
                if (peer?.m_rpc == rpc) return peer;
            }
            return null;
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Update))]
        [HarmonyPostfix]
        static void ZoneSystemUpdatePostfix()
        {
           // if (!ZNet.instance?.IsServer() ?? true) return;

            if (Enabled == null || !Enabled.Value) return;

            if (!_isInitialized)
            {
                Initialize();
                return;
            }

            if (Time.time - _lastOwnerUpdateTime < GetOwnerUpdateInterval()) return;

            _lastOwnerUpdateTime = Time.time;

            var zones = ZoneSystem.instance?.m_zones;
            if (zones == null) return;

            var activeZones = ObjectPool.RentList<Vector2i>();
            try
            {
                foreach (var kvp in zones)
                {
                    activeZones.Add(kvp.Key);
                }

                foreach (var zone in activeZones)
                {
                    UpdateZoneOwnership(zone);
                }
            }
            finally
            {
                ObjectPool.ReturnList(activeZones);
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
        [HarmonyPostfix]
        static void ZNetOnNewConnectionPostfix(ZNet __instance, ZNetPeer peer)
        {
          //  if (!ZNet.instance?.IsServer() ?? true) return;

            if (ModConfig.DebugEnabled.Value)
            {
                ZLog.Log($"[ZoneOwnerManager] New connection from {GetPeerName(peer)}");
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_CharacterID))]
        [HarmonyPostfix]
        static void ZNetRPCCharacterIDPostfix(ZNet __instance, ZRpc rpc, ZDOID characterID)
        {
           // if (!ZNet.instance?.IsServer() ?? true) return;
            if (Enabled == null || !Enabled.Value) return;

            try
            {
                var peer = GetPeerByRpc(rpc);
                if (peer != null && ModConfig.DebugEnabled.Value)
                {
                    ZLog.Log($"[ZoneOwnerManager] Player {GetPeerName(peer)} joined world with character {characterID}");
                }
            }
            catch
            {
                // Игнорируем ошибки
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        [HarmonyPrefix]
        static void ZNetDisconnectPrefix(ZNet __instance, ZNetPeer peer)
        {
          //  if (!ZNet.instance?.IsServer() ?? true) return;
            if (Enabled == null || !Enabled.Value) return;

            RemovePlayer(peer?.m_uid ?? 0);
        }
    }
}
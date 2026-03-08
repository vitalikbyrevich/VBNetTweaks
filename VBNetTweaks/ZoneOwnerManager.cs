namespace VBNetTweaks
{
    public static class ZoneOwnerManager
    {
        private class ZoneOwnerInfo
        {
            public long OwnerPeerId;
            public float LastTransferTime;
            public int LastKnownPing = -1;
        }

        private static readonly Dictionary<Vector2i, ZoneOwnerInfo> _owners = new();
        
        public static ConfigEntry<bool> Enabled { get; set; }
        public static ConfigEntry<int> PingThreshold { get; set; }
        public static ConfigEntry<int> Hysteresis { get; set; }
        public static ConfigEntry<float> TransferCooldown { get; set; }
        public static ConfigEntry<float> OwnerUpdateInterval { get; set; }

        private static float _lastOwnerUpdateTime;

        // Инициализация
        public static void Initialize()
        {
            if (!ZNet.instance?.IsServer() ?? true) return;

            if (Enabled != null && !Enabled.Value)
            {
                ZLog.Log("[ZoneOwnerManager] Disabled via config");
                return;
            }

            ZLog.Log("[ZoneOwnerManager] Initialized with config: " +
                     $"PingThreshold={PingThreshold?.Value ?? 150}ms, " +
                     $"Hysteresis={Hysteresis?.Value ?? 20}ms, " +
                     $"TransferCooldown={TransferCooldown?.Value ?? 5f}s, " +
                     $"OwnerUpdateInterval={OwnerUpdateInterval?.Value ?? 2f}s");
        }

        // Получить пинг игрока из AdaptiveThrottler
        private static int GetPlayerPing(long peerUid)
        {
            return AdaptiveThrottler.GetPlayerPingMs(peerUid);
        }

        // Очистка данных игрока при выходе
        public static void RemovePlayer(long peerUid)
        {
        }

        public static void UpdateZoneOwnership(Vector2i zone)
        {
            if (Enabled != null && !Enabled.Value) return;

            if (!ZNet.instance?.IsServer() ?? true)
                return;

            var players = GetPlayersInZone(zone);
            if (players.Count == 0) return;

            if (!_owners.TryGetValue(zone, out var info))
            {
                SetZoneOwner(zone, players[0], null);
                return;
            }

            var ownerPeer = GetPeerById(info.OwnerPeerId);
            if (ownerPeer == null)
            {
                SetZoneOwner(zone, players[0], info);
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

        private static void SetZoneOwner(Vector2i zone, ZNetPeer newOwner, ZoneOwnerInfo existingInfo)
        {
            int ping = GetPlayerPing(newOwner.m_uid);
            
            if (existingInfo != null)
            {
                existingInfo.OwnerPeerId = newOwner.m_uid;
                existingInfo.LastTransferTime = Time.time;
                existingInfo.LastKnownPing = ping;
            }
            else
            {
                _owners[zone] = new ZoneOwnerInfo
                {
                    OwnerPeerId = newOwner.m_uid,
                    LastTransferTime = Time.time,
                    LastKnownPing = ping
                };
            }

            if (VBNetTweaks.DebugEnabled.Value)
            {
                ZLog.Log($"[ZoneOwnerManager] Initial owner for zone {zone}: {GetPeerName(newOwner)} (ping: {ping}ms)");
            }
        }

        private static void TransferZoneOwnership(Vector2i zone, ZNetPeer oldOwner, ZNetPeer newOwner, int oldPing, int newPing, ZoneOwnerInfo info)
        {
            ZLog.Log($"[ZoneOwnerManager] Transfer zone {zone} from {GetPeerName(oldOwner)}({oldPing}ms) → {GetPeerName(newOwner)}({newPing}ms)");

            info.OwnerPeerId = newOwner.m_uid;
            info.LastTransferTime = Time.time;
            info.LastKnownPing = newPing;
        }

        private static List<ZNetPeer> GetPlayersInZone(Vector2i zone)
        {
            var list = new List<ZNetPeer>();
            var peers = ZNet.instance.GetPeers();
            
            foreach (var peer in peers)
            {
                if (peer?.m_socket == null) continue;
                
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
                if (peer?.m_uid == peerId) return peer;
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
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Update))]
    public static class ZoneSystem_Update_Patch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            if (!ZNet.instance?.IsServer() ?? true)
                return;

            // Проверяем, включена ли система
            if (ZoneOwnerManager.Enabled != null && !ZoneOwnerManager.Enabled.Value) return;

            if (Time.time - ZoneOwnerManager.LastOwnerUpdateTime < ZoneOwnerManager.GetOwnerUpdateInterval()) return;

            ZoneOwnerManager.LastOwnerUpdateTime = Time.time;

            var zones = ZoneSystem.instance?.m_zones;
            if (zones == null) return;

            foreach (var kvp in zones)
            {
                ZoneOwnerManager.UpdateZoneOwnership(kvp.Key);
            }
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_CharacterID))]
    public static class ZNet_RPC_CharacterID_Patch
    {
        [HarmonyPostfix]
        static void Postfix(ZNet __instance, ZRpc rpc, ZDOID characterID)
        {
            try
            {
                if (!ZNet.instance?.IsServer() ?? true) return;

                var peer = GetPeerByRpc(rpc);
                if (peer != null)
                {
                    ZLog.Log($"[ZoneOwnerManager] Player {GetPeerName(peer)} joined world");
                }
            }
            catch { }
        }

        private static ZNetPeer GetPeerByRpc(ZRpc rpc)
        {
            var peers = ZNet.instance.GetPeers();
            foreach (var peer in peers)
            {
                if (peer?.m_rpc == rpc) return peer;
            }
            return null;
        }

        private static string GetPeerName(ZNetPeer peer)
        {
            return !string.IsNullOrEmpty(peer.m_playerName) ? peer.m_playerName : $"Peer_{peer.m_uid}";
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
    public static class ZNet_OnNewConnection_Patch
    {
        [HarmonyPostfix]
        static void Postfix(ZNet __instance, ZNetPeer peer)
        {
            if (!ZNet.instance?.IsServer() ?? true) return;

            ZoneOwnerManager.Initialize();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
    public static class ZNet_Disconnect_Patch
    {
        [HarmonyPrefix]
        static void Prefix(ZNet __instance, ZNetPeer peer)
        {
            if (!ZNet.instance?.IsServer() ?? true) return;

            ZoneOwnerManager.RemovePlayer(peer?.m_uid ?? 0);
        }
    }
}
namespace VBNetTweaks
{
    [HarmonyPatch]
    public static class PlayerCache
    {
        private static List<Player> _cachedPlayers = new();
        private static Dictionary<long, Player> _playersById = new();
        private static Dictionary<long, bool> _playerAttachedState = new();
        private static Dictionary<long, ZDOID> _playerShipMap = new();
        
        private static int _cachedFrame = -1;
        private static float _cachedTime = -1f;

        public static List<Player> GetAll() => GetCached(0.5f);
        public static List<Player> GetCurrentFrame() => GetCached(0f);
        
        public static List<Player> GetCached(float maxAgeSeconds = 0.5f)
        {
            if (Time.time - _cachedTime > maxAgeSeconds || _cachedFrame != Time.frameCount)
            {
                RefreshCache();
                _cachedTime = Time.time;
                _cachedFrame = Time.frameCount;
            }
            return _cachedPlayers;
        }

        public static Player GetById(long id)
        {
            if (_cachedFrame != Time.frameCount) RefreshCache();
                
            return _playersById.TryGetValue(id, out var player) ? player : null;
        }
        
        public static bool IsPlayerOnShip(long playerId) => _playerShipMap.ContainsKey(playerId);

        public static bool IsPlayerAttached(long playerId) => _playerAttachedState.TryGetValue(playerId, out var attached) && attached;

        public static ZDOID GetPlayerShip(long playerId) => _playerShipMap.TryGetValue(playerId, out var shipId) ? shipId : new ZDOID();

        public static void UpdatePlayerState(long playerId, bool attached, ZDOID shipId)
        {
            _playerAttachedState[playerId] = attached;
            
            if (shipId.IsNone()) _playerShipMap.Remove(playerId);
            else _playerShipMap[playerId] = shipId;
        }

        public static void RemovePlayer(long playerId)
        {
            _playersById.Remove(playerId);
            _playerAttachedState.Remove(playerId);
            _playerShipMap.Remove(playerId);
        }

        private static void RefreshCache()
        {
            _cachedPlayers.Clear();
            _playersById.Clear();
            
            var all = Player.GetAllPlayers();
            _cachedPlayers.AddRange(all);
            
            foreach (var p in all)
            {
                if (p) 
                {
                    long id = p.GetPlayerID();
                    _playersById[id] = p;
                }
            }
            
            if (ModConfig.DebugEnabled.Value && ModConfig.VerboseLogging.Value) Helper.LogVerbose($"PlayerCache refreshed: {_cachedPlayers.Count} players");
        }

        public static void Invalidate()
        {
            _cachedFrame = -1;
            _cachedTime = -1f;
        }
        
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
        [HarmonyPostfix]
        private static void OnNewConnection(ZNet __instance, ZNetPeer peer) 
        { 
            PlayerCache.Invalidate(); 
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        [HarmonyPostfix]
        private static void OnDisconnect(ZNet __instance, ZNetPeer peer)
        {
            PlayerCache.Invalidate();
            if (peer != null) PlayerCache.RemovePlayer(peer.m_uid);
        }
    }
}
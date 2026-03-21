namespace VBNetTweaks
{
    public static class AdaptiveThrottler
    {
        private static float _lastEvalTime;
        private static float _currentInterval = 0.05f; // старт по умолчанию

        private const float LowPingThresholdSec  = 0.15f; // < 150 мс — можно ускоряться
        private const float HighPingThresholdSec = 0.60f; // > 600 мс — надо замедляться

        private const float MinInterval = 0.03f;
        private const float MaxInterval = 0.10f;
        private const float EvalInterval = 1.0f; // раз в секунду пересчитываем

        public static void Update(ZNet znet, float dt)
        {
            if (!znet/* || !Helper.IsServer()*/) return;

            _lastEvalTime += dt;
            if (_lastEvalTime < EvalInterval) return;

            _lastEvalTime = 0f;

            List<ZNetPeer> peers = znet.m_peers;
            if (peers == null || peers.Count == 0)
            {
                _currentInterval = ModConfig.SendInterval?.Value ?? 0.05f;
                return;
            }

            float maxPingSec = 0f;
            int activePeers = 0;

            for (int i = 0; i < peers.Count; i++)
            {
                var peer = peers[i];
                if (peer == null) continue;

                var socket = peer.m_socket as ZSteamSocket;
                if (socket == null || !socket.IsConnected())
                    continue;

                socket.GetConnectionQuality(out _, out _, out int pingMs, out _, out _);

                if (pingMs <= 0) continue;

                float pingSec = pingMs / 1000f;
                if (pingSec > maxPingSec) maxPingSec = pingSec;

                activePeers++;
            }

            if (activePeers == 0)
            {
                _currentInterval = ModConfig.SendInterval?.Value ?? 0.05f;
                return;
            }

            float baseInterval = ModConfig.SendInterval?.Value ?? 0.05f;
            float newInterval = baseInterval;

            if (maxPingSec < LowPingThresholdSec)
            {
                newInterval = Mathf.Max(MinInterval, baseInterval * 0.7f);
            }
            else if (maxPingSec > HighPingThresholdSec)
            {
                newInterval = Mathf.Min(MaxInterval, baseInterval * 1.5f);
            }

            _currentInterval = Mathf.Clamp(newInterval, MinInterval, MaxInterval);

            if (ModConfig.DebugEnabled.Value)
            {
                Helper.LogDebug($"AdaptiveThrottler: peers={activePeers} maxPing={maxPingSec:0.000}s " + $"base={baseInterval:0.000}s -> interval={_currentInterval:0.000}s");
            }
        }

        public static int GetPlayerPingMs(long peerUid)
        {
            var peer = GetPeerByUid(peerUid);
            if (peer == null)
                return -1;

            var socket = peer.m_socket as ZSteamSocket;
            if (socket == null || !socket.IsConnected()) return -1;

            socket.GetConnectionQuality(out _, out _, out int pingMs, out _, out _);
            return pingMs > 0 ? pingMs : -1;
        }

        private static ZNetPeer GetPeerByUid(long uid)
        {
            var peers = ZNet.instance?.GetPeers();
            if (peers == null) return null;

            foreach (var peer in peers)
            {
                if (peer?.m_uid == uid) return peer;
            }

            return null;
        }
        public static float GetInterval(float fallback) => _currentInterval > 0f ? _currentInterval : fallback;
    }
}

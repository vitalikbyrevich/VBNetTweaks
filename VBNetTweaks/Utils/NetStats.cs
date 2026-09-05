namespace VBNetTweaks.Utils
{
    public static class NetStats
    {
        // Отправка
        public static long SendOkPackets;
        public static long SendEmpty;
        public static long SkipQueueFull;
        public static long SkipLowSpace;
        public static long ZdoSerialized;
        public static long ZdoDeferred;
        public static long SteamSendFail;   // инкрементируется в Replacement_Server/Client

        // Приём
        public static long ZdoDataIn;
        public static long ZdoApplied;
        public static long ZdoUnknownHost;

        private static float _timer;
        private static readonly Dictionary<ZRpc, long[]> _peerPrev = new Dictionary<ZRpc, long[]>();

        public static bool Enabled => VBNetTweaks.c_NetStatsLogging.Value;

        public static void RemovePeer(ZRpc rpc) => _peerPrev.Remove(rpc);
        public static void ClearPeers() => _peerPrev.Clear();

        public static void ResetCounters()
        {
            SendOkPackets = SendEmpty = SkipQueueFull = SkipLowSpace = 0;
            ZdoSerialized = ZdoDeferred = SteamSendFail = 0;
            ZdoDataIn = ZdoApplied = ZdoUnknownHost = 0;
        }

        public static void Tick(float dt)
        {
            if (!Enabled) return;
            _timer += dt;
            if (_timer < VBNetTweaks.c_NetStatsInterval.Value) return;
            float secs = _timer;
            _timer = 0f;
            Print(secs);
        }

        private static void Print(float secs)
        {
            if (ZNet.instance == null) { ResetCounters(); return; }

            var sb = new System.Text.StringBuilder();
            sb.Append($"[NetStats {secs:0}s] OUT: pk={SendOkPackets} zdo={ZdoSerialized} deferred={ZdoDeferred} skipQ={SkipQueueFull} skipLow={SkipLowSpace} steamFail={SteamSendFail}");
            sb.Append($" | IN: pk={ZdoDataIn} zdo={ZdoApplied} unknownHost={ZdoUnknownHost}");

            foreach (var peer in ZNet.instance.GetPeers())
            {
                var rpc = peer?.m_rpc;
                if (rpc == null) continue;

                if (!_peerPrev.TryGetValue(rpc, out var prev))
                {
                    prev = new long[4];
                    _peerPrev[rpc] = prev;
                }
                long dPkS = rpc.m_sentPackages - prev[0];
                long dBS  = rpc.m_sentData     - prev[1];
                long dPkR = rpc.m_recvPackages - prev[2];
                long dBR  = rpc.m_recvData     - prev[3];
                prev[0] = rpc.m_sentPackages; prev[1] = rpc.m_sentData;
                prev[2] = rpc.m_recvPackages; prev[3] = rpc.m_recvData;

                int queue = peer.m_socket != null ? peer.m_socket.GetSendQueueSize() : 0;
                string name = string.IsNullOrEmpty(peer.m_playerName) ? peer.m_uid.ToString() : peer.m_playerName;
                sb.Append($"\n  peer={name} out={dPkS}pk/{dBS}B in={dPkR}pk/{dBR}B sendQueue={queue}B");
            }

            VBNetTweaks.Logger.LogWarning(sb.ToString());
            ResetCounters();
        }
    }
}
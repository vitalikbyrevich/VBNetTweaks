namespace VBNetTweaks.ZDOUtills
{
    [HarmonyPatch]
    public static class ZDONetworkOptimizer
    {
        public static class NetworkMetrics
        {
            public static int ZdosSent { get; private set; }
            public static int ZdosReceived { get; private set; }
            public static int BytesCompressed { get; private set; }
            public static int BytesOriginal { get; private set; }
            public static float AvgCompressionRatio => BytesOriginal > 0 ? (float)BytesCompressed / BytesOriginal : 1f;
            public static bool CompressionActive { get; private set; }
            public static void RecordZdoSent() => ZdosSent++;
            public static void RecordZdoReceived() => ZdosReceived++;
            public static void RecordCompression(int originalSize, int compressedSize)
            {
                BytesOriginal += originalSize;
                BytesCompressed += compressedSize;
                CompressionActive = true;
            }
    
            public static void Reset()
            {
                ZdosSent = 0;
                ZdosReceived = 0;
                BytesCompressed = 0;
                BytesOriginal = 0;
                CompressionActive = false;
            }
            
            public static string GetStats()
            {
                return $"ZDOs Sent/Recv: {ZdosSent}/{ZdosReceived}, Ratio: {AvgCompressionRatio:P1}";
            }
        }
        
        private static readonly int COMPRESSION_VERSION = 1;

        private const string RPC_VERSION = "VBNT.CompressionVersion";
        private const string RPC_ENABLED = "VBNT.CompressionEnabled";
        private const string RPC_STARTED = "VBNT.CompressionStarted";

        private static bool _firstTickLog = true;
        private static ICompressor _compressor;
        private static bool _serverMode;
        private static readonly Dictionary<ISocket, PeerCompressionStatus> _peerStatus = new();

        private class PeerCompressionStatus
        {
            public int Version { get; set; }
            public bool PeerEnabled { get; set; }
            public bool SendingCompressed { get; set; }
            public bool ReceivingCompressed { get; set; }
            public bool IsCompatible => Version == COMPRESSION_VERSION;
        }
        private static float _lastPeerCheck = 0f;
        private const float PEER_CHECK_INTERVAL = 10f;

        public static void CheckAndInitCompression()
        {
            if (!VBNetTweaks.ModuleCompression.Value) return;
            if (_compressor == null && VBNetTweaks.ModuleCompression.Value) InitCompressor();
            
            if (ZNet.instance == null) return;
            
            float now = Time.time;
            if (!(now - _lastPeerCheck > PEER_CHECK_INTERVAL)) return;
            _lastPeerCheck = now;
            
            int compatiblePeers = 0;
            int totalPeers = _peerStatus.Count;
            
            foreach (var kvp in _peerStatus)
            {
                if (kvp.Value.IsCompatible && kvp.Value.ReceivingCompressed) compatiblePeers++;
            }
            
            if (totalPeers > 0 && compatiblePeers == 0 && _compressor != null)
            {
                Helper.LogVerbose($"[Compression] ⚠️ No compatible peers! Total={totalPeers}, Compressor={_compressor.GetType().Name}");
            }
            else if (compatiblePeers > 0 && VBNetTweaks.DebugEnabled.Value)
            {
                Helper.LogDebug($"[Compression] Active: {compatiblePeers}/{totalPeers} peers using compression");
            }
        }
        
        public static string GetCompressionStatus()
        {
            if (_compressor == null) return "NOT INITIALIZED";
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Compressor: {_compressor.GetType().Name}");
            sb.AppendLine($"Server Mode: {_serverMode}");
            sb.AppendLine($"Active Peers: {_peerStatus.Count}");
            
            int compatible = 0, sending = 0, receiving = 0;
            foreach (var status in _peerStatus.Values)
            {
                if (status.IsCompatible) compatible++;
                if (status.SendingCompressed) sending++;
                if (status.ReceivingCompressed) receiving++;
            }
            
            sb.AppendLine($"Compatible: {compatible}, Sending: {sending}, Receiving: {receiving}");
            sb.AppendLine($"Metrics: {NetworkMetrics.ZdosSent} sent, {NetworkMetrics.ZdosReceived} received");
            sb.AppendLine($"Compression ratio: {NetworkMetrics.AvgCompressionRatio:P1}");
            
            return sb.ToString();
        }
        
        private static void InitCompressor()
        {
            try
            {
                int level = VBNetTweaks.CompressionLevel.Value;
                var algo = VBNetTweaks.m_CompressionAlgorithm.Value;

                switch (algo)
                {
                    case CompressionAlgorithm.Deflate:
                        _compressor = new DeflateCompressor(level);
                        ZLog.LogWarning($"Using Deflate compressor (level {level})");
                        break;
                
                    case CompressionAlgorithm.Vanilla:
                        _compressor = new VanillaCompressor();
                        ZLog.LogWarning($"Using Vanilla (native) compressor");
                        break;
                }
            }
            catch (Exception e)
            {
                ZLog.LogError($"Compression init failed: {e.Message}, falling back to NO compression");
                _compressor = null;
            }
        }

        public static void Initialize()
        {
            if (!VBNetTweaks.ModuleCompression.Value) return;
    
            _serverMode = Helper.IsServer();
    
            if (_serverMode)
            {
                InitCompressor();
                ZLog.LogWarning("Compression initialized in SERVER mode");
            }
            else
            {
                if (VBNetTweaks.EnableClientCompression.Value)
                {
                    InitCompressor();
                    ZLog.LogWarning("Compression initialized in CLIENT mode");
                }
                else ZLog.LogWarning("Compression disabled on client");
            }
        }
    
        public static bool ShouldCompressSend(ISocket socket)
        {
            if (_serverMode) return _compressor != null;
            if (!_peerStatus.TryGetValue(socket, out var peerStatus)) return false;
            return peerStatus.SendingCompressed && _compressor != null;
        }
    
        public static bool ShouldCompressReceive(ISocket socket)
        {
            if (!_peerStatus.TryGetValue(socket, out var peerStatus)) return false;
            if (!_serverMode && peerStatus.ReceivingCompressed) return true;
            return peerStatus.ReceivingCompressed && _compressor != null;
        }
        
        public static byte[] Compress(byte[] data) 
        {
            if (_compressor == null) return data;
    
            try 
            {
                var result = _compressor.Compress(data);
                NetworkMetrics.RecordCompression(data.Length, result.Length);
                return result;
            }
            catch (Exception e)
            {
                ZLog.LogError($"Compression failed: {e.Message}");
                return data;
            }
        }
        
        public static byte[] Decompress(byte[] data)
        {
            if (_compressor == null) return data;
    
            try 
            {
                return _compressor.Decompress(data);
            }
            catch (Exception e)
            {
                ZLog.LogError($"Decompression failed: {e.Message}");
                return data;
            }
        }

        public static void OptimizedSendZDOToPeers(ZDOMan zdoManager, float dt)
        {
            try
            {
                int peerCount = zdoManager.m_peers.Count;
                if (peerCount <= 0) return;

                zdoManager.m_sendTimer += dt;
        
                float sendInterval = VBNetTweaks.SendInterval?.Value ?? 0.05f;
                if (zdoManager.m_sendTimer < sendInterval) return;

                zdoManager.m_sendTimer = 0f;
                int startPeer = Mathf.Max(zdoManager.m_nextSendPeer, 0);
                int peersPerUpdate = VBNetTweaks.PeersPerUpdate?.Value ?? 40;
        
                if (_firstTickLog)
                {
                    _firstTickLog = false;
                    ZLog.LogWarning($"[VBNetTweaks] ️ ZDO Tick Started -> Interval: {sendInterval:F3}s | Peers/Update: {peersPerUpdate} | ActivePeers: {peerCount}");
                }

                int processed = 0;
                for (int i = 0; i < Mathf.Min(peersPerUpdate, peerCount); i++)
                {
                    int peerIndex = (startPeer + i) % peerCount;
                    var peer = zdoManager.m_peers[peerIndex];
                    if (peer?.m_peer?.m_socket?.IsConnected() != true) continue;

                    PerformanceMonitor.Track("SendZDOs", () => {
                        zdoManager.SendZDOs(peer, flush: false);
                        NetworkMetrics.RecordZdoSent();
                    });
                    processed++;
                }
                zdoManager.m_nextSendPeer = (startPeer + processed) % peerCount;
            }
            catch (Exception ex)
            {
                ZLog.LogError($"[VBNetTweaks] ERROR in OptimizedSendZDOToPeers: {ex.Message}");
                zdoManager.SendZDOToPeers2(dt);
            }
        }

        private static void RegisterCompressionRPCs(ZNetPeer peer)
        {
            peer.m_rpc.Register<int>(RPC_VERSION, (rpc, version) => RPC_CompressionVersion(peer, version));
            peer.m_rpc.Register<bool>(RPC_ENABLED, (rpc, enabled) => RPC_CompressionEnabled(peer, enabled));
            peer.m_rpc.Register<bool>(RPC_STARTED, (rpc, started) => RPC_CompressionStarted(peer, started));
        }

        private static void SendCompressionVersion(ZNetPeer peer)
        {
            peer.m_rpc.Invoke(RPC_VERSION, COMPRESSION_VERSION);
        }

        private static void RPC_CompressionVersion(ZNetPeer peer, int version)
        {
            if (!_peerStatus.TryGetValue(peer.m_socket, out var peerStatus)) return;

            peerStatus.Version = version;

            if (peerStatus.IsCompatible)
            {
                ZLog.LogWarning($"Compression compatible with {GetPeerName(peer)}");
                SendCompressionEnabledStatus(peer);
            }
        }

        private static void SendCompressionEnabledStatus(ZNetPeer peer)
        {
            bool enabled = VBNetTweaks.ModuleCompression.Value;
            peer.m_rpc.Invoke(RPC_ENABLED, enabled);
        }

        private static void RPC_CompressionEnabled(ZNetPeer peer, bool enabled)
        {
            if (!_peerStatus.TryGetValue(peer.m_socket, out var peerStatus)) return;

            peerStatus.PeerEnabled = enabled;

            bool shouldCompress = VBNetTweaks.ModuleCompression.Value;
    
            if (!Helper.IsServer())
            {
                shouldCompress = shouldCompress && VBNetTweaks.EnableClientCompression.Value;
            }
    
            shouldCompress = shouldCompress && enabled && peerStatus.IsCompatible;
    
            SendCompressionStarted(peer, shouldCompress);
        }
        
        private static void SendCompressionStarted(ZNetPeer peer, bool started)
        {
            if (!_peerStatus.TryGetValue(peer.m_socket, out var peerStatus)) return;
        
            if (peerStatus.SendingCompressed == started) return;

            peer.m_rpc.Invoke(RPC_STARTED, started);
    
            var socketType = peer.m_socket.GetType();
            var flushMethod = socketType.GetMethod("Flush", Type.EmptyTypes);
    
            if (flushMethod != null && flushMethod.DeclaringType != typeof(object))
            {
                try
                {
                    peer.m_socket.Flush();
                }
                catch (Exception e)
                {
                    ZLog.LogError($"Error flushing socket: {e.Message}");
                }
            }
    
            peerStatus.SendingCompressed = started;
            ZLog.LogWarning($"Compression {(started ? "started" : "stopped")} with {GetPeerName(peer)}");
        }

        private static void RPC_CompressionStarted(ZNetPeer peer, bool started)
        {
            if (!_peerStatus.TryGetValue(peer.m_socket, out var peerStatus)) return;

            peerStatus.ReceivingCompressed = started;
            ZLog.LogWarning($"Receiving {(started ? "compressed" : "uncompressed")} from {GetPeerName(peer)}");
        }
        
        private static string GetPeerName(ZNetPeer peer)
        {
            try
            {
                return peer.m_socket?.GetEndPointString() ?? peer.m_uid.ToString();
            }
            catch
            {
                return peer.m_uid.ToString();
            }
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update))]
        private static IEnumerable<CodeInstruction> ZDOManUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions).Start();
            matcher.MatchStartForward(new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(ZDOMan), "SendZDOToPeers2")));

            if (matcher.IsInvalid)
            {
                ZLog.LogError("WARNING: SendZDOToPeers2 not found");
                return instructions;
            }

            matcher.SetOperandAndAdvance(AccessTools.Method(typeof(ZDONetworkOptimizer), nameof(OptimizedSendZDOToPeers)));
            return matcher.InstructionEnumeration();
            
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.RemoveObjects))]
        static bool RemoveObjectsPrefix(ZNetScene __instance, List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects)
        {
            try
            {
                PerformanceMonitor.Track("RemoveObjects", () =>
                {
                    if (currentNearObjects == null || currentDistantObjects == null) return;
                    ZDORemoval.OptimizedRemoveObjects(__instance, currentNearObjects, currentDistantObjects);
                });
                return false;
            }
            catch
            {
                return true;
            }
            
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
        [HarmonyPostfix]
        static void OnNewConnection(ZNet __instance, ZNetPeer peer)
        {
            if (_compressor == null) return;

            _peerStatus[peer.m_socket] = new PeerCompressionStatus();
            RegisterCompressionRPCs(peer);
            SendCompressionVersion(peer);
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        [HarmonyPostfix]
        static void OnDisconnect(ZNet __instance, ZNetPeer peer)
        {
            _peerStatus.Remove(peer.m_socket);
        }
    }
}

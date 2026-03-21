namespace VBNetTweaks.ZDOUtills
{
    [HarmonyPatch]
    public static class ZDONetworkOptimizer
    {
        private const int COMPRESSION_VERSION = 1;
        
        private static readonly int HashAI = "ai".GetStableHashCode();

        private const string RPC_VERSION = "VBNT.CompressionVersion";
        private const string RPC_ENABLED = "VBNT.CompressionEnabled";
        private const string RPC_STARTED = "VBNT.CompressionStarted";

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

        public static void Initialize()
        {
            try
            {
                if (!ModConfig.ModuleCompression.Value) return;

                _serverMode = Helper.IsServer();
        
                if (CompressionController.Algorithm == null || CompressionController.Level == null)
                {
                    Helper.LogDebug("CompressionController не инициализирован, пропускаем");
                    return;
                }

                CompressionController.Initialize();

                Helper.LogDebug($"Compression system initialized in {(_serverMode ? "SERVER" : "CLIENT")} mode");
            }
            catch (Exception e)
            {
                Helper.LogDebug($"Error in ZDONetworkOptimizer.Initialize: {e.Message}");
            }
        }

        public static void Shutdown()
        {
            CompressionController.Shutdown();
            _peerStatus.Clear();
        }

        public static bool ShouldCompressSend(ISocket socket)
        {
          //  if (_serverMode) return true;
    
            if (!_peerStatus.TryGetValue(socket, out var peerStatus)) return false;
            return peerStatus.SendingCompressed;
        }

        public static bool ShouldCompressReceive(ISocket socket)
        {
            if (!_peerStatus.TryGetValue(socket, out var peerStatus)) return false;
            if (/*!_serverMode &&*/ peerStatus.ReceivingCompressed) return true;
            return peerStatus.ReceivingCompressed;
        }

        public static byte[] Decompress(ISocket socket, byte[] data, CompressionController.DataType dataType)
        {
            return CompressionController.Decompress(socket, data, dataType);
        }

        public static byte[] Compress(ISocket socket, byte[] data, CompressionController.DataType dataType)
        {
            return CompressionController.Compress(socket, data, dataType);
        }

        public static void OptimizedSendZDOToPeers(ZDOMan zdoManager, float dt)
        {
            try
            {
                int peerCount = zdoManager.m_peers.Count;
                if (peerCount <= 0) return;

                zdoManager.m_sendTimer += dt;
                float sendInterval = Helper.GetEffectiveSendInterval();
                if (zdoManager.m_sendTimer < sendInterval) return;
                zdoManager.m_sendTimer = 0f;

                int startPeer = Mathf.Max(zdoManager.m_nextSendPeer, 0);
                int peersPerUpdate = Helper.GetPeersPerUpdate();
                int processed = 0;

                for (int i = 0; i < Mathf.Min(peersPerUpdate, peerCount); i++)
                {
                    int peerIndex = (startPeer + i) % peerCount;
                    var peer = zdoManager.m_peers[peerIndex];
                    if (peer?.m_peer?.m_socket?.IsConnected() != true) continue;

                    if (/*Helper.IsServer() &&*/ ModConfig.ModuleZDOThrottling.Value) ZDOThrottling.ApplyZDOThrottle(zdoManager, peer);

                    PerformanceMonitor.Track("SendZDOs", () => { zdoManager.SendZDOs(peer, flush: false); });

                    processed++;
                }

                zdoManager.m_nextSendPeer = (startPeer + processed) % peerCount;
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"ERROR in OptimizedSendZDOToPeers: {ex.Message}");
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
                Helper.LogDebug($"Compression compatible with {GetPeerName(peer)}");
                SendCompressionEnabledStatus(peer);
            }
        }

        private static void SendCompressionEnabledStatus(ZNetPeer peer)
        {
            bool enabled = ModConfig.ModuleCompression.Value;
            peer.m_rpc.Invoke(RPC_ENABLED, enabled);
        }

        private static void RPC_CompressionEnabled(ZNetPeer peer, bool enabled)
        {
            if (!_peerStatus.TryGetValue(peer.m_socket, out var peerStatus)) return;

            peerStatus.PeerEnabled = enabled;

            bool shouldCompress = ModConfig.ModuleCompression.Value && enabled && peerStatus.IsCompatible;

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
                    Helper.LogDebug($"Error flushing socket: {e.Message}");
                }
            }

            peerStatus.SendingCompressed = started;
            Helper.LogDebug($"Compression {(started ? "started" : "stopped")} with {GetPeerName(peer)}");
        }

        private static void RPC_CompressionStarted(ZNetPeer peer, bool started)
        {
            if (!_peerStatus.TryGetValue(peer.m_socket, out var peerStatus)) return;

            peerStatus.ReceivingCompressed = started;
            Helper.LogDebug($"Receiving {(started ? "compressed" : "uncompressed")} from {GetPeerName(peer)}");
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
                Helper.LogDebug("WARNING: SendZDOToPeers2 not found");
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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.RPC_ZDOData))]
        static bool ZDOMan_RPC_ZDOData_Prefix(ZRpc rpc, ZPackage pkg)
        {
            if (!ModConfig.ModuleCompression.Value) return true;

            try
            {
                var socket = rpc.GetSocket();
                var data = pkg.GetArray();

                if (ShouldCompressReceive(socket))
                {
                    var decompressed = Decompress(socket, data, CompressionController.DataType.ZDO);

                    if (decompressed.Length != data.Length)
                    {
                        var newPkg = new ZPackage(decompressed);

                        var method = typeof(ZDOMan).GetMethod("RPC_ZDOData", BindingFlags.NonPublic | BindingFlags.Instance);
                        method?.Invoke(ZDOMan.instance, new object[] { rpc, newPkg });

                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                Helper.LogDebug($"Error in ZDO decompression: {e.Message}");
            }

            return true;
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
        [HarmonyPostfix]
        static void OnNewConnection(ZNet __instance, ZNetPeer peer)
        {
            if (!ModConfig.ModuleCompression.Value) return;

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
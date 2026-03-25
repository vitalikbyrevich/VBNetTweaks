namespace VBNetTweaks.ZDOUtills
{
    [HarmonyPatch]
    public static class ZDONetworkOptimizer
    {
        private const int COMPRESSION_VERSION = 1;
        
        private static readonly int HashAI = "ai".GetStableHashCode();
        private static readonly List<ZPackage> _zdoBuffer = new();

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
        
        private static Dictionary<ZDO, float> _lastUpdateTime = new();
        private static Dictionary<ZDO, Vector3> _lastPos = new();
        
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
    
        public static bool ShouldUpdatePosition(ZDO zdo, Vector3 newPos, float cullSize)
        {
            if (!_lastPos.TryGetValue(zdo, out var lastPos)) 
            {
                _lastPos[zdo] = newPos;
                return true;
            }
        
            float sqrDist = (newPos - lastPos).sqrMagnitude;
            if (sqrDist < cullSize * cullSize) return false;
        
            _lastPos[zdo] = newPos;
            return true;
        }

        public static bool ShouldCompressSend(ISocket socket)
        {
            if (!_peerStatus.TryGetValue(socket, out var peerStatus)) return false;
            
            // Отправляем сжатые данные, если:
            // 1. Пир поддерживает компрессию (версия совместима)
            // 2. Компрессия включена на пире
            // 3. Мы договорились использовать компрессию
            return peerStatus.IsCompatible && peerStatus.PeerEnabled && peerStatus.SendingCompressed;
        }

        public static bool ShouldCompressReceive(ISocket socket)
        {
            if (!_peerStatus.TryGetValue(socket, out var peerStatus)) return false;
            
            // Получаем сжатые данные, если пир отправляет их сжатыми
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

        private static CompressionController.DataType DetectDataType(byte[] data)
        {
            if (data.Length < 4) return CompressionController.DataType.Unknown;
            
            // Простая эвристика для определения ZDO пакетов
            // ZDO пакеты обычно начинаются с количества ZDO (небольшое число)
            // и содержат много данных
            try
            {
                int possibleCount = BitConverter.ToInt32(data, 0);
                
                // ZDO пакеты: разумное количество объектов и большой размер
                if (possibleCount > 0 && possibleCount < 2000 && data.Length > 100)
                {
                    return CompressionController.DataType.ZDO;
                }
            }
            catch
            {
                // Игнорируем
            }
            
            return CompressionController.DataType.Unknown;
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

                    if (ModConfig.ModuleZDOThrottling.Value) 
                        ZDOThrottling.ApplyZDOThrottle(zdoManager, peer);

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

            // Устанавливаем флаг отправки сжатых данных
            peerStatus.SendingCompressed = started;
            
            Helper.LogDebug($"Compression {(started ? "started" : "stopped")} with {GetPeerName(peer)}");
        }

        private static void RPC_CompressionStarted(ZNetPeer peer, bool started)
        {
            if (!_peerStatus.TryGetValue(peer.m_socket, out var peerStatus)) return;

            // Устанавливаем флаг получения сжатых данных
            peerStatus.ReceivingCompressed = started;
            
            // Если мы клиент, также устанавливаем флаг отправки (для двусторонней компрессии)
            if (!Helper.IsServer())
            {
                peerStatus.SendingCompressed = started;
            }
            
            Helper.LogDebug($"Compression {(started ? "enabled" : "disabled")} for {GetPeerName(peer)} (Sending: {peerStatus.SendingCompressed}, Receiving: {peerStatus.ReceivingCompressed})");
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

        // Используем ZSteamSocket.Send вместо ZRpc.SendPackage
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.Send))]
        static bool ZSteamSocket_Send_Prefix(ZSteamSocket __instance, ZPackage pkg)
        {
            if (!ModConfig.ModuleCompression.Value) return true;

            var socket = __instance as ISocket;
            if (socket == null) return true;
            
            if (!ShouldCompressSend(socket)) return true;

            var data = pkg.GetArray();
            var dataType = DetectDataType(data);
            
            // Сжимаем только ZDO данные
            if (dataType == CompressionController.DataType.ZDO)
            {
                var compressed = Compress(socket, data, dataType);

                if (compressed.Length < data.Length)
                {
                    var compressedPkg = new ZPackage(compressed);
                    __instance.Send(compressedPkg);
                    return false; // Пропускаем оригинальный вызов
                }
            }

            return true; // Отправляем оригинальный пакет
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
        [HarmonyPostfix]
        static void OnNewConnection(ZNet __instance, ZNetPeer peer)
        {
            if (!ModConfig.ModuleCompression.Value) return;

            _peerStatus[peer.m_socket] = new PeerCompressionStatus();
            RegisterCompressionRPCs(peer);
            SendCompressionVersion(peer);
            
            // Для клиента: буферизация ZDO пакетов до инициализации ZDOMan
            if (!Helper.IsServer())
            {
                peer.m_rpc.Register("ZDOData", delegate(ZRpc _, ZPackage package)
                {
                    lock (_zdoBuffer) _zdoBuffer.Add(package);
                });
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        [HarmonyPostfix]
        static void OnDisconnect(ZNet __instance, ZNetPeer peer)
        {
            _peerStatus.Remove(peer.m_socket);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
        private static void ClearBufferOnShutdown()
        {
            lock (_zdoBuffer) _zdoBuffer.Clear();
            _peerStatus.Clear();
            _lastPos.Clear();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.AddPeer))]
        private static void ParseBufferedZPackages(ZDOMan __instance, ZNetPeer netPeer)
        {
            if (Helper.IsServer()) return;
            
            if (_zdoBuffer.Count == 0) return;
            
            List<ZPackage> bufferCopy;
            lock (_zdoBuffer)
            {
                bufferCopy = new List<ZPackage>(_zdoBuffer);
                _zdoBuffer.Clear();
            }
            
            foreach (ZPackage item in bufferCopy)
            {
                try
                {
                    __instance.RPC_ZDOData(netPeer.m_rpc, item);
                }
                catch (Exception e)
                {
                    Helper.LogDebug($"Error parsing buffered ZDO: {e.Message}");
                }
            }
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZDO), nameof(ZDO.Set), typeof(int), typeof(Vector3))]
        public static bool Set_Prefix(ZDO __instance, int hash, Vector3 value)
        {
            if (Helper.IsServer()) return true;
            if (!ModConfig.ModuleZDOThrottling.Value) return true;
            if (__instance.IsOwner()) return true;
    
            return ShouldUpdatePosition(__instance, value, 0.05f);
        }
    }
}
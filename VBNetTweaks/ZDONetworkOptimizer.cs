namespace VBNetTweaks
{
    [HarmonyPatch]
    public static class ZDONetworkOptimizer
    {
        private static readonly Dictionary<ZDO, float> _distanceCache = new();
        private static Vector3 _cachedRefPos;
        private static int _cachedFrame = -1;
        
        private const int MaxZDOsPerTick = 350;
        private const float MobNearDistance = 40f;
        private const float MobMediumDistance = 100f;
        private const float ImportantObjectDistance = 200f;

        private static readonly int PlayerPrefab = "Player".GetStableHashCode();
        private static readonly HashSet<int> ShipPrefabs = new()
        {
            "Karve".GetStableHashCode(),
            "VikingShip".GetStableHashCode(),
            "Raft".GetStableHashCode(),
            "VikingShip_Ashlands".GetStableHashCode()
        };

        private static readonly HashSet<int> ImportantPrefabs = new()
        {
            "portal_wood".GetStableHashCode(),
            "portal_stone".GetStableHashCode(),
            "piece_workbench".GetStableHashCode(),
            "piece_bed".GetStableHashCode(),
            "piece_chest".GetStableHashCode()
        };

        private static readonly int HashAI = "ai".GetStableHashCode();

        private const int COMPRESSION_VERSION = 1;
        private const string RPC_VERSION = "VBNT.CompressionVersion";
        private const string RPC_ENABLED = "VBNT.CompressionEnabled";
        private const string RPC_STARTED = "VBNT.CompressionStarted";

        private static ICompressor _compressor;
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
            if (VBNetTweaks.EnableNetworkCompression.Value)
            {
                try
                {
                    string algo = VBNetTweaks.CompressionAlgorithm.Value;
                    if (algo.Equals("Zstd", StringComparison.OrdinalIgnoreCase))
                    {
                        _compressor = new ZstdCompressor();
                        VBNetTweaks.LogDebug("Using Zstd compressor");
                    }
                    else
                    {
                        _compressor = new DeflateCompressor();
                        VBNetTweaks.LogDebug("Using Deflate compressor");
                    }
                }
                catch (Exception e)
                {
                    VBNetTweaks.LogDebug($"Compression init failed: {e.Message}, using Deflate fallback");
                    try
                    {
                        _compressor = new DeflateCompressor();
                    }
                    catch { }
                }
            }
        }

        public static void OptimizedSendZDOToPeers(ZDOMan zdoManager, float dt)
        {
            try
            {
                int peerCount = zdoManager.m_peers.Count;
                if (peerCount <= 0) return;

                zdoManager.m_sendTimer += dt;
                float sendInterval = VBNetTweaks.GetEffectiveSendInterval();

                if (zdoManager.m_sendTimer < sendInterval) return;
                zdoManager.m_sendTimer = 0f;

                int startPeer = Mathf.Max(zdoManager.m_nextSendPeer, 0);
                int peersPerUpdate = VBNetTweaks.GetPeersPerUpdate();
                int processed = 0;

                for (int i = 0; i < Mathf.Min(peersPerUpdate, peerCount); i++)
                {
                    int peerIndex = (startPeer + i) % peerCount;
                    var peer = zdoManager.m_peers[peerIndex];

                    if (peer?.m_peer?.m_socket?.IsConnected() != true) continue;

                    if (Helper.IsServer() && VBNetTweaks.EnableZDOThrottling.Value)
                        ApplyZDOThrottle(zdoManager, peer);

                    PerformanceMonitor.Track("SendZDOs", () => zdoManager.SendZDOs(peer, flush: false));

                    processed++;
                }

                zdoManager.m_nextSendPeer = (startPeer + processed) % peerCount;
            }
            catch (Exception ex)
            {
                VBNetTweaks.LogDebug($"ERROR in OptimizedSendZDOToPeers: {ex.Message}");
                zdoManager.SendZDOToPeers2(dt);
            }
        }

        private static void ApplyZDOThrottle(ZDOMan zdoManager, ZDOMan.ZDOPeer peer)
        {
            List<ZDO> near = null;
            List<ZDO> distant = null;

            try
            {
                Vector3 refPos = peer.m_peer.GetRefPos();
                Vector2i zone = ZoneSystem.GetZone(refPos);

                near = ObjectPool.RentList<ZDO>();
                distant = ObjectPool.RentList<ZDO>();

                int activeArea = ZoneSystem.instance?.m_activeArea ?? 3;
                int distantArea = ZoneSystem.instance?.m_activeDistantArea ?? 5;

                zdoManager.FindSectorObjects(zone, activeArea, distantArea, near, distant);

                float throttleDist = VBNetTweaks.ZDOThrottleDistance.Value;

                foreach (var z in near)
                {
                    float d = Vector3.Distance(z.GetPosition(), refPos);
                    z.m_tempSortValue = d - 150f;
                }

                foreach (var z in distant)
                {
                    float d = Vector3.Distance(z.GetPosition(), refPos);
                    z.m_tempSortValue = d + 150f;
                    if (d > throttleDist * 2f) z.m_tempSortValue += 300f;
                }
            }
            finally
            {
                if (near != null) ObjectPool.ReturnList(near);
                if (distant != null) ObjectPool.ReturnList(distant);
            }
        }

        private static float GetDistance(ZDO zdo, Vector3 refPos)
        {
            if (_distanceCache.TryGetValue(zdo, out float d)) return d;

            d = Vector3.Distance(zdo.GetPosition(), refPos);
            _distanceCache[zdo] = d;
            return d;
        }

        private static bool IsMob(ZDO zdo) => zdo.GetInt(HashAI, -1) != -1;

        private static void OptimizedRemoveObjects(ZNetScene scene, List<ZDO> near, List<ZDO> distant)
        {
            byte mark = (byte)(Time.frameCount & 255);

            foreach (var z in near) if (z != null) z.TempRemoveEarmark = mark;
            foreach (var z in distant) if (z != null) z.TempRemoveEarmark = mark;

            var instances = scene.m_instances;
            var tempRemoved = scene.m_tempRemoved;

            tempRemoved.Clear();

            var keys = new List<ZDO>(instances.Keys);

            foreach (var zdo in keys)
            {
                if (zdo == null || !instances.TryGetValue(zdo, out var view) || view == null)
                {
                    instances.Remove(zdo);
                    continue;
                }

                if (zdo.TempRemoveEarmark != mark) tempRemoved.Add(view);
            }

            foreach (var view in tempRemoved)
            {
                if (!view) continue;

                var zdo = view.m_zdo;
                if (zdo != null)
                {
                    zdo.Created = false;
                    view.m_zdo = null;
                }

                UnityEngine.Object.Destroy(view.gameObject);
                instances.Remove(zdo);
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
            if (!_peerStatus.TryGetValue(peer.m_socket, out var status)) return;

            status.Version = version;

            if (status.IsCompatible)
            {
                VBNetTweaks.LogDebug($"Compression compatible with {GetPeerName(peer)}");
                SendCompressionEnabledStatus(peer);
            }
        }

        private static void SendCompressionEnabledStatus(ZNetPeer peer)
        {
            bool enabled = VBNetTweaks.EnableNetworkCompression.Value;
            peer.m_rpc.Invoke(RPC_ENABLED, enabled);
        }

        private static void RPC_CompressionEnabled(ZNetPeer peer, bool enabled)
        {
            if (!_peerStatus.TryGetValue(peer.m_socket, out var status)) return;

            status.PeerEnabled = enabled;

            bool shouldCompress = VBNetTweaks.EnableNetworkCompression.Value && enabled && status.IsCompatible;
            SendCompressionStarted(peer, shouldCompress);
        }

        private static void SendCompressionStarted(ZNetPeer peer, bool started)
        {
            if (!_peerStatus.TryGetValue(peer.m_socket, out var status)) return;
            if (status.SendingCompressed == started) return;

            peer.m_rpc.Invoke(RPC_STARTED, started);
            peer.m_socket.Flush();
            status.SendingCompressed = started;

            VBNetTweaks.LogDebug($"Compression {(started ? "started" : "stopped")} with {GetPeerName(peer)}");
        }

        private static void RPC_CompressionStarted(ZNetPeer peer, bool started)
        {
            if (!_peerStatus.TryGetValue(peer.m_socket, out var status)) return;

            status.ReceivingCompressed = started;
            VBNetTweaks.LogDebug($"Receiving {(started ? "compressed" : "uncompressed")} from {GetPeerName(peer)}");
        }

        public static bool ShouldCompressSend(ISocket socket) => _peerStatus.TryGetValue(socket, out var status) && status.SendingCompressed && _compressor != null;

        public static bool ShouldCompressReceive(ISocket socket) => _peerStatus.TryGetValue(socket, out var status) && status.ReceivingCompressed && _compressor != null;

        public static byte[] Compress(byte[] data) => _compressor?.Compress(data) ?? data;

        public static byte[] Decompress(byte[] data) => _compressor?.Decompress(data) ?? data;

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
                VBNetTweaks.LogDebug("WARNING: SendZDOToPeers2 not found");
                return instructions;
            }

            matcher.SetOperandAndAdvance(AccessTools.Method(typeof(ZDONetworkOptimizer), nameof(OptimizedSendZDOToPeers)));
            return matcher.InstructionEnumeration();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSortSendZDOS))]
        public static void ApplyWeights(List<ZDO> objects, Vector3 refPos)
        {
            if (!Helper.IsServer()) return;
            if (!VBNetTweaks.EnablePlayerPositionBoost.Value) return;

            if (_cachedFrame != Time.frameCount || _cachedRefPos != refPos)
            {
                _distanceCache.Clear();
                _cachedRefPos = refPos;
                _cachedFrame = Time.frameCount;
            }

            foreach (var zdo in objects)
            {
                if (zdo == null) continue;

                int prefab = zdo.GetPrefab();

                if (prefab == PlayerPrefab)
                {
                    zdo.m_tempSortValue -= 500f;
                    continue;
                }

                if (ShipPrefabs.Contains(prefab))
                {
                    bool hasPlayers = ShipSyncSystem.ShipHasPlayers(zdo.m_uid);
                    zdo.m_tempSortValue += hasPlayers ? -450f : -200f;
                    continue;
                }

                float distance = GetDistance(zdo, refPos);

                if (IsMob(zdo))
                {
                    if (distance < MobNearDistance) zdo.m_tempSortValue -= 300f;
                    else if (distance < MobMediumDistance) zdo.m_tempSortValue -= 150f;
                    else zdo.m_tempSortValue += distance;
                    continue;
                }

                if (ImportantPrefabs.Contains(prefab) && distance < ImportantObjectDistance) zdo.m_tempSortValue -= 150f;
                else zdo.m_tempSortValue += distance;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSortSendZDOS))]
        public static void LimitZDOs(List<ZDO> objects)
        {
            if (!Helper.IsServer()) return;
            if (objects.Count <= MaxZDOsPerTick) return;

            objects.Sort((a, b) => a.m_tempSortValue.CompareTo(b.m_tempSortValue));
            int removeCount = objects.Count - MaxZDOsPerTick;
            objects.RemoveRange(MaxZDOsPerTick, removeCount);

            if (VBNetTweaks.DebugEnabled.Value) VBNetTweaks.LogVerbose($"ZDO limiter: removed {removeCount} objects");
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.RemoveObjects))]
        static bool RemoveObjectsPrefix(ZNetScene __instance, List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects)
        {
            try
            {
                PerformanceMonitor.Track("RemoveObjects", () =>
                {
                    OptimizedRemoveObjects(__instance, currentNearObjects, currentDistantObjects);
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
        static void OnNewConnection(ZNetPeer peer)
        {
            if (_compressor == null) return;

            _peerStatus[peer.m_socket] = new PeerCompressionStatus();
            RegisterCompressionRPCs(peer);
            SendCompressionVersion(peer);
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        [HarmonyPostfix]
        static void OnDisconnect(ZNetPeer peer)
        {
            _peerStatus.Remove(peer.m_socket);
        }
    }
}
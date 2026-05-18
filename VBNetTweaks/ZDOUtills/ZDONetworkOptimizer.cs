using VBNetTweaks.CompressionUtills;

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
            
            public static void RecordZdoSent() => ZdosSent++;
            public static void RecordZdoReceived() => ZdosReceived++;
            public static void RecordCompression(int original, int compressed) 
            { 
                BytesOriginal += original; 
                BytesCompressed += compressed; 
            }
            public static void Reset()
            {
                ZdosSent = 0;
                ZdosReceived = 0;
                BytesCompressed = 0;
                BytesOriginal = 0;
            }
        }

        private static ICompressor _compressor;
        private static bool _serverMode;
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
    
            if (!VBNetTweaks.ModuleCompression.Value) 
            {
                ZLog.LogWarning("[VBNetTweaks] Compression disabled");
                return;
            }
    
            _serverMode = Helper.IsServer();
    
            int level = VBNetTweaks.CompressionLevel?.Value ?? 3;
            int minSize = VBNetTweaks.CompressionMinSize?.Value ?? 1024;
            float minRatio = VBNetTweaks.CompressionMinRatio?.Value ?? 0.85f;
    
            _compressor = new DeflateCompressor(level, minSize, minRatio);
            ZLog.LogWarning($"[VBNetTweaks] Compression initialized (mode: {(_serverMode ? "SERVER" : "CLIENT")}) | Level={level}, MinSize={minSize}, MinRatio={minRatio:P0}");
        }

        public static void ReinitializeCompressor()
        {
            _compressor = null;
            _initialized = false;
            Initialize();
        }

        public static bool ShouldCompressSend()
        {
            if (!VBNetTweaks.ModuleCompression.Value) return false;
            return _compressor != null;
        }

        public static byte[] Compress(byte[] data)
        {
            if (_compressor == null || data == null) return data;
            try
            {
                byte[] result = _compressor.Compress(data);
                NetworkMetrics.RecordCompression(data.Length, result.Length);
                return result;
            }
            catch (Exception ex)
            {
                ZLog.LogError($"[Compression] Failed: {ex.Message}");
                return data;
            }
        }

        public static byte[] Decompress(byte[] data)
        {
            if (_compressor == null || data == null) return data;
            try
            {
                return _compressor.Decompress(data);
            }
            catch (Exception ex)
            {
                ZLog.LogError($"[Decompression] Failed: {ex.Message}");
                return data;
            }
        }

        public static string GetCompressionStatus()
        {
            return $"[VBNetTweaks] Compression: {(_compressor?.GetType().Name ?? "NONE")} | " +
                   $"Ratio: {NetworkMetrics.AvgCompressionRatio:P1} | " +
                   $"Sent/Recv: {NetworkMetrics.ZdosSent}/{NetworkMetrics.ZdosReceived}";
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ZDOMan), "Update")]
        private static IEnumerable<CodeInstruction> ZDOManUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions).Start();
            matcher.MatchStartForward(new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(ZDOMan), "SendZDOToPeers2")));
            
            if (matcher.IsInvalid)
            {
                ZLog.LogError("[VBNetTweaks] SendZDOToPeers2 not found!");
                return instructions;
            }
            
            matcher.SetOperandAndAdvance(AccessTools.Method(typeof(ZDONetworkOptimizer), "OptimizedSendZDOToPeers"));
            return matcher.InstructionEnumeration();
        }

        public static void OptimizedSendZDOToPeers(ZDOMan zdoManager, float dt)
        {
            try
            {
                int count = zdoManager.m_peers.Count;
                if (count <= 0) return;
                
                zdoManager.m_sendTimer += dt;
                float interval = VBNetTweaks.SendInterval?.Value ?? 0.05f;
                
                if (zdoManager.m_sendTimer < interval) return;
                
                zdoManager.m_sendTimer = 0f;
                int startPeer = Math.Max(zdoManager.m_nextSendPeer, 0);
                int peersPerUpdate = VBNetTweaks.PeersPerUpdate?.Value ?? 30;
                
                int sent = 0;
                for (int i = 0; i < Math.Min(peersPerUpdate, count); i++)
                {
                    int idx = (startPeer + i) % count;
                    var peer = zdoManager.m_peers[idx];
                    
                    if (peer?.m_peer?.m_socket?.IsConnected() == true)
                    {
                        zdoManager.SendZDOs(peer, flush: false);
                        NetworkMetrics.RecordZdoSent();
                        sent++;
                    }
                }
                zdoManager.m_nextSendPeer = (startPeer + sent) % count;
            }
            catch (Exception ex)
            {
                ZLog.LogError($"[VBNetTweaks] OptimizedSendZDOToPeers error: {ex.Message}");
                zdoManager.SendZDOToPeers2(dt);
            }
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZNetScene), "RemoveObjects")]
        private static bool RemoveObjectsPrefix(ZNetScene __instance, List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects)
        {
            if (__instance == null || __instance.m_instances == null || __instance.m_tempRemoved == null)
                return true;
    
            try
            {
                PerformanceMonitor.Track("RemoveObjects", delegate
                {
                    ZDORemoval.OptimizedRemoveObjects(__instance, currentNearObjects, currentDistantObjects);
                });
                return false; // Важно! Не вызываем оригинальный метод
            }
            catch (Exception ex)
            {
                ZLog.LogError($"[VBNetTweaks] RemoveObjects error: {ex.Message}");
                return true; // Fallback к оригинальному методу при ошибке
            }
        }
    }
}
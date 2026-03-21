namespace VBNetTweaks.CompressionUtills
{
    public static class CompressionController
    {
        public static ConfigEntry<CompressionAlgorithm> Algorithm { get; set; }
        public static ConfigEntry<int> Level { get; set; }
        public static ConfigEntry<int> MinSize { get; set; }
        public static ConfigEntry<bool> Adaptive { get; set; }
        public static ConfigEntry<float> TargetRatio { get; set; }

        private static ICompressor _compressor;
        private static readonly ConcurrentDictionary<ISocket, PeerCompressionStats> _peerStats = new();
        private static bool _initialized;

        public enum DataType
        {
            ZDO,
            RPC,
            Config,
            Unknown
        }

        private class PeerCompressionStats
        {
            public long TotalOriginal;
            public long TotalCompressed;
            public float AverageRatio = 1f;
            public bool UseCompression = true;
            public float LastOptimizationCheck;
        }

        public static void Initialize()
        {
            try
            {
                if (_initialized) return;
                if (!ModConfig.ModuleCompression.Value) return;

                if (Algorithm == null || Level == null)
                {
                    Helper.LogDebug("CompressionController configs not initialized");
                    return;
                }

                CreateCompressor();
                _initialized = true;
                Helper.LogDebug($"Compression initialized: {Algorithm.Value} level {Level.Value}");
            }
            catch (Exception e)
            {
                Helper.LogDebug($"Compression init failed: {e.Message}");
            }
        }

        public static void Shutdown()
        {
            _peerStats.Clear();
            _initialized = false;
        }

        private static void CreateCompressor()
        {
            int level = Level.Value;
            var algo = Algorithm.Value;

            switch (algo)
            {
                case CompressionAlgorithm.Zstd:
                    _compressor = new ZstdCompressor(level);
                    break;
                case CompressionAlgorithm.Deflate:
                default:
                    _compressor = new DeflateCompressor(level);
                    break;
            }
        }

        public static byte[] Compress(ISocket socket, byte[] data, DataType dataType)
        {
            if (dataType == DataType.RPC) return data;

            if (data.Length < MinSize.Value) return data;

            if (Adaptive.Value && socket != null)
            {
                var stats = _peerStats.GetOrAdd(socket, _ => new PeerCompressionStats());
                if (!stats.UseCompression) return data;
            }

            return CompressInternal(socket, data);
        }

        private static byte[] CompressInternal(ISocket socket, byte[] data)
        {
            try
            {
                var compressed = _compressor.Compress(data);
                
                if (compressed.Length >= data.Length)
                {
                    UpdatePeerStats(socket, data.Length, data.Length);
                    return data;
                }

                UpdatePeerStats(socket, data.Length, compressed.Length);
                return compressed;
            }
            catch
            {
                return data;
            }
        }

        public static byte[] Decompress(ISocket socket, byte[] data, DataType dataType)
        {
            if (dataType == DataType.RPC) return data;

            return DecompressInternal(data);
        }

        private static byte[] DecompressInternal(byte[] data)
        {
            try
            {
                return _compressor.Decompress(data);
            }
            catch
            {
                return data;
            }
        }

        private static void UpdatePeerStats(ISocket socket, int original, int compressed)
        {
            if (!Adaptive.Value || socket == null) return;

            var stats = _peerStats.GetOrAdd(socket, _ => new PeerCompressionStats());
            
            stats.TotalOriginal += original;
            stats.TotalCompressed += compressed;
            stats.AverageRatio = (float)stats.TotalCompressed / stats.TotalOriginal;

            float now = Time.time;
            if (now - stats.LastOptimizationCheck > 60f)
            {
                stats.LastOptimizationCheck = now;
                
                if (stats.AverageRatio > TargetRatio.Value && stats.TotalOriginal > 1024 * 1024)
                {
                    stats.UseCompression = false;
                    Helper.LogVerbose($"Disabled compression for {GetSocketName(socket)} - ratio {stats.AverageRatio:F2}");
                }
            }
        }

        private static string GetSocketName(ISocket socket)
        {
            try
            {
                return socket?.GetEndPointString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        public static DataType GetDataType(string context)
        {
            if (string.IsNullOrEmpty(context)) return DataType.Unknown;

            if (context.StartsWith("RPC_") || context == "VBNT_RPCBatch" || context.Contains("Command") || context.Contains("Request"))
            {
                return DataType.RPC;
            }
            
            if (context.Contains("ZDO") || context.Contains("Sector") || context.Contains("Object"))
            {
                return DataType.ZDO;
            }
            
            if (context.Contains("Config") || context.Contains("Settings"))
            {
                return DataType.Config;
            }
            
            return DataType.Unknown;
        }
    }
}
namespace VBNetTweaks.CompressionUtills;

public class ZstdCompressor : ICompressor
    {
        private readonly int _level;
        private readonly Type _compressorType;
        private readonly Type _decompressorType;
        private readonly object[] _compressorArgs;
        private readonly object[] _decompressorArgs;

        private static readonly ConcurrentQueue<object> _compressorPool = new();
        private static readonly ConcurrentQueue<object> _decompressorPool = new();

        public ZstdCompressor(int level)
        {
            _level = Math.Clamp(level, 1, 22);
            
            try
            {
                var assembly = Assembly.Load("ZstdSharp");
                _compressorType = assembly.GetType("ZstdSharp.Compressor");
                _decompressorType = assembly.GetType("ZstdSharp.Decompressor");
                
                _compressorArgs = new object[] { _level };
                _decompressorArgs = Array.Empty<object>();
            }
            catch
            {
                throw new Exception("ZstdSharp not available");
            }
        }

        public byte[] Compress(byte[] data)
        {
            if (data.Length < 32) return data;

            var compressor = GetCompressor();
            try
            {
                var result = _compressorType.GetMethod("Wrap").Invoke(compressor, new object[] { data });
                return result is byte[] bytes ? bytes : (byte[])result.GetType().GetMethod("ToArray").Invoke(result, null);
            }
            finally
            {
                ReturnCompressor(compressor);
            }
        }

        public byte[] Decompress(byte[] data)
        {
            var decompressor = GetDecompressor();
            try
            {
                var result = _decompressorType.GetMethod("Unwrap").Invoke(decompressor, new object[] { data });
                return result is byte[] bytes ? bytes : (byte[])result.GetType().GetMethod("ToArray").Invoke(result, null);
            }
            finally
            {
                ReturnDecompressor(decompressor);
            }
        }

        public float GetCompressionRatio(byte[] data)
        {
            var compressed = Compress(data);
            return (float)compressed.Length / data.Length;
        }

        private object GetCompressor()
        {
            if (_compressorPool.TryDequeue(out var compressor)) return compressor;
            return Activator.CreateInstance(_compressorType, _compressorArgs);
        }

        private void ReturnCompressor(object compressor) => _compressorPool.Enqueue(compressor);

        private object GetDecompressor()
        {
            if (_decompressorPool.TryDequeue(out var decompressor)) return decompressor;
            return Activator.CreateInstance(_decompressorType, _decompressorArgs);
        }

        private void ReturnDecompressor(object decompressor) => _decompressorPool.Enqueue(decompressor);
    }
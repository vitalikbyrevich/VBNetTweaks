namespace VBNetTweaks
{
    public interface ICompressor
    {
        byte[] Compress(byte[] data);
        byte[] Decompress(byte[] data);
    }

    public class DeflateCompressor : ICompressor
    {
        public byte[] Compress(byte[] data)
        {
            if (data.Length < 32) return data;
            
            using var ms = new MemoryStream();
            using (var ds = new DeflateStream(ms, (System.IO.Compression.CompressionLevel)CompressionLevel.Fastest))
            {
                ds.Write(data, 0, data.Length);
            }
            var compressed = ms.ToArray();
            return compressed.Length < data.Length ? compressed : data;
        }

        public byte[] Decompress(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var ds = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            ds.CopyTo(output);
            return output.ToArray();
        }
    }

    public class ZstdCompressor : ICompressor
    {
        private dynamic _compressor;
        private dynamic _decompressor;

        public ZstdCompressor()
        {
            try
            {
                var assembly = Assembly.Load("ZstdSharp");
                var compType = assembly.GetType("ZstdSharp.Compressor");
                var decompType = assembly.GetType("ZstdSharp.Decompressor");
                
                _compressor = Activator.CreateInstance(compType, 1);
                _decompressor = Activator.CreateInstance(decompType);
            }
            catch
            {
                throw new Exception("ZstdSharp not available");
            }
        }

        public byte[] Compress(byte[] data)
        {
            if (data.Length < 32) return data;
            
            var result = _compressor.Wrap(data);
            return result is byte[] bytes ? bytes : result.ToArray();
        }

        public byte[] Decompress(byte[] data)
        {
            var result = _decompressor.Unwrap(data);
            return result is byte[] bytes ? bytes : result.ToArray();
        }
    }
}
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace VBNetTweaks.CompressionUtills
{
    public enum CompressionAlgorithm
    {
        Deflate,
        Vanilla
    }

    public interface ICompressor
    {
        byte[] Compress(byte[] data);
        byte[] Decompress(byte[] data);
    }

    public class VanillaCompressor : ICompressor
    {
        public byte[] Compress(byte[] data)
        {
            if (data.Length < 32) return data;

            try
            {
                ZPackage original = new ZPackage(data);
                ZPackage compressed = new ZPackage();
                compressed.WriteCompressed(original);

                byte[] result = compressed.GetArray();
                if (result.Length > 4)
                {
                    byte[] trimmed = new byte[result.Length - 4];
                    Buffer.BlockCopy(result, 4, trimmed, 0, trimmed.Length);
                    return trimmed.Length < data.Length ? trimmed : data;
                }

                return data;
            }
            catch
            {
                return data;
            }
        }

        public byte[] Decompress(byte[] data)
        {
            try
            {
                ZPackage pkg = new ZPackage();
                byte[] withHeader = new byte[data.Length + 4];
                byte[] lengthBytes = BitConverter.GetBytes(data.Length);
                Buffer.BlockCopy(lengthBytes, 0, withHeader, 0, 4);
                Buffer.BlockCopy(data, 0, withHeader, 4, data.Length);

                pkg.Load(withHeader);
                return pkg.ReadCompressedPackage().GetArray();
            }
            catch
            {
                return data;
            }
        }
    }

    public class DeflateCompressor : ICompressor
    {
        private readonly int _level;
        private readonly object _lock = new object();

        public DeflateCompressor(int level)
        {
            _level = Math.Max(1, Math.Min(9, level));
            ZLog.LogWarning($"[Deflate] Compressor created with level {_level}");
        }

        public byte[] Compress(byte[] data)
        {
            if (data.Length < 32) return data;

            try
            {
                using var ms = new MemoryStream();
                using (var ds = new DeflateStream(ms, CompressionLevel.Fastest))
                {
                    ds.Write(data, 0, data.Length);
                }

                var compressed = ms.ToArray();
                float ratio = (float)compressed.Length / data.Length;

                if (compressed.Length < data.Length)
                {
                    Helper.LogDebug($"[Deflate] Compressed {data.Length} -> {compressed.Length} bytes ({ratio:P1})");
                    return compressed;
                }

                return data;
            }
            catch (Exception e)
            {
                ZLog.LogError($"[Deflate] Compression failed: {e.Message}");
                return data;
            }
        }

        public byte[] Decompress(byte[] data)
        {
            try
            {
                using var input = new MemoryStream(data);
                using var ds = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                ds.CopyTo(output);
                var decompressed = output.ToArray();

                Helper.LogDebug($"[Deflate] Decompressed {data.Length} -> {decompressed.Length} bytes");
                return decompressed;
            }
            catch (Exception e)
            {
                ZLog.LogError($"[Deflate] Decompression failed: {e.Message}");
                return data;
            }
        }
    }
}
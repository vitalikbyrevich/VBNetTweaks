using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace VBNetTweaks.CompressionUtills
{
    public interface ICompressor
    {
        byte[] Compress(byte[] data);
        byte[] Decompress(byte[] data);
    }

    public class DeflateCompressor : ICompressor
    {
        private const byte COMPRESSION_MAGIC = 0xCE;
        private const int HEADER_SIZE = 5;

        private readonly int _level;
        private readonly int _minSizeThreshold;
        private readonly float _minUsefulRatio;

        public DeflateCompressor(int level, int minSizeThreshold = 1024, float minUsefulRatio = 0.85f)
        {
            _level = Math.Max(1, Math.Min(9, level));
            _minSizeThreshold = Math.Max(64, minSizeThreshold);
            _minUsefulRatio = Math.Max(0.5f, Math.Min(1f, minUsefulRatio));

            ZLog.LogWarning($"[VBNetTweaks] Deflate compressor: level={_level}, minSize={_minSizeThreshold}B, minRatio={_minUsefulRatio:P0}");
        }

        public byte[] Compress(byte[] data)
        {
            if (data == null || data.Length < _minSizeThreshold)
                return data;

            if (IsCompressed(data))
                return data;

            try
            {
                using var outputStream = new MemoryStream();

                // Write header with safe format (1 byte magic + 4 bytes length, big-endian)
                outputStream.WriteByte(COMPRESSION_MAGIC);
                WriteInt32BigEndian(outputStream, data.Length);

                using (var deflateStream = new DeflateStream(outputStream, (CompressionLevel)_level, leaveOpen: true))
                {
                    deflateStream.Write(data, 0, data.Length);
                }

                byte[] compressed = outputStream.ToArray();
                float ratio = (float)compressed.Length / data.Length;

                if (ratio >= _minUsefulRatio)
                {
                    Helper.LogDebug($"[Deflate] Skip: ratio {ratio:P1} >= {_minUsefulRatio:P0} ({data.Length}B -> {compressed.Length}B)");
                    return data;
                }

                Helper.LogDebug($"[Deflate] Compressed {data.Length}B -> {compressed.Length}B ({ratio:P1})");
                return compressed;
            }
            catch (Exception ex)
            {
                ZLog.LogError($"[Deflate] Compression failed: {ex.Message}");
                return data;
            }
        }

        public byte[] Decompress(byte[] data)
        {
            if (data == null || data.Length < HEADER_SIZE)
                return data;

            if (!IsCompressed(data))
                return data;

            try
            {
                int originalSize = ReadInt32BigEndian(data, 1);

                if (originalSize <= 0 || originalSize > 50 * 1024 * 1024) // 50MB max
                {
                    ZLog.LogError($"[Deflate] Invalid original size: {originalSize}");
                    return data;
                }

                using var inputStream = new MemoryStream(data, HEADER_SIZE, data.Length - HEADER_SIZE);
                using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
                using var outputStream = new MemoryStream();

                deflateStream.CopyTo(outputStream);
                byte[] result = outputStream.ToArray();

                if (result.Length != originalSize)
                {
                    ZLog.LogError($"[Deflate] Size mismatch: expected {originalSize}B, got {result.Length}B");
                    return data;
                }

                Helper.LogDebug($"[Deflate] Decompressed {data.Length}B -> {result.Length}B");
                return result;
            }
            catch (Exception ex)
            {
                ZLog.LogError($"[Deflate] Decompression failed: {ex.Message}");
                return data;
            }
        }

        private static bool IsCompressed(byte[] data)
        {
            return data.Length >= HEADER_SIZE && data[0] == COMPRESSION_MAGIC;
        }

        private static void WriteInt32BigEndian(Stream stream, int value)
        {
            byte[] bytes = new byte[4];
            bytes[0] = (byte)((value >> 24) & 0xFF);
            bytes[1] = (byte)((value >> 16) & 0xFF);
            bytes[2] = (byte)((value >> 8) & 0xFF);
            bytes[3] = (byte)(value & 0xFF);
            stream.Write(bytes, 0, 4);
        }

        private static int ReadInt32BigEndian(byte[] data, int offset)
        {
            return (data[offset] << 24) |
                   (data[offset + 1] << 16) |
                   (data[offset + 2] << 8) |
                   data[offset + 3];
        }
    }
}
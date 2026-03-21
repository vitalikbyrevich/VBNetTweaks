namespace VBNetTweaks.CompressionUtills;

public class DeflateCompressor : ICompressor
{
    private readonly int _level;
    private readonly Dictionary<int, System.IO.Compression.CompressionLevel> _levelMap = new()
    {
        [1] = System.IO.Compression.CompressionLevel.Fastest,
        [2] = System.IO.Compression.CompressionLevel.Fastest,
        [3] = System.IO.Compression.CompressionLevel.Fastest,
        [4] = System.IO.Compression.CompressionLevel.Optimal,
        [5] = System.IO.Compression.CompressionLevel.Optimal,
        [6] = System.IO.Compression.CompressionLevel.Optimal,
        [7] = System.IO.Compression.CompressionLevel.Optimal,
        [8] = System.IO.Compression.CompressionLevel.Optimal,
        [9] = System.IO.Compression.CompressionLevel.Optimal
    };

    public DeflateCompressor(int level)
    {
        _level = Math.Clamp(level, 1, 9);
    }

    public byte[] Compress(byte[] data)
    {
        if (data.Length < 32) return data;

        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, _levelMap[_level], true))
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

    public float GetCompressionRatio(byte[] data)
    {
        var compressed = Compress(data);
        return (float)compressed.Length / data.Length;
    }
}
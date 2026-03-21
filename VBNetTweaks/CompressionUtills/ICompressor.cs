namespace VBNetTweaks.CompressionUtills
{
    public interface ICompressor
    {
        byte[] Compress(byte[] data);
        byte[] Decompress(byte[] data);
        float GetCompressionRatio(byte[] data);
    }
}
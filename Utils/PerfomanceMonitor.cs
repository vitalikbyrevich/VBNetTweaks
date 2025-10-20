namespace VBNetTweaks;

public static class PerformanceMonitor
{
    private static readonly List<float> m_frameTimes = new List<float>();
    private static readonly List<int> m_networkPackets = new List<int>();
    private const int SAMPLE_COUNT = 60;
    
    public static float AverageFrameTime { get; private set; }
    public static int AveragePacketsPerSecond { get; private set; }
    public static bool IsPerformanceDegraded { get; private set; }
    
    public static void UpdateMetrics(float frameTime, int packetsSent)
    {
        m_frameTimes.Add(frameTime);
        m_networkPackets.Add(packetsSent);
    
        if (m_frameTimes.Count > SAMPLE_COUNT)
        {
            m_frameTimes.RemoveAt(0);
            m_networkPackets.RemoveAt(0);
        }
    
        AverageFrameTime = m_frameTimes.Average();
        AveragePacketsPerSecond = (int)m_networkPackets.Average();
        IsPerformanceDegraded = AverageFrameTime > 0.033f;
    
        if (VBNetTweaks.DebugEnabled.Value && IsPerformanceDegraded)
        {
            VBNetTweaks.LogDebug($"Performance warning: FrameTime {AverageFrameTime:F3}s, " + $"Packets: {AveragePacketsPerSecond}/s");
        }
    }
    
    // Интеграция с адаптивной системой
    public static void ApplyPerformanceAdjustments(ref float sendInterval, ref int peersPerUpdate, ref float broadcastInterval)
    {
        if (IsPerformanceDegraded)
        {
            sendInterval *= 1.2f;
            peersPerUpdate = Math.Max(1, peersPerUpdate - 1);
            broadcastInterval *= 1.3f;
        }
    }
}
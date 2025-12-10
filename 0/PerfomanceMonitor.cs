namespace VBNetTweaks;

public static class PerformanceMonitor
{
    private static readonly List<float> m_frameTimes = new List<float>();
    private static readonly List<int> m_networkPackets = new List<int>();
    private static readonly List<int> m_zdoCounts = new List<int>();
    private const int SAMPLE_COUNT = 60;

    public static float AverageFrameTime { get; private set; }
    public static int AveragePacketsPerSecond { get; private set; }
    public static int AverageZDOCount { get; private set; }
    public static bool IsPerformanceDegraded { get; private set; }

    public static void UpdateMetrics(float frameTime, int packetsSent)
    {
        m_frameTimes.Add(frameTime);
        m_networkPackets.Add(packetsSent);

        // Добавляем мониторинг ZDO - правильный доступ через m_objectsByID
        if (ZDOMan.instance != null)
        {
            m_zdoCounts.Add(ZDOMan.instance.m_objectsByID.Count);
        }

        if (m_frameTimes.Count > SAMPLE_COUNT)
        {
            m_frameTimes.RemoveAt(0);
            m_networkPackets.RemoveAt(0);
            if (m_zdoCounts.Count > SAMPLE_COUNT) m_zdoCounts.RemoveAt(0);
        }

        AverageFrameTime = m_frameTimes.Average();
        AveragePacketsPerSecond = (int)m_networkPackets.Average();
        AverageZDOCount = m_zdoCounts.Count > 0 ? (int)m_zdoCounts.Average() : 0;

        // Более строгие критерии деградации
        IsPerformanceDegraded = AverageFrameTime > 0.05f || AveragePacketsPerSecond > 5000;

        if (VBNetTweaks.DebugEnabled.Value && Time.frameCount % 300 == 0) // Логируем каждые ~5 секунд
        {
            VBNetTweaks.LogDebug($"Performance: FrameTime {AverageFrameTime:F3}s, " +
                                 $"Packets: {AveragePacketsPerSecond}/s, " +
                                 $"ZDO: {AverageZDOCount}");
        }

        // Критическое логирование при проблемах
        if (IsPerformanceDegraded && VBNetTweaks.DebugEnabled.Value)
        {
            VBNetTweaks.LogDebug($"PERFORMANCE WARNING: FrameTime {AverageFrameTime:F3}s, " +
                                 $"Packets: {AveragePacketsPerSecond}/s");
        }
    }

    // Упрощенная версия без адаптивных корректировок
    public static bool ShouldReduceLoad()
    {
        return IsPerformanceDegraded || AverageZDOCount > 50000;
    }
}
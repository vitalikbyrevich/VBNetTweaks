namespace VBNetTweaks.Utils;

public static class AdaptiveOptimizationManager
{
    // ФИКСИРОВАННЫЕ НАСТРОЙКИ ДЛЯ 25-50 ИГРОКОВ
    private const float FIXED_SEND_INTERVAL = 0.1f; // 100ms
    private const int FIXED_PEERS_PER_UPDATE = 6; // 6 пиров за раз
    private const float FIXED_BROADCAST_INTERVAL = 0.5f; // 500ms
    private const int MAX_ZDO_PER_PEER = 200; // Лимит ZDO

    private static int m_lastPlayerCount;
    private static float m_lastLogTime;
    private const float LOG_INTERVAL = 30f;

    public static void UpdateAdaptiveSettings()
    {
        // Только мониторинг, без изменений настроек
        if (Time.time - m_lastLogTime < LOG_INTERVAL) return;
        if (!ZNet.instance) return;

        m_lastLogTime = Time.time;
        int currentPlayers = ZNet.instance.GetPeerConnections();

        if (currentPlayers != m_lastPlayerCount || VBNetTweaks.DebugEnabled.Value)
        {
            m_lastPlayerCount = currentPlayers;

            VBNetTweaks.LogVerbose($"Network Status: {currentPlayers} players, " +
                                   $"Fixed Settings: Send={FIXED_SEND_INTERVAL}s, " +
                                   $"Peers={FIXED_PEERS_PER_UPDATE}, " +
                                   $"Broadcast={FIXED_BROADCAST_INTERVAL}s");
        }
    }

    public static int GetPeersPerUpdate()
    {
        // Динамическое уменьшение при проблемах производительности
        int basePeers = FIXED_PEERS_PER_UPDATE;

        if (PerformanceMonitor.ShouldReduceLoad())
        {
            basePeers = Math.Max(2, basePeers / 2);
            if (VBNetTweaks.DebugEnabled.Value)
                VBNetTweaks.LogDebug($"Reducing peers per update to {basePeers} due to performance issues");
        }

        return basePeers;
    }

    public static float GetSendInterval()
    {
        // Динамическое увеличение интервала при проблемах
        float baseInterval = FIXED_SEND_INTERVAL;

        if (PerformanceMonitor.ShouldReduceLoad())
        {
            baseInterval *= 1.5f;
            if (VBNetTweaks.DebugEnabled.Value)
                VBNetTweaks.LogDebug($"Increasing send interval to {baseInterval:F3}s due to performance issues");
        }

        return baseInterval;
    }

    public static float GetBroadcastInterval()
    {
        return FIXED_BROADCAST_INTERVAL;
    }

    public static int GetMaxZDOPerPeer()
    {
        return MAX_ZDO_PER_PEER;
    }
}
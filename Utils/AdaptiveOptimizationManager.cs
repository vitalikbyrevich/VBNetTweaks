namespace VBNetTweaks.Utils;

public static class AdaptiveOptimizationManager
{
    private static int m_lastPlayerCount;
    private static float m_lastAdjustmentTime;
    private const float ADJUSTMENT_INTERVAL = 10f;

    public static float CurrentSendInterval { get; private set; } = 0.05f;
    public static int CurrentPeersPerUpdate { get; private set; } = 8;
    public static float CurrentBroadcastInterval { get; private set; } = 0.3f;

    public static void UpdateAdaptiveSettings()
    {
        if (Time.time - m_lastAdjustmentTime < ADJUSTMENT_INTERVAL) return;
        if (!ZNet.instance) return;

        m_lastAdjustmentTime = Time.time;
        int currentPlayers = ZNet.instance.GetPeerConnections();
        if (currentPlayers == m_lastPlayerCount) return;

        m_lastPlayerCount = currentPlayers;
        ApplyAdaptiveSettings(currentPlayers);
    }

    private static void ApplyAdaptiveSettings(int playerCount)
    {
        float baseSendInterval = VBNetTweaks.GetSendInterval();
        int basePeersPerUpdate = GetBasePeersPerUpdate(playerCount);

        (CurrentSendInterval, CurrentPeersPerUpdate, CurrentBroadcastInterval) = playerCount switch
        {
            < 10 => (baseSendInterval, basePeersPerUpdate, 0.3f),
            < 25 => (baseSendInterval * 1.5f, basePeersPerUpdate / 2, 0.4f),
            < 50 => (baseSendInterval * 2f, basePeersPerUpdate / 3, 0.5f),
            < 65 => (baseSendInterval * 3f, 2, 0.7f),
            _ => (baseSendInterval * 4f, 1, 1.0f)
        };

        CurrentSendInterval = Math.Max(CurrentSendInterval, 0.02f);
        CurrentPeersPerUpdate = Math.Max(CurrentPeersPerUpdate, 1);
        CurrentBroadcastInterval = Math.Max(CurrentBroadcastInterval, 0.2f);

        if (VBNetTweaks.DebugEnabled.Value)
        {
            VBNetTweaks.LogDebug($"Adaptive settings: {playerCount} players -> " + $"Send: {CurrentSendInterval:F3}s, " + $"Peers: {CurrentPeersPerUpdate}, " + $"Broadcast: {CurrentBroadcastInterval:F3}s");
        }
    }

    private static int GetBasePeersPerUpdate(int playerCount)
    {
        return playerCount switch
        {
            < 15 => 8,
            < 30 => 6,
            < 50 => 4,
            < 65 => 3,
            _ => 2
        };
    }

    public static int GetPeersPerUpdate()
    {
        UpdateAdaptiveSettings();
        return CurrentPeersPerUpdate;
    }

    public static float GetSendInterval()
    {
        UpdateAdaptiveSettings();
        return CurrentSendInterval;
    }

    public static float GetBroadcastInterval()
    {
        UpdateAdaptiveSettings();
        return CurrentBroadcastInterval;
    }
}
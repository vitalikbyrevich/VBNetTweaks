namespace VBNetTweaks.Utils;

public static class Helper
{
    public static float GetTimeoutConnected() => Mathf.Clamp(VBNetTweaks.c_SteamTimeoutConnected.Value, 5000f, 600000f);
    public static int GetSteamSendBufferSizeKB() => Math.Max(512 * 1024, VBNetTweaks.c_SteamSendBufferSizeKB.Value * 1024);
    public static int GetSteamSendRateMaxKB() => Math.Max(128 * 1024, VBNetTweaks.c_SteamSendRateMaxKB.Value * 1024);
    public static int GetSteamRecvBufferMessages() => Mathf.Clamp(VBNetTweaks.c_SteamRecvBufferMessages.Value, 256, 4096);
    
    public static float GetSendInterval() => Mathf.Clamp(VBNetTweaks.c_SendInterval_S.Value, 0.01f, 0.2f);
    
    public static float GetMapPositionSendInterval() => Mathf.Clamp(VBNetTweaks.c_MapPositionSendInterval.Value, 0.1f, 2f);
    
    
    public static bool IsServer() => ZNet.instance && ZNet.instance.IsServer();
    
    public static void LogDebug(string message)
    {
        if (VBNetTweaks.c_DebugEnabled.Value) VBNetTweaks.Logger.LogWarning(message);
    }

    public static void LogVerbose(string message)
    {
        if (VBNetTweaks.c_VerboseLogging.Value) VBNetTweaks.Logger.LogInfo(message);
    }
}
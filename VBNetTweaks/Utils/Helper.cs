namespace VBNetTweaks.Utils;

public static class Helper
{
    public static bool IsServer() 
    {
        return ZNet.instance && ZNet.instance.IsServer();
    }

    public static ZNet SafeZNetInstance() => ZNet.instance;

    public static void LogErrorWithContext(string module, string message, Exception ex = null)
    {
        string context = ex != null ? $" [{ex.Message}]" : "";
        Debug.LogError($"[VBNetTweaks][{module}] {message}{context}");
    }
    
    public static bool IsServerInitialized() => IsServer() && ZNet.instance && ZNet.instance.IsServer();

    public static void LogDebug(string message)
    {
        if (VBNetTweaks.DebugEnabled.Value) Debug.LogWarning($"[VBNetTweaks] {message}");
    }

    public static void LogVerbose(string message)
    {
        if (VBNetTweaks.VerboseLogging.Value) Debug.LogWarning($"[VBNetTweaks] {message}");
    }

    public static void CheckCompressionStatus()
    {
        if (VBNetTweaks.ModuleCompression.Value && VBNetTweaks.DebugEnabled.Value)
        {
            ZLog.LogWarning(ZDONetworkOptimizer.GetCompressionStatus());
        }
    }
}
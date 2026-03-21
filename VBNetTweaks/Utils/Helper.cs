namespace VBNetTweaks.Utils;

public static class Helper
{
    private static int _lastFrame = -1;
    private static bool _cachedIsServer;
    
    public static bool IsServer()
    {
        if (_lastFrame == Time.frameCount) return _cachedIsServer;
        
        try
        {
            var znet = ZNet.instance;
            _cachedIsServer = znet && znet.IsServer();
            _lastFrame = Time.frameCount;
            return _cachedIsServer;
        }
        catch
        {
            return false;
        }
    }
    
    public static bool ShouldProcess(ZNetView view)
    {
        if (!view || !view.IsValid()) return false;
        
        if (IsServer()) return true;
        
        return view.IsOwner();
    }
    
    public static bool ShouldProcess(ZDO zdo)
    {
        if (zdo == null) return false;
        
        if (IsServer()) return true;
        
        return zdo.IsOwner();
    }

    public static void LogDebug(string message)
    {
        if (ModConfig.DebugEnabled.Value) Debug.LogWarning($"[VBNetTweaks] {message}");
    }

    public static void LogVerbose(string message)
    {
        if (ModConfig.VerboseLogging.Value) Debug.Log($"[VBNetTweaks] {message}");
    }

    public static float GetEffectiveSendInterval()
    {
        float cfg = (!ModConfig._serverConfigsInitialized) ? 0.05f : (ModConfig.SendInterval?.Value ?? 0.05f);
        return AdaptiveThrottler.GetInterval(cfg);
    }

    public static int GetPeersPerUpdate() => (!ModConfig._serverConfigsInitialized) ? 20 : (ModConfig.PeersPerUpdate?.Value ?? 20);
}
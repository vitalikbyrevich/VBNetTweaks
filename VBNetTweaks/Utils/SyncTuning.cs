namespace VBNetTweaks.Utils;

public static class SyncTuning
{
    private static int _frame;
    private static float _smoothPos;
    private static float _smoothRot;
    private static float _microThreshold;
    private static float _clientDistance;
    private static float _teleportDistance;
    private static float _teleportRotation;

    private static void RefreshIfNeeded()
    {
        int currentFrame = Time.frameCount;
        if (currentFrame == _frame) return;
        
        _frame = currentFrame;
        
        _smoothPos = Mathf.Clamp(VBNetTweaks.c_SmoothPosition.Value, 0.01f, 1f);
        _smoothRot = Mathf.Clamp(VBNetTweaks.c_SmoothRotation.Value, 0.01f, 1f);
        _microThreshold = Mathf.Clamp(VBNetTweaks.c_MicroThreshold.Value, 0f, 0.05f);
        _clientDistance = Mathf.Clamp(VBNetTweaks.c_ClientDistanceThreshold.Value, 0f, 0.1f);
        _teleportDistance = Mathf.Clamp(VBNetTweaks.c_TeleportDistanceThreshold.Value, 1f, 100f);
        _teleportRotation = Mathf.Clamp(VBNetTweaks.c_TeleportRotationThreshold.Value, 10f, 180f);
    }

    public static float GetSmoothPosition() { RefreshIfNeeded(); return _smoothPos; }
    public static float GetSmoothRotation() { RefreshIfNeeded(); return _smoothRot; }
    public static float GetMicroThreshold() { RefreshIfNeeded(); return _microThreshold; }
    public static float GetClientDistanceThreshold() { RefreshIfNeeded(); return _clientDistance; }
    public static float GetTeleportDistanceThreshold() { RefreshIfNeeded(); return _teleportDistance; }
    public static float GetTeleportRotationThreshold() { RefreshIfNeeded(); return _teleportRotation; }
    
    public static float GetTimeoutConnected() => Mathf.Clamp(VBNetTweaks.c_SteamTimeoutConnected.Value, 5000f, 600000f);
}
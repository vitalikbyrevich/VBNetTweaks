namespace VBNetTweaks.Utils;

public static class Helper
{
    private static readonly Dictionary<int, bool> _creatureCache = new();
    private static readonly Dictionary<int, bool> _shipCache = new();

    public static bool IsCreature(ZDO zdo)
    {
        int prefab = zdo.GetPrefab();

        if (_creatureCache.TryGetValue(prefab, out bool cached)) return cached;

        bool result = false;

        if (ZNetScene.instance)
        {
            GameObject go = ZNetScene.instance.GetPrefab(prefab);
            if (go) result = go.GetComponent<Humanoid>();
        }

        _creatureCache[prefab] = result;
        return result;
    }

    public static bool IsShip(ZDO zdo)
    {
        int prefab = zdo.GetPrefab();

        if (_shipCache.TryGetValue(prefab, out bool cached)) return cached;

        bool result = false;

        if (ZNetScene.instance)
        {
            GameObject go = ZNetScene.instance.GetPrefab(prefab);
            if (go) result = go.GetComponent<Ship>();
        }

        _shipCache[prefab] = result;
        return result;
    }
    
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
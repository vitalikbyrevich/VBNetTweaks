namespace VBNetTweaks.ZDOUtills;

public static class ZDOThrottling
{
    public static readonly Dictionary<ZDO, float> _distanceCache = new();
    public static Vector3 _cachedRefPos;
    public static int _cachedFrame = -1;

    private static readonly int HashAI = "ai".GetStableHashCode();

    public static void ApplyZDOThrottle(ZDOMan zdoManager, ZDOMan.ZDOPeer peer)
    {
        if (!ModConfig.ModuleZDOThrottling.Value) return;
        List<ZDO> near = null;
        List<ZDO> distant = null;

        try
        {
            Vector3 refPos = peer.m_peer.GetRefPos();
            Vector2i zone = ZoneSystem.GetZone(refPos);

            near = ObjectPool.RentList<ZDO>();
            distant = ObjectPool.RentList<ZDO>();

            int activeArea = ZoneSystem.instance?.m_activeArea ?? 3;
            int distantArea = ZoneSystem.instance?.m_activeDistantArea ?? 5;

            zdoManager.FindSectorObjects(zone, activeArea, distantArea, near, distant);

            foreach (var z in near)
            {
                if (z == null) continue;
                float d = Vector3.Distance(z.GetPosition(), refPos);
                z.m_tempSortValue = d - 100f;
            }

            foreach (var z in distant)
            {
                if (z == null) continue;

                float d = Vector3.Distance(z.GetPosition(), refPos);

                if (IsMob(z))
                {
                    z.m_tempSortValue = d - 50f;
                    continue;
                }

                z.m_tempSortValue = d + 150f;
            }
        }
        finally
        {
            if (near != null) ObjectPool.ReturnList(near);
            if (distant != null) ObjectPool.ReturnList(distant);
        }
    }

    public static float GetDistance(ZDO zdo, Vector3 refPos)
    {
        if (_cachedFrame != Time.frameCount || _cachedRefPos != refPos)
        {
            _distanceCache.Clear();
            _cachedRefPos = refPos;
            _cachedFrame = Time.frameCount;
        }

        if (_distanceCache.TryGetValue(zdo, out float d)) return d;

        d = Vector3.Distance(zdo.GetPosition(), refPos);
        _distanceCache[zdo] = d;
        return d;
    }

    public static bool IsMob(ZDO zdo) => zdo.GetInt(HashAI, -1) != -1;
}
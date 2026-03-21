namespace VBNetTweaks.ZDOUtills;

public class ZDOSorting
{

    private const float ImportantObjectDistance = 200f;

    private static readonly int PlayerPrefab = "Player".GetStableHashCode();

    private static readonly HashSet<int> ShipPrefabs = new()
    {
        "Karve".GetStableHashCode(),
        "VikingShip".GetStableHashCode(),
        "Raft".GetStableHashCode(),
        "VikingShip_Ashlands".GetStableHashCode()
    };

    private static readonly HashSet<int> ImportantPrefabs = new()
    {
        "portal_wood".GetStableHashCode(),
        "portal_stone".GetStableHashCode(),
        "piece_workbench".GetStableHashCode(),
        "piece_bed".GetStableHashCode(),
        "piece_chest".GetStableHashCode()
    };

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSortSendZDOS))]
    public static void ApplyWeights(List<ZDO> objects, Vector3 refPos)
    {
        if (ZDOThrottling._cachedFrame != Time.frameCount || ZDOThrottling._cachedRefPos != refPos)
        {
            ZDOThrottling._distanceCache.Clear();
            ZDOThrottling._cachedRefPos = refPos;
            ZDOThrottling._cachedFrame = Time.frameCount;
        }

        foreach (var zdo in objects)
        {
            if (zdo == null) continue;

            int prefab = zdo.GetPrefab();

            if (prefab == PlayerPrefab)
            {
                zdo.m_tempSortValue -= 500f;
                continue;
            }

            if (ShipPrefabs.Contains(prefab))
            {
                bool hasPlayers = ShipSyncSystem.ShipHasPlayers(zdo.m_uid);
                zdo.m_tempSortValue += hasPlayers ? -450f : -200f;
                continue;
            }

            float distance = ZDOThrottling.GetDistance(zdo, refPos);

            if (ZDOThrottling.IsMob(zdo))
            {
                zdo.m_tempSortValue -= 300f;
                continue;
            }

            if (ImportantPrefabs.Contains(prefab) && distance < ImportantObjectDistance) zdo.m_tempSortValue -= 150f;
            else
                zdo.m_tempSortValue += distance;
        }
    }
}
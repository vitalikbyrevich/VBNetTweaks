namespace VBNetTweaks.Utils;

[HarmonyPatch(typeof(ZDOMan))]
public static class ZDOManMonitoring
{
    private static float m_lastStatsTime;
    private const float STATS_INTERVAL = 60f; // Раз в минуту
    
    [HarmonyPostfix]
    [HarmonyPatch("Update")]
    static void Update_Postfix(ZDOMan __instance)
    {
        if (!Helper.IsServer()) return;
        if (Time.time - m_lastStatsTime < STATS_INTERVAL) return;
    
        m_lastStatsTime = Time.time;
    
        // Сбор статистики по ZDO - правильный доступ через m_objectsByID
        int totalZDO = __instance.m_objectsByID.Count;
        int persistentZDO = __instance.m_objectsByID.Count(pair => pair.Value?.Persistent == true);
        int deadZDO = __instance.m_objectsByID.Count(pair => !pair.Value?.IsValid() == true);
    
        VBNetTweaks.LogVerbose($"ZDO Statistics: Total={totalZDO}, " + $"Persistent={persistentZDO}, " + $"Dead={deadZDO}, " + $"Peers={__instance.m_peers.Count}");
    }
}
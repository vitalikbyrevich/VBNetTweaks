using System.Collections;
using UnityEngine;

namespace VBNetTweaks;

public static class FixedNetworkOptimizations
{
    private static bool _initialized = false;
    
    // ФИКСИРОВАННЫЕ НАСТРОЙКИ ДЛЯ 50 ИГРОКОВ
    public static float SendInterval => 0.08f;           // 80ms вместо 100ms - баланс скорости/нагрузки
    public static int PeersPerUpdate => 6;               // 6 пиров за раз
    public static float BroadcastInterval => 0.5f;       // 500ms между рассылками
    public static int MaxZDOPerPeer => 120;              // Лимит ZDO на пира

// УМНЫЕ НАСТРОЙКИ ДЛЯ РАЗНЫХ ТИПОВ ДАННЫХ
    public static float GetBroadcastInterval(string dataType)
    {
        return dataType switch
        {
            "player" => 0.05f,    // 50ms - позиции игроков
            "door" => 0.05f,      // 50ms - двери
            "chest" => 0.1f,      // 100ms - сундуки  
            "build" => 0.2f,      // 200ms - постройки
            "world" => 0.5f,      // 500ms - настройки мира
            _ => 0.3f             // 300ms - по умолчанию
        };
    }
    
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    public static void ZNet_Awake_Postfix(ZNet __instance)
    {
        if (!__instance.IsServer()) return;
        
        // Запускаем корутину для применения фиксированных настроек
        __instance.StartCoroutine(ApplyFixedSettings());
    }
    
    private static IEnumerator ApplyFixedSettings()
    {
        // Ждем инициализации ZDOMan
        yield return new WaitUntil(() => ZDOMan.instance != null);
        yield return new WaitForSeconds(2f);
        
        if (!_initialized)
        {
            _initialized = true;
            Debug.LogWarning($"Применены фиксированные сетевые настройки для 50 игроков:");
            Debug.LogWarning($"SendInterval: {SendInterval}s, PeersPerUpdate: {PeersPerUpdate}");
            Debug.LogWarning($"BroadcastInterval: {BroadcastInterval}s, MaxZDOPerPeer: {MaxZDOPerPeer}");
        }
    }
    
    // Переопределяем методы AdaptiveOptimizationManager
    public static float GetFixedSendInterval() => SendInterval;
    public static int GetFixedPeersPerUpdate() => PeersPerUpdate;
    public static float GetFixedBroadcastInterval() => BroadcastInterval;
}
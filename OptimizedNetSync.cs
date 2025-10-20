using VBNetTweaks.Utils;

namespace VBNetTweaks;

[HarmonyPatch]
public static class OptimizedNetSync
{
    // ==================== СЕРВЕРНАЯ ЧАСТЬ ====================
    private static readonly Dictionary<string, ZPackage> m_serverData = new Dictionary<string, ZPackage>();
    private static readonly Dictionary<string, HashSet<long>> m_dataSubscribers = new Dictionary<string, HashSet<long>>();
    private static readonly Dictionary<string, float> m_lastBroadcastTime = new Dictionary<string, float>();
    private static readonly Dictionary<string, ZPackage> m_pendingUpdates = new Dictionary<string, ZPackage>();
    
    // Настройки оптимизации
    private const float BROADCAST_INTERVAL = 0.3f; // 300ms между рассылками
    private const float CLEANUP_INTERVAL = 30f; // Очистка каждые 30 секунд
    private static float m_lastCleanupTime;
    private const int MAX_DATA_SIZE = 1024 * 1024; // 1MB максимум на данные

    // ==================== КЛИЕНТСКАЯ ЧАСТЬ ====================
    private static readonly Dictionary<string, ZPackage> m_localData = new Dictionary<string, ZPackage>();
    private static readonly HashSet<string> m_subscriptions = new HashSet<string>();
    private static readonly Dictionary<string, Action<ZPackage>> m_dataCallbacks = new Dictionary<string, Action<ZPackage>>();
    private static readonly Dictionary<string, float> m_lastRequestTime = new Dictionary<string, float>();
    private const float REQUEST_INTERVAL = 2f; // 2 секунды между повторными запросами

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    public static void ZNet_Awake_Postfix(ZNet __instance)
    {
        if (__instance.IsServer())
        {
            // Серверные RPC
            ZRoutedRpc.instance.Register<ZPackage, string>("NetSync_RequestData", RPC_NetSyncRequestData);
            ZRoutedRpc.instance.Register<ZPackage, string>("NetSync_UpdateData", RPC_NetSyncUpdateData);
            ZRoutedRpc.instance.Register<string>("NetSync_Subscribe", RPC_NetSyncSubscribe);
            ZRoutedRpc.instance.Register<string>("NetSync_Unsubscribe", RPC_NetSyncUnsubscribe);
            ZRoutedRpc.instance.Register<ZPackage>("NetSync_BatchUpdate", RPC_NetSyncBatchUpdate);
        }
        else
        {
            // Клиентские RPC
            ZRoutedRpc.instance.Register<ZPackage, string>("NetSync_ReceiveData", RPC_NetSyncReceiveData);
            ZRoutedRpc.instance.Register<ZPackage>("NetSync_BatchData", RPC_NetSyncBatchData);
        }
    }

    // ==================== СЕРВЕРНЫЕ МЕТОДЫ ====================

    public static void RPC_NetSyncRequestData(long sender, ZPackage pkg, string dataID)
    {
        if (!ZNet.instance.IsServer()) return;

        // Проверяем размер данных
        if (m_serverData.TryGetValue(dataID, out var data) && data.Size() < MAX_DATA_SIZE) ZRoutedRpc.instance.InvokeRoutedRPC(sender, "NetSync_ReceiveData", dataID, data);
    }

    public static void RPC_NetSyncUpdateData(long sender, ZPackage pkg, string dataID)
    {
        if (!ZNet.instance.IsServer()) return;

        // Проверяем размер перед сохранением
        if (pkg.Size() > MAX_DATA_SIZE)
        {
            VBNetTweaks.LogDebug($"Data {dataID} too large: {pkg.Size()} bytes");
            return;
        }

        m_serverData[dataID] = pkg;
        ScheduleBroadcast(dataID, pkg);
    }

    public static void RPC_NetSyncBatchUpdate(long sender, ZPackage batchPkg)
    {
        if (!ZNet.instance.IsServer()) return;

        int updateCount = batchPkg.ReadInt();
        for (int i = 0; i < updateCount; i++)
        {
            string dataID = batchPkg.ReadString();
            ZPackage data = batchPkg.ReadPackage();
            
            if (data.Size() <= MAX_DATA_SIZE)
            {
                m_serverData[dataID] = data;
                ScheduleBroadcast(dataID, data);
            }
        }
    }

    private static void ScheduleBroadcast(string dataID, ZPackage data)
    {
        m_pendingUpdates[dataID] = data;

        // ИСПОЛЬЗОВАТЬ адаптивный интервал
        float broadcastInterval = AdaptiveOptimizationManager.GetBroadcastInterval();
    
        if (!m_lastBroadcastTime.TryGetValue(dataID, out float lastTime) || Time.time - lastTime >= broadcastInterval) ProcessPendingBroadcasts();
    }

    private static void ProcessPendingBroadcasts()
    {
        if (m_pendingUpdates.Count == 0) return;

        var currentTime = Time.time;
        
        // Группируем обновления по подписчикам для минимизации трафика
        var subscriberUpdates = new Dictionary<long, ZPackage>();
        
        foreach (var update in m_pendingUpdates)
        {
            string dataID = update.Key;
            ZPackage data = update.Value;

            m_lastBroadcastTime[dataID] = currentTime;

            if (m_dataSubscribers.TryGetValue(dataID, out var subscribers))
            {
                foreach (var peerID in subscribers)
                {
                    if (!subscriberUpdates.TryGetValue(peerID, out var batchPkg))
                    {
                        batchPkg = new ZPackage();
                        batchPkg.Write(0); // placeholder for count
                        subscriberUpdates[peerID] = batchPkg;
                    }

                    // Добавляем в батч
                    int currentPos = batchPkg.GetPos();
                    batchPkg.SetPos(0);
                    int count = batchPkg.ReadInt();
                    batchPkg.SetPos(0);
                    batchPkg.Write(count + 1);
                    batchPkg.SetPos(currentPos);
                    
                    batchPkg.Write(dataID);
                    batchPkg.Write(data);
                }
            }
        }

        // Отправляем батчи
        foreach (var subscriber in subscriberUpdates) ZRoutedRpc.instance.InvokeRoutedRPC(subscriber.Key, "NetSync_BatchData", subscriber.Value);

        m_pendingUpdates.Clear();

        if (VBNetTweaks.DebugEnabled.Value) VBNetTweaks.LogVerbose($"Batch broadcast to {subscriberUpdates.Count} subscribers, {m_pendingUpdates.Count} updates");
    }

    public static void RPC_NetSyncSubscribe(long sender, string dataID)
    {
        if (!ZNet.instance.IsServer()) return;

        if (!m_dataSubscribers.TryGetValue(dataID, out var subscribers))
        {
            subscribers = new HashSet<long>();
            m_dataSubscribers[dataID] = subscribers;
        }

        subscribers.Add(sender);
    }

    public static void RPC_NetSyncUnsubscribe(long sender, string dataID)
    {
        if (!ZNet.instance.IsServer()) return;

        if (m_dataSubscribers.TryGetValue(dataID, out var subscribers))
        {
            subscribers.Remove(sender);
            if (subscribers.Count == 0) m_dataSubscribers.Remove(dataID);
        }
    }

    // ==================== КЛИЕНТСКИЕ МЕТОДЫ ====================

    public static void RPC_NetSyncReceiveData(long sender, ZPackage pkg, string dataID) => ProcessReceivedData(dataID, pkg);

    public static void RPC_NetSyncBatchData(long sender, ZPackage batchPkg)
    {
        int updateCount = batchPkg.ReadInt();
        for (int i = 0; i < updateCount; i++)
        {
            string dataID = batchPkg.ReadString();
            ZPackage data = batchPkg.ReadPackage();
            ProcessReceivedData(dataID, data);
        }
    }

    private static void ProcessReceivedData(string dataID, ZPackage data)
    {
        m_localData[dataID] = data;

        if (m_dataCallbacks.TryGetValue(dataID, out var callback))
        {
            try
            {
                callback(data);
            }
            catch (Exception e)
            {
                VBNetTweaks.LogDebug($"Error in data callback for {dataID}: {e.Message}");
            }
        }
    }

    // ==================== ПУБЛИЧНЫЕ МЕТОДЫ ====================

    public static void RequestData(string dataID)
    {
        if (ZNet.instance?.IsServer() == true) return;

        // Ограничиваем частоту запросов
        if (m_lastRequestTime.TryGetValue(dataID, out float lastTime) && Time.time - lastTime < REQUEST_INTERVAL) return;

        m_lastRequestTime[dataID] = Time.time;
        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "NetSync_RequestData", dataID);
    }

    public static void UpdateData(string dataID, ZPackage data)
    {
        if (ZNet.instance?.IsServer() == true) return;

        if (data.Size() > MAX_DATA_SIZE)
        {
            VBNetTweaks.LogDebug($"Data {dataID} too large for sending: {data.Size()} bytes");
            return;
        }

        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "NetSync_UpdateData", dataID, data);
    }

    public static void UpdateMultipleData(Dictionary<string, ZPackage> dataUpdates)
    {
        if (ZNet.instance?.IsServer() == true) return;

        // Батчим несколько обновлений в один пакет
        var batchPkg = new ZPackage();
        batchPkg.Write(dataUpdates.Count);
        
        foreach (var update in dataUpdates)
        {
            if (update.Value.Size() <= MAX_DATA_SIZE)
            {
                batchPkg.Write(update.Key);
                batchPkg.Write(update.Value);
            }
        }

        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "NetSync_BatchUpdate", batchPkg);
    }

    public static void Subscribe(string dataID)
    {
        if (ZNet.instance?.IsServer() == true) return;

        if (m_subscriptions.Add(dataID)) ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "NetSync_Subscribe", dataID);
    }

    public static void Unsubscribe(string dataID)
    {
        if (ZNet.instance?.IsServer() == true) return;

        if (m_subscriptions.Remove(dataID)) ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "NetSync_Unsubscribe", dataID);
    }

    public static ZPackage GetLocalData(string dataID)
    {
        m_localData.TryGetValue(dataID, out var data);
        return data;
    }

    public static void RegisterDataCallback(string dataID, Action<ZPackage> callback) => m_dataCallbacks[dataID] = callback;

    public static void UnregisterDataCallback(string dataID) => m_dataCallbacks.Remove(dataID);

    // ==================== ОПТИМИЗАЦИИ ДЛЯ 40 ИГРОКОВ ====================

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
    public static void ZNet_Update_Postfix(ZNet __instance)
    {
        if (!__instance.IsServer()) return;

        // ВЫЗЫВАТЬ обновление адаптивных настроек каждый кадр
        AdaptiveOptimizationManager.UpdateAdaptiveSettings();

        // Периодическая очистка с динамическим интервалом
        float cleanupInterval = AdaptiveOptimizationManager.GetBroadcastInterval() * 100f;
        if (Time.time - m_lastCleanupTime > cleanupInterval)
        {
            CleanupOldData();
            m_lastCleanupTime = Time.time;
        }

        // Принудительная рассылка с адаптивной частотой
        float broadcastFreq = 60f / AdaptiveOptimizationManager.GetBroadcastInterval();
        if (m_pendingUpdates.Count > 0 && Time.frameCount % (int)broadcastFreq == 0) ProcessPendingBroadcasts();
    }

    private static void CleanupOldData()
    {
        // Очищаем данные без подписчиков
        var toRemove = m_serverData.Where(x => !m_dataSubscribers.ContainsKey(x.Key) && !IsPersistentData(x.Key)).Select(x => x.Key).ToList();

        foreach (var dataID in toRemove) m_serverData.Remove(dataID);
        if (toRemove.Count > 0 && VBNetTweaks.DebugEnabled.Value) VBNetTweaks.LogVerbose($"Cleaned up {toRemove.Count} unused data entries");
    }

    private static bool IsPersistentData(string dataID)
    {
        // Помечаем важные данные которые не должны очищаться
        return dataID.StartsWith("Config_") || dataID.StartsWith("World_");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    public static void ZNet_OnDestroy_Postfix()
    {
        m_serverData.Clear();
        m_dataSubscribers.Clear();
        m_localData.Clear();
        m_subscriptions.Clear();
        m_dataCallbacks.Clear();
        m_pendingUpdates.Clear();
        m_lastBroadcastTime.Clear();
        m_lastRequestTime.Clear();
    }
}
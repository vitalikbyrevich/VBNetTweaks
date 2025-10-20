using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VBNetTweaks;

[HarmonyPatch]
public static class NetSyncServer
{
    private static readonly Dictionary<string, ZPackage> m_serverData = new Dictionary<string, ZPackage>();
    private static readonly Dictionary<string, HashSet<long>> m_dataSubscribers = new Dictionary<string, HashSet<long>>();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), "Awake")]
    public static void ZNet_Awake_Postfix(ZNet __instance)
    {
        if (!__instance.IsServer()) return;

        // Регистрируем RPC методы на сервере
        ZRoutedRpc.instance.Register<ZPackage, string>("NetSync_RequestData", RPC_NetSyncRequestData);
        ZRoutedRpc.instance.Register<ZPackage, string>("NetSync_UpdateData", RPC_NetSyncUpdateData);
        ZRoutedRpc.instance.Register<string>("NetSync_Subscribe", RPC_NetSyncSubscribe);
        ZRoutedRpc.instance.Register<string>("NetSync_Unsubscribe", RPC_NetSyncUnsubscribe);
    }

    public static void RPC_NetSyncRequestData(long sender, ZPackage pkg, string dataID)
    {
        if (!ZNet.instance.IsServer()) return;

        if (m_serverData.TryGetValue(dataID, out var data))
        {
            // Отправляем данные обратно запросившему клиенту
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, "NetSync_ReceiveData", dataID, data);
            
            if (VBNetTweaks.DebugEnabled.Value)
            {
                VBNetTweaks.LogVerbose($"Sent data {dataID} to client {sender}, size: {data.Size()}");
            }
        }
    }

    public static void RPC_NetSyncUpdateData(long sender, ZPackage pkg, string dataID)
    {
        if (!ZNet.instance.IsServer()) return;

        m_serverData[dataID] = pkg;

        // Рассылаем обновление всем подписчикам
        if (m_dataSubscribers.TryGetValue(dataID, out var subscribers))
        {
            foreach (var peerID in subscribers)
            {
                if (peerID != sender) // Не отправляем обратно отправителю
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(peerID, "NetSync_ReceiveData", dataID, pkg);
                }
            }
        }

        if (VBNetTweaks.DebugEnabled.Value)
        {
            VBNetTweaks.LogVerbose($"Data {dataID} updated by {sender}, size: {pkg.Size()}, subscribers: {subscribers?.Count ?? 0}");
        }
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

        // Немедленно отправляем текущие данные новому подписчику
        if (m_serverData.TryGetValue(dataID, out var data))
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, "NetSync_ReceiveData", dataID, data);
        }

        if (VBNetTweaks.DebugEnabled.Value)
        {
            VBNetTweaks.LogVerbose($"Client {sender} subscribed to {dataID}, total subscribers: {subscribers.Count}");
        }
    }

    public static void RPC_NetSyncUnsubscribe(long sender, string dataID)
    {
        if (!ZNet.instance.IsServer()) return;

        if (m_dataSubscribers.TryGetValue(dataID, out var subscribers))
        {
            subscribers.Remove(sender);
            
            if (subscribers.Count == 0)
            {
                m_dataSubscribers.Remove(dataID);
            }
        }

        if (VBNetTweaks.DebugEnabled.Value)
        {
            VBNetTweaks.LogVerbose($"Client {sender} unsubscribed from {dataID}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    public static void ZNet_OnDestroy_Postfix()
    {
        m_serverData.Clear();
        m_dataSubscribers.Clear();
    }
}
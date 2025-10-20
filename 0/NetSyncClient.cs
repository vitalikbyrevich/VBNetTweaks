using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VBNetTweaks;

[HarmonyPatch]
public static class NetSyncClient
{
    private static readonly Dictionary<string, ZPackage> m_localData = new Dictionary<string, ZPackage>();
    private static readonly HashSet<string> m_subscriptions = new HashSet<string>();
    private static readonly Dictionary<string, Action<ZPackage>> m_dataCallbacks = new Dictionary<string, Action<ZPackage>>();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), "Awake")]
    public static void ZNet_Awake_Postfix(ZNet __instance)
    {
        if (__instance.IsServer()) return;

        // Регистрируем RPC методы на клиенте
        ZRoutedRpc.instance.Register<ZPackage, string>("NetSync_ReceiveData", RPC_NetSyncReceiveData);
    }

    public static void RPC_NetSyncReceiveData(long sender, ZPackage pkg, string dataID)
    {
        m_localData[dataID] = pkg;

        // Вызываем колбэк если он зарегистрирован
        if (m_dataCallbacks.TryGetValue(dataID, out var callback))
        {
            try
            {
                callback(pkg);
            }
            catch (Exception e)
            {
                VBNetTweaks.LogDebug($"Error in data callback for {dataID}: {e.Message}");
            }
        }

        if (VBNetTweaks.DebugEnabled.Value)
        {
            VBNetTweaks.LogVerbose($"Received data {dataID} from server, size: {pkg.Size()}");
        }
    }

    public static void RequestData(string dataID)
    {
        if (ZNet.instance?.IsServer() == true) return;

        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "NetSync_RequestData", dataID);
        
        if (VBNetTweaks.DebugEnabled.Value)
        {
            VBNetTweaks.LogVerbose($"Requested data {dataID} from server");
        }
    }

    public static void UpdateData(string dataID, ZPackage data)
    {
        if (ZNet.instance?.IsServer() == true) return;

        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "NetSync_UpdateData", dataID, data);
        
        if (VBNetTweaks.DebugEnabled.Value)
        {
            VBNetTweaks.LogVerbose($"Sent data update {dataID} to server, size: {data.Size()}");
        }
    }

    public static void Subscribe(string dataID)
    {
        if (ZNet.instance?.IsServer() == true) return;

        if (m_subscriptions.Add(dataID))
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "NetSync_Subscribe", dataID);
            
            if (VBNetTweaks.DebugEnabled.Value)
            {
                VBNetTweaks.LogVerbose($"Subscribed to {dataID}");
            }
        }
    }

    public static void Unsubscribe(string dataID)
    {
        if (ZNet.instance?.IsServer() == true) return;

        if (m_subscriptions.Remove(dataID))
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "NetSync_Unsubscribe", dataID);
            
            if (VBNetTweaks.DebugEnabled.Value)
            {
                VBNetTweaks.LogVerbose($"Unsubscribed from {dataID}");
            }
        }
    }

    public static ZPackage GetLocalData(string dataID)
    {
        m_localData.TryGetValue(dataID, out var data);
        return data;
    }

    public static void RegisterDataCallback(string dataID, Action<ZPackage> callback)
    {
        m_dataCallbacks[dataID] = callback;
    }

    public static void UnregisterDataCallback(string dataID)
    {
        m_dataCallbacks.Remove(dataID);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    public static void ZNet_OnDestroy_Postfix()
    {
        m_localData.Clear();
        m_subscriptions.Clear();
        m_dataCallbacks.Clear();
    }
}
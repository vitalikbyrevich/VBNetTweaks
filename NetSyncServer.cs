// VBNetTweaks.ServerTick.cs
namespace VBNetTweaks;

using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;

[HarmonyPatch]
public static class NetSyncServer
{
    // Конфиг с безопасными значениями по умолчанию
    public static int TickRateHz = 30;
    public static float NearRadius = 40f;
    public static float MidRadius = 120f;
    public static float FarRadius = 250f;
    
    public static int MidSkipTicks => Mathf.Max(1, (int)(VBNetTweaks.GetSendInterval() * 10f) - 1);
    public static int FarSkipTicks => Mathf.Max(3, (int)(VBNetTweaks.GetSendInterval() * 20f) - 1);

    // Снапшоты на сервере для дельта-вычисления
    private static readonly Dictionary<ZDOID, ServerSnapshot> _lastSent = new(8192);
    private static float _tickAccum;
    private static int _tickIndex;
    public static bool _isInitialized = false;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZDOMan), "Update")]
    public static void ZDOMan_Update_Postfix(ZDOMan __instance)
    {
        if (!Helper.IsServer() || !_isInitialized) return;

        float tickInterval = 1f / TickRateHz;
        _tickAccum += Time.deltaTime;

        while (_tickAccum >= tickInterval)
        {
            _tickAccum -= tickInterval;
            _tickIndex++;
            SendTick(__instance, _tickIndex);
        }
    }

    // ИСПРАВЛЕННЫЙ метод - безопасная инициализация
    // В методе ZNet_Awake_Postfix в NetSyncServer.cs:
    // В NetSyncServer.cs - упрощенная инициализация
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), "Awake")]
    public static void ZNet_Awake_Postfix(ZNet __instance)
    {
        try
        {
            if (__instance == null || !__instance.IsServer()) return;

            // Простая инициализация без RPC регистрации
            _isInitialized = true;
            VBNetTweaks.LogDebug("NetSyncServer initialized - RPC will be handled per-peer");
        }
        catch (Exception e)
        {
            VBNetTweaks.LogDebug($"Error initializing NetSyncServer: {e.Message}");
        }
    }

// В методе SendTick отправляем напрямую peer RPC:
   

    // Альтернативная инициализация - если Awake не сработал
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), "Start")]
    public static void ZNet_Start_Postfix(ZNet __instance)
    {
        if (_isInitialized) return;
        
        try
        {
            if (__instance == null || !__instance.IsServer()) return;
            
            if (__instance.m_routedRpc != null)
            {
                __instance.m_routedRpc.Register<ZRpc>("VBNT_RequestFullSync", OnRequestFullSync);
                _isInitialized = true;
                VBNetTweaks.LogDebug("NetSyncServer initialized in Start");
            }
        }
        catch (Exception e)
        {
            VBNetTweaks.LogDebug($"Error in ZNet_Start_Postfix: {e.Message}");
        }
    }

    private static void OnRequestFullSync(long l, ZRpc zRpc)
    {
        try
        {
            // Клиент запросил полную синхронизацию
            _lastSent.Clear();
            VBNetTweaks.LogVerbose("Full sync requested by client");
        }
        catch (Exception e)
        {
            VBNetTweaks.LogDebug($"Error in OnRequestFullSync: {e.Message}");
        }
    }

    private static void SendTick(ZDOMan zdoMan, int tick)
    {
        try
        {
            var peers = zdoMan.m_peers;
            if (peers == null || peers.Count == 0) return;

            var allZDOs = zdoMan.m_objectsByID;
            if (allZDOs == null || allZDOs.Count == 0) return;

            // Оптимизация: предварительный расчет квадратов расстояний
            float nearSqr = NearRadius * NearRadius;
            float midSqr = MidRadius * MidRadius;
            float farSqr = FarRadius * FarRadius;

            for (int p = 0; p < peers.Count; p++)
            {
                var peer = peers[p];
                if (peer?.m_peer?.m_rpc == null || !peer.m_peer.m_rpc.IsConnected()) continue;

                Vector3 peerPos = peer.m_peer.m_refPos;
                var batch = new List<byte>(4096);
                int sent = 0;

                foreach (var kv in allZDOs)
                {
                    var zdo = kv.Value;
                    if (zdo == null || !zdo.IsValid()) continue;

                    Vector3 pos = zdo.m_position;
                    float distSqr = (pos - peerPos).sqrMagnitude;

                    // Приоритизация по дистанции
                    bool sendNow;
                    ZoneClass zone;
                    if (distSqr <= nearSqr) 
                    { 
                        sendNow = true; 
                        zone = ZoneClass.Near; 
                    }
                    else if (distSqr <= midSqr) 
                    { 
                        sendNow = (tick % (MidSkipTicks + 1) == 0); 
                        zone = ZoneClass.Mid; 
                    }
                    else if (distSqr <= farSqr) 
                    { 
                        sendNow = (tick % (FarSkipTicks + 1) == 0); 
                        zone = ZoneClass.Far; 
                    }
                    else 
                    { 
                        continue; 
                    }

                    if (!sendNow) continue;

                    // Вычисляем дельту
                    var current = ServerSnapshot.FromZDO(zdo);
                    var zdoId = zdo.m_uid;

                    if (_lastSent.TryGetValue(zdoId, out var prev))
                    {
                        var delta = DeltaCodec.MakeDelta(prev, current);
                        if (delta.HasAny)
                        {
                            DeltaCodec.WriteDelta(batch, zdoId, delta, zone);
                            _lastSent[zdoId] = current;
                            sent++;
                        }
                    }
                    else
                    {
                        // Первая синхронизация
                        var delta = DeltaCodec.MakeDelta(ServerSnapshot.Empty, current);
                        DeltaCodec.WriteDelta(batch, zdoId, delta, zone);
                        _lastSent[zdoId] = current;
                        sent++;
                    }
                }

                // Отправляем батч если есть данные
                if (sent > 0 && batch.Count > 0)
                {
                    try
                    {
                        var pkg = new ZPackage();
                        pkg.Write(batch.ToArray());
                
                        // Отправляем напрямую через peer RPC
                        peer.m_peer.m_rpc.Invoke("VBNT_DeltaBatch", pkg);
                
                        if (VBNetTweaks.DebugEnabled.Value && VBNetTweaks.VerboseLogging.Value && tick % 60 == 0)
                            VBNetTweaks.LogVerbose($"Tick {tick}: sent {sent} deltas, size: {batch.Count} bytes");
                    }
                    catch (Exception e)
                    {
                        VBNetTweaks.LogDebug($"Error sending delta batch: {e.Message}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            VBNetTweaks.LogDebug($"Error in SendTick: {e.Message}");
        }
    }

    public enum ZoneClass { Near, Mid, Far }

    private struct ServerSnapshot
    {
        public Vector3 Pos;
        public Quaternion Rot;
        public Vector3 Vel;
        public int HP;
        public uint Flags;

        public static ServerSnapshot FromZDO(ZDO z)
        {
            if (z == null) return Empty;
            
            return new ServerSnapshot 
            {
                Pos = z.m_position,
                Rot = z.GetRotation(),
                Vel = z.GetVec3("velocity", Vector3.zero),
                HP = z.GetInt(ZDOVars.s_health, -1),
                Flags = (uint)z.GetInt("flags", 0)
            };
        }

        public static readonly ServerSnapshot Empty = default;
    }

    private struct Delta
    {
        public bool HasAny;
        public byte Mask;
        public Vector3 Pos;
        public Quaternion Rot;
        public Vector3 Vel;
        public int HP;
        public uint Flags;
    }

    private static class DeltaCodec
    {
        public static Delta MakeDelta(ServerSnapshot prev, ServerSnapshot curr)
        {
            byte mask = 0;
            var delta = new Delta();

            // Позиция - проверяем значимое изменение
            if ((curr.Pos - prev.Pos).sqrMagnitude > 0.0001f) 
            { 
                delta.Pos = curr.Pos; 
                mask |= 1; 
            }
            
            // Вращение - проверяем значимое изменение
            if (Quaternion.Angle(curr.Rot, prev.Rot) > 0.5f) 
            { 
                delta.Rot = curr.Rot; 
                mask |= 2; 
            }
            
            // Скорость
            if ((curr.Vel - prev.Vel).sqrMagnitude > 0.0001f) 
            { 
                delta.Vel = curr.Vel; 
                mask |= 4; 
            }
            
            // HP
            if (curr.HP != prev.HP) 
            { 
                delta.HP = curr.HP; 
                mask |= 8; 
            }
            
            // Флаги
            if (curr.Flags != prev.Flags) 
            { 
                delta.Flags = curr.Flags; 
                mask |= 16; 
            }

            delta.Mask = mask;
            delta.HasAny = mask != 0;
            return delta;
        }

        public static void WriteDelta(List<byte> buffer, ZDOID id, Delta delta, ZoneClass zone)
        {
            WriteID(buffer, id);
            buffer.Add(delta.Mask);
            buffer.Add((byte)zone);

            if ((delta.Mask & 1) != 0) WriteVec3Q(buffer, delta.Pos, 0.01f);
            if ((delta.Mask & 2) != 0) WriteRotQ(buffer, delta.Rot);
            if ((delta.Mask & 4) != 0) WriteVec3Q(buffer, delta.Vel, 0.01f);
            if ((delta.Mask & 8) != 0) WriteInt(buffer, delta.HP);
            if ((delta.Mask & 16) != 0) WriteUInt(buffer, delta.Flags);
        }

        private static void WriteID(List<byte> buffer, ZDOID id) 
        { 
            WriteLong(buffer, id.UserID); 
            WriteUInt(buffer, id.ID); 
        }

        private static void WriteVec3Q(List<byte> buffer, Vector3 v, float step)
        {
            WriteShort(buffer, (short)Mathf.RoundToInt(v.x / step));
            WriteShort(buffer, (short)Mathf.RoundToInt(v.y / step));
            WriteShort(buffer, (short)Mathf.RoundToInt(v.z / step));
        }

        private static void WriteRotQ(List<byte> buffer, Quaternion q)
        {
            buffer.Add((byte)((q.x * 0.5f + 0.5f) * 255));
            buffer.Add((byte)((q.y * 0.5f + 0.5f) * 255));
            buffer.Add((byte)((q.z * 0.5f + 0.5f) * 255));
            buffer.Add((byte)((q.w * 0.5f + 0.5f) * 255));
        }

        private static void WriteShort(List<byte> buffer, short value) 
        { 
            buffer.Add((byte)(value & 0xFF));
            buffer.Add((byte)((value >> 8) & 0xFF));
        }

        private static void WriteInt(List<byte> buffer, int value)
        {
            buffer.Add((byte)(value & 0xFF));
            buffer.Add((byte)((value >> 8) & 0xFF));
            buffer.Add((byte)((value >> 16) & 0xFF));
            buffer.Add((byte)((value >> 24) & 0xFF));
        }

        private static void WriteUInt(List<byte> buffer, uint value)
        {
            buffer.Add((byte)(value & 0xFF));
            buffer.Add((byte)((value >> 8) & 0xFF));
            buffer.Add((byte)((value >> 16) & 0xFF));
            buffer.Add((byte)((value >> 24) & 0xFF));
        }

        private static void WriteLong(List<byte> buffer, long value)
        {
            for (int i = 0; i < 8; i++)
            {
                buffer.Add((byte)(value & 0xFF));
                value >>= 8;
            }
        }
    }
}
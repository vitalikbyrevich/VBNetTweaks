namespace VBNetTweaks.Patches;

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

public class Owner_Patch
{
    [HarmonyPatch]
    public static class ClientHealthMonitor
    {
        private static float _deltaTimeAccumulator;
        private static int _frameCount;
        private static float _avgDeltaTime = 0.016f;
        private static float _reportTimer;

        [HarmonyPatch(typeof(Player), nameof(Player.LateUpdate))]
        [HarmonyPostfix]
        public static void TrackPerformance(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            if (Helper.IsDedicated()) return;

            _deltaTimeAccumulator += Time.deltaTime;
            _frameCount++;

            if (_frameCount >= 60)
            {
                _avgDeltaTime = _deltaTimeAccumulator / _frameCount;
                _deltaTimeAccumulator = 0f;
                _frameCount = 0;
            }

            _reportTimer += Time.deltaTime;
            if (_reportTimer >= VBNetTweaks.c_UnhealthyDuration.Value)
            {
                _reportTimer = 0f;
                if (ZNet.instance != null) ZNet.instance.m_serverSyncedPlayerData["avgDeltaTime"] = _avgDeltaTime.ToString("F4");
            }
        }

        public static float GetAvgDeltaTime() => _avgDeltaTime;
    }

    public static class PeerHealthEvaluator
    {
        // Пороги входа в карантин (теперь из конфига)
        private static float EnterMaxDt => VBNetTweaks.c_EnterMaxDeltaTime.Value;
        private static float EnterMinQuality => VBNetTweaks.c_EnterMinQuality.Value;
        private static int EnterStaleSeconds => (int)VBNetTweaks.c_EnterStaleSeconds.Value;
        private static int EnterSamples => (int)VBNetTweaks.c_EnterSamples.Value;

        // Пороги выхода из карантина
        private static float ExitMaxDt => VBNetTweaks.c_ExitMaxDeltaTime.Value;
        private static float ExitMinQuality => VBNetTweaks.c_ExitMinQuality.Value;
        private static float ExitDuration => VBNetTweaks.c_ExitDuration.Value;

        private static readonly Dictionary<long, PeerHealthState> _states = new();

        private class PeerHealthState
        {
            public bool InQuarantine;
            public int BadSamples;
            public int GoodSamples;
            public float UnhealthySince;
            public float QuarantineSince;
            public float LastReportTime;
            public float LastAvgDt;
            // Кеш качества соединения
            public float CachedRemoteQuality;
            public float CachedQualityTime;
        }

        // Вызывается из ReleaseNearbyZDOS для проверки, должен ли пир быть в карантине
        public static bool ShouldQuarantine(long uid, ZNetPeer peer)
        {
            if (!_states.TryGetValue(uid, out var st))
            {
                st = new PeerHealthState();
                _states[uid] = st;
            }

            float now = Time.time;

            // Читаем отчёт клиента
            bool hasReport = peer.m_serverSyncedPlayerData.TryGetValue("avgDeltaTime", out string dtStr);
            if (hasReport)
            {
                st.LastReportTime = now;
                if (float.TryParse(dtStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float dt))
                {
                    st.LastAvgDt = dt;
                }
            }

            // Обновляем кеш качества не чаще чем раз в 2 секунды
            if (now - st.CachedQualityTime > 2f)
            {
                peer.m_socket.GetConnectionQuality(out _, out float remoteQuality, out _, out _, out _);
                st.CachedRemoteQuality = remoteQuality;
                st.CachedQualityTime = now;
            }

            bool unhealthy = EvaluateUnhealthy(st, now);
            bool healthy = EvaluateHealthy(st, now);

            if (st.InQuarantine)
            {
                if (healthy)
                {
                    st.GoodSamples++;
                    if (now - st.QuarantineSince > ExitDuration && st.GoodSamples >= EnterSamples)
                    {
                        st.InQuarantine = false;
                        st.GoodSamples = 0;
                        st.BadSamples = 0;
                        if (VBNetTweaks.c_VerboseLogging.Value)
                            Helper.LogVerbose($"[Health] Peer {uid} exited quarantine");
                        return false;
                    }
                }
                else
                {
                    st.GoodSamples = 0;
                }
                return true;
            }
            else
            {
                if (unhealthy)
                {
                    st.BadSamples++;
                    if (st.BadSamples == 1) st.UnhealthySince = now;
                    if (st.BadSamples >= EnterSamples && now - st.UnhealthySince > EnterStaleSeconds)
                    {
                        st.InQuarantine = true;
                        st.QuarantineSince = now;
                        if (VBNetTweaks.c_VerboseLogging.Value)
                            Helper.LogVerbose($"[Health] Peer {uid} entered quarantine (bad samples: {st.BadSamples}, dt: {st.LastAvgDt:F3})");
                        return true;
                    }
                }
                else
                {
                    st.BadSamples = 0;
                }
                return false;
            }
        }

        private static bool EvaluateUnhealthy(PeerHealthState st, float now)
        {
            // Нет отчёта дольше EnterStaleSeconds → клиент завис
            if (now - st.LastReportTime > EnterStaleSeconds) return true;

            // Низкий FPS
            if (st.LastAvgDt > EnterMaxDt) return true;

            // Плохое качество соединения
            if (st.CachedRemoteQuality < EnterMinQuality) return true;

            return false;
        }

        private static bool EvaluateHealthy(PeerHealthState st, float now)
        {
            // Отчёт должен быть свежим (не старше 5 секунд)
            if (now - st.LastReportTime > 5f) return false;

            // FPS должен быть выше порога выхода
            if (st.LastAvgDt > ExitMaxDt) return false;

            // Качество должно быть выше порога выхода
            if (st.CachedRemoteQuality < ExitMinQuality) return false;

            return true;
        }

        // Чистое чтение состояния (без мутации)
        public static bool IsPeerUnhealthy(ZNetPeer peer)
        {
            if (peer == null) return false;
            return _states.TryGetValue(peer.m_uid, out var st) && st.InQuarantine;
        }
    }

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ReleaseNearbyZDOS))]
    [HarmonyPrefix]
    public static bool ReleaseNearbyZDOS_Quarantine(
        ZDOMan __instance, Vector3 refPosition, long uid)
    {
        if (uid == __instance.m_sessionID) return true; // сервер — всегда здоров

        ZNetPeer peer = ZNet.instance.GetPeer(uid);
        if (peer == null) return true;

        // Обновляем состояние карантина
        if (!PeerHealthEvaluator.ShouldQuarantine(uid, peer))
            return true; // здоров → ванила

        // Пир в карантине — проверяем, есть ли здоровый в той же зоне
        Vector2i zone = ZoneSystem.GetZone(refPosition);
        long healthyUid = FindHealthyPeerInZone(__instance, zone, uid);

        // Если здорового рядом нет — ничего не делаем, оставляем владельца больному
        if (healthyUid == 0)
        {
            if (VBNetTweaks.c_VerboseLogging.Value)
                Helper.LogVerbose($"[Quarantine] Peer {uid} in quarantine but no healthy peer nearby, leaving ownership");
            return false; // заменяем ванилу, но ничего не делаем
        }

        // Есть здоровый — забираем у больного объекты в его зоне и передаём здоровому
        __instance.m_tempNearObjects.Clear();
        __instance.FindSectorObjects(zone, ZoneSystem.instance.m_activeArea, 0, __instance.m_tempNearObjects);

        int freed = 0;
        foreach (ZDO zdo in __instance.m_tempNearObjects)
        {
            if (!zdo.Persistent) continue;
            if (zdo.GetOwner() != uid) continue;

            // Проверяем, что объект находится в зоне здорового пира
            Vector2i sector = zdo.GetSector();
            bool inHealthyZone = IsZDOInPeerZone(__instance, healthyUid, sector);
            if (!inHealthyZone) continue; // не передаём, если здоровый не может его видеть

            // Передаём владение здоровому
            zdo.SetOwner(healthyUid);
            freed++;
        }

        if (freed > 0 && VBNetTweaks.c_VerboseLogging.Value)
        {
            Helper.LogVerbose($"[Quarantine] Transferred {freed} ZDOs from quarantined peer {uid} to healthy peer {healthyUid}");
        }

        return false; // ванилу пропускаем
    }

    private static long FindHealthyPeerInZone(ZDOMan man, Vector2i zone, long excludeUid)
    {
        int activeArea = ZoneSystem.instance.m_activeArea;
        foreach (var zdoPeer in man.m_peers)
        {
            ZNetPeer peer = zdoPeer.m_peer;
            long uid = peer.m_uid;
            if (uid == excludeUid) continue;
            if (PeerHealthEvaluator.IsPeerUnhealthy(peer)) continue;

            Vector2i peerZone = ZoneSystem.GetZone(peer.m_refPos);
            int dist = Math.Max(Math.Abs(peerZone.x - zone.x), Math.Abs(peerZone.y - zone.y));
            if (dist <= activeArea)
                return uid;
        }
        return 0;
    }

    private static bool IsZDOInPeerZone(ZDOMan man, long peerUid, Vector2i sector)
    {
        ZNetPeer peer = ZNet.instance.GetPeer(peerUid);
        if (peer == null) return false;
        Vector2i peerZone = ZoneSystem.GetZone(peer.m_refPos);
        int activeArea = ZoneSystem.instance.m_activeArea;
        int dist = Math.Max(Math.Abs(peerZone.x - sector.x), Math.Abs(peerZone.y - sector.y));
        return dist <= activeArea;
    }
}
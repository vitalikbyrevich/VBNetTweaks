

namespace VBNetTweaks
{
    public static class AdaptiveThrottler
    {
        private static float _lastEvalTime;
        private static float _currentInterval = 0.05f; // старт по умолчанию

        private const float LowPingThreshold = 0.15f; // < 150 мс — можно ускоряться
        private const float HighPingThreshold = 0.60f; // > 600 мс — надо замедляться
        private const float MinInterval = 0.03f; // минимум
        private const float MaxInterval = 0.10f; // максимум
        private const float EvalInterval = 1.0f; // раз в секунду пересчитываем

        private class PeerStats
        {
            public float lastPing;
            public float lastPingTime;
            public int missedPings;
        }

        private static readonly Dictionary<ZNetPeer, PeerStats> _peerStats = new();

        public static void Update(ZNet znet, float dt)
        {
            if (!znet || !Helper.IsServer()) return;

            _lastEvalTime += dt;
            if (_lastEvalTime < EvalInterval) return;
            _lastEvalTime = 0f;

            List<ZNetPeer> peers = znet.m_peers;
            if (peers == null || peers.Count == 0)
            {
                _currentInterval = VBNetTweaks.SendInterval?.Value ?? 0.05f;
                return;
            }

            float now = Time.time;
            float maxReliablePing = 0f;
            float packetLoss = 0f;
            int activePeers = 0;

            for (int i = 0; i < peers.Count; i++)
            {
                var p = peers[i];
                if (p?.m_rpc == null) continue;

                // Получаем статистику пира
                if (!_peerStats.TryGetValue(p, out var stats))
                {
                    stats = new PeerStats();
                    _peerStats[p] = stats;
                }

                float ping = p.m_rpc.GetTimeSinceLastPing();

                // Обновляем статистику
                if (ping < 10f) // Разумный пинг (меньше 10 секунд)
                {
                    if (ping > stats.lastPing * 2f) // Пинг резко вырос
                        stats.missedPings++;
                    else stats.missedPings = Mathf.Max(0, stats.missedPings - 1);

                    stats.lastPing = ping;
                    stats.lastPingTime = now;
                    activePeers++;
                }
                else if (now - stats.lastPingTime > 5f) // Нет пинга >5 секунд
                {
                    stats.missedPings += 2; // Штраф
                }

                // Используем пинг только если он актуален
                if (now - stats.lastPingTime < 3f)
                {
                    if (ping > maxReliablePing) maxReliablePing = ping;
                    packetLoss += stats.missedPings;
                }
            }

            if (activePeers == 0)
            {
                _currentInterval = VBNetTweaks.SendInterval?.Value ?? 0.05f;
                return;
            }

            packetLoss /= activePeers; // Средняя потеря пакетов

            float baseInterval = VBNetTweaks.SendInterval?.Value ?? 0.05f;
            float newInterval = baseInterval;

            // Адаптируем на основе пинга
            if (maxReliablePing < LowPingThreshold) newInterval = Mathf.Max(MinInterval, baseInterval * 0.7f);
            else if (maxReliablePing > HighPingThreshold) newInterval = Mathf.Min(MaxInterval, baseInterval * 1.5f);

            // Дополнительная адаптация на основе потери пакетов
            if (packetLoss > 2f) // Много потерянных пингов
                newInterval = Mathf.Min(MaxInterval, newInterval * 1.3f);
            else if (packetLoss < 0.5f && maxReliablePing < HighPingThreshold) newInterval = Mathf.Max(MinInterval, newInterval * 0.9f);

            _currentInterval = newInterval;

            if (VBNetTweaks.DebugEnabled.Value)
            {
                VBNetTweaks.LogDebug($"AdaptiveThrottler: ping={maxReliablePing:0.000}s loss={packetLoss:F1} " + $"base={baseInterval:0.000}s -> interval={_currentInterval:0.000}s");
            }
        }

        // Очистка при дисконнекте
        public static void OnPeerDisconnected(ZNetPeer peer) => _peerStats.Remove(peer);

        public static float GetInterval(float fallback) => _currentInterval > 0f ? _currentInterval : fallback;
    }
}
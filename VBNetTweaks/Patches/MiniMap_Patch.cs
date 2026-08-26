namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class MiniMap_Patch
    {
        public class PlayerTrackData
        {
            public Vector3 lastRealPosition;
            public Vector3 lastRealVelocity;
            public Vector3 smoothPosition;
            public Vector3 smoothVelocity;
            public float lastUpdateTime;
            public float teleportEndTime;
        }

        public static readonly Dictionary<long, PlayerTrackData> _playerTracks = new Dictionary<long, PlayerTrackData>();
        private static int _lastCleanupFrame;

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePlayerPins)), HarmonyPostfix]
        private static void UpdatePlayerPins_SmoothPostfix(Minimap __instance, float dt)
        {
            if (!VBNetTweaks.c_ModuleMapPositionSync.Value) return;
            if (Helper.IsDedicated()) return;
            if (__instance.m_playerPins == null || __instance.m_tempPlayerInfo == null) return;

            int count = Mathf.Min(__instance.m_playerPins.Count, __instance.m_tempPlayerInfo.Count);
            if (count == 0) return;

            float now = Time.time;
            float maxSpeed = VBNetTweaks.c_MapMaxPredictionSpeed.Value;

            if (Time.frameCount - _lastCleanupFrame > 600)
            {
                _lastCleanupFrame = Time.frameCount;
                CleanupStaleTracks(now);
            }

            for (int i = 0; i < count; i++)
            {
                var pin = __instance.m_playerPins[i];
                var info = __instance.m_tempPlayerInfo[i];

                if (!info.m_publicPosition) continue;

                long playerId = GetPlayerId(info);
                if (playerId == 0) continue;

                Vector3 realPosition = info.m_position;

                if (!_playerTracks.TryGetValue(playerId, out var track))
                {
                    track = new PlayerTrackData();
                    _playerTracks[playerId] = track;
                    track.smoothPosition = realPosition;
                    track.lastRealPosition = realPosition;
                    track.lastUpdateTime = now;
                    pin.m_pos = realPosition;
                    continue;
                }

                float deltaTime = now - track.lastUpdateTime;

                if (deltaTime > 0.001f)
                {
                    Vector3 deltaPos = realPosition - track.lastRealPosition;
                    float distance = deltaPos.magnitude;

                    if (distance > 50f)
                    {
                        track.teleportEndTime = now + 0.3f;
                        track.lastRealVelocity = Vector3.zero;
                    }
                    else if (deltaTime < 2f && distance > 0.01f)
                    {
                        track.lastRealVelocity = deltaPos / deltaTime;
                        if (track.lastRealVelocity.magnitude > 100f) track.lastRealVelocity = track.lastRealVelocity.normalized * 100f;
                    }
                }

                if (deltaTime >= 2f)
                {
                    float distFromSmooth = Vector3.Distance(track.smoothPosition, realPosition);
                    if (distFromSmooth > 50f)
                    {
                        track.teleportEndTime = now + 0.3f;
                        track.lastRealVelocity = Vector3.zero;
                    }
                }

                track.lastRealPosition = realPosition;
                track.lastUpdateTime = now;

                if (track.teleportEndTime > now)
                {
                    pin.m_pos = realPosition;
                    track.smoothPosition = realPosition;
                    track.smoothVelocity = Vector3.zero;
                    continue;
                }

                Vector3 targetPosition = realPosition;
                float timeSinceUpdate = now - track.lastUpdateTime;
                if (timeSinceUpdate > 0.05f && track.lastRealVelocity.magnitude > 0.5f)
                {
                    float predictTime = Mathf.Min(timeSinceUpdate, 0.5f);
                    targetPosition = realPosition + track.lastRealVelocity * predictTime;
                }

                float distToTarget = Vector3.Distance(track.smoothPosition, targetPosition);
                float smoothTime = distToTarget > 30f ? 0.01f : distToTarget > 10f ? 0.03f : 0.05f;

                track.smoothPosition = Vector3.SmoothDamp(track.smoothPosition, targetPosition, ref track.smoothVelocity, smoothTime, maxSpeed, dt);

                pin.m_pos = track.smoothPosition;
            }
        }

        private static long GetPlayerId(ZNet.PlayerInfo info)
        {
            if (!info.m_characterID.IsNone()) return info.m_characterID.UserID;
            if (!string.IsNullOrEmpty(info.m_name)) return info.m_name.GetHashCode();
            return 0;
        }

        private static void CleanupStaleTracks(float now)
        {
            var toRemove = new List<long>();
            foreach (var kvp in _playerTracks)
            {
                if (now - kvp.Value.lastUpdateTime > 10f) toRemove.Add(kvp.Key);
            }
            foreach (var key in toRemove) _playerTracks.Remove(key);
        }
    }
}
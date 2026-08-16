namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class MapPositionSync
    {
        private static CustomRPC _mapPositionRPC;
        private static float _mapPosTimer;
        private static readonly Dictionary<ZDOID, MapTrackData> _mapTracks = new();

        public static void Initialize()
        {
            if (_mapPositionRPC != null) return;
            
            _mapPositionRPC = NetworkManager.Instance.AddRPC("VBNet_MapPositions", OnServerReceiveMapPos, OnClientReceiveMapPos);
            Helper.LogDebug("[MapPositionSync] RPC initialized");
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
        [HarmonyPostfix]
        static void ZNet_Update_MapPos(ZNet __instance)
        {
            if (!VBNetTweaks.c_ModuleMapPositionSync.Value) return;
            if (!__instance.IsServer()) return;
            if (_mapPositionRPC == null) return;

            _mapPosTimer += Time.deltaTime;
    
            float interval = VBNetTweaks.c_MapPositionSendInterval.Value;
            if (_mapPosTimer < interval) return;
            _mapPosTimer = 0f;

            SendMapPositions(__instance);
        }

        private static void SendMapPositions(ZNet net)
        {
            var peers = net.GetPeers();
            if (peers.Count == 0) return;

            var positions = new List<(ZDOID id, Vector3 pos)>();
            
            foreach (var peer in peers)
            {
                if (!peer.IsReady()) continue;
                if (!peer.m_publicRefPos) continue;
                if (peer.m_characterID.IsNone()) continue;
                
                positions.Add((peer.m_characterID, peer.m_refPos));
            }

            if (positions.Count == 0) return;

            ZPackage pkg = new ZPackage();
            pkg.Write(positions.Count);
            
            foreach (var (id, pos) in positions)
            {
                pkg.Write(id);
                pkg.Write(pos);
            }

            _mapPositionRPC.SendPackage(ZRoutedRpc.Everybody, pkg);
            
            if (VBNetTweaks.c_VerboseLogging.Value) Helper.LogVerbose($"[MapPositionSync] Sent {positions.Count} positions");
        }

        private static IEnumerator OnServerReceiveMapPos(long sender, ZPackage pkg)
        {
            yield break;
        }

        private static IEnumerator OnClientReceiveMapPos(long sender, ZPackage pkg)
        {
            if (!VBNetTweaks.c_ModuleMapPositionSync.Value) yield break;
            if (Helper.IsServer()) yield break;
            if (!ZNet.instance) yield break;

            var serverPeer = ZNet.instance.GetServerPeer();
            if (serverPeer == null || sender != serverPeer.m_uid) yield break;

            try
            {
                int count = pkg.ReadInt();
                float now = Time.time;

                for (int i = 0; i < count; i++)
                {
                    ZDOID id = pkg.ReadZDOID();
                    Vector3 pos = pkg.ReadVector3();

                    if (!_mapTracks.TryGetValue(id, out var track))
                    {
                        track = new MapTrackData();
                        _mapTracks[id] = track;
                    }

                    track.AddSnapshot(now, pos);
                }

                if (VBNetTweaks.c_VerboseLogging.Value && count > 0) Helper.LogVerbose($"[MapPositionSync] Received {count} positions");
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"[MapPositionSync] Error processing positions: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePlayerPins))]
        [HarmonyPostfix]
        static void Minimap_UpdatePlayerPins_Postfix(Minimap __instance, float dt)
        {
            if (!VBNetTweaks.c_ModuleMapPositionSync.Value) return;
            if (Helper.IsServer()) return;
            if (__instance.m_playerPins == null || __instance.m_tempPlayerInfo == null) return;

            int count = Mathf.Min(__instance.m_playerPins.Count, __instance.m_tempPlayerInfo.Count);
            if (count == 0) return;

            float renderTime = Time.time - VBNetTweaks.c_MapInterpolationDelay.Value;
            float maxPredictionTime = VBNetTweaks.c_MapMaxPredictionTime.Value;
            float maxPredictionSpeed = VBNetTweaks.c_MapMaxPredictionSpeed.Value;

            for (int i = 0; i < count; i++)
            {
                var pin = __instance.m_playerPins[i];
                var info = __instance.m_tempPlayerInfo[i];

                if (!info.m_publicPosition) continue;

                ZDOID id = info.m_characterID;
                if (id.IsNone()) continue;

                if (_mapTracks.TryGetValue(id, out var track))
                {
                    if (track.TryGetInterpolated(renderTime, maxPredictionTime, maxPredictionSpeed, out Vector3 interpolated)) pin.m_pos = interpolated;
                    else pin.m_pos = info.m_position;
                }
                else pin.m_pos = info.m_position;
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
        [HarmonyPostfix]
        static void ClearCache() => _mapTracks.Clear();

        private class MapTrackData
        {
            public struct Snapshot
            {
                public float time;
                public Vector3 pos;
            }

            private readonly List<Snapshot> _snapshots = new();
            private Vector3 _lastRealPos;
            private float _lastRealChangeTime;
            private Vector3 _velocity;
            private const int MAX_SNAPSHOTS = 30;

            public void AddSnapshot(float time, Vector3 pos)
            {
                if (_snapshots.Count > 0)
                {
                    var last = _snapshots[_snapshots.Count - 1];
                    
                    if (last.pos == pos)
                    {
                        _snapshots[_snapshots.Count - 1] = new Snapshot { time = time, pos = pos };
                        return;
                    }

                    float dt = time - last.time;
                    if (dt > 0.05f && dt < 5f)
                    {
                        Vector3 delta = pos - last.pos;
                        _velocity = Vector3.ClampMagnitude(delta / dt, 100f);
                    }
                    else
                    {
                        _velocity = Vector3.zero;
                    }
                }

                _snapshots.Add(new Snapshot { time = time, pos = pos });

                while (_snapshots.Count > MAX_SNAPSHOTS)
                {
                    _snapshots.RemoveAt(0);
                }

                _lastRealPos = pos;
                _lastRealChangeTime = time;
            }

            public bool TryGetInterpolated(float renderTime, float maxPredictionTime, float maxPredictionSpeed, out Vector3 result)
            {
                result = Vector3.zero;

                if (_snapshots.Count == 0) return false;

                if (renderTime <= _snapshots[0].time)
                {
                    result = _snapshots[0].pos;
                    return true;
                }

                if (renderTime >= _snapshots[_snapshots.Count - 1].time)
                {
                    var last = _snapshots[_snapshots.Count - 1];
                    float extraTime = renderTime - last.time;

                    if (extraTime > maxPredictionTime) extraTime = maxPredictionTime;

                    Vector3 prediction = _velocity * extraTime;
                    
                    if (prediction.magnitude > maxPredictionSpeed * extraTime) prediction = prediction.normalized * maxPredictionSpeed * extraTime;

                    result = last.pos + prediction;
                    return true;
                }

                for (int i = 1; i < _snapshots.Count; i++)
                {
                    if (renderTime <= _snapshots[i].time)
                    {
                        var a = _snapshots[i - 1];
                        var b = _snapshots[i];
                        
                        float t = Mathf.InverseLerp(a.time, b.time, renderTime);
                        result = Vector3.Lerp(a.pos, b.pos, t);
                        return true;
                    }
                }

                result = _snapshots[_snapshots.Count - 1].pos;
                return true;
            }
        }
    }
}
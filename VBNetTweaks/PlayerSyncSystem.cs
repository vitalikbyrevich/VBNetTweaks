namespace VBNetTweaks
{
    [HarmonyPatch]
    public static class PlayerSyncSystem
    {
        private static readonly Dictionary<long, PD> _playerData = new();
        private static readonly Stack<PD> _pdPool = new();
        
        private class PD
        {
            public Vector3 pos;
            public Quaternion rot;
            public Vector3 vel;
            public float t;
            public bool ok;
        }
        private static PD GetOrCreatePD()
        {
            if (_pdPool.Count > 0) return _pdPool.Pop();
            return new PD();
        }

        private static void ReturnPD(PD pd)
        {
            pd.ok = false;
            _pdPool.Push(pd);
        }

        public static void CleanupPeer(long uid)
        {
            if (_playerData.TryGetValue(uid, out var pd)) ReturnPD(pd);
            _playerData.Remove(uid);
            PlayerCache.RemovePlayer(uid);
        }

        [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.Deserialize))]
        [HarmonyPostfix]
        public static void CapturePlayerState(ZNetView __instance)
        {
           // if (Helper.IsServer() || !__instance) return;
           if (!__instance) return;

            Player player = __instance.GetComponent<Player>();
            if (!player) return;

            var zdo = __instance.GetZDO();
            if (zdo == null || !zdo.IsValid()) return;

            long owner = zdo.GetOwner();
            if (owner == 0 || owner == ZNet.GetUID()) return;

            Vector3 pos = zdo.GetPosition();
            Quaternion rot = zdo.GetRotation();

            if (!_playerData.TryGetValue(owner, out var d))
            {
                d = GetOrCreatePD();
                d.pos = pos;
                d.rot = rot;
                d.t = Time.time;
                d.ok = true;
                _playerData[owner] = d;
                return;
            }

            float dt = Time.time - d.t;
            if (dt > 0f) d.vel = (pos - d.pos) / dt;

            d.pos = pos;
            d.rot = rot;
            d.t = Time.time;
            d.ok = true;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.LateUpdate))]
        [HarmonyPostfix]
        public static void SmoothRemotePlayers(Player __instance)
        {
            if (__instance == Player.m_localPlayer || Helper.IsServer()) return;

            long owner = __instance.GetPlayerID();
            if (!_playerData.TryGetValue(owner, out var d) || !d.ok) return;
            
            bool isAttached = PlayerCache.IsPlayerAttached(owner);
            bool isOnShip = PlayerCache.IsPlayerOnShip(owner);

            if (isAttached || isOnShip) return;

            if (VBNetTweaks.EnablePlayerPrediction.Value)
            {
                float predictTime = Time.deltaTime * 1.5f;
                Vector3 predicted = d.pos + d.vel * predictTime;
                __instance.transform.position = Vector3.Lerp(__instance.transform.position, predicted, 0.8f);
            }

            if (VBNetTweaks.EnableClientInterpolation.Value)
            {
                float t = Mathf.Clamp01(Time.deltaTime * 12f);
                Vector3 target = d.pos + d.vel * Time.deltaTime * 0.5f;
                __instance.transform.position = Vector3.Lerp(__instance.transform.position, target, t);
                __instance.transform.rotation = Quaternion.Slerp(__instance.transform.rotation, d.rot, t);
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        [HarmonyPostfix]
        public static void OnPeerDisconnect(ZNet __instance, ZNetPeer peer)
        {
            if (peer == null) return;

            long uid = peer.m_uid;
            CleanupPeer(uid);
            AdaptiveThrottler.OnPeerDisconnected(peer);
        }
    }
}
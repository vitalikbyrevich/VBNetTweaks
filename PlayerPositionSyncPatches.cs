using VBNetTweaks.Utils;

namespace VBNetTweaks
{
    [HarmonyPatch]
    public static class PlayerPositionSyncPatches
    {
        // SERVER: Boost player ZDO send priority
        [HarmonyPatch(typeof(ZDOMan), "ServerSortSendZDOS")]
        [HarmonyPrefix]
        public static void ServerSortSendZDOS_Prefix(List<ZDO> objects, Vector3 refPos)
        {
            if (!Helper.IsServer() || VBNetTweaks.EnablePlayerPositionBoost?.Value != true) return;
            float mult = Mathf.Clamp(VBNetTweaks.PlayerPositionUpdateMultiplier.Value, 1f, 5f);

            for (int i = 0; i < objects.Count; i++)
            {
                var zdo = objects[i];
                if (zdo == null) continue;
                zdo.m_tempSortValue = Vector3.Distance(zdo.GetPosition(), refPos);
                if (IsPlayerZDO(zdo)) zdo.m_tempSortValue -= 150f * mult;
            }
        }

        private static bool IsPlayerZDO(ZDO zdo)
        {
            if (zdo == null) return false;
            return zdo.GetString("playerName".GetStableHashCode(), "").Length > 0;
        }

        // CLIENT: prediction + interpolation
        private static readonly Dictionary<long, PD> data = new Dictionary<long, PD>();
        private class PD { public Vector3 pos; public Quaternion rot; public Vector3 vel; public float t; public bool ok; }

        [HarmonyPatch(typeof(ZNetView), "Deserialize")]
        [HarmonyPostfix]
        public static void Deserialize_Postfix(ZNetView __instance, ZPackage pkg)
        {
            if (VBNetTweaks.EnableClientInterpolation?.Value != true && VBNetTweaks.EnablePlayerPrediction?.Value != true) return;
            if (ZNet.instance == null || Helper.IsServer() || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected) return;

            var zdo = __instance?.GetZDO();
            if (zdo == null || !IsPlayerZDO(zdo)) return;

            long owner = zdo.GetOwner();
            if (owner == ZNet.GetUID()) return;

            Vector3 newPos = zdo.GetPosition();
            Quaternion newRot = zdo.GetRotation();

            if (!data.TryGetValue(owner, out var d))
            {
                d = new PD { pos = newPos, rot = newRot, t = Time.time, ok = true };
                data[owner] = d;
                return;
            }

            float dt = Mathf.Max(0f, Time.time - d.t);
            if (dt > 0f && d.ok)
            {
                d.vel = (newPos - d.pos) / dt;
            }

            d.pos = newPos;
            d.rot = newRot;
            d.t = Time.time;
            d.ok = true;
        }

        [HarmonyPatch(typeof(Player), "LateUpdate")]
        [HarmonyPostfix]
        public static void Player_LateUpdate_Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) return;
            if (ZNet.instance == null || Helper.IsServer()) return;
            if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected) return;

            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;

            var zdo = nview.GetZDO();
            if (zdo == null || !IsPlayerZDO(zdo)) return;

            long owner = zdo.GetOwner();
            if (!data.TryGetValue(owner, out var d) || !d.ok) return;

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
    }
}

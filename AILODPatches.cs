using VBNetTweaks.Utils;

namespace VBNetTweaks
{
    [HarmonyPatch]
    public static class AILODPatches
    {
        [HarmonyPatch(typeof(Character), "FixedUpdate")]
        [HarmonyPrefix]
        public static bool FixedUpdate_Prefix(Character __instance)
        {
            if (!Helper.IsServer() || VBNetTweaks.EnableAILOD?.Value != true) return true;

            // Игроки и прирученные — всегда полный апдейт
            if (__instance.IsPlayer() || (__instance.GetComponent<Tameable>() is Tameable tame && tame.IsTamed())) return true;

            float nearestDist = float.MaxValue;
            var players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null) continue;
                float d = Vector3.Distance(__instance.transform.position, p.transform.position);
                if (d < nearestDist) nearestDist = d;
            }

            if (nearestDist <= VBNetTweaks.AILODNearDistance.Value)
                return true;

            if (nearestDist > VBNetTweaks.AILODFarDistance.Value)
            {
                // Пропускаем часть апдейтов
                float factor = Mathf.Clamp(VBNetTweaks.AILODThrottleFactor.Value, 0.25f, 0.75f);
                if (Time.time % (1f / factor) > Time.fixedDeltaTime) return false;
            }

            return true;
        }
    }
}
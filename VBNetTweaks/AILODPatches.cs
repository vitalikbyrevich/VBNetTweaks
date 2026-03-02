namespace VBNetTweaks
{
    [HarmonyPatch]
    public static class AILODPatches
    {
        private static bool ShouldUpdateAI(Character c)
        {
            var players = PlayerCache.GetCached(0.5f);
            float nearestDist = float.MaxValue;

            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!p) continue;
        
                float d = Vector3.Distance(c.transform.position, p.transform.position);
                if (d < nearestDist) nearestDist = d;
            }

            if (nearestDist <= VBNetTweaks.AILODNearDistance.Value) return true;

            if (nearestDist > VBNetTweaks.AILODFarDistance.Value)
            {
                float factor = Mathf.Clamp(VBNetTweaks.AILODThrottleFactor.Value, 0.25f, 0.75f);
                if (Time.time % (1f / factor) > Time.fixedDeltaTime) return false;
            }

            return true;
        }

        [HarmonyPrefix]
        public static bool FixedUpdate_Prefix(Character __instance)
        {
            if (!Helper.IsServer() || VBNetTweaks.EnableAILOD?.Value != true) return true;

            if (__instance.IsPlayer() || (__instance.GetComponent<Tameable>() is { } tame && tame.IsTamed())) return true;

            bool result = true;

            PerformanceMonitor.Track("AI.FixedUpdate", () => result = ShouldUpdateAI(__instance));

            return result;
        }
    }
}
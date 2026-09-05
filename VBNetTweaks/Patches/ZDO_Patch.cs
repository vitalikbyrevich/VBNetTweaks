namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZDO_Patch
    {
        public static float Vec3CullSizeSq = 0.005f;

        [HarmonyPatch(typeof(ZDO), nameof(ZDO.Set), new Type[] { typeof(int), typeof(Vector3) }),HarmonyPrefix]
        private static bool SetVec3Prefix(ZDO __instance, int hash, Vector3 value)
        {
            if (!VBNetTweaks.c_ModuleRevisionOptimization.Value) return true;

            if (__instance.GetFloat(ZDOVars.s_rudder, out _)) return true;

            if (__instance.GetVec3(hash, out var oldValue))
            {
                if ((oldValue - value).sqrMagnitude < Vec3CullSizeSq) return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ZDO), nameof(ZDO.Set), new Type[] { typeof(int), typeof(Quaternion) }),HarmonyPrefix]
        private static bool SetQuatPrefix(ZDO __instance, int hash, Quaternion value)
        {
            if (!VBNetTweaks.c_ModuleRevisionOptimization.Value) return true;

            if (__instance.GetFloat(ZDOVars.s_rudder, out _)) return true;

            if (__instance.GetQuaternion(hash, out var oldValue))
            {
                if (Mathf.Abs(Quaternion.Dot(oldValue, value)) > 0.9999f) return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SyncVelocity)),HarmonyPrefix]
        private static bool SyncVelocityPrefix(ref Rigidbody ___m_body, ref Vector3 ___m_bodyVelocityCached)
        {
            if (!VBNetTweaks.c_ModuleRevisionOptimization.Value) return true;
            if (!___m_body) return true;
            if ((___m_body.velocity - ___m_bodyVelocityCached).sqrMagnitude < Vec3CullSizeSq) return false;
            return true;
        }
    }
}
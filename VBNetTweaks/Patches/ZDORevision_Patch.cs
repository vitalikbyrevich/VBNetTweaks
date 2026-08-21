namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZDORevision_Patch
    {
        private static readonly HashSet<ZDO> _frozenZDOs = new HashSet<ZDO>();
        private static readonly HashSet<ZDO> _forcedZDOs = new HashSet<ZDO>();

        public static float DeltaTimePhysics = 0.01f;
        public static float Vec3CullSizeSq = 0.00025f;

        [HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.FixedUpdate))]
        [HarmonyPrefix]
        private static void FixedUpdatePrefix()
        {
            if (!VBNetTweaks.c_ModuleRevisionOptimization.Value) return;
            DeltaTimePhysics = Time.fixedDeltaTime;
            _frozenZDOs.Clear();
            _forcedZDOs.Clear();
        }

        [HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.LateUpdate))]
        [HarmonyPrefix]
        private static void LateUpdatePrefix()
        {
            if (!VBNetTweaks.c_ModuleRevisionOptimization.Value) return;
            DeltaTimePhysics = Time.deltaTime;
            _frozenZDOs.Clear();
            _forcedZDOs.Clear();
        }

        [HarmonyPatch(typeof(ZDO), nameof(ZDO.IncreaseDataRevision))]
        [HarmonyPrefix]
        private static bool IncreaseDataRevisionPrefix(ZDO __instance)
        {
            if (!VBNetTweaks.c_ModuleRevisionOptimization.Value) return true;
            if (_frozenZDOs.Contains(__instance) && !_forcedZDOs.Contains(__instance)) return false;
            return true;
        }

        [HarmonyPatch(typeof(ZDO), nameof(ZDO.Set), new Type[] { typeof(int), typeof(Vector3) })]
        [HarmonyPrefix]
        private static bool SetVec3Prefix(ZDO __instance, int hash, Vector3 value)
        {
            if (!VBNetTweaks.c_ModuleRevisionOptimization.Value) return true;
            if (_forcedZDOs.Contains(__instance)) return true;

            if (__instance.GetVec3(hash, out var oldValue))
            {
                if ((oldValue - value).sqrMagnitude < Vec3CullSizeSq) return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ZDO), nameof(ZDO.Set), new Type[] { typeof(int), typeof(Quaternion) })]
        [HarmonyPrefix]
        private static bool SetQuatPrefix(ZDO __instance, int hash, Quaternion value)
        {
            if (!VBNetTweaks.c_ModuleRevisionOptimization.Value) return true;
            if (_forcedZDOs.Contains(__instance)) return true;

            if (__instance.GetQuaternion(hash, out var oldValue))
            {
                if (Mathf.Abs(Quaternion.Dot(oldValue, value)) > 0.9999f) return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.CustomLateUpdate))]
        [HarmonyPrefix]
        private static void ZSyncTransformPrefix(ZSyncTransform __instance, ref ZNetView ___m_nview)
        {
            if (!VBNetTweaks.c_ModuleRevisionOptimization.Value) return;
            if (!___m_nview) return;
            ZDO zdo = ___m_nview.GetZDO();
            if (zdo == null) return;

            if (zdo.GetFloat(ZDOVars.s_rudder, out var _)) return; // Корабли исключаем

            float rate = VBNetTweaks.c_NetRatePhysics.Value;
            if (!__instance.m_syncPosition) rate *= 2f;

            bool forcing = ShouldUpdateZDO(zdo, 0.5f, DeltaTimePhysics);
            bool freezing = !forcing && !ShouldUpdateZDO(zdo, rate, DeltaTimePhysics);

            if (forcing) _forcedZDOs.Add(zdo);
            if (freezing) _frozenZDOs.Add(zdo);
        }

        [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
        [HarmonyPrefix]
        private static void CharacterFixedUpdatePrefix(Character __instance, float dt, ref ZNetView ___m_nview)
        {
            if (!VBNetTweaks.c_ModuleRevisionOptimization.Value) return;
            if (__instance.IsPlayer()) return;
            if (!___m_nview) return;
            
            ZDO zdo = ___m_nview.GetZDO();
            if (zdo == null) return;

            bool forcing = ShouldUpdateZDO(zdo, 0.5f, DeltaTimePhysics);
            bool freezing = !forcing && !ShouldUpdateZDO(zdo, VBNetTweaks.c_NetRateNPC.Value, DeltaTimePhysics);

            if (forcing) _forcedZDOs.Add(zdo);
            if (freezing) _frozenZDOs.Add(zdo);
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SyncVelocity))]
        [HarmonyPrefix]
        private static bool SyncVelocityPrefix(Character __instance, ref ZNetView ___m_nview, ref Rigidbody ___m_body, ref Vector3 ___m_bodyVelocityCached)
        {
            if (!VBNetTweaks.c_ModuleRevisionOptimization.Value) return true;
            if (!___m_body) return true;
            if (___m_nview && ___m_nview.m_zdo != null && _forcedZDOs.Contains(___m_nview.m_zdo)) return true;
            if ((___m_body.velocity - ___m_bodyVelocityCached).sqrMagnitude < Vec3CullSizeSq) return false;

            return true;
        }

        private static bool ShouldUpdateZDO(ZDO zdo, float netRate, float deltaTime)
        {
            double time = Time.unscaledTimeAsDouble;
            double offset = 0.023 * (zdo.m_uid.ID & 0xFFFu);
            double adjustedTime = time + offset;
            return Mathf.RoundToInt((float)(adjustedTime * netRate)) != Mathf.RoundToInt((float)((adjustedTime + deltaTime) * netRate));
        }
    }
}
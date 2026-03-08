namespace VBNetTweaks
{
    public static class SupportManager
    {
        private static readonly HashSet<WearNTear> _dirty = new HashSet<WearNTear>();
        private static readonly Dictionary<WearNTear, float> _lastRecalcTime = new Dictionary<WearNTear, float>();

        public static float SupportRecalcInterval = 5f;
        public static float SupportCacheDuration = 1.0f;

        private static readonly Dictionary<WearNTear, (float value, float time)> _supportCache = new Dictionary<WearNTear, (float, float)>();

        public static void MarkDirty(WearNTear wnt)
        {
            if (!wnt) return;
            _dirty.Add(wnt);
        }

        public static void Clear(WearNTear wnt)
        {
            if (!wnt) return;
            _dirty.Remove(wnt);
            _lastRecalcTime.Remove(wnt);
            _supportCache.Remove(wnt);
        }

        public static bool TryGetCachedSupport(WearNTear wnt, out float value)
        {
            value = 0f;
            if (!wnt) return false;

            if (_supportCache.TryGetValue(wnt, out var entry))
            {
                if (Time.time - entry.time < SupportCacheDuration)
                {
                    value = entry.value;
                    return true;
                }
            }

            return false;
        }

        public static void StoreSupport(WearNTear wnt, float value)
        {
            if (!wnt) return;
            _supportCache[wnt] = (value, Time.time);
        }

        public static void ProcessDirtyFor(WearNTear wnt)
        {
            if (!wnt || !wnt.m_nview || !wnt.m_nview.IsOwner()) return;

            float now = Time.time;

            if (!_lastRecalcTime.TryGetValue(wnt, out float last)) last = 0f;

            bool timeExpired = now - last > SupportRecalcInterval;
            bool isDirty = _dirty.Contains(wnt);

            if (!isDirty && !timeExpired) return;

            wnt.UpdateSupport();

            _lastRecalcTime[wnt] = now;
            _dirty.Remove(wnt);
        }
    }
    
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ClearCachedSupport))]
    public static class WearNTear_ClearCachedSupport_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance)
        {
            SupportManager.MarkDirty(__instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnDestroy))]
    public static class WearNTear_OnDestroy_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance)
        {
            SupportManager.Clear(__instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
    public static class WearNTear_UpdateWear_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance, float time)
        {
            if (!__instance.m_nview || !__instance.m_nview.IsOwner()) return;
            SupportManager.ProcessDirtyFor(__instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.GetSupport))]
    public static class WearNTear_GetSupport_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(WearNTear __instance, ref float __result)
        {
            if (!__instance.m_nview || !__instance.m_nview.IsOwner()) return true;

            if (SupportManager.TryGetCachedSupport(__instance, out float cached))
            {
                __result = cached;
                return false;
            }
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance, float __result)
        {
            if (!__instance.m_nview || !__instance.m_nview.IsOwner()) return;
            SupportManager.StoreSupport(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.RPC_Damage))]
    public static class WearNTear_RPC_Damage_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance, HitData hit)
        {
            SupportManager.MarkDirty(__instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Destroy))]
    public static class WearNTear_Destroy_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance)
        {
            try
            {
                if (__instance.m_colliders == null) __instance.SetupColliders();

                foreach (var bound in __instance.m_bounds)
                {
                    int count = Physics.OverlapBoxNonAlloc(bound.m_pos, bound.m_size, WearNTear.s_tempColliders, bound.m_rot, WearNTear.s_rayMask);

                    for (int i = 0; i < count; i++)
                    {
                        var col = WearNTear.s_tempColliders[i];
                        if (!col || col.attachedRigidbody || col.isTrigger) continue;

                        var other = col.GetComponentInParent<WearNTear>();
                        if (!other || other == __instance) continue;

                        SupportManager.MarkDirty(other);

                        if (other.m_nview && other.m_nview.IsValid())
                        {
                            if (other.m_nview.IsOwner()) other.ClearCachedSupport();
                            else
                                other.m_nview.InvokeRPC(other.m_nview.GetZDO().GetOwner(), "RPC_ClearCachedSupport");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SupportManager] Error notifying neighbors on Destroy: {e}");
            }
        }
    }
}
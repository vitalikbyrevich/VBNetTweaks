using Object = UnityEngine.Object;

namespace VBNetTweaks.Patches
{
    public static class StatusEffectVFXManager
    {
        private static readonly Dictionary<ZNetView, HashSet<GameObject>> _registry = new();

        public static void Register(ZNetView parent, GameObject vfx)
        {
            if (!parent || !vfx) return;
            if (!_registry.TryGetValue(parent, out var set))
            {
                set = new HashSet<GameObject>();
                _registry[parent] = set;
            }
            set.Add(vfx);
        }

        public static void Unregister(ZNetView parent, GameObject vfx)
        {
            if (parent && _registry.TryGetValue(parent, out var set))
            {
                set.Remove(vfx);
                if (set.Count == 0) _registry.Remove(parent);
            }
        }

        public static void CleanupByParent(ZNetView parent)
        {
            if (_registry.TryGetValue(parent, out var vfxSet))
            {
                foreach (var vfx in vfxSet)
                {
                    if (!vfx) continue;

                    if (vfx.TryGetComponent<ZNetView>(out var znv))
                    {
                        if (znv.IsValid()) znv.ClaimOwnership();
                        znv.Destroy();
                    }
                    else
                    {
                        Object.Destroy(vfx);
                    }
                }
                _registry.Remove(parent);
            }
        }

        public static void Maintenance()
        {
            var toRemove = new List<ZNetView>();
            foreach (var kvp in _registry)
            {
                kvp.Value.RemoveWhere(g => !g);
                if (kvp.Value.Count == 0) toRemove.Add(kvp.Key);
            }
            foreach (var key in toRemove) _registry.Remove(key);
        }
    }

    [HarmonyPatch]
    public static class StatusEffectVFXFix
    {
        [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.TriggerStartEffects))]
        [HarmonyPostfix]
        public static void TrackVFX_Postfix(StatusEffect __instance)
        {
            if (!__instance?.m_character || __instance.m_startEffectInstances == null) return;
            
            var parentZNet = __instance.m_character.GetComponent<ZNetView>();
            if (!parentZNet) return;

            foreach (var vfx in __instance.m_startEffectInstances)
                if (vfx) StatusEffectVFXManager.Register(parentZNet, vfx);
        }

        [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.RemoveStartEffects))]
        [HarmonyPrefix]
        public static bool RemoveVFX_Prefix(StatusEffect __instance)
        {
            if (__instance.m_startEffectInstances == null || !ZNetScene.instance) return true;

            var parentZNet = __instance.m_character?.GetComponent<ZNetView>();

            foreach (var vfx in __instance.m_startEffectInstances)
            {
                if (!vfx) continue;

                StatusEffectVFXManager.Unregister(parentZNet, vfx);

                if (vfx.TryGetComponent<ZNetView>(out var znv))
                {
                    if (znv.IsValid()) znv.ClaimOwnership();
                    znv.Destroy();
                }
                else
                {
                    Object.Destroy(vfx);
                }
            }
            __instance.m_startEffectInstances = null;
            return false;
        }

        [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.OnDestroy))]
        [HarmonyPostfix]
        public static void ZNetView_OnDestroy_Postfix(ZNetView __instance)
        {
            StatusEffectVFXManager.CleanupByParent(__instance);
        }
    }
}
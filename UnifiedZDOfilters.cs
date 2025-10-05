namespace VBNetTweaks;

[HarmonyPatch]
public static class UnifiedZDOfilters
{
    // Система заморозки ревизий (из LeanNet)
    public static class ZDORevisionFreeze
    {
        public static int Freeze;
        public static int Force;
        public static uint DataRevision;

        public static void Reset()
        {
            Freeze = 0;
            Force = 0;
            DataRevision = 0;
        }

        public static bool IsFreezing() => Freeze > 0 && Force <= 0;
        public static bool IsForcing() => Force > 0;
    }

    // Фильтрация Vector3 изменений
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ZDO), "Set", new Type[] { typeof(int), typeof(Vector3) })]
    static bool ZDO_Set_Vector3_Prefix(ZDO __instance, int hash, Vector3 value)
    {
        if (!VBNetTweaks.Enabled.Value || ZDORevisionFreeze.IsForcing())
            return true;

        // Проверяем, было ли изменение достаточно значительным
        if (__instance.GetVec3(hash, out Vector3 currentValue))
        {
            float sqrMagnitude = (currentValue - value).sqrMagnitude;
            if (sqrMagnitude < VBNetTweaks.Vec3CullSizeSq)
            {
                if (VBNetTweaks.DebugEnabled.Value)
                {
                    VBNetTweaks.LogDebug($"Filtered Vector3 change: " + $"hash={hash}, delta={Mathf.Sqrt(sqrMagnitude):F4}, " + $"from={currentValue}, to={value}");
                }
                return false;
            }
        }

        return true;
    }

    // Фильтрация Quaternion изменений
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ZDO), "Set", new Type[] { typeof(int), typeof(Quaternion) })]
    static bool ZDO_Set_Quaternion_Prefix(ZDO __instance, int hash, Quaternion value)
    {
        if (!VBNetTweaks.Enabled.Value || ZDORevisionFreeze.IsForcing()) return true;

        float dot = Quaternion.Dot(__instance.GetQuaternion(hash, value), value);
        if (dot > 0.98f) // Порог для кватернионов
        {
            if (VBNetTweaks.DebugEnabled.Value)
            {
                VBNetTweaks.LogDebug($"Filtered Quaternion change: " + $"hash={hash}, dot={dot:F4}");
            }
            return false;
        }

        return true;
    }

    // Оптимизация ZSyncTransform
    [HarmonyPatch(typeof(ZSyncTransform), "CustomLateUpdate")]
    static class ZSyncTransform_CustomLateUpdate_Patch
    {
        static bool _freezing;
        static bool _forcing;

        static void Prefix(ZSyncTransform __instance, ZNetView ___m_nview)
        {
            if (!VBNetTweaks.Enabled.Value) return;

            ZDO zdo = ___m_nview?.GetZDO();
            if (zdo == null || zdo.GetFloat(ZDOVars.s_rudder, out _))
                return;

            float netRate = VBNetTweaks.NetRatePhysics.Value;
            if (!__instance.m_syncPosition) netRate *= 2f;

            _forcing = VBNetTweaks.ShouldUpdateZDO(zdo, 0.5f, VBNetTweaks.DeltaTimePhysics);
            _freezing = !_forcing && !VBNetTweaks.ShouldUpdateZDO(zdo, netRate, VBNetTweaks.DeltaTimePhysics);

            if (_forcing) ZDORevisionFreeze.Force++;
            if (_freezing) ZDORevisionFreeze.Freeze++;

            if (VBNetTweaks.DebugEnabled.Value && (_freezing || _forcing))
            {
                VBNetTweaks.LogDebug($"ZSyncTransform: " +
                                     $"freezing={_freezing}, forcing={_forcing}, " + $"netRate={netRate}, object={__instance.name}");
            }
        }

        static void Postfix()
        {
            if (_freezing)
            {
                _freezing = false;
                ZDORevisionFreeze.Freeze--;
            }
            if (_forcing)
            {
                _forcing = false;
                ZDORevisionFreeze.Force--;
            }
        }
    }

    // Оптимизация Character
    [HarmonyPatch(typeof(Character), "CustomFixedUpdate")]
    static class Character_CustomFixedUpdate_Patch
    {
        static bool _freezing;
        static bool _forcing;

        static void Prefix(Character __instance, ZNetView ___m_nview)
        {
            if (!VBNetTweaks.Enabled.Value || __instance.IsPlayer()) return;

            ZDO zdo = ___m_nview?.GetZDO();
            if (zdo == null) return;

            _forcing = VBNetTweaks.ShouldUpdateZDO(zdo, 0.5f, VBNetTweaks.DeltaTimeFixedPhysics);
            _freezing = !_forcing && !VBNetTweaks.ShouldUpdateZDO(zdo, VBNetTweaks.NetRateNPC.Value, VBNetTweaks.DeltaTimeFixedPhysics);

            if (_forcing) ZDORevisionFreeze.Force++;
            if (_freezing) ZDORevisionFreeze.Freeze++;

            if (VBNetTweaks.DebugEnabled.Value && (_freezing || _forcing))
            {
                VBNetTweaks.LogDebug($"Character {__instance.name}: " + $"freezing={_freezing}, forcing={_forcing}");
            }
        }

        static void Postfix()
        {
            if (_freezing)
            {
                _freezing = false;
                ZDORevisionFreeze.Freeze--;
            }
            if (_forcing)
            {
                _forcing = false;
                ZDORevisionFreeze.Force--;
            }
        }
    }

    // Блокировка IncreaseDataRevision при заморозке
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ZDO), "IncreaseDataRevision")]
    static bool ZDO_IncreaseDataRevision_Prefix()
    {
        if (ZDORevisionFreeze.IsFreezing())
        {
            if (VBNetTweaks.DebugEnabled.Value)
            {
                VBNetTweaks.LogDebug("Blocked DataRevision increase due to freeze");
            }
            return false;
        }
        return true;
    }
}
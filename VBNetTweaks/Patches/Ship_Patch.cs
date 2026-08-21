namespace VBNetTweaks.Patches;

[HarmonyPatch]
public static class Ship_Patch
{
    private static float _lastDamageLog;

    [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.RPC_RequestRespons))]
    [HarmonyPrefix]
    public static void OnControlGranted(ShipControlls __instance, long sender, bool granted)
    {
        if (!VBNetTweaks.c_ModuleShipSync.Value) return;
        if (!granted) return;
        if (!__instance.m_nview.IsValid()) return;
    
        var ship = __instance.m_ship;
        if (!ship) return;
    
        var nview = ship.m_nview;
        if (!nview || !nview.IsValid()) return;
    
        var zdo = nview.GetZDO();
        if (zdo == null) return;
    
        long me = ZDOMan.GetSessionID();
        if (zdo.GetOwner() == me) return;
    
        zdo.SetOwner(me);
        ZDOMan.instance.ForceSendZDO(zdo.m_uid);
    
        if (VBNetTweaks.c_VerboseLogging.Value) Helper.LogVerbose($"[ShipOwnership] Local player took ownership of ship (response from {sender})");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Ship), nameof(Ship.UpdateWaterForce))]
    static bool UpdateWaterForce_Prefix(Ship __instance, ref float depth, ref float time)
    {
        if (!VBNetTweaks.c_ModuleShipSync.Value) return true;
        float num = depth - __instance.m_lastDepth;
        float num2 = time - __instance.m_lastUpdateWaterForceTime;
        __instance.m_lastDepth = depth;
        __instance.m_lastUpdateWaterForceTime = time;

        if (num2 <= 0.001f) return false;

        float num3 = num / num2;
        bool isHardImpact = num3 <= 0f && Mathf.Abs(num3) > __instance.m_minWaterImpactForce && time - __instance.m_lastWaterImpactTime > __instance.m_minWaterImpactInterval;

        if (isHardImpact)
        {
            __instance.m_lastWaterImpactTime = time;

            __instance.m_waterImpactEffect.Create(__instance.transform.position, __instance.transform.rotation);

            if (__instance.m_nview.IsOwner() && __instance.m_players.Count > 0)
            {
                HitData hitData = new HitData();
                hitData.m_damage.m_blunt = __instance.m_waterImpactDamage;
                hitData.m_point = __instance.transform.position;
                hitData.m_dir = Vector3.up;
                __instance.m_destructible.Damage(hitData);

                if (VBNetTweaks.c_VerboseLogging.Value && Time.time - _lastDamageLog > 5f)
                {
                    _lastDamageLog = Time.time;
                    float speed = Mathf.Abs(num3);
                    Helper.LogVerbose($"[Ship] Water impact damage: speed={speed:F2}, threshold={__instance.m_minWaterImpactForce:F2}, players={__instance.m_players.Count}");
                }
            }
        }
        return false;
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.ApplyControlls))]
    [HarmonyPrefix]
    static bool ApplyControlls_Prefix(Ship __instance, Vector3 dir)
    {
        if (!VBNetTweaks.c_ModuleShipSync.Value) return true;

        bool forward = dir.z > 0.5;
        bool backward = dir.z < -0.5;

        if (forward && !__instance.m_forwardPressed) __instance.Forward();
        if (backward && !__instance.m_backwardPressed) __instance.Backward();

        __instance.m_forwardPressed = forward;
        __instance.m_backwardPressed = backward;

        float fixedDeltaTime = Time.fixedDeltaTime;
        float num = Mathf.Lerp(0.5f, 1f, Mathf.Abs(__instance.m_rudderValue));

        __instance.m_rudder = dir.x * num;
        __instance.m_rudderValue += __instance.m_rudder * __instance.m_rudderSpeed * fixedDeltaTime;
        __instance.m_rudderValue = Mathf.Clamp(__instance.m_rudderValue, -1f, 1f);

        if (Time.time - __instance.m_sendRudderTime > 0.05f)
        {
            __instance.m_sendRudderTime = Time.time;
            if (!__instance.m_nview.IsOwner()) __instance.m_nview.InvokeRPC("Rudder", __instance.m_rudderValue);
        }
        return false;
    }
}
namespace VBNetTweaks.Patches;

[HarmonyPatch(typeof(Ship))]
public static class ShipWaterDamagePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Ship.UpdateWaterForce))]
    static bool Prefix(Ship __instance, ref float depth, ref float time)
    {
        // Всегда обновляем внутренние переменные для плавности расчетов, 
        // даже если мы не владелец, чтобы эффекты работали корректно,
        // но урон наносим только если мы владелец.
        
        float num = depth - __instance.m_lastDepth;
        float num2 = time - __instance.m_lastUpdateWaterForceTime;
        __instance.m_lastDepth = depth;
        __instance.m_lastUpdateWaterForceTime = time;
        
        // Если делитель слишком мал, избегаем деления на ноль
        if (num2 <= 0.001f) return true; 

        float num3 = num / num2;
        
        // Проверяем условие удара
        bool isHardImpact = num3 <= 0f && Mathf.Abs(num3) > __instance.m_minWaterImpactForce && time - __instance.m_lastWaterImpactTime > __instance.m_minWaterImpactInterval;

        if (isHardImpact)
        {
            __instance.m_lastWaterImpactTime = time;
            
            // Эффекты видим все
            __instance.m_waterImpactEffect.Create(__instance.transform.position, __instance.transform.rotation);
            
            // Урон наносит ТОЛЬКО владелец
            if (__instance.m_nview.IsOwner() && __instance.m_players.Count > 0)
            {
                HitData hitData = new HitData();
                hitData.m_damage.m_blunt = __instance.m_waterImpactDamage;
                hitData.m_point = __instance.transform.position;
                hitData.m_dir = Vector3.up;
                __instance.m_destructible.Damage(hitData);
            }
        }

        // Возвращаем false, чтобы оригинальный метод не выполнялся дважды
        return false;
    }
}
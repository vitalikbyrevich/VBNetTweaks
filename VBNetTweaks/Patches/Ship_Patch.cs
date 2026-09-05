namespace VBNetTweaks.Patches;

[HarmonyPatch]
public static class Ship_Patch
{
    private class ShipData
    {
        public Vector3 prevPos, targetPos;
        public Quaternion prevRot, targetRot;
        public float prevTime, targetTime;
        public Vector3 vel;
        public long lastOwner;
        public bool initialized;
    }

    private static readonly Dictionary<long, ShipData> _shipData = new();

    private const float SMOOTH_POS_ON_SHIP = 0.4f;
    private const float SMOOTH_ROT_ON_SHIP = 0.3f;
    private const float SMOOTH_POS_OFF_SHIP = 0.25f;
    private const float SMOOTH_ROT_OFF_SHIP = 0.15f;
    private const float CORRECTION_THRESHOLD = 2.5f;
    private const float ROT_CORRECTION_THRESHOLD = 10f;
    private const float INTERP_DELAY = 0.1f; // 100мс буфер для плавности

    private static readonly Dictionary<long, ZDOID> _playerShipMap = new();
    public static bool IsPlayerOnShip(long playerId)
    {
        return _playerShipMap.TryGetValue(playerId, out var shipId) && !shipId.IsNone();
    }
    
    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate)),HarmonyPostfix]
    public static void SmoothShip(Ship __instance, float fixedDeltaTime)
    {
        if (!VBNetTweaks.c_ModuleShipSync.Value) return;
        if (Helper.IsDedicated()) return;
        if (!__instance.m_nview || !__instance.m_nview.IsValid()) return;

        var zdo = __instance.m_nview.GetZDO();
        if (zdo == null) return;

        long owner = zdo.GetOwner();
        if (owner == 0 || owner == ZNet.GetUID()) return; // владелец → ванила

        Vector3 targetPos = zdo.GetPosition();
        Quaternion targetRot = zdo.GetRotation();
        float now = Time.time;

        if (!_shipData.TryGetValue(owner, out var d))
        {
            d = new ShipData
            {
                prevPos = targetPos, targetPos = targetPos,
                prevRot = targetRot, targetRot = targetRot,
                prevTime = now, targetTime = now,
                lastOwner = owner, initialized = true
            };
            _shipData[owner] = d;
            __instance.transform.position = targetPos;
            __instance.transform.rotation = targetRot;
            return;
        }

        // Смена владельца — сброс
        if (d.lastOwner != owner)
        {
            d.vel = Vector3.zero;
            d.lastOwner = owner;
            d.prevPos = d.targetPos = targetPos;
            d.prevRot = d.targetRot = targetRot;
            d.prevTime = d.targetTime = now;
            return;
        }

        // Обновляем буфер только если позиция реально изменилась (пришёл новый ZDO)
        float elapsed = now - d.targetTime;
        if (elapsed > 0.01f && Vector3.Distance(d.targetPos, targetPos) > 0.001f)
        {
            d.prevPos = d.targetPos;
            d.prevRot = d.targetRot;
            d.prevTime = d.targetTime;
            d.targetPos = targetPos;
            d.targetRot = targetRot;
            d.targetTime = now;

            // Скорость для экстраполяции (на случай редких ZDO)
            d.vel = (targetPos - d.prevPos) / elapsed;
        }

        // Интерполяция с буфером
        var t = __instance.transform;
        float interpWindow = 0.15f; // фиксированные 150мс
        float interpElapsed = now - d.targetTime + INTERP_DELAY;
        float lerpT = Mathf.Clamp01(interpElapsed / interpWindow);

        Vector3 interpolated = Vector3.Lerp(d.prevPos, d.targetPos, lerpT);

        // Если lerpT >= 1 (данных давно не было), экстраполируем по скорости
        Vector3 predicted;
        if (lerpT >= 1.0f && d.vel.sqrMagnitude > 0.01f)
        {
            float extraTime = interpElapsed - interpWindow;
            predicted = d.targetPos + d.vel * Mathf.Min(extraTime, 0.25f); // не больше 0.5с экстраполяции
        }
        else predicted = interpolated;

        // Выбор скорости сглаживания
        bool localPlayerOnShip = IsPlayerOnShip(Player.m_localPlayer?.GetPlayerID() ?? 0);
        float lerpPos = localPlayerOnShip ? SMOOTH_POS_ON_SHIP : SMOOTH_POS_OFF_SHIP;
        float lerpRot = localPlayerOnShip ? SMOOTH_ROT_ON_SHIP : SMOOTH_ROT_OFF_SHIP;

        float error = Vector3.Distance(t.position, predicted);
        if (error > CORRECTION_THRESHOLD)
        {
            t.position = predicted;
            t.rotation = d.targetRot;
        }
        else
        {
            t.position = Vector3.Lerp(t.position, predicted, lerpPos);
            float rotError = Quaternion.Angle(t.rotation, d.targetRot);
            t.rotation = rotError > ROT_CORRECTION_THRESHOLD ? d.targetRot : Quaternion.Slerp(t.rotation, d.targetRot, lerpRot);
        }
    }
}
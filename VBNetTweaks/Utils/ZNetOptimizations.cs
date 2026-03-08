namespace VBNetTweaks.Utils
{
    [HarmonyPatch]
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
    public static class ZNetOptimizations
    {
        private static float _lastThrottlerUpdate;
        private const float THROTTLER_INTERVAL = 0.5f;

        [HarmonyPostfix]
        public static void Postfix(ZNet __instance)
        {
            if (!__instance || !ZNet.instance) return;

            RpcBatcher.Update();

            float now = Time.time;
            if (now - _lastThrottlerUpdate >= THROTTLER_INTERVAL)
            {
                AdaptiveThrottler.Update(__instance, Time.deltaTime);
                _lastThrottlerUpdate = now;
            }

            PerformanceMonitor.Track("ZNet.Update", () => { });
        }
    }
}
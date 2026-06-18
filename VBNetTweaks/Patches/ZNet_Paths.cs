namespace VBNetTweaks.Patches
{
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
    public static class ZNet_Paths
    {
        [HarmonyPostfix]
        public static void Postfix(ZNet __instance)
        {
            if (__instance && ZNet.instance) PerformanceMonitor.Track("ZNet.Update", delegate { });
        }
    }
}
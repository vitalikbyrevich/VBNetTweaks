namespace VBNetTweaks.Patches
{
    [HarmonyPatch(typeof(ZNet), "Update")]
    public static class ZNet_Paths
    {
        [HarmonyPostfix]
        public static void Postfix(ZNet __instance)
        {
            if (__instance != null && ZNet.instance != null)
            {
                if (VBNetTweaks.ModuleCompression.Value)
                {
                    ZDONetworkOptimizer.Initialize();
                }
                PerformanceMonitor.Track("ZNet.Update", delegate { });
            }
        }
    }
}
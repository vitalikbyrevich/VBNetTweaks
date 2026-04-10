namespace VBNetTweaks.Patches
{
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
    public static class ZNet_Paths
    {
        [HarmonyPostfix]
        public static void Postfix(ZNet __instance)
        {
            if (!__instance || !ZNet.instance) return;
        
            if (VBNetTweaks.ModuleCompression.Value)
            {
                ZDONetworkOptimizer.CheckAndInitCompression();
            }
        
            PerformanceMonitor.Track("ZNet.Update", () => { });
        }
    }
}
namespace VBNetTweaks.RPCUtills;

[HarmonyPatch(typeof(ZRoutedRpc))]
static class ZRoutedRpcInvokePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(ZRoutedRpc.InvokeRoutedRPC), new[] { typeof(long), typeof(ZDOID), typeof(string), typeof(object[]) })]
    static void Prefix(ZRoutedRpc __instance, long targetPeerID, ZDOID targetZDO, string methodName, object[] parameters)
    {
        if (!VBNetTweaks.ModuleRPCRadiusFiltering.Value) return;
        if (!__instance.m_server) return;
        
        Vector3 origin = GetOriginFromParameters(parameters, targetZDO);
        if (origin != Vector3.zero) RPCPositionContext.SetCurrentPosition(origin);
    }
    
    private static Vector3 GetOriginFromParameters(object[] parameters, ZDOID targetZDO)
    {
        foreach (var param in parameters)
        {
            if (param is Vector3 vec) return vec;
            if (param is HitData hit) return hit.m_point;
            if (param is ZNetView view && view.IsValid()) return view.transform.position;
        }
        
        if (!targetZDO.IsNone())
        {
            var zdo = ZDOMan.instance?.GetZDO(targetZDO);
            if (zdo != null) return zdo.GetPosition();
        }
        
        return Vector3.zero;
    }
}
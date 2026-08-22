namespace VBNetTweaks.Patches;

[HarmonyPatch]
public static class Ship_Patch
{
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
}
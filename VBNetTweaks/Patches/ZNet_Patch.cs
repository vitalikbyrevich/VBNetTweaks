namespace VBNetTweaks.Patches;

[HarmonyPatch]
public class ZNet_Patch
{
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Start)), HarmonyPostfix]
    private static void ZNet_Start_Patch()
    {
        if (Helper.IsDedicated()) Application.targetFrameRate = VBNetTweaks.c_TargetFPS.Value;
    }
    
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.SendPeriodicData)), HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> SendPeriodicData_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        bool replaced = false;

        for (int i = 0; i < codes.Count; i++)
        {
            if (codes[i].opcode == OpCodes.Ldc_R4 && codes[i].operand is float f && Math.Abs(f - 2f) < 0.001f)
            {
                codes[i].opcode = OpCodes.Call;
                codes[i].operand = AccessTools.Method(typeof(Helper), nameof(Helper.GetMapPositionSendInterval));
                replaced = true;
            }
        }

        if (!replaced) Helper.LogDebug("[MapPositionSync] SendPeriodicData constant 2f not found!");

        return codes;
    }

    // Клиент: начинаем буферизовать входящие ZDOData сразу при подключении
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection)),HarmonyPostfix]
    private static void OnNewConnection_Postfix(ZNet __instance, ZNetPeer peer)
    {
        if (!VBNetTweaks.c_ModEnabled.Value) return;
        if (__instance.IsServer()) return;

        var packages = new List<ZPackage>();
        Helper._buffers[peer.m_rpc] = packages;

        peer.m_rpc.Register("ZDOData", (ZRpc _, ZPackage pkg) =>
        {
            packages.Add(pkg);
        });
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown)),HarmonyPostfix]
    private static void Shutdown_Postfix() => Helper._buffers.Clear();

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect), new Type[] { typeof(ZNetPeer) }),HarmonyPostfix]
    private static void Disconnect_Postfix(ZNetPeer peer) => Helper._buffers.Remove(peer.m_rpc);
    
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy)), HarmonyPostfix]
    private static void ClearTracks() => MiniMap_Patch._playerTracks.Clear();
}
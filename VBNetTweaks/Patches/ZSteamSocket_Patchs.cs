namespace VBNetTweaks.Patches;

[HarmonyPatch]
public static class ZSteamSocket_Patchs
{
    private static bool _isPatched = false;

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
    static IEnumerable<CodeInstruction> ZSteamSocket_RegisterGlobalCallbacks_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        if (_isPatched) return instructions;

        ZLog.LogWarning("[VBNetTweaks] Transpiler entered for ZSteamSocket.RegisterGlobalCallbacks");

        var code = new List<CodeInstruction>(instructions);
        bool found = false;

        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].opcode == OpCodes.Ldc_I4 && (int)code[i].operand == 153600)
            {
                int newLimit = 50000000; // 50 МБ/с
                code[i].operand = newLimit;
                found = true;
                ZLog.LogWarning($"[VBNetTweaks] Steam transfer rate patched: 153600 -> {newLimit}");
                break;
            }
        }

        if (!found)
        {
            ZLog.LogWarning("[VBNetTweaks] WARNING: Steam transfer rate constant 153600 NOT FOUND in IL!");
        }
        
        _isPatched = true;
        return code;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
    static void ZSteamSocket_RegisterGlobalCallbacks_Postfix()
    {
        try
        {
            ZLog.LogWarning("[VBNetTweaks] Applying Steam Socket Settings via Postfix...");

            var utils = typeof(SteamNetworkingUtils);
            if (utils == null)
            {
                ZLog.LogError("[VBNetTweaks] SteamNetworkingUtils type not found!");
                return;
            }

            var setCfg = utils.GetMethod("SetConfigValue", new Type[]
            {
                typeof(ESteamNetworkingConfigValue), typeof(ESteamNetworkingConfigScope), typeof(IntPtr), typeof(ESteamNetworkingConfigDataType), typeof(IntPtr)
            });

            if (setCfg == null)
            {
                ZLog.LogError("[VBNetTweaks] SetConfigValue method not found!");
                return;
            }

            void SetInt(ESteamNetworkingConfigValue key, int value)
            {
                GCHandle h = GCHandle.Alloc(value, GCHandleType.Pinned);
                try
                {
                    setCfg.Invoke(null, new object[]
                    {
                        key,
                        ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero,
                        ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                        h.AddrOfPinnedObject()
                    });
                }
                finally
                {
                    h.Free();
                }
            }

            int min = Math.Max(64, VBNetTweaks.SteamSendRateMinKB.Value) * 1024;
            int max = Math.Max(min, VBNetTweaks.SteamSendRateMaxKB.Value) * 1024;
            int buf = Math.Max(8 * 1024 * 1024, VBNetTweaks.SteamSendBufferSize.Value);

            SetInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, min);
            SetInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, max);
            SetInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, buf);

            ZLog.LogWarning($"[VBNetTweaks] Steam send rates applied: min={min / 1024}KB/s, max={max / 1024}KB/s, buffer={buf / 1024 / 1024}MB");
        }
        catch (Exception e)
        {
            ZLog.LogError($"[VBNetTweaks] Error applying Steam send rates: {e.Message}\n{e.StackTrace}");
        }
    }
}
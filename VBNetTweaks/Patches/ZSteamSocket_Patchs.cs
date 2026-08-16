namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZSteamSocket_Patchs
    {
        private static bool _steamApplied;

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
        private static IEnumerable<CodeInstruction> RegisterGlobalCallbacks_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!VBNetTweaks.c_ModuleSteamOptimizations.Value) return instructions;

            var codes = new List<CodeInstruction>(instructions);
            
            bool timeoutReplaced = false;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4 && codes[i].operand is float f && Math.Abs(f - 30000f) < 0.001f)
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = AccessTools.Method(typeof(SyncTuning), nameof(SyncTuning.GetTimeoutConnected));
                    timeoutReplaced = true;
                }
            }

            if (!timeoutReplaced) Helper.LogDebug("TimeoutConnected constant 30000 not found!");

            return codes;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
        private static void ApplySteamBuffers()
        {
            if (!VBNetTweaks.c_ModuleSteamOptimizations.Value || _steamApplied) return;
            _steamApplied = true;
            
            try
            {
                int sendBuffer = Math.Max(512 * 1024, ( VBNetTweaks.c_SteamSendBufferSizeKB.Value) * 1024);
                int maxRate = Math.Max(64, VBNetTweaks.c_SteamSendRateMaxKB.Value) * 1024;
                int minRate = Math.Max(256 * 1024, maxRate / 4);
                int recvMax = VBNetTweaks.c_SteamRecvMaxMessageSize.Value * 1024 * 1024;

                SetConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, sendBuffer);
                Helper.LogDebug($"SendBufferSize: {sendBuffer/1024}KB");

                SetConfigInt((ESteamNetworkingConfigValue)47, sendBuffer);
                Helper.LogDebug($"RecvBufferSize: {sendBuffer/1024}KB");

                SetConfigInt((ESteamNetworkingConfigValue)49, recvMax);
                Helper.LogDebug($"RecvMaxMessageSize: {recvMax/1024/1024}MB");
                
                SetConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, minRate);
                SetConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, maxRate);
            }
            catch (Exception e)
            {
                Helper.LogDebug($"Failed to apply Steam buffers: {e.Message}");
            }
        }

        private static void SetConfigInt(ESteamNetworkingConfigValue config, int value)
        {
            try
            {
                GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);
                try
                {
                    SteamNetworkingUtils.SetConfigValue(config, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, handle.AddrOfPinnedObject());
                }
                finally
                {
                    handle.Free();
                }
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"Failed to set int {config}: {ex.Message}");
            }
        }
    }
}
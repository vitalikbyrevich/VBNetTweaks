namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZSteamSocket_Patchs
    {
        private static readonly float TIMEOUT_CONNECTED = VBNetTweaks.c_SteamTimeoutConnected.Value;
        private static readonly float TIMEOUT_KEEPALIVE = VBNetTweaks.c_SteamTimeoutKeepalive.Value;
        private static readonly int RECV_MAX_MESSAGE_SIZE = VBNetTweaks.c_SteamRecvMaxMessageSize.Value * 1024 * 1024;

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
        private static IEnumerable<CodeInstruction> RegisterGlobalCallbacks_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!VBNetTweaks.c_ModuleSteamOptimizations.Value) return instructions;

            var codes = new List<CodeInstruction>(instructions);
            
            bool timeoutReplaced = false;
            bool rateReplaced = false;
            
            int maxRate = Math.Max(64, Helper.IsServer() ? VBNetTweaks.c_SteamSendRateMaxKB_S.Value : VBNetTweaks.c_SteamSendRateMaxKB.Value) * 1024;

            for (int i = 0; i < codes.Count; i++)
            {
                var instruction = codes[i];

                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float floatValue && Math.Abs(floatValue - 30000f) < 0.001f)
                {
                    codes[i].operand = TIMEOUT_CONNECTED;
                    timeoutReplaced = true;
                    Helper.LogDebug($"TimeoutConnected: 30000 -> {TIMEOUT_CONNECTED}");
                }

                if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int intValue && intValue == 153600)
                {
                    codes[i].operand = maxRate;
                    rateReplaced = true;
                    Helper.LogDebug($"SendRate: 153600 -> {maxRate}");
                }
            }

            if (!timeoutReplaced) Helper.LogDebug("TimeoutConnected constant 30000 not found!");
            if (!rateReplaced) Helper.LogDebug("SendRate constant 153600 not found!");

            return codes;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
        private static void ApplySteamBuffers()
        {
            if (!VBNetTweaks.c_ModuleSteamOptimizations.Value) return;
            
            try
            {
                int sendBuffer = Math.Max(512 * 1024, (Helper.IsServer() ? VBNetTweaks.c_SteamSendBufferSizeKB_S.Value : VBNetTweaks.c_SteamSendBufferSizeKB.Value) * 1024);
                int maxRate = Math.Max(64, Helper.IsServer() ? VBNetTweaks.c_SteamSendRateMaxKB_S.Value : VBNetTweaks.c_SteamSendRateMaxKB.Value) * 1024;
                int minRate = Math.Max(256 * 1024, maxRate / 4);

                SetConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, sendBuffer);
                Helper.LogDebug($"SendBufferSize: {sendBuffer/1024}KB");

                // ============================================
                // RecvBufferSize — через ID (10)
                // ============================================
                SetConfigInt((ESteamNetworkingConfigValue)47, sendBuffer);
                Helper.LogDebug($"RecvBufferSize: {sendBuffer/1024}KB");

                // ============================================
                // RecvMaxMessageSize — через ID (12)
                // ============================================
                SetConfigInt((ESteamNetworkingConfigValue)49, RECV_MAX_MESSAGE_SIZE);
                Helper.LogDebug($"RecvMaxMessageSize: {RECV_MAX_MESSAGE_SIZE/1024/1024}MB");

                // ============================================
                // TimeoutKeepAlive — через ID (1)
                // ============================================
              /*  SetConfigFloat((ESteamNetworkingConfigValue)1, TIMEOUT_KEEPALIVE);
                Helper.LogDebug($"TimeoutKeepAlive: {TIMEOUT_KEEPALIVE}s");*/
                
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

        private static void SetConfigFloat(ESteamNetworkingConfigValue config, float value)
        {
            try
            {
                GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);
                try
                {
                    SteamNetworkingUtils.SetConfigValue(config, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Float, handle.AddrOfPinnedObject());
                }
                finally
                {
                    handle.Free();
                }
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"Failed to set float {config}: {ex.Message}");
            }
        }
    }
}
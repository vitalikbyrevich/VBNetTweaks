namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZSteamSocket_Patchs
    {
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
        private static IEnumerable<CodeInstruction> RegisterGlobalCallbacks_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
          //  if (!VBNetTweaks.c_ModuleSteamOptimizations.Value) return instructions;

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
           // if (!VBNetTweaks.c_ModuleSteamOptimizations.Value) return;
            try
            {
                // === БУФЕРЫ И СКОРОСТИ ===
                int sendBuffer = Math.Max(512 * 1024, (VBNetTweaks.c_SteamSendBufferSizeKB.Value) * 1024);
                int maxRate = Math.Max(64, VBNetTweaks.c_SteamSendRateMaxKB.Value) * 1024;
                int recvMax = VBNetTweaks.c_SteamRecvMaxMessageSize.Value * 1024 * 1024;

               /* SetConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, sendBuffer);
                Helper.LogDebug($"SendBufferSize: {sendBuffer / 1024}KB");*/

                SetConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, maxRate);

                // === ПРИЁМНЫЕ БУФЕРЫ (Защита от дропов при лагах CPU) ===
                // 47 = RecvBufferSize (байты). Должен быть >= 48 * средний_размер_пакета
                SetConfigInt((ESteamNetworkingConfigValue)47, Math.Max(sendBuffer, recvMax));
                Helper.LogDebug($"RecvBufferSize: {Math.Max(sendBuffer, recvMax) / 1024}KB");

                // 48 = RecvBufferMessages (количество пакетов в очереди)
                SetConfigInt((ESteamNetworkingConfigValue)48, 2048);
                Helper.LogDebug("RecvBufferMessages: 2048 packets");

                // 49 = RecvMaxMessageSize (максимальный размер ОДНОГО сообщения)
                SetConfigInt((ESteamNetworkingConfigValue)49, recvMax);
                Helper.LogDebug($"RecvMaxMessageSize: {recvMax / 1024 / 1024}MB");

                // === ОПТИМИЗАЦИЯ ОТЗЫВЧИВОСТИ (Nagle) ===
                // 12 = NagleTime (микросекунды). 0 = отключить задержку склеивания пакетов.
                SetConfigInt((ESteamNetworkingConfigValue)12, 1000);
                Helper.LogDebug("NagleTime: 1000 (disabled for instant send)");

                // === ЗАЩИТА ОТ ТАЙМАУТОВ ПРИ КОННЕКТЕ ===
                // 24 = TimeoutInitial (миллисекунды). Время на первоначальный handshake.
                SetConfigFloat((ESteamNetworkingConfigValue)24, 30000f);
                Helper.LogDebug("TimeoutInitial: 30000ms");
            }
            catch (Exception e)
            {
                Helper.LogDebug($"Failed to apply Steam buffers: {e.Message}");
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

        private static void SetConfigInt(ESteamNetworkingConfigValue config, int value)
        {
            try
            {
                IntPtr p = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    Marshal.WriteInt32(p, value);
                    bool ok = SteamNetworkingUtils.SetConfigValue(
                        config,
                        ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero,
                        ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                        p);

                    if (ok)
                        Helper.LogDebug($"Steam cfg {(int)config} = {value} — OK");
                    else
                        Helper.LogDebug($"Steam cfg {(int)config} = {value} — ОТКЛОНЕНО (нативный SDK не знает этот параметр)");

                    return;
                }
                finally
                {
                    Marshal.FreeHGlobal(p);
                }
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"Steam cfg {(int)config} — исключение: {ex.Message}");
                return;
            }
        }
    }
}
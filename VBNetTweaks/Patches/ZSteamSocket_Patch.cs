namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZSteamSocket_Patch
    {
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks)), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> RegisterGlobalCallbacks_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!VBNetTweaks.c_ModuleSteamOptimizations.Value) return instructions;

            var codes = new List<CodeInstruction>(instructions);

            bool timeoutReplaced = false;
            bool rateReplaced = false;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4 && codes[i].operand is float f && Math.Abs(f - 30000f) < 0.001f)
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = AccessTools.Method(typeof(Helper), nameof(Helper.GetTimeoutConnected));
                    timeoutReplaced = true;
                    Helper.LogDebug($"TimeoutConnected: 30000 -> {Helper.GetTimeoutConnected()}");
                }
                if (codes[i].opcode == OpCodes.Ldc_I4 && codes[i].operand is int intValue && intValue == 153600)
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = AccessTools.Method(typeof(Helper), nameof(Helper.GetSteamSendRateMaxKB));
                    rateReplaced = true;
                    Helper.LogDebug($"SendRate: 153600 -> {Helper.GetSteamSendRateMaxKB()}");
                }
            }

            if (!timeoutReplaced) Helper.LogDebug("TimeoutConnected constant 30000 not found!");
            if (!rateReplaced) Helper.LogDebug("SendRate constant 153600 not found!");

            return codes;
        }
        
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks)), HarmonyPostfix]
        private static void ApplySteamBuffers()
        {
            if (!VBNetTweaks.c_ModuleSteamOptimizations.Value) return;
            try
            {
                int sendBuffer = Helper.GetSteamSendBufferSizeKB();
                int recvBuffer = Helper.GetSteamRecvBufferMessages();

                // === ПРИЁМНЫЕ БУФЕРЫ (Защита от дропов при лагах CPU) ===
                SetConfigInt((ESteamNetworkingConfigValue)47, sendBuffer);
                Helper.LogDebug($"RecvBufferSize: {sendBuffer / 1024}KB");

                // 48 = RecvBufferMessages (количество пакетов в очереди)
                SetConfigInt((ESteamNetworkingConfigValue)48, recvBuffer);
                Helper.LogDebug($"RecvBufferMessages: {recvBuffer}");

                // === ОПТИМИЗАЦИЯ ОТЗЫВЧИВОСТИ (Nagle) ===
                // 12 = NagleTime (микросекунды). 0 = отключить задержку склеивания пакетов.
                SetConfigInt((ESteamNetworkingConfigValue)12, 2500);
                Helper.LogDebug("NagleTime: 2500 (disabled for instant send)");

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
        
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.Send)),HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Send_Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceSendQueuedPackages(instructions);

        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.Flush)),HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Flush_Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceSendQueuedPackages(instructions);

        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.Update)),HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceSendQueuedPackages(instructions);

        private static IEnumerable<CodeInstruction> ReplaceSendQueuedPackages(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo operand = AccessTools.Method(typeof(ZSteamSocket_Patch), (Helper.IsDedicated() || Helper.IsServer()) ? nameof(ZSteamSocket_Patch.Replacement_Server) : nameof(ZSteamSocket_Patch.Replacement_Client));
            MethodInfo operand2 = AccessTools.Method(typeof(ZSteamSocket), nameof(ZSteamSocket.SendQueuedPackages));
            CodeMatcher codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(false, new CodeMatch(OpCodes.Call, operand2));
            if (!codeMatcher.IsValid)
            {
                codeMatcher.Start();
                codeMatcher.MatchForward(false, new CodeMatch(OpCodes.Callvirt, operand2));
            }

            if (!codeMatcher.IsValid)
            {
                Debug.LogError("NetworkSpeedup: Failed to find ZSteamSocket.SendQueuedPackages call");
                return codeMatcher.InstructionEnumeration();
            }

            codeMatcher.SetInstruction(new CodeInstruction(OpCodes.Call, operand));
            return codeMatcher.InstructionEnumeration();
        }
        
        private unsafe static void Replacement_Client(ZSteamSocket socket)
        {
            if (!socket.IsConnected() || socket.m_con == HSteamNetConnection.Invalid) return;
            while (socket.m_sendQueue.Count > 0)
            {
                byte[] array = socket.m_sendQueue.Peek();
                if (array == null || array.Length == 0)
                {
                    socket.m_sendQueue.Dequeue();
                    continue;
                }
                EResult eResult;
                fixed (byte* ptr = array)
                {
                    eResult = SteamNetworkingSockets.SendMessageToConnection(socket.m_con, (IntPtr)ptr, (uint)array.Length, 8, out var _);
                }
                if (eResult != EResult.k_EResultOK)
                {
                    ZLog.Log("Failed to send data " + eResult);
                    break;
                }
                socket.m_totalSent += array.Length;
                socket.m_sendQueue.Dequeue();
            }
        }

        private unsafe static void Replacement_Server(ZSteamSocket socket)
        {
            if (!socket.IsConnected()) return;
            while (socket.m_sendQueue.Count > 0)
            {
                byte[] array = socket.m_sendQueue.Peek();
                EResult eResult;
                if (array == null || array.Length == 0)
                {
                    socket.m_sendQueue.Dequeue();
                    continue;
                }
                fixed (byte* ptr = array)
                {
                    eResult = SteamGameServerNetworkingSockets.SendMessageToConnection(socket.m_con, (IntPtr)ptr, (uint)array.Length, 8, out var _);
                }
                if (eResult != EResult.k_EResultOK)
                {
                    ZLog.Log("Failed to send data " + eResult);
                    break;
                }
                socket.m_totalSent += array.Length;
                socket.m_sendQueue.Dequeue();
            }
        }

        private static void SetConfigFloat(ESteamNetworkingConfigValue config, float value)
        {
            try
            {
                GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);
                try
                {
                    SteamNetworkingUtils.SetConfigValue(config, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Float, handle.AddrOfPinnedObject());
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
                    bool ok = SteamNetworkingUtils.SetConfigValue(config, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, p);

                    if (ok) Helper.LogDebug($"Steam cfg {(int)config} = {value} — OK");
                    else Helper.LogDebug($"Steam cfg {(int)config} = {value} — ОТКЛОНЕНО (нативный SDK не знает этот параметр)");
                }
                finally
                {
                    Marshal.FreeHGlobal(p);
                }
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"Steam cfg {(int)config} — исключение: {ex.Message}");
            }
        }
    }
}
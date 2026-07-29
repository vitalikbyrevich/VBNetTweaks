using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;
using Steamworks;

namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZSteamSocket_Patchs
    {
        // ============================================
        // Настройки
        // ============================================
        private const float TIMEOUT_CONNECTED = 120f;
        private const float TIMEOUT_KEEPALIVE = 30f;
        private const int IP_ALLOW_WITHOUT_AUTH = 1;
        private const int RECV_MAX_MESSAGE_SIZE = 8 * 1024 * 1024; // 8 MB

        // ============================================
        // 1. Transpiler: заменяем ТОЛЬКО константы в RegisterGlobalCallbacks
        // ============================================
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
        private static IEnumerable<CodeInstruction> RegisterGlobalCallbacks_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!VBNetTweaks.ModuleSteamOptimizations.Value) 
                return instructions;

            var codes = new List<CodeInstruction>(instructions);
            
            bool timeoutReplaced = false;
            bool rateReplaced = false;
            bool bufferReplaced = false;
            
            // Кешируем значения из конфига
            int sendBuffer = Math.Max(512 * 1024, VBNetTweaks.SteamSendBufferSizeKB.Value * 1024);
            int maxRate = Math.Max(64, VBNetTweaks.SteamSendRateMaxKB.Value) * 1024;
            int minRate = Math.Max(256 * 1024, maxRate / 2);

            for (int i = 0; i < codes.Count; i++)
            {
                var instruction = codes[i];

                // ============================================
                // 1. Заменяем TimeoutConnected: 30000 -> 120
                // ============================================
                if (instruction.opcode == OpCodes.Ldc_R4 && 
                    instruction.operand is float floatValue && 
                    Math.Abs(floatValue - 30000f) < 0.001f)
                {
                    codes[i].operand = TIMEOUT_CONNECTED;
                    timeoutReplaced = true;
                    Helper.LogDebug($"[VBNetTweaks] TimeoutConnected: 30000 -> {TIMEOUT_CONNECTED}");
                }

                // ============================================
                // 2. Заменяем SendRate: 153600 -> из конфига
                // ============================================
                if (instruction.opcode == OpCodes.Ldc_I4 && 
                    instruction.operand is int intValue && 
                    intValue == 153600)
                {
                    // Определяем, какое это вхождение (первое или второе)
                    // Первое 153600 — это SendRateMin, второе — SendRateMax
                    // Но проще заменить оба на одно значение или определить по контексту
                    
                    // Вариант: заменяем оба на maxRate (как в NetworkTweaks)
                    // Или можно заменить первое на minRate, второе на maxRate
                    // Но в IL коде они идут подряд, поэтому:
                    
                    // Заменяем оба вхождения на maxRate (как в оригинальном NetworkTweaks)
                    codes[i].operand = maxRate;
                    rateReplaced = true;
                    Helper.LogDebug($"[VBNetTweaks] SendRate: 153600 -> {maxRate}");
                }

                // ============================================
                // 3. Заменяем SendBufferSize (добавляем новый вызов SetConfigValue)
                // ============================================
                // В оригинальном IL коде нет SendBufferSize, поэтому мы его добавляем
                // Через Transpiler это сложно сделать чисто.
                // Лучше оставить Postfix для этого.
            }

            if (!timeoutReplaced) 
                Helper.LogDebug("[VBNetTweaks] TimeoutConnected constant 30000 not found!");

            if (!rateReplaced) 
                Helper.LogDebug("[VBNetTweaks] SendRate constant 153600 not found!");

            return codes;
        }

        // ============================================
        // 2. Postfix: добавляем настройки, которых нет в оригинале
        // ============================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
        private static void ApplySteamBuffers()
        {
            if (!VBNetTweaks.ModuleSteamOptimizations.Value) return;
            
            try
            {
                int sendBuffer = Math.Max(512 * 1024, VBNetTweaks.SteamSendBufferSizeKB.Value * 1024);
                int maxRate = Math.Max(64, VBNetTweaks.SteamSendRateMaxKB.Value) * 1024;
                int minRate = Math.Max(256 * 1024, maxRate / 2);

                // ============================================
                // SendBufferSize — есть в enum
                // ============================================
                SetConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, sendBuffer);
                Helper.LogDebug($"[VBNetTweaks] SendBufferSize: {sendBuffer/1024}KB");

                // ============================================
                // RecvBufferSize — через ID (10)
                // ============================================
                SetConfigInt((ESteamNetworkingConfigValue)10, sendBuffer);
                Helper.LogDebug($"[VBNetTweaks] RecvBufferSize: {sendBuffer/1024}KB");

                // ============================================
                // RecvMaxMessageSize — через ID (12)
                // ============================================
                SetConfigInt((ESteamNetworkingConfigValue)12, RECV_MAX_MESSAGE_SIZE);
                Helper.LogDebug($"[VBNetTweaks] RecvMaxMessageSize: {RECV_MAX_MESSAGE_SIZE/1024/1024}MB");

                // ============================================
                // TimeoutKeepAlive — через ID (1)
                // ============================================
                SetConfigFloat((ESteamNetworkingConfigValue)1, TIMEOUT_KEEPALIVE);
                Helper.LogDebug($"[VBNetTweaks] TimeoutKeepAlive: {TIMEOUT_KEEPALIVE}s");
                
                // ============================================
                // Убеждаемся, что SendRateMin тоже установлен (на случай, если Transpiler не сработал)
                // ============================================
                SetConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, minRate);
                SetConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, maxRate);
            }
            catch (Exception e)
            {
                Helper.LogDebug($"[VBNetTweaks] Failed to apply Steam buffers: {e.Message}");
            }
        }

        // ============================================
        // Вспомогательные методы
        // ============================================
        private static void SetConfigInt(ESteamNetworkingConfigValue config, int value)
        {
            try
            {
                GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);
                try
                {
                    SteamNetworkingUtils.SetConfigValue(
                        config,
                        ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero,
                        ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                        handle.AddrOfPinnedObject()
                    );
                }
                finally
                {
                    handle.Free();
                }
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"[VBNetTweaks] Failed to set int {config}: {ex.Message}");
            }
        }

        private static void SetConfigFloat(ESteamNetworkingConfigValue config, float value)
        {
            try
            {
                GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);
                try
                {
                    SteamNetworkingUtils.SetConfigValue(
                        config,
                        ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero,
                        ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Float,
                        handle.AddrOfPinnedObject()
                    );
                }
                finally
                {
                    handle.Free();
                }
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"[VBNetTweaks] Failed to set float {config}: {ex.Message}");
            }
        }
    }
}
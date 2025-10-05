namespace VBNetTweaks;

[HarmonyPatch]
public static class UnifiedSteamOptimizations
{
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(ZSteamSocket), "RegisterGlobalCallbacks")]
    static IEnumerable<CodeInstruction> ZSteamSocket_RegisterGlobalCallbacks_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // На клиенте не применяем Steam оптимизации
       /* if (!VBNetTweaks.IsServer)
        {
            VBNetTweaks.LogDebug("Steam оптимизации пропущены (клиентский режим)");
            return instructions;
        }*/

        var matcher = new CodeMatcher(instructions).MatchForward(false, new CodeMatch(OpCodes.Ldc_I4, 153600));

        if (matcher.IsInvalid)
        {
            VBNetTweaks.LogDebug("WARNING: Steam transfer rate limit not found");
            return instructions;
        }

        // Используем безопасный метод доступа к настройкам
        int newTransferRate = VBNetTweaks.GetSteamTransferRate();
        matcher.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, newTransferRate)).Print(1, 1, "Steam transfer rate patched");

        VBNetTweaks.LogDebug($"Steam transfer rate patched: 153600 -> {newTransferRate}");

        return matcher.InstructionEnumeration();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZSteamSocket), "RegisterGlobalCallbacks")]
    static void ZSteamSocket_RegisterGlobalCallbacks_Postfix(ZSteamSocket __instance)
    {
        // На клиенте не применяем Steam оптимизации
      /*  if (!VBNetTweaks.IsServer)
        {
            return;
        }*/

        try
        {
            // ВАРИАНТ ЧЕРЕЗ РЕФЛЕКСИЮ - самый надежный
            var steamNetworkingUtils = typeof(ZSteamSocket).Assembly.GetType("Steamworks.SteamNetworkingUtils");
            if (steamNetworkingUtils != null)
            {
                var setConfigValueMethod = steamNetworkingUtils.GetMethod("SetConfigValue",
                    new Type[] { 
                        typeof(ESteamNetworkingConfigValue),
                        typeof(ESteamNetworkingConfigScope), 
                        typeof(IntPtr),
                        typeof(ESteamNetworkingConfigDataType),
                        typeof(IntPtr)
                    });

                if (setConfigValueMethod != null)
                {
                    // Используем безопасный метод доступа к настройкам
                    int bufferSize = VBNetTweaks.GetSteamSendBufferSize();
                    GCHandle handle = GCHandle.Alloc(bufferSize, GCHandleType.Pinned);
                    
                    setConfigValueMethod.Invoke(null, new object[] {
                        ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize,
                        ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero,
                        ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                        handle.AddrOfPinnedObject()
                    });
                    
                    handle.Free();
                    VBNetTweaks.LogDebug($"Steam send buffer size set to: {bufferSize}");
                }
                else VBNetTweaks.LogDebug("SetConfigValue method not found");
            }
            else VBNetTweaks.LogDebug("SteamNetworkingUtils type not found");
        }
        catch (Exception e)
        {
            VBNetTweaks.LogDebug($"Error setting Steam buffer size: {e.Message}");
        }
    }
}
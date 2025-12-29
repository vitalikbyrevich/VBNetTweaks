namespace VBNetTweaks;

[HarmonyPatch]
public static class UnifiedSteamOptimizations
{
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
    static IEnumerable<CodeInstruction> ZSteamSocket_RegisterGlobalCallbacks_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions).MatchForward(false, new CodeMatch(OpCodes.Ldc_I4, 153600));

        if (matcher.IsInvalid)
        {
            VBNetTweaks.LogDebug("WARNING: Steam transfer rate limit not found");
            return instructions;
        }

        // Используем безопасный метод доступа к настройкам
        int newTransferRate = 50000000;
        matcher.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, newTransferRate));

        // Простое логирование вместо Print
        if (VBNetTweaks.DebugEnabled.Value)
        {
            VBNetTweaks.LogVerbose("Steam transfer rate patched in transpiler");
        }

        VBNetTweaks.LogVerbose($"Steam transfer rate patched: 153600 -> {newTransferRate}");

        return matcher.InstructionEnumeration();
    }

    /*  [HarmonyPostfix]
      [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
      static void ZSteamSocket_RegisterGlobalCallbacks_Postfix(ZSteamSocket __instance)
      {
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
                      int bufferSize = 100000000;
                      GCHandle handle = GCHandle.Alloc(bufferSize, GCHandleType.Pinned);

                      setConfigValueMethod.Invoke(null, new object[] {
                          ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize,
                          ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                          IntPtr.Zero,
                          ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                          handle.AddrOfPinnedObject()
                      });

                      handle.Free();
                      VBNetTweaks.LogVerbose($"Steam send buffer size set to: {bufferSize}");
                  }
                  else
                  {
                      VBNetTweaks.LogDebug("SetConfigValue method not found");
                  }
              }
              else
              {
                  VBNetTweaks.LogDebug("SteamNetworkingUtils type not found");
              }
          }
          catch (Exception e)
          {
              VBNetTweaks.LogDebug($"Error setting Steam buffer size: {e.Message}");
          }
      }*/

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
    static void ZSteamSocket_RegisterGlobalCallbacks_Postfix(ZSteamSocket __instance)
    {
        try
        {
            if (VBNetTweaks.EnableSteamSendRate?.Value != true) return;

            var asm = typeof(ZSteamSocket).Assembly;
            var utils = asm.GetType("Steamworks.SteamNetworkingUtils");
            if (utils == null)
            {
                VBNetTweaks.LogDebug("SteamNetworkingUtils type not found");
                return;
            }

            var setCfg = utils.GetMethod("SetConfigValue", new Type[]
            {
                typeof(ESteamNetworkingConfigValue),
                typeof(ESteamNetworkingConfigScope),
                typeof(IntPtr),
                typeof(ESteamNetworkingConfigDataType),
                typeof(IntPtr)
            });
            if (setCfg == null)
            {
                VBNetTweaks.LogDebug("SetConfigValue method not found");
                return;
            }

            // Helper to set int32
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

            VBNetTweaks.LogVerbose($"Steam send rates applied: min={min / 1024}KB/s max={max / 1024}KB/s buffer={buf}");
        }
        catch (Exception e)
        {
            VBNetTweaks.LogDebug($"Error applying Steam send rates: {e.Message}");
        }
    }
}
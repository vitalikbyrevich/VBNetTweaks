namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZSteamSocket_Patchs
    {
        private static bool _steamConfigApplied = false;
        private static bool _steamConfigAttempted = false;
        
        private static int _cachedSendBuffer = -1;
        private static int _cachedMaxRate = -1;
        
        private static bool HasSettingsChanged()
        {
            if (!_steamConfigAttempted) return true;
            
            int currentSendBuffer = VBNetTweaks.SteamSendBufferSizeKB.Value * 1024;
            int currentMaxRate = VBNetTweaks.SteamSendRateMaxKB.Value * 1024;
            
            return currentSendBuffer != _cachedSendBuffer || currentMaxRate != _cachedMaxRate;
        }
        
        private static void UpdateCachedSettings()
        {
            _cachedSendBuffer = VBNetTweaks.SteamSendBufferSizeKB.Value * 1024;
            _cachedMaxRate = VBNetTweaks.SteamSendRateMaxKB.Value * 1024;
            _steamConfigAttempted = true;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
        static IEnumerable<CodeInstruction> RegisterGlobalCallbacks_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!VBNetTweaks.ModuleSteamOptimizations.Value) return instructions;

            var codes = new List<CodeInstruction>(instructions);
            bool found = false;

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == 153600)
                {
                    int rate = Math.Max(64, VBNetTweaks.SteamSendRateMaxKB.Value) * 1024;
                    codes[i].operand = rate;
                    found = true;
                    ZLog.LogWarning($"[VBNetTweaks] Steam rate patched: 153600 -> {rate} bytes/s ({VBNetTweaks.SteamSendRateMaxKB.Value}KB/s)");
                    break;
                }
            }

            if (!found) ZLog.LogWarning("[VBNetTweaks] Steam rate constant 153600 not found in IL!");

            return codes;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
        static void ApplySteamBuffersOnce()
        {
            if (!VBNetTweaks.ModuleSteamOptimizations.Value) return;
            
            if (_steamConfigApplied && !HasSettingsChanged())
            {
                Helper.LogVerbose("[VBNetTweaks] Steam buffers already applied, settings unchanged - skipping");
                return;
            }
            
            ZLog.LogWarning("[VBNetTweaks] Applying Steam buffer settings (first time or config changed)...");

            try
            {
                Type utilsType = null;
                
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType("Steamworks.SteamNetworkingUtils");
                    if (type != null)
                    {
                        utilsType = type;
                        break;
                    }
                    
                    type = assembly.GetType("Steamworks.SteamGameServerNetworkingUtils");
                    if (type != null)
                    {
                        utilsType = type;
                        break;
                    }
                }

                if (utilsType == null)
                {
                    ZLog.LogWarning("[VBNetTweaks] SteamNetworkingUtils type not found, buffer settings skipped.");
                    _steamConfigApplied = true;
                    UpdateCachedSettings();
                    return;
                }

                var setConfigMethod = utilsType.GetMethod("SetConfigValue", 
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new Type[] { typeof(int), typeof(int), typeof(IntPtr), typeof(int), typeof(IntPtr) },
                    null);

                if (setConfigMethod == null)
                {
                    ZLog.LogWarning("[VBNetTweaks] SetConfigValue method not found, buffer settings skipped.");
                    _steamConfigApplied = true;
                    UpdateCachedSettings();
                    return;
                }

                Type configValueType = null;
                Type configScopeType = null;
                Type configDataType = null;
                
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (configValueType == null) configValueType = assembly.GetType("Steamworks.ESteamNetworkingConfigValue");
                    if (configScopeType == null) configScopeType = assembly.GetType("Steamworks.ESteamNetworkingConfigScope");
                    if (configDataType == null) configDataType = assembly.GetType("Steamworks.ESteamNetworkingConfigDataType");
                    
                    if (configValueType != null && configScopeType != null && configDataType != null)
                        break;
                }

                if (configValueType == null || configScopeType == null || configDataType == null)
                {
                    ZLog.LogWarning("[VBNetTweaks] Steam enums not found, buffer settings skipped.");
                    _steamConfigApplied = true;
                    UpdateCachedSettings();
                    return;
                }

                object globalScope = Enum.Parse(configScopeType, "k_ESteamNetworkingConfig_Global");
                object intDataType = Enum.Parse(configDataType, "k_ESteamNetworkingConfig_Int32");

                void SetConfigValue(string keyName, int value)
                {
                    try
                    {
                        object enumValue = Enum.Parse(configValueType, keyName);
                        GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);
                        try
                        {
                            setConfigMethod.Invoke(null, new object[] 
                            {
                                (int)enumValue,
                                (int)globalScope,
                                IntPtr.Zero,
                                (int)intDataType,
                                handle.AddrOfPinnedObject()
                            });
                        }
                        finally
                        {
                            handle.Free();
                        }
                    }
                    catch (Exception ex)
                    {
                        ZLog.LogWarning($"[VBNetTweaks] Failed to set {keyName}: {ex.Message}");
                    }
                }

                int sendBuffer = Math.Max(512 * 1024, VBNetTweaks.SteamSendBufferSizeKB.Value * 1024);
                SetConfigValue("k_ESteamNetworkingConfig_SendBufferSize", sendBuffer);
                
                SetConfigValue("k_ESteamNetworkingConfig_RecvBufferSize", sendBuffer);
                
                SetConfigValue("k_ESteamNetworkingConfig_RecvMaxMessageSize", 4 * 1024 * 1024);
                
                int minRate = Math.Max(256 * 1024, VBNetTweaks.SteamSendRateMaxKB.Value * 1024 / 2);
                SetConfigValue("k_ESteamNetworkingConfig_SendRateMin", minRate);

                _steamConfigApplied = true;
                UpdateCachedSettings();
                ZLog.LogWarning($"[VBNetTweaks] Steam buffers applied: SendBuffer={sendBuffer/1024}KB, MinRate={minRate/1024}KB/s");
            }
            catch (Exception e)
            {
                ZLog.LogError($"[VBNetTweaks] Failed to apply Steam buffer settings: {e.Message}");
                _steamConfigApplied = true;
                UpdateCachedSettings();
            }
        }
    }
}
namespace VBNetTweaks;

[HarmonyPatch]
public static class SteamOptimizations
{
    private static bool _configApplied = false;
    
    private static class SteamUtilsCache
    {
        public static readonly Type UtilsType;
        public static readonly MethodInfo SetConfigValueMethod;
        public static readonly bool IsAvailable;
        
        static SteamUtilsCache()
        {
            try
            {
                var asm = typeof(ZSteamSocket).Assembly;
                UtilsType = asm.GetType("Steamworks.SteamNetworkingUtils");
                
                if (UtilsType != null)
                {
                    SetConfigValueMethod = UtilsType.GetMethod("SetConfigValue", new Type[]
                    {
                        typeof(ESteamNetworkingConfigValue),
                        typeof(ESteamNetworkingConfigScope),
                        typeof(IntPtr),
                        typeof(ESteamNetworkingConfigDataType),
                        typeof(IntPtr)
                    });
                    
                    IsAvailable = SetConfigValueMethod != null;
                }
            }
            catch
            {
                IsAvailable = false;
            }
        }
    }

    private static void ApplySteamConfig()
    {
        if (!SteamUtilsCache.IsAvailable) return;

        try
        {
            int min = Math.Max(64, ModConfig.SteamSendRateMinKB.Value) * 1024;
            int max = Math.Max(min, ModConfig.SteamSendRateMaxKB.Value) * 1024;
            int buf = Math.Max(8 * 1024 * 1024, ModConfig.SteamSendBufferSize.Value);

            SetConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, min);
            SetConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, max);
            SetConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, buf);
        
            Helper.LogVerbose($"Steam rates updated: min={min/1024}KB/s max={max/1024}KB/s buffer={buf/1024/1024}MB");
            _configApplied = true;
        }
        catch (Exception e)
        {
            Helper.LogDebug($"Steam config error: {e.Message}");
        }
    }

    private static void SetConfigInt(ESteamNetworkingConfigValue key, int value)
    {
        if (!SteamUtilsCache.IsAvailable) return;
        
        GCHandle h = GCHandle.Alloc(value, GCHandleType.Pinned);
        try
        {
            SteamUtilsCache.SetConfigValueMethod.Invoke(null, new object[]
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

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
      //  if (!Helper.IsServer()) return instructions;
        
        var codes = instructions.ToList();
        bool patched = false;
        
        int[] targetValues = { 153600, 150000, 155000 };
        
        for (int i = 0; i < codes.Count; i++)
        {
            if (codes[i].opcode == OpCodes.Ldc_I4)
            {
                foreach (var val in targetValues)
                {
                    if (codes[i].operand is int intVal && intVal == val)
                    {
                        int newRate = ModConfig.SteamSendRateMaxKB.Value * 1024;
                        codes[i].operand = newRate;
                        Helper.LogVerbose($"Steam rate patched: {val} -> {newRate}");
                        patched = true;
                        break;
                    }
                }
            }
            if (patched) break;
        }
        
        if (!patched)
        {
            Helper.LogDebug("Steam rate limit not found");
        }
        
        return codes;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.RegisterGlobalCallbacks))]
    static void Postfix()
    {
        if (_configApplied) return;
        if (ModConfig.ModuleSteamOptimizations?.Value != true) return;
        
        ApplySteamConfig();
    }
}
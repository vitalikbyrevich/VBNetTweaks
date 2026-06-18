namespace VBNetTweaks.RPCUtills;

[HarmonyPatch(typeof(ZRoutedRpc))]
static class ZRoutedRpcRegisterPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(ZRoutedRpc.Register), new[] { typeof(string), typeof(Action<long>) })]
    static void RegisterMethod(string name)
    {
        int hash = name.GetStableHashCode();
        if (!_rpcNameCache.ContainsKey(hash)) _rpcNameCache[hash] = name;
    }
    
    private static Dictionary<int, string> _rpcNameCache = new();
    
    public static string GetMethodName(int hash) => _rpcNameCache.GetValueOrDefault(hash, hash.ToString());
}
namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class NetworkSyncPatches
    {
        private static float _teleportBoostEnd = 0f;
        public static void TriggerTeleportWindow() => _teleportBoostEnd = Time.time + 5f;
        
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SendZDOs_QueueLimitFix(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int replacedCount = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == 10240)
                {
                    codes[i].operand = VBNetTweaks.ZDOQueueLimit.Value;
                    replacedCount++;
                }
            }
            
            if (replacedCount < 2)
            {
                ZLog.LogWarning("[VBNetTweaks] ZDOQueueLimit patch failed: found less than 2 instances of 10240!");
            }
            else if (replacedCount == 2)
            {
                ZLog.LogWarning($"[VBNetTweaks] ZDOQueueLimit patch to: {VBNetTweaks.ZDOQueueLimit.Value}");
            }

            return codes;
        }
        
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.InLoadingScreen))]
        [HarmonyPrefix]
        public static bool InLoadingScreen_Extend(ref bool __result)
        {
            if (Time.time < _teleportBoostEnd)
            {
                __result = true;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateDestroyObjects))]
        [HarmonyPostfix]
        public static void CreateDestroyObjects_TriggerTeleport()
        {
            if (Player.m_localPlayer?.IsTeleporting() == true) TriggerTeleportWindow();
        }
    }
}
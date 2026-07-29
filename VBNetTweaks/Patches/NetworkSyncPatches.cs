namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class NetworkSyncPatches
    {
        private static float _teleportBoostEnd = 0f;
        public static void TriggerTeleportWindow() => _teleportBoostEnd = Time.time + 5f;
        
        // ============================================
        // 1. SyncPosition Transpiler
        // ============================================
        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.SyncPosition))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SyncPosition_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            
            // Получаем значения из конфига
            float smoothPos = VBNetTweaks.SmoothPosition.Value;
            float smoothRot = VBNetTweaks.SmoothRotation.Value;
            float microThreshold = VBNetTweaks.MicroThreshold.Value;
            
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_R4)
                {
                    float a = (float)list[i].operand;
                    if (Mathf.Approximately(a, 0.2f)) list[i].operand = smoothPos;
                    else if (Mathf.Approximately(a, 0.5f)) list[i].operand = smoothRot;
                    else if (Mathf.Approximately(a, 0.001f)) list[i].operand = microThreshold;
                }
            }
            return list;
        }

        // ============================================
        // 2. ClientSync Transpiler
        // ============================================
        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.ClientSync))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> ClientSync_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            
            float smoothPos = VBNetTweaks.SmoothPosition.Value;
            float microThreshold = VBNetTweaks.MicroThreshold.Value;
            float clientDistanceThreshold = VBNetTweaks.ClientDistanceThreshold.Value;
            
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_R4)
                {
                    float a = (float)list[i].operand;
                    if (Mathf.Approximately(a, 0.2f)) list[i].operand = smoothPos;
                    else if (Mathf.Approximately(a, 0.001f)) list[i].operand = microThreshold;
                    else if (Mathf.Approximately(a, 0.01f)) list[i].operand = clientDistanceThreshold;
                }
            }
            return list;
        }

        // ============================================
        // 3. OwnerSync Transpiler
        // ============================================
        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.OwnerSync))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> OwnerSync_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            
            float microThreshold = VBNetTweaks.MicroThreshold.Value;
            
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_R4 && Mathf.Approximately((float)list[i].operand, 0.001f))
                    list[i].operand = microThreshold;
            }
            return list;
        }
        
        // ============================================
        // 4. QueueLimit Fix (оставляем как было)
        // ============================================
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
                Helper.LogDebug("[VBNetTweaks] ZDOQueueLimit patch failed: found less than 2 instances of 10240!");
            }
            else if (replacedCount == 2)
            {
                Helper.LogDebug($"[VBNetTweaks] ZDOQueueLimit patch to: {VBNetTweaks.ZDOQueueLimit.Value}");
            }

            return codes;
        }
        
        // ============================================
        // 5. Teleport Boost
        // ============================================
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
namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class NetworkSyncPatches
    {
        private static float _teleportBoostEnd = 0f;
        public static void TriggerTeleportWindow() => _teleportBoostEnd = Time.time + 5f;

        public static int GetQueueLimit() => Mathf.Max(4096, Helper.IsServer() ? VBNetTweaks.c_ZDOQueueLimit_S.Value : VBNetTweaks.c_ZDOQueueLimit.Value);

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.SyncPosition))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SyncPosition_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);

            var getSmoothPos = AccessTools.Method(typeof(SyncTuning), nameof(SyncTuning.GetSmoothPosition));
            var getSmoothRot = AccessTools.Method(typeof(SyncTuning), nameof(SyncTuning.GetSmoothRotation));
            var getMicroThreshold = AccessTools.Method(typeof(SyncTuning), nameof(SyncTuning.GetMicroThreshold));
            var getTeleportDistance = AccessTools.Method(typeof(SyncTuning), nameof(SyncTuning.GetTeleportDistanceThreshold));

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_R4)
                {
                    float a = (float)list[i].operand;
                    if (Mathf.Approximately(a, 0.2f))
                    {
                        list[i].opcode = OpCodes.Call;
                        list[i].operand = getSmoothPos;
                    }
                    else if (Mathf.Approximately(a, 0.5f))
                    {
                        list[i].opcode = OpCodes.Call;
                        list[i].operand = getSmoothRot;
                    }
                    else if (Mathf.Approximately(a, 0.001f))
                    {
                        list[i].opcode = OpCodes.Call;
                        list[i].operand = getMicroThreshold;
                    }
                    else if (Mathf.Approximately(a, 5f))
                    {
                        list[i].opcode = OpCodes.Call;
                        list[i].operand = getTeleportDistance;
                    }
                }
            }

            return list;
        }

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.ClientSync))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> ClientSync_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);

            var getSmoothPos = AccessTools.Method(typeof(SyncTuning), nameof(SyncTuning.GetSmoothPosition));
            var getSmoothRot = AccessTools.Method(typeof(SyncTuning), nameof(SyncTuning.GetSmoothRotation));
            var getMicroThreshold = AccessTools.Method(typeof(SyncTuning), nameof(SyncTuning.GetMicroThreshold));
            var getClientDistance = AccessTools.Method(typeof(SyncTuning), nameof(SyncTuning.GetClientDistanceThreshold));
            var getTeleportRotation = AccessTools.Method(typeof(SyncTuning), nameof(SyncTuning.GetTeleportRotationThreshold));

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_R4)
                {
                    float a = (float)list[i].operand;
                    if (Mathf.Approximately(a, 0.2f))
                    {
                        list[i].opcode = OpCodes.Call;
                        list[i].operand = getSmoothPos;
                    }
                    else if (Mathf.Approximately(a, 0.5f))
                    {
                        list[i].opcode = OpCodes.Call;
                        list[i].operand = getSmoothRot;
                    }
                    else if (Mathf.Approximately(a, 0.001f))
                    {
                        list[i].opcode = OpCodes.Call;
                        list[i].operand = getMicroThreshold;
                    }
                    else if (Mathf.Approximately(a, 0.01f))
                    {
                        list[i].opcode = OpCodes.Call;
                        list[i].operand = getClientDistance;
                    }
                    else if (Mathf.Approximately(a, 45f))
                    {
                        list[i].opcode = OpCodes.Call;
                        list[i].operand = getTeleportRotation;
                    }
                }
            }

            return list;
        }

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.OwnerSync))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> OwnerSync_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);

            var getMicroThreshold = AccessTools.Method(typeof(SyncTuning), nameof(SyncTuning.GetMicroThreshold));

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_R4 && Mathf.Approximately((float)list[i].operand, 0.001f))
                {
                    list[i].opcode = OpCodes.Call;
                    list[i].operand = getMicroThreshold;
                }
            }

            return list;
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SendZDOs_QueueLimitFix(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int replacedCount = 0;

            var getQueueLimitMethod = AccessTools.Method(typeof(NetworkSyncPatches), nameof(NetworkSyncPatches.GetQueueLimit));

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == 10240)
                {
                    // Заменяем инструкцию
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = getQueueLimitMethod;
                    // ✅ Не создаем новую инструкцию, а изменяем существующую
                    // Все метки и информация об исключениях сохраняются автоматически!
                    replacedCount++;
                }
            }

            if (replacedCount < 2)
            {
                Helper.LogDebug("ZDOQueueLimit patch failed: found less than 2 instances of 10240!");
            }
            else if (replacedCount == 2)
            {
                if (Helper.IsServer()) Helper.LogDebug($"ZDOQueueLimit patch to: {VBNetTweaks.c_ZDOQueueLimit_S.Value}");
                else Helper.LogDebug($"ZDOQueueLimit patch to: {VBNetTweaks.c_ZDOQueueLimit.Value}");
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
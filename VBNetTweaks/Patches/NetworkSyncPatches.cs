namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class NetworkSyncPatches
    {
        private const float SmoothPos = 0.22f;      // Быстро догоняет позицию (ваниль 0.2f)
        private const float SmoothRot = 0.45f;      // Быстро выравнивает поворот
        private const float TeleportThreshold = 5f; // Ванильный порог (резкий скачок только при рассинхроне >5м)
        private const float MicroThreshold = 0.004f;// Фильтр дрожания на месте
        private static bool _loggedSettings;
        
        private static float _teleportBoostEnd = 0f;
        public static void TriggerTeleportWindow() => _teleportBoostEnd = Time.time + 5f;
        
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update))]
        [HarmonyPostfix]
        static void LogNetworkSettingsOnce()
        {
            if (!_loggedSettings && ZNet.instance)
            {
                _loggedSettings = true;
                float interval = VBNetTweaks.SendInterval?.Value ?? 0.05f;
                int peers = VBNetTweaks.PeersPerUpdate?.Value ?? 20;
                Helper.LogVerbose($"[VBNetTweaks] Network Config Applied -> SendInterval: {interval:F3}s ({(1f/interval):F1}Hz) | PeersPerUpdate: {peers}");
            }
        }
        
        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.SyncPosition))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SyncPosition_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4)
                {
                    float val = (float)codes[i].operand;
                    if (Mathf.Approximately(val, 0.2f)) codes[i].operand = SmoothPos;
                    else if (Mathf.Approximately(val, 0.5f)) codes[i].operand = SmoothRot;
                    else if (Mathf.Approximately(val, 5f)) codes[i].operand = TeleportThreshold;
                    else if (Mathf.Approximately(val, 0.001f)) codes[i].operand = MicroThreshold;
                }
            }
            return codes;
        }

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.ClientSync))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> ClientSync_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4)
                {
                    float val = (float)codes[i].operand;
                    if (Mathf.Approximately(val, 0.2f)) codes[i].operand = SmoothPos;
                    else if (Mathf.Approximately(val, 5f)) codes[i].operand = TeleportThreshold;
                    else if (Mathf.Approximately(val, 0.001f)) codes[i].operand = MicroThreshold;
                    else if (Mathf.Approximately(val, 0.01f)) codes[i].operand = 0.005f;
                }
            }
            return codes;
        }

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
        
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs))]
        [HarmonyPrefix]
        public static void BeforeSendZDOs(ZDOMan.ZDOPeer peer)
        {
            if (VBNetTweaks.DebugEnabled.Value && peer?.m_peer?.m_socket != null)
            {
                if (Time.frameCount % 600 == 0)
                {
                    var status = ZDONetworkOptimizer.GetCompressionStatus();
                    ZLog.LogWarning($"[Network] Compression status:\n{status}");
                }
            }
        }
        
      /*  [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs))]
        [HarmonyPostfix]
        public static void SendZDOs_Debug(ZDOMan __instance, ZDOMan.ZDOPeer peer, bool flush)
        {
            if (VBNetTweaks.DebugEnabled.Value)
            {
                int queueSize = peer.m_peer?.m_socket?.GetSendQueueSize() ?? 0;
                if (queueSize > 8000) Helper.LogDebug($"[ZDO] Queue spike: {queueSize}B");
            }
        }*/
        
        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.OwnerSync))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> OwnerSync_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4 && Mathf.Approximately((float)codes[i].operand, 0.001f)) codes[i].operand = MicroThreshold;
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
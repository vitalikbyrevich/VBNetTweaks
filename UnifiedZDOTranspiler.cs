namespace VBNetTweaks;

[HarmonyPatch]
public static class UnifiedZDOTranspiler
{
    // Инструменты отладки (из ZRpcPatch)
    public static CodeMatcher GetPosition(this CodeMatcher codeMatcher, out int position)
    {
        position = codeMatcher.Pos;
        return codeMatcher;
    }

    public static CodeMatcher AddLabel(this CodeMatcher codeMatcher, out Label label)
    {
        label = new Label();
        codeMatcher.AddLabels(new[] { label });
        return codeMatcher;
    }

    public static CodeMatcher Print(this CodeMatcher codeMatcher, int before, int after, string context = "")
    {
        if (!VBNetTweaks.DebugEnabled.Value) return codeMatcher;

        VBNetTweaks.LogVerbose($"=== IL Code Dump [{context}] ===");
        for (int i = -before; i <= after; ++i)
        {
            int index = codeMatcher.Pos + i;
            if (index <= 0 || index >= codeMatcher.Length) continue;

            try
            {
                var instruction = codeMatcher.InstructionAt(i);
                VBNetTweaks.LogDebug($"[{i:+#;-#;0}] {instruction}");
            }
            catch (Exception e)
            {
                VBNetTweaks.LogDebug($"Ошибка чтения инструкции [{i}]: {e.Message}");
            }
        }
        VBNetTweaks.LogVerbose("=== End IL Dump ===");
        return codeMatcher;
    }

    public static bool IsVirtCall(this CodeInstruction i, string declaringType, string name) 
        => i.opcode == OpCodes.Callvirt && i.operand is MethodInfo methodInfo && methodInfo.DeclaringType?.Name == declaringType && methodInfo.Name == name;

    // Главный транспайлер для ZDOMan.Update - ТОЛЬКО ДЛЯ СЕРВЕРА
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(ZDOMan), "Update")]
    static IEnumerable<CodeInstruction> ZDOManUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        // Если не сервер, возвращаем оригинальные инструкции
     /*   if (!VBNetTweaks.IsServer)
        {
            VBNetTweaks.LogDebug("ZDOMan.Update патч пропущен (клиентский режим)");
            return instructions;
        }*/

        var matcher = new CodeMatcher(instructions).Start().Print(3, 3, "ZDOMan.Update - Start");

        // Ищем вызов SendZDOToPeers2
        matcher.MatchStartForward(new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(ZDOMan), "SendZDOToPeers2")));

        if (matcher.IsInvalid)
        {
            VBNetTweaks.LogDebug("WARNING: SendZDOToPeers2 не найден, патч не применен");
            return instructions;
        }

        VBNetTweaks.LogDebug("Найден SendZDOToPeers2, заменяем на OptimizedSendZDOToPeers");

        matcher
            .Print(2, 2, "Before SendZDOToPeers2 replacement")
            .SetOperandAndAdvance(AccessTools.Method(typeof(UnifiedZDOTranspiler), "OptimizedSendZDOToPeers"))
            .Print(2, 2, "After SendZDOToPeers2 replacement");

        return matcher.InstructionEnumeration();
    }

    // Оптимизированная версия SendZDOToPeers - ТОЛЬКО ДЛЯ СЕРВЕРА
    public static void OptimizedSendZDOToPeers(ZDOMan zdoManager, float dt)
    {
        try
        {
        // Дополнительная проверка на сервер (на всякий случай)
        /*  if (!VBNetTweaks.IsServer)
          {
              zdoManager.SendZDOToPeers2(dt);
              return;
          }*/
            int peerCount = zdoManager.m_peers.Count;
            if (peerCount <= 0)
            {
                VBNetTweaks.LogVerbose($"No peers to send, count: {peerCount}");
                return;
            }

            zdoManager.m_sendTimer += dt;

            // Используем безопасный метод доступа к настройкам
            float sendInterval = VBNetTweaks.GetSendInterval();
            if (zdoManager.m_sendTimer >= sendInterval)
            {
                zdoManager.m_sendTimer = 0f;
                List<ZDOMan.ZDOPeer> peers = zdoManager.m_peers;

                int startPeer = Math.Max(zdoManager.m_nextSendPeer, 0);
                int peersPerUpdate = VBNetTweaks.GetPeersPerUpdate();
                int endPeer = Math.Min(startPeer + peersPerUpdate, peerCount);

                int sentCount = 0;
                for (int i = startPeer; i < endPeer; i++)
                {
                    zdoManager.SendZDOs(peers[i], flush: false);
                    sentCount++;
                }

                zdoManager.m_nextSendPeer = (endPeer < peerCount) ? endPeer : 0;

                if (VBNetTweaks.DebugEnabled.Value)
                {
                    VBNetTweaks.LogVerbose($"Sent ZDOs to {sentCount}/{peerCount} peers (interval: {sendInterval:F3}s, next: {zdoManager.m_nextSendPeer})");
                }
                
            }
            else if (VBNetTweaks.DebugEnabled.Value && peerCount > 0)
            {
                VBNetTweaks.LogVerbose($"Send timer: {zdoManager.m_sendTimer:F3}/{sendInterval:F3}s, peers: {peerCount}, next: {zdoManager.m_nextSendPeer}");
            }
        }
        catch (Exception e)
        {
            VBNetTweaks.LogDebug($"ERROR in OptimizedSendZDOToPeers: {e.Message}");
            // На всякий случай вызываем оригинальный метод
            zdoManager.SendZDOToPeers2(dt);
        }
    }

    // Патч для буферизации ZDO данных (из NetworkTweaks) - ТОЛЬКО ДЛЯ СЕРВЕРА
    private static readonly List<ZPackage> _zdoBuffer = new List<ZPackage>();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    private static void ZNet_OnNewConnection_Postfix(ZNet __instance, ZNetPeer peer)
    {
      //  if (!VBNetTweaks.IsServer) return;
        
        if (!__instance || !__instance.IsServer())
        {
            peer.m_rpc.Register("ZDOData", (ZRpc rpc, ZPackage pkg) =>
            {
                _zdoBuffer.Add(pkg);
                VBNetTweaks.LogVerbose($"Buffered ZDOData package, buffer size: {_zdoBuffer.Count}");
            });
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), "Shutdown")]
    private static void ZNet_Shutdown_Postfix()
    {
     //   if (!VBNetTweaks.IsServer) return;
        
        _zdoBuffer.Clear();
        VBNetTweaks.LogVerbose("Cleared ZDO buffer on shutdown");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZDOMan), "AddPeer")]
    private static void ZDOMan_AddPeer_Postfix(ZDOMan __instance, ZNetPeer netPeer)
    {
     //   if (!VBNetTweaks.IsServer) return;
        
        if (_zdoBuffer.Count > 0)
        {
            VBNetTweaks.LogVerbose($"Processing {_zdoBuffer.Count} buffered ZDO packages for new peer");
            foreach (var package in _zdoBuffer) __instance.RPC_ZDOData(netPeer.m_rpc, package);
            _zdoBuffer.Clear();
        }
    }
}
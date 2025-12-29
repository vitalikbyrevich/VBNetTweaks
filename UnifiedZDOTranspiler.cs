using VBNetTweaks.Utils;

namespace VBNetTweaks;

[HarmonyPatch]
public static class UnifiedZDOTranspiler
{
    private static readonly List<ZPackage> _zdoBuffer = new List<ZPackage>();

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update))]
    private static IEnumerable<CodeInstruction> ZDOManUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions).Start();
        matcher.MatchStartForward(new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(ZDOMan), "SendZDOToPeers2")));
        
        if (matcher.IsInvalid)
        {
            VBNetTweaks.LogDebug("WARNING: SendZDOToPeers2 не найден, патч не применен");
            return instructions;
        }

        VBNetTweaks.LogDebug("Найден SendZDOToPeers2, заменяем на OptimizedSendZDOToPeers");
        matcher.SetOperandAndAdvance(AccessTools.Method(typeof(UnifiedZDOTranspiler), "OptimizedSendZDOToPeers"));
        
        return matcher.InstructionEnumeration();
    }

    private static void TryApplyZDOThrottle(ZDOMan zdoManager, ZDOMan.ZDOPeer peer)
    {
        try
        {
            // Получаем ближайшие ZDO для сектора пира (vanilla пути)
            Vector3 refPos = peer.m_peer.GetRefPos();
            Vector2i zone = ZoneSystem.GetZone(refPos);

            // Списки временных объектов (используем поля ZDOMan)
            var near = new List<ZDO>();
            var distant = new List<ZDO>();

            // vanilla active/distant: берём из ZoneSystem
            int activeArea = ZoneSystem.instance?.m_activeArea ?? 3;
            int distantArea = ZoneSystem.instance?.m_activeDistantArea ?? 5;

            zdoManager.FindSectorObjects(zone, activeArea, distantArea, near, distant);

            float throttleDist = VBNetTweaks.ZDOThrottleDistance.Value;

            // Помечаем tempSortValue — это влияет на приоритет отправки
            for (int i = 0; i < near.Count; i++)
            {
                var z = near[i];
                float d = Vector3.Distance(z.GetPosition(), refPos);
                // ближние — повышаем приоритет
                z.m_tempSortValue = d - 150f;
            }
            for (int i = 0; i < distant.Count; i++)
            {
                var z = distant[i];
                float d = Vector3.Distance(z.GetPosition(), refPos);
                // дальние — понижаем приоритет и чуть реже шлём
                z.m_tempSortValue = d + 150f;
                if (d > throttleDist * 2f)
                {
                    // агрессивное: помечаем как низкоприоритетный — SendZDOs сам отсортирует
                    z.m_tempSortValue += 300f;
                }
            }
        }
        catch (System.Exception e)
        {
            VBNetTweaks.LogDebug($"ZDO throttle error: {e.Message}");
        }
    }

    
    public static void OptimizedSendZDOToPeers(ZDOMan zdoManager, float dt)
    {
        try
        {
            int peerCount = zdoManager.m_peers.Count;
            if (peerCount <= 0) return;

            zdoManager.m_sendTimer += dt;
            float sendInterval = VBNetTweaks.GetSendInterval();
            
            if (zdoManager.m_sendTimer >= sendInterval)
            {
                zdoManager.m_sendTimer = 0f;
                
                int startPeer = Math.Max(zdoManager.m_nextSendPeer, 0);
                int peersPerUpdate = VBNetTweaks.GetPeersPerUpdate();
                int processed = 0;

                for (int i = 0; i < Math.Min(peersPerUpdate, peerCount); i++)
                {
                    int peerIndex = (startPeer + i) % peerCount;
                    var peer = zdoManager.m_peers[peerIndex];

                    if (peer?.m_peer?.m_socket?.IsConnected() == true)
                    {
                        if (Helper.IsServer() && VBNetTweaks.EnableZDOThrottling?.Value == true && VBNetTweaks.ZDOThrottleDistance.Value > 0f) { TryApplyZDOThrottle(zdoManager, peer); }
                        // ПРОСТАЯ ОТПРАВКА БЕЗ ПРИОРИТЕТОВ
                        // ForceSend ZDO автоматически обрабатываются в SendZDOs
                        zdoManager.SendZDOs(peer, flush: false);
                        processed++;
                    }
                }

                zdoManager.m_nextSendPeer = (startPeer + processed) % peerCount;

                if (VBNetTweaks.DebugEnabled.Value)
                {
                    VBNetTweaks.LogVerbose($"Sent ZDOs to {processed}/{peerCount} peers " + $"(interval: {sendInterval:F3}s, next: {zdoManager.m_nextSendPeer})");
                }
            }
        }
        catch (Exception ex)
        {
            VBNetTweaks.LogDebug($"ERROR in OptimizedSendZDOToPeers: {ex.Message}");
            // Fallback to original method
            zdoManager.SendZDOToPeers2(dt);
        }
    }

    // Убрали всю сложную буферизацию - оставляем только базовую
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
    private static void ZNet_OnNewConnection_Postfix(ZNet __instance, ZNetPeer peer)
    {
        if (!Helper.IsServer()) return;
        
        // Минимальная буферизация для новых клиентов
        // Вся основная логика остается в ванильной игре
        VBNetTweaks.LogVerbose($"New client connected: {peer.m_playerName}");
    }
}
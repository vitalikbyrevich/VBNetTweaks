namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class NetStats_Patch
    {
        public struct SendState
        {
            public bool active;   // префикс вообще работал
            public int preSent;   // m_zdosSent до вызова
            public int skip;      // 0 = нет, 1 = queueFull, 2 = lowSpace
        }

        // === ТИК: ZNet.Update() БЕЗ ПАРАМЕТРОВ! ===
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update)),HarmonyPostfix]
        private static void ZNetUpdate_Postfix()   // ← было (float dt) — ошибка
        {
            NetStats.Tick(Time.deltaTime);
        }

        // === ОТПРАВКА: SendZDOs(ZDOPeer peer, bool flush) ===
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs)),HarmonyPrefix]
        private static void SendZDOs_Prefix(ZDOMan __instance, ZDOMan.ZDOPeer peer, bool flush, ref SendState __state)
        {
            __state = default;
            if (!NetStats.Enabled || peer?.m_peer?.m_socket == null) return;

            __state.active = true;
            __state.preSent = __instance.m_zdosSent;

            int q = peer.m_peer.m_socket.GetSendQueueSize();
            int limit = Helper.GetQueueLimit();
            if (!flush && q > limit) { __state.skip = 1; NetStats.SkipQueueFull++; }
            else if (limit - q < 2048) { __state.skip = 2; NetStats.SkipLowSpace++; }
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs)),HarmonyPostfix]
        private static void SendZDOs_Postfix(ZDOMan __instance, bool __result, SendState __state) // by value — только читаем
        {
            if (!__state.active) return;

            if (__state.skip != 0) return;          // цикл пропущен — уже посчитано в prefix

            if (__result)
            {
                NetStats.SendOkPackets++;
                int delta = __instance.m_zdosSent - __state.preSent;
                if (delta > 0) NetStats.ZdoSerialized += delta;

                // m_tempToSync после вызова = список кандидатов этого вызова.
                // Сколько не влезло в байт-бюджет = кандидаты - упакованные.
                int deferred = __instance.m_tempToSync.Count - delta;
                if (deferred > 0) NetStats.ZdoDeferred += deferred;
            }
            else
            {
                NetStats.SendEmpty++;               // вызов был, но отправлять нечего
            }
        }

        // === ПРИЁМ: RPC_ZDOData(ZRpc rpc, ZPackage pkg) ===
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.RPC_ZDOData)),HarmonyPrefix]
        private static void RpcZdoData_Prefix(ZDOMan __instance, ZRpc rpc, ref int __state)
        {
            __state = -1;
            if (!NetStats.Enabled) return;

            if (__instance.FindPeer(rpc) == null)
            {
                NetStats.ZdoUnknownHost++;          // пакет отброшен ванилой
                return;
            }
            __state = __instance.m_zdosRecv;
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.RPC_ZDOData)),HarmonyPostfix]
        private static void RpcZdoData_Postfix(ZDOMan __instance, int __state)
        {
            if (__state < 0) return;
            NetStats.ZdoDataIn++;
            int delta = __instance.m_zdosRecv - __state;
            if (delta > 0) NetStats.ZdoApplied += delta;
        }

        // === ОЧИСТКА ===
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown)),HarmonyPostfix]
        private static void Shutdown_Cleanup() => NetStats.ClearPeers();

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect), new Type[] { typeof(ZNetPeer) }),HarmonyPostfix]
        private static void Disconnect_Cleanup(ZNetPeer peer)
        {
            if (peer?.m_rpc != null) NetStats.RemovePeer(peer.m_rpc);
        }
    }
}
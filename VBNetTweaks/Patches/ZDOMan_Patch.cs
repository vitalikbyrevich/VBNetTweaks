namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZDOMan_Patch
    {
        private static float _budget;
        private static int _cursor;

        private static float PerPeerRate(int peerCount, float interval)
        {
            float baseRate = 1f / Mathf.Max(interval, 0.01f);
            if (peerCount <= 0) return 0f;
            // Малые серверы (< 15 пиров) получают буст до 1.5×
            return baseRate * Mathf.Max(1f, Mathf.Min(1.5f, 15f / peerCount));
        }

        public static void OptimizedSendZDOToPeers(ZDOMan man, float dt)
        {
            try
            {
                int count = man.m_peers.Count;
                if (count == 0)
                {
                    _budget = 0f;
                    return;
                }

                float interval = Helper.GetSendInterval();
                _budget += count * dt * PerPeerRate(count, interval);

                int num = (int)_budget;
                _budget -= num;

                if (num <= 0) return;

                int maxPerFrame = VBNetTweaks.c_MaxPeersPerFrame.Value;
                if (maxPerFrame > 0) num = Mathf.Min(num, maxPerFrame);
                if (num > count)
                {
                    num = count;
                    _budget = 0f;
                }

                for (int i = 0; i < num; i++)
                {
                    ZDOMan.ZDOPeer peer = man.m_peers[(_cursor + i) % count];
                    if (peer?.m_peer?.m_socket?.IsConnected() == true) man.SendZDOs(peer, false);
                }

                _cursor = (_cursor + num) % count;

                // Гасим ванильные поля, чтобы не мешали
                man.m_sendTimer = 0f;
                man.m_nextSendPeer = -1;
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"Error in OptimizedSendZDOToPeers: {ex.Message}");
            }
        }

        private static readonly Dictionary<int, float> _prefabBonuses = new Dictionary<int, float>(256);
        private static readonly List<ZDO> _prioritized = new List<ZDO>(64);
        private static readonly List<ZDO> _remaining = new List<ZDO>(512);

        private static float GetSendBonus(ZDO zdo)
        {
            if (zdo == null) return 0f;
            int prefab = zdo.GetPrefab();
            if (_prefabBonuses.TryGetValue(prefab, out float cached)) return cached;

            float bonus = 0f;
            if (ZNetScene.instance)
            {
                GameObject go = ZNetScene.instance.GetPrefab(prefab);
                if (go)
                {
                    if (go.GetComponent<Player>())    bonus = 120f;
                    else if (go.GetComponent<Ship>()) bonus = 80f;
                    else if (go.GetComponent<Character>()) bonus = 40f;
                }
            }

            _prefabBonuses[prefab] = bonus;
            return bonus;
        }

        private static void ApplyPriority(List<ZDO> objects, Comparison<ZDO> compare)
        {
            if (objects == null || objects.Count < 2) return;

            _prioritized.Clear();
            _remaining.Clear();

            for (int i = 0; i < objects.Count; i++)
            {
                ZDO zdo = objects[i];
                float bonus = GetSendBonus(zdo);
                if (bonus > 0f)
                {
                    zdo.m_tempSortValue -= bonus;
                    _prioritized.Add(zdo);
                }
                else _remaining.Add(zdo);
            }

            if (_prioritized.Count == 0)
            {
                _remaining.Clear();
                return;
            }

            _prioritized.Sort(compare);

            int li = 0, ri = 0, idx = 0;
            while (li < _prioritized.Count && ri < _remaining.Count)
            {
                if (compare(_prioritized[li], _remaining[ri]) <= 0) objects[idx++] = _prioritized[li++];
                else objects[idx++] = _remaining[ri++];
            }
            while (li < _prioritized.Count) objects[idx++] = _prioritized[li++];
            while (ri < _remaining.Count)  objects[idx++] = _remaining[ri++];

            _prioritized.Clear();
            _remaining.Clear();
        }


        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSortSendZDOS)), HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void ServerSortSendZDOS_Postfix(List<ZDO> objects)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return;
            ApplyPriority(objects, ZDOMan.ServerSendCompare);
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ClientSortSendZDOS)), HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void ClientSortSendZDOS_Postfix(List<ZDO> objects)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return;
            ApplyPriority(objects, ZDOMan.ClientSendCompare);
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ShutDown)), HarmonyPostfix]
        private static void ClearCaches()
        {
            _prefabBonuses.Clear();
            _budget = 0f;
            _cursor = 0;
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2)), HarmonyPrefix]
        private static bool ZDOMan_SendZDOToPeers2_Patch(ZDOMan __instance, float dt)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return true;
            OptimizedSendZDOToPeers(__instance, dt);
            return false;
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs)), HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SendZDOs_QueueLimitFix(
            IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int replacedCount = 0;
            var getQueueLimitMethod = AccessTools.Method(typeof(Helper), nameof(Helper.GetQueueLimit));

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == 10240)
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = getQueueLimitMethod;
                    replacedCount++;
                }
            }

            if (replacedCount < 2) Helper.LogDebug("ZDOQueueLimit patch failed: found less than 2 instances of 10240!");
            else if (replacedCount == 2) Helper.LogDebug($"ZDOQueueLimit patch to: {VBNetTweaks.c_ZDOQueueLimit.Value}");

            return codes;
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs)), HarmonyPrefix]
        private static void SendZDOs_RefreshInterestPosition(ZDOMan.ZDOPeer peer)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return;
            if (!Helper.IsServer()) return;
            if (peer?.m_peer == null || !peer.m_peer.IsReady()) return;
            if (peer.m_peer.m_characterID.IsNone()) return;

            ZDO charZdo = ZDOMan.instance.GetZDO(peer.m_peer.m_characterID);
            if (charZdo != null) peer.m_peer.m_refPos = charZdo.GetPosition();
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.AddPeer)), HarmonyPostfix]
        private static void AddPeer_Postfix(ZDOMan __instance, ZNetPeer netPeer)
        {
            if (!Helper._buffers.TryGetValue(netPeer.m_rpc, out var packages)) return;
            foreach (var pkg in packages)
                __instance.RPC_ZDOData(netPeer.m_rpc, pkg);
            Helper._buffers.Remove(netPeer.m_rpc);
        }
    }
}
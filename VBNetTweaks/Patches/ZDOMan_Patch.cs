namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZDOMan_Patch
    {
        public static void OptimizedSendZDOToPeers(ZDOMan man, float dt)
        {
            try
            {
                int count = man.m_peers.Count;
                if (count == 0) return;

                man.m_sendTimer += dt;

                GetAdaptiveParams(count, out float interval, out int maxPeers);

                if (man.m_sendTimer < interval) return;
                man.m_sendTimer -= interval;

                int start = (man.m_nextSendPeer >= 0) ? man.m_nextSendPeer : 0;
                int processed = 0;

                for (int i = 0; i < maxPeers; i++)
                {
                    int idx = (start + i) % count;
                    processed++;
                    ZDOMan.ZDOPeer peer = man.m_peers[idx];
                    if (peer?.m_peer?.m_socket?.IsConnected() != true) continue;
                    man.SendZDOs(peer, flush: false);
                }

                man.m_nextSendPeer = (start + processed) % count;
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"Error in OptimizedSendZDOToPeers: {ex.Message}");
              //  man.SendZDOToPeers2(dt);
            }
        }

        private static void GetAdaptiveParams(int peerCount, out float interval, out int maxPeers)
        {
            interval = Helper.GetSendInterval();

            int divisor = VBNetTweaks.c_PeerCycleDivisor.Value;
            maxPeers = Mathf.CeilToInt((float)peerCount / divisor);

            maxPeers = Mathf.Clamp(maxPeers, 1, peerCount);
        }
        
        
        private const byte PLAYER     = 0;
        private const byte MOBILE     = 1; // Characters + Ships
        private const byte PROJECTILE = 2;
        private const byte REST       = 3;
        private const int BUCKET_COUNT = 4;

        private static readonly List<ZDO>[] Buckets = new List<ZDO>[BUCKET_COUNT]
        {
            new List<ZDO>(32),   // Player
            new List<ZDO>(64),   // Mobile
            new List<ZDO>(64),   // Projectile
            new List<ZDO>(512)   // Rest
        };

        private static readonly int[] Counts = new int[BUCKET_COUNT];
        private static readonly Dictionary<int, byte> _cache = new Dictionary<int, byte>(256);
        private static readonly int PlayerHash = "Player".GetStableHashCode();

        private static void Partition(List<ZDO> objects)
        {
            if (objects == null || objects.Count < 2) return;

            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                Buckets[i].Clear();
                Counts[i] = 0;
            }

            bool alreadySorted = true;
            byte prevRank = 0;

            for (int i = 0; i < objects.Count; i++)
            {
                ZDO zdo = objects[i];
                byte rank = Classify(zdo);
                if (rank < prevRank) alreadySorted = false;
                prevRank = rank;
                Buckets[rank].Add(zdo);
                Counts[rank]++;
            }

            if (!alreadySorted)
            {
                int idx = 0;
                for (int b = 0; b < BUCKET_COUNT; b++)
                {
                    var bucket = Buckets[b];
                    for (int i = 0; i < bucket.Count; i++) objects[idx++] = bucket[i];
                }
            }
        }

        private static byte Classify(ZDO zdo)
        {
            if (zdo == null) return REST;
            int prefab = zdo.GetPrefab();

            if (prefab == PlayerHash) return PLAYER;
            if (_cache.TryGetValue(prefab, out byte cached)) return cached;

            byte result = ClassifyUncached(zdo, prefab);
            if (ZNetScene.instance) _cache[prefab] = result;
            return result;
        }

        private static byte ClassifyUncached(ZDO zdo, int prefab)
        {
            if (!ZNetScene.instance) return REST;

            GameObject go = ZNetScene.instance.GetPrefab(prefab);
            if (!go) return zdo.Type == ZDO.ObjectType.Prioritized ? MOBILE : REST;

            if (go.GetComponent<Projectile>()) return PROJECTILE;
            if (go.GetComponent<Character>()) return MOBILE;
            if (go.GetComponent<Ship>()) return MOBILE;
            if (zdo.Type == ZDO.ObjectType.Prioritized) return MOBILE;

            return REST;
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSortSendZDOS)),HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void ServerSortSendZDOS_Postfix(List<ZDO> objects)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return;
            Partition(objects);
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ClientSortSendZDOS)),HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void ClientSortSendZDOS_Postfix(List<ZDO> objects)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return;
            Partition(objects);
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ShutDown)),HarmonyPostfix]
        private static void ClearCache() => _cache.Clear();


        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2)), HarmonyPrefix]
        private static bool ZDOMan_SendZDOToPeers2_Patch(ZDOMan __instance, float dt)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return true;

            OptimizedSendZDOToPeers(__instance, dt);
            return false;
        }

      /*  [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update)), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> ZDOManUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher codeMatcher = new CodeMatcher(instructions).Start();
            codeMatcher.MatchStartForward(new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(ZDOMan), nameof(ZDOMan.SendZDOToPeers2))));
            if (codeMatcher.IsInvalid)
            {
                Helper.LogDebug("WARNING: SendZDOToPeers2 not found");
                return instructions;
            }
            else Helper.LogDebug("SendZDOToPeers2 success replace to OptimizedSendZDOToPeers");

            codeMatcher.SetOperandAndAdvance(AccessTools.Method(typeof(ZDOMan_Patch), nameof(ZDOMan_Patch.OptimizedSendZDOToPeers)));
            return codeMatcher.InstructionEnumeration();
        }*/

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs)), HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> SendZDOs_QueueLimitFix(IEnumerable<CodeInstruction> instructions)
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

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs)),HarmonyPrefix]
        private static void SendZDOs_RefreshInterestPosition(ZDOMan.ZDOPeer peer)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return;
            if (!Helper.IsServer()) return;
            if (peer?.m_peer == null || !peer.m_peer.IsReady()) return;
            if (peer.m_peer.m_characterID.IsNone()) return;

            ZDO charZdo = ZDOMan.instance.GetZDO(peer.m_peer.m_characterID);
            if (charZdo != null) peer.m_peer.m_refPos = charZdo.GetPosition();
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.AddPeer)),HarmonyPostfix]
        private static void AddPeer_Postfix(ZDOMan __instance, ZNetPeer netPeer)
        {
            if (!Helper._buffers.TryGetValue(netPeer.m_rpc, out var packages)) return;

            foreach (var pkg in packages) __instance.RPC_ZDOData(netPeer.m_rpc, pkg);
            Helper._buffers.Remove(netPeer.m_rpc);
        }
    }
}
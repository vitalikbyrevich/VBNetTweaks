using System.Linq;

namespace VBNetTweaks.RPCUtills
{
    [HarmonyPatch]
    public static class RpcFilter
    {
        private static bool IsFeatureEnabled
        {
            get
            {
                if (!VBNetTweaks.ModuleRPCRadiusFiltering.Value) return false;
                if (ZNet.IsSinglePlayer) return false;
                return true;
            }
        }

        private static bool IsDebugMode
        {
            get
            {
                if (!VBNetTweaks.VerboseLogging.Value) return false;
                if (ZoneSystem.instance) return ZoneSystem.instance.GetGlobalKey("VBNET_DEBUG_RPC");
                return false;
            }
        }

        private static int[] _rangeLimitedMethodHashes;

        private static bool IsRangeLimitedRpc(ZRoutedRpc.RoutedRPCData rpcData)
        {
            return IsRangeLimitedRpc(rpcData.m_methodHash, rpcData.m_targetZDO);
        }

        private static bool IsRangeLimitedRpc(int methodHash, ZDOID nviewTargetZDOID)
        {
            if (_rangeLimitedMethodHashes == null)
            {
                _rangeLimitedMethodHashes = new int[]
                {
                    // Character
                    "Step".GetStableHashCode(),
                    "RPC_DamageText".GetStableHashCode(),
                    "RPC_Heal".GetStableHashCode(),
                    "RPC_Stagger".GetStableHashCode(),
                    "RPC_AddNoise".GetStableHashCode(),
                    "RPC_OnTargeted".GetStableHashCode(),
                    "RPC_OnDeath".GetStableHashCode(),
                    "RPC_UseStamina".GetStableHashCode(),
                    "RPC_UseEitr".GetStableHashCode(),
                    "RPC_Emote".GetStableHashCode(),
                    // MonsterAI
                    "RPC_Wakeup".GetStableHashCode(),
                    "RPC_Sleep".GetStableHashCode(),
                    "RPC_OnNearProjectileHit".GetStableHashCode(),
                    // Ship
                    "Rudder".GetStableHashCode(),
                    "RequestControl".GetStableHashCode(),
                    "ReleaseControl".GetStableHashCode(),
                    // Destructible, MineRock5, TreeLog, TreeBase, WearNTear
                    "RPC_Damage".GetStableHashCode(),
                    // Destructible, WearNTear
                    "RPC_CreateFragments".GetStableHashCode(),
                    // MineRock5
                    "RPC_SetAreaHealth".GetStableHashCode(),
                    // MineRock
                    "Hit".GetStableHashCode(),
                    "Hide".GetStableHashCode(),
                    // TreeBase
                    "RPC_Grow".GetStableHashCode(),
                    "RPC_Shake".GetStableHashCode(),
                    // WearNTear
                    "RPC_Remove".GetStableHashCode(),
                    "RPC_Repair".GetStableHashCode(),
                    "RPC_HealthChanged".GetStableHashCode(),
                    "RPC_ClearCachedSupport".GetStableHashCode(),
                    // Pickable
                    "RPC_SetPicked".GetStableHashCode(),
                    "RPC_Pick".GetStableHashCode(),
                    // Tameable
                    "Command".GetStableHashCode(),
                    "SetName".GetStableHashCode(),
                    "RPC_UnSummon".GetStableHashCode(),
                    "AddSaddle".GetStableHashCode(),
                    "SetSaddle".GetStableHashCode(),
                    // Fireplace, CookingStation
                    "RPC_AddFuel".GetStableHashCode(),
                    "RPC_AddFuelAmount".GetStableHashCode(),
                    "RPC_SetFuelAmount".GetStableHashCode(),
                    "RPC_ToggleOn".GetStableHashCode(),
                    // CookingStation
                    "RPC_RemoveDoneItem".GetStableHashCode(),
                    "RPC_AddItem".GetStableHashCode(),
                    "RPC_SetSlotVisual".GetStableHashCode(),
                    // Catapult
                    "RPC_Shoot".GetStableHashCode(),
                    "RPC_OnLegUse".GetStableHashCode(),
                    "RPC_SetLoadedVisual".GetStableHashCode(),
                };
            }

            if (!nviewTargetZDOID.IsNone()) return true;
            return _rangeLimitedMethodHashes.Contains(methodHash);
        }

        private static bool IsNearby(Vector3 a, Vector3 b)
        {
            if (Character.InInterior(a) != Character.InInterior(b)) return false;

            int radius = ZoneSystem.instance.m_activeArea + ZoneSystem.instance.m_activeDistantArea; // 3+3
            float zoneSize = ZoneSystem.instance.m_zoneSize; // 64м
            float halfZone = zoneSize / 2f;
            float threshold = (radius + 1) * zoneSize + halfZone;

            if (Mathf.Abs(a.x - b.x) <= threshold) return Mathf.Abs(a.z - b.z) <= threshold;

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.InvokeRoutedRPC), new Type[] { typeof(long), typeof(ZDOID), typeof(string), typeof(object[]) })]
        private static void ZRoutedRpc_InvokeRoutedRpc(ZRoutedRpc __instance, ZDOID targetZDO, ref long targetPeerID, string methodName)
        {
            if (!IsFeatureEnabled) return;

            if (targetPeerID != ZRoutedRpc.Everybody) return;

            if (!Player.m_localPlayer) return;

            if (!IsRangeLimitedRpc(methodName.GetStableHashCode(), targetZDO)) return;

            Vector3 srcPos;
            if (!targetZDO.IsNone())
            {
                var zdo = ZDOMan.instance?.GetZDO(targetZDO);
                srcPos = zdo?.GetPosition() ?? Player.m_localPlayer.transform.position;
            }
            else
            {
                srcPos = Player.m_localPlayer.transform.position;
            }

            bool hasNearbyPlayers = false;
            var allPlayers = Player.GetAllPlayers();
            
            foreach (var player in allPlayers)
            {
                if (player == Player.m_localPlayer) continue;
                    
                if (IsNearby(srcPos, player.transform.position))
                {
                    hasNearbyPlayers = true;
                    break;
                }
            }

            if (!hasNearbyPlayers)
            {
                if (IsDebugMode) Helper.LogVerbose($"[RPCFilter] Client: No nearby players, sending {methodName} only to self");
                targetPeerID = __instance.m_id;
            }
            else if (IsDebugMode)
            {
                Helper.LogVerbose($"[RPCFilter] Client: Nearby players found, broadcasting {methodName}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.RouteRPC))]
        private static void ZRoutedRpc_RouteRPC(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData rpcData, ref bool __runOriginal)
        {
            if (!IsFeatureEnabled) return;
            if (!__instance.m_server) return;
            if (rpcData.m_targetPeerID != ZRoutedRpc.Everybody) return;
            if (!IsRangeLimitedRpc(rpcData)) return;

            Vector3? srcPos = null;
            
            if (!rpcData.m_targetZDO.IsNone())
            {
                var zdo = ZDOMan.instance?.GetZDO(rpcData.m_targetZDO);
                if (zdo != null) srcPos = zdo.GetPosition();
            }

            if (!srcPos.HasValue)
            {
                var sender = __instance.GetPeer(rpcData.m_senderPeerID);
                if (sender != null) srcPos = sender.m_refPos;
            }

            if (!srcPos.HasValue)
            {
                if (IsDebugMode) Helper.LogVerbose($"[RPCFilter] Server: No source position for RPC {rpcData.m_methodHash}, skipping filter");
                return;
            }

            __runOriginal = false;

            ZPackage zPackage = new ZPackage();
            rpcData.Serialize(zPackage);

            int sentCount = 0;
            foreach (var peer in __instance.m_peers)
            {
                if (peer == null) continue;
                if (rpcData.m_senderPeerID == peer.m_uid) continue;
                if (!peer.IsReady()) continue;

                if (IsNearby(srcPos.Value, peer.m_refPos))
                {
                    peer.m_rpc.Invoke("RoutedRPC", zPackage);
                    sentCount++;
                }
            }

            if (IsDebugMode && sentCount < __instance.m_peers.Count - 1)
            {
                Helper.LogVerbose($"[RPCFilter] Server: {rpcData.m_methodHash} sent to {sentCount}/{__instance.m_peers.Count} peers");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.RouteRPC))]
        private static void ZRoutedRpc_RouteRPC_DEBUG(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData rpcData)
        {
            if (!IsDebugMode) return;
            if (!__instance.m_server) return;
            if (!IsRangeLimitedRpc(rpcData)) return;

            Helper.LogVerbose($"[RPCFilter] SERVER ROUTE: ID: {__instance.m_id} MsgID: {rpcData.m_msgID} Peers: {__instance.m_peers.Count}");
            Helper.LogVerbose("[RPCFilter] NAME\tID\t\tSendByID\tIsReady");
            
            foreach (var peer in __instance.m_peers)
            {
                Helper.LogVerbose($"[RPCFilter] {peer.m_playerName}\t{peer.m_uid}\t{peer.m_uid == rpcData.m_senderPeerID}\t\t{peer.IsReady()}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.HandleRoutedRPC))]
        private static void ZRoutedRpc_HandleRoutedRPC_DEBUG(
            ZRoutedRpc __instance,
            ZRoutedRpc.RoutedRPCData data)
        {
            if (!IsDebugMode) return;
            if (__instance.m_server) return;
            if (!IsRangeLimitedRpc(data)) return;

            Helper.LogVerbose($"[RPCFilter] CLIENT HANDLING RPC: " + $"localSession={ZDOMan.GetSessionID()}, " +
                $"msgID={data.m_msgID}, " + $"sender={data.m_senderPeerID}, " +
                $"targetPeer={data.m_targetPeerID}, " + $"method={data.m_methodHash}"); }
    }
}
using Object = UnityEngine.Object;

namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ServerReliabilityPatches
    {
        private static readonly List<ZDO> _staleSceneKeys = new List<ZDO>();
        
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.RemoveObjects))]
        [HarmonyPrefix]
        private static void RemoveObjects_CleanupStaleInstances(ZNetScene __instance)
        {
            if (!Helper.IsServer()) return;
            
            try
            {
                var instancesField = AccessTools.Field(typeof(ZNetScene), "m_instances");
                if (instancesField == null) return;
                
                var instances = instancesField.GetValue(__instance) as Dictionary<ZDO, ZNetView>;
                if (instances == null || instances.Count == 0) return;
                
                _staleSceneKeys.Clear();
                foreach (var kvp in instances)
                {
                    ZDO key = kvp.Key;
                    ZNetView view = kvp.Value;
                    
                    bool isStale = false;
                    if (key == null || !key.IsValid()) isStale = true;
                    else if (!view) isStale = true;
                    else
                    {
                        try
                        {
                            ZDO viewZdo = view.GetZDO();
                            if (viewZdo == null || viewZdo != key) isStale = true;
                        }
                        catch { isStale = true; }
                    }
                    
                    if (isStale) _staleSceneKeys.Add(key);
                }
                
                int removed = 0;
                foreach (ZDO staleKey in _staleSceneKeys)
                {
                    if (instances.TryGetValue(staleKey, out ZNetView staleView))
                    {
                        instances.Remove(staleKey);
                        removed++;
                        
                        if (staleView)
                        {
                            try
                            {
                                if (staleView.GetZDO() == null) Object.Destroy(staleView.gameObject);
                            }
                            catch { /* ignore destroy errors */ }
                        }
                    }
                }
                
                if (removed > 0 && VBNetTweaks.c_DebugEnabled.Value) Helper.LogDebug($"[Reliability] Cleaned {removed} stale scene instances before RemoveObjects");
                    
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"[Reliability] Stale cleanup error: {ex.Message}");
            }
            finally
            {
                _staleSceneKeys.Clear();
            }
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.CreateSyncList))]
        [HarmonyPostfix]
        private static void CreateSyncList_Dedupe(ZDOMan.ZDOPeer peer, List<ZDO> toSync)
        {
            if (!Helper.IsServer() || toSync == null || toSync.Count <= 1) return;
            
            try
            {
                var seen = new HashSet<ZDOID>();
                int writeIdx = 0;
                
                for (int i = 0; i < toSync.Count; i++)
                {
                    ZDO zdo = toSync[i];
                    if (zdo != null && zdo.IsValid() && !zdo.m_uid.IsNone() && seen.Add(zdo.m_uid)) toSync[writeIdx++] = zdo;
                }
                
                int removed = toSync.Count - writeIdx;
                if (removed > 0)
                {
                    toSync.RemoveRange(writeIdx, removed);
                    if (VBNetTweaks.c_VerboseLogging.Value) Helper.LogVerbose($"[Reliability] Deduped {removed} ZDOs from sync list for peer {peer?.m_peer?.m_uid ?? 0}");
                }
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"[Reliability] Sync dedupe error: {ex.Message}");
            }
        }

        private static MethodInfo _wearNTearRemoveMethod;
        private static FieldInfo _routedRpcTargetPeerIdField;
        private static FieldInfo _routedRpcSenderPeerIdField;
        private static FieldInfo _routedRpcTargetZdoField;
        private static FieldInfo _routedRpcMethodHashField;
        private static FieldInfo _routedRpcParametersField;
        private static bool _rpcReflectionInitialized;
        private const float MaxRemoveRepairDistance = 8f;

        private static void EnsureRpcReflection()
        {
            if (_rpcReflectionInitialized) return;
            _rpcReflectionInitialized = true;
            
            try
            {
                var routeRpcMethod = AccessTools.Method(typeof(ZRoutedRpc), "RouteRPC");
                Type rpcDataType = null;
                if (routeRpcMethod != null)
                {
                    var parameters = routeRpcMethod.GetParameters();
                    if (parameters.Length == 1) rpcDataType = parameters[0].ParameterType;
                }
                
                if (rpcDataType != null)
                {
                    _routedRpcTargetPeerIdField = AccessTools.Field(rpcDataType, "m_targetPeerID");
                    _routedRpcSenderPeerIdField = AccessTools.Field(rpcDataType, "m_senderPeerID");
                    _routedRpcTargetZdoField = AccessTools.Field(rpcDataType, "m_targetZDO");
                    _routedRpcMethodHashField = AccessTools.Field(rpcDataType, "m_methodHash");
                    _routedRpcParametersField = AccessTools.Field(rpcDataType, "m_parameters");
                }
                
                _wearNTearRemoveMethod = AccessTools.Method(typeof(WearNTear), "RPC_Remove");
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"[Reliability] RPC reflection init failed: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.HandleRoutedRPC))]
        [HarmonyPrefix]
        private static bool HandleRoutedRPC_StaleOwnershipRepair(object __0)
        {
            if (!Helper.IsServer()) return true;
            
            EnsureRpcReflection();
            if (_routedRpcMethodHashField == null || _wearNTearRemoveMethod == null) return true;
            
            try
            {
                int methodHash = (int)_routedRpcMethodHashField.GetValue(__0);
                if (methodHash != "RPC_Remove".GetStableHashCode()) return true;
                
                ZDOID targetZdoId = (ZDOID)_routedRpcTargetZdoField.GetValue(__0);
                if (targetZdoId.IsNone()) return true;
                
                long senderPeerId = (long)_routedRpcSenderPeerIdField.GetValue(__0);
                long serverUid = ZDOMan.GetSessionID();
                
                ZDO zdo = ZDOMan.instance.GetZDO(targetZdoId);
                if (zdo == null || !zdo.IsValid()) return true;
                
                long currentOwner = zdo.GetOwner();
                if (currentOwner == serverUid || currentOwner == senderPeerId) return true;
                
                ZNetPeer senderPeer = ZNet.instance.GetPeer(senderPeerId);
                if (senderPeer == null || !senderPeer.IsReady()) return true;
                
                float distSqr = (senderPeer.GetRefPos() - zdo.GetPosition()).sqrMagnitude;
                if (distSqr > MaxRemoveRepairDistance * MaxRemoveRepairDistance) return true;
                
                if (Location.IsInsideNoBuildLocation(zdo.GetPosition())) return true;
                
                ZNetView zNetView = ZNetScene.instance.FindInstance(zdo);
                if (!zNetView) return true;
                
                WearNTear wearNTear = zNetView.GetComponent<WearNTear>();
                if (!wearNTear) return true;
                
                Piece piece = zNetView.GetComponent<Piece>();
                if (piece && !piece.CanBeRemoved()) return true;
                
                zdo.SetOwner(serverUid);
                
                try
                {
                    ZPackage pkg = _routedRpcParametersField.GetValue(__0) as ZPackage;
                    if (pkg != null)
                    {
                        int savedPos = pkg.GetPos();
                        pkg.SetPos(0);
                        bool blockDrop = pkg.ReadBool();
                        pkg.SetPos(savedPos);
                        
                        _wearNTearRemoveMethod.Invoke(wearNTear, new object[] { senderPeerId, blockDrop });
                    }
                }
                catch (Exception ex)
                {
                    Helper.LogDebug($"[Reliability] Stale remove repair execution failed: {ex.Message}");
                    if (zdo.IsValid()) zdo.SetOwner(currentOwner);
                    return true;
                }
                
                if (zdo.IsValid())
                {
                    zdo.SetOwner(currentOwner);
                    return true;
                }
                
                _routedRpcTargetPeerIdField.SetValue(__0, serverUid);
                
                if (VBNetTweaks.c_VerboseLogging.Value) Helper.LogVerbose($"[Reliability] Repaired stale ownership for ZDO {targetZdoId} remove RPC from peer {senderPeerId}");
                
                return false;
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"[Reliability] Stale ownership repair error: {ex.Message}");
                return true;
            }
        }

        [HarmonyPatch(typeof(AudioMan), nameof(AudioMan.Update))]
        [HarmonyPrefix]
        private static bool AudioMan_Update_HeadlessGuard()
        {
            if (!Helper.IsServer()) return true;
            return !Application.isBatchMode && !ZNet.instance.IsDedicated();
        }

        [HarmonyPatch(typeof(ShieldDomeImageEffect), nameof(ShieldDomeImageEffect.Awake))]
        [HarmonyPrefix]
        private static bool ShieldDome_Awake_HeadlessGuard(ShieldDomeImageEffect __instance)
        {
            if (!Helper.IsServer()) return true;
            if (Application.isBatchMode || ZNet.instance.IsDedicated())
            {
                __instance.enabled = false;
                return false;
            }
            return true;
        }
    }
}
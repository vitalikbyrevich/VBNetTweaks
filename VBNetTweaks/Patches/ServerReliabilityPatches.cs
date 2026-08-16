using Object = UnityEngine.Object;

namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ServerReliabilityPatches
    {
        private static readonly List<ZDO> _staleSceneKeys = new List<ZDO>();
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> _instancesRef = AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");
        private static float _nextCleanupTime = 0f;
        private static readonly HashSet<ZDOID> _dedupSeen = new HashSet<ZDOID>();
        
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.RemoveObjects))]
        [HarmonyPrefix]
        private static void RemoveObjects_CleanupStaleInstances(ZNetScene __instance)
        {
            if (!Helper.IsServer()) return;
            if (Time.time < _nextCleanupTime) return;
            _nextCleanupTime = Time.time + 10f;
            
            try
            {
                var instances = _instancesRef(__instance);
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
                _dedupSeen.Clear(); // Очищаем без аллокации
                int writeIdx = 0;
        
                for (int i = 0; i < toSync.Count; i++)
                {
                    ZDO zdo = toSync[i];
                    if (zdo != null && zdo.IsValid() && !zdo.m_uid.IsNone() && _dedupSeen.Add(zdo.m_uid)) toSync[writeIdx++] = zdo;
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
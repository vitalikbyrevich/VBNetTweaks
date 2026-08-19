using Object = UnityEngine.Object;

namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ServerReliabilityPatches
    {
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> _instancesRef = AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");
        private static float _nextCleanupTime;
        private static readonly HashSet<ZDOID> _dedupSeen = new HashSet<ZDOID>();
        
        private static readonly List<KeyValuePair<ZDO, ZNetView>> _sceneKeep = new List<KeyValuePair<ZDO, ZNetView>>();
        private static readonly List<ZNetView> _sceneOrphans = new List<ZNetView>();

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

                _sceneKeep.Clear();
                _sceneOrphans.Clear();

                foreach (var kvp in instances)
                {
                    ZDO key = kvp.Key;
                    ZNetView view = kvp.Value;

                    if (!key.IsValid())
                    {
                        if (view) _sceneOrphans.Add(view);
                        continue;
                    }
                    if (!view) continue;

                    _sceneKeep.Add(kvp);
                }

                int dropped = instances.Count - _sceneKeep.Count;
                if (dropped <= 0) return;

                instances.Clear();
                foreach (var kvp in _sceneKeep) instances[kvp.Key] = kvp.Value;

                for (int i = 0; i < _sceneOrphans.Count; i++)
                    if (_sceneOrphans[i]) Object.Destroy(_sceneOrphans[i].gameObject);

                Helper.LogDebug($"[Reliability] Scene instances rebuilt: dropped {dropped}, destroyed {_sceneOrphans.Count} orphans");
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"[Reliability] Stale cleanup error: {ex.Message}");
            }
            finally
            {
                _sceneKeep.Clear();
                _sceneOrphans.Clear();
            }
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.CreateSyncList))]
        [HarmonyPostfix]
        private static void CreateSyncList_Dedupe(ZDOMan.ZDOPeer peer, List<ZDO> toSync)
        {
            if (!Helper.IsServer() || toSync == null || toSync.Count <= 1) return;
    
            try
            {
                _dedupSeen.Clear();
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
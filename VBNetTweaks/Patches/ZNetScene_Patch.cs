namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZNetScene_Patch
    {
        private static float _teleportBoostEnd = 0f;
        public static void TriggerTeleportWindow() => _teleportBoostEnd = Time.time + 5f;
        
        private static readonly List<ZDO> _zdosToRemove = new List<ZDO>();
        private static byte _currentMark = 0;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.RemoveObjects))]
        private static bool RemoveObjectsPrefix(ZNetScene __instance, List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects)
        {
            if (!VBNetTweaks.c_ModEnabled.Value) return true;
            if (!__instance || __instance.m_instances == null) return true;

            _currentMark++;
            byte mark = _currentMark;

            for (int i = 0; i < currentNearObjects.Count; i++)
            {
                if (currentNearObjects[i] != null) currentNearObjects[i].TempRemoveEarmark = mark;
            }

            for (int i = 0; i < currentDistantObjects.Count; i++)
            {
                if (currentDistantObjects[i] != null) currentDistantObjects[i].TempRemoveEarmark = mark;
            }

            var instances = __instance.m_instances;
            var tempRemoved = __instance.m_tempRemoved;
            tempRemoved.Clear();
            _zdosToRemove.Clear();

            foreach (var pair in instances)
            {
                ZDO zdo = pair.Key;
                ZNetView view = pair.Value;

                if (zdo == null || !view)
                {
                    if (view) tempRemoved.Add(view);
                    if (zdo != null) _zdosToRemove.Add(zdo);
                    continue;
                }

                if (zdo.TempRemoveEarmark != mark)
                {
                    tempRemoved.Add(view);
                    _zdosToRemove.Add(zdo);
                }
            }

            ZDOMan zdoManager = ZDOMan.s_instance;
            for (int i = 0; i < tempRemoved.Count; i++)
            {
                ZNetView view = tempRemoved[i];
                if (!view) continue;

                if (view.m_zdo != null)
                {
                    view.m_zdo.Created = false;
                    view.m_zdo = null;
                }
                UnityEngine.Object.Destroy(view.gameObject);
            }

            for (int i = 0; i < _zdosToRemove.Count; i++)
            {
                ZDO zdo = _zdosToRemove[i];
                if (zdo == null) continue;

                if (!zdo.Persistent && zdo.Owner) zdoManager.m_destroySendList.Add(zdo.m_uid);
                instances.Remove(zdo);
            }

            tempRemoved.Clear();
            _zdosToRemove.Clear();

            return false;
        }
        
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.InLoadingScreen))]
        [HarmonyPrefix]
        public static bool InLoadingScreen_Extend(ref bool __result)
        {
            if (!VBNetTweaks.c_ModEnabled.Value) return true;
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
            if (!VBNetTweaks.c_ModEnabled.Value) return;
            if (Player.m_localPlayer?.IsTeleporting() == true) TriggerTeleportWindow();
        }
    }
}
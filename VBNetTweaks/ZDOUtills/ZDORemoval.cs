namespace VBNetTweaks.ZDOUtills
{
    public static class ZDORemoval
    {
        public static void OptimizedRemoveObjects(ZNetScene scene, List<ZDO> near, List<ZDO> distant)
        {
            if (scene == null) return;
            
            // Mark objects that should stay
            byte earmark = (byte)(Time.frameCount & 0xFF);
            
            if (near != null)
            {
                foreach (ZDO zdo in near)
                {
                    if (zdo != null) zdo.TempRemoveEarmark = earmark;
                }
            }
            
            if (distant != null)
            {
                foreach (ZDO zdo in distant)
                {
                    if (zdo != null) zdo.TempRemoveEarmark = earmark;
                }
            }
            
            // Find objects to remove
            var instances = scene.m_instances;
            var tempRemoved = scene.m_tempRemoved;
            tempRemoved.Clear();
            
            // Collect keys to avoid modification during iteration
            var toRemove = new List<ZNetView>();
            
            foreach (var kvp in instances)
            {
                ZDO zdo = kvp.Key;
                ZNetView view = kvp.Value;
                
                if (zdo != null && view != null && zdo.TempRemoveEarmark != earmark)
                {
                    toRemove.Add(view);
                }
            }
            
            // Perform removal
            foreach (ZNetView view in toRemove)
            {
                if (view != null)
                {
                    ZDO zdo = view.m_zdo;
                    if (zdo != null)
                    {
                        zdo.Created = false;
                        view.m_zdo = null;
                    }
                    UnityEngine.Object.Destroy(view.gameObject);
                    instances.Remove(zdo);
                }
            }
        }
    }
}
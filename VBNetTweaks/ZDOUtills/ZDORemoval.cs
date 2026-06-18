namespace VBNetTweaks.ZDOUtills;

public static class ZDORemoval
{
    public static void OptimizedRemoveObjects(ZNetScene scene, List<ZDO> near, List<ZDO> distant)
    {
        if (!scene || scene.m_instances == null) return;
        
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
        
        var instances = scene.m_instances;
        var tempRemoved = scene.m_tempRemoved;
        if (tempRemoved == null) return;
        
        tempRemoved.Clear();
        
        var toRemove = new List<ZNetView>();
        
        foreach (var kvp in instances)
        {
            ZDO zdo = kvp.Key;
            ZNetView view = kvp.Value;
            
            if (zdo != null && view && zdo.TempRemoveEarmark != earmark) toRemove.Add(view);
        }
        
        foreach (ZNetView view in toRemove)
        {
            if (!view) continue;
            
            ZDO zdo = view.m_zdo;
            if (zdo == null) continue;
            
            view.ResetZDO();
            
            ZDO zdoForRemoval = zdo;
            
            UnityEngine.Object.Destroy(view.gameObject);
            instances.Remove(zdoForRemoval);
        }
    }
}
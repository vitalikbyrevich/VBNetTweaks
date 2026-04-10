namespace VBNetTweaks.ZDOUtills;

public class ZDORemoval
{
    public static void OptimizedRemoveObjects(ZNetScene scene, List<ZDO> near, List<ZDO> distant)
    {
        byte mark = (byte)(Time.frameCount & 255);

        foreach (var z in near)
            if (z != null) z.TempRemoveEarmark = mark;
        foreach (var z in distant)
            if (z != null) z.TempRemoveEarmark = mark;

        var instances = scene.m_instances;
        var tempRemoved = scene.m_tempRemoved;

        tempRemoved.Clear();

        var keys = new List<ZDO>(instances.Keys);

        foreach (var zdo in keys)
        {
            if (zdo == null || !instances.TryGetValue(zdo, out var view) || view == null)
            {
                instances.Remove(zdo);
                continue;
            }

            if (zdo.TempRemoveEarmark != mark) tempRemoved.Add(view);
        }

        foreach (var view in tempRemoved)
        {
            if (!view) continue;

            var zdo = view.m_zdo;
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
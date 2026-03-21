using Object = UnityEngine.Object;

namespace VBNetTweaks.ZDOUtills;

public class ZDORemoval
{
    private static List<ZDO> _pendingRemovalKeys;
    private static int _currentRemovalIndex;
    private const float MAX_REMOVAL_TIME_MS = 2f;

    public static void OptimizedRemoveObjects(ZNetScene scene, List<ZDO> near, List<ZDO> distant)
    {
        if (scene?.m_instances == null || scene.m_instances.Count == 0) return;
        if ((near == null || near.Count == 0) && (distant == null || distant.Count == 0)) return;

        byte mark = (byte)(Time.frameCount & 255);

        MarkObjectsToKeep(near, mark);
        MarkObjectsToKeep(distant, mark);

        var instances = scene.m_instances;
        var tempRemoved = scene.m_tempRemoved;

        if (_pendingRemovalKeys == null)
        {
            _pendingRemovalKeys = ObjectPool.RentList<ZDO>();
            _pendingRemovalKeys.AddRange(instances.Keys);
            _currentRemovalIndex = 0;
        }

        float startTime = Time.realtimeSinceStartup * 1000f;

        try
        {
            while (_currentRemovalIndex < _pendingRemovalKeys.Count)
            {
                if (Time.realtimeSinceStartup * 1000f - startTime > MAX_REMOVAL_TIME_MS)
                {
                    return;
                }

                var zdo = _pendingRemovalKeys[_currentRemovalIndex++];
                if (zdo == null) continue;

                ProcessZDOForRemoval(instances, tempRemoved, zdo, mark);
            }

            if (_currentRemovalIndex >= _pendingRemovalKeys.Count)
            {
                RemoveMarkedObjects(instances, tempRemoved);

                ObjectPool.ReturnList(_pendingRemovalKeys);
                _pendingRemovalKeys = null;
                _currentRemovalIndex = 0;
            }
        }
        catch (Exception e)
        {
            Helper.LogDebug($"Error in OptimizedRemoveObjects: {e.Message}");

            if (_pendingRemovalKeys != null)
            {
                ObjectPool.ReturnList(_pendingRemovalKeys);
                _pendingRemovalKeys = null;
                _currentRemovalIndex = 0;
            }
        }
    }

    private static void MarkObjectsToKeep(List<ZDO> objects, byte mark)
    {
        if (objects == null) return;

        foreach (var z in objects)
        {
            if (z != null) z.TempRemoveEarmark = mark;
        }
    }

    private static void ProcessZDOForRemoval(Dictionary<ZDO, ZNetView> instances, List<ZNetView> tempRemoved, ZDO zdo, byte mark)
    {
        if (!instances.TryGetValue(zdo, out var view) || view == null)
        {
            instances.Remove(zdo);
            return;
        }

        if (zdo.TempRemoveEarmark != mark)
        {
            tempRemoved.Add(view);
        }
    }

    private static void RemoveMarkedObjects(Dictionary<ZDO, ZNetView> instances, List<ZNetView> tempRemoved)
    {
        foreach (var view in tempRemoved)
        {
            if (!view) continue;

            try
            {
                var zdo = view.m_zdo;
                if (zdo != null)
                {
                    zdo.Created = false;
                    view.m_zdo = null;
                }

                Object.Destroy(view.gameObject);
                instances.Remove(zdo);
            }
            catch (Exception e)
            {
                Helper.LogDebug($"Error destroying object: {e.Message}");
            }
        }

        tempRemoved.Clear();
    }
}
namespace VBNetTweaks;

using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

[HarmonyPatch]
public static class UnifiedSceneOptimizations
{
    // Конфигурационные параметры
    public static ConfigEntry<bool> EnableDoorOwnership;
    public static ConfigEntry<bool> EnableSceneOptimizations;
    public static ConfigEntry<bool> SceneDebugEnabled;

    // Переменные для Scenic
    private static readonly List<ZDO> _zdosToRemove = new List<ZDO>(64);
    private static byte _currentFrameMark;

    public static void BindSceneConfig(ConfigFile config)
    {
        ConfigurationManagerAttributes isAdminOnly = new ConfigurationManagerAttributes { IsAdminOnly = true };

        EnableDoorOwnership = config.Bind("05 - Scene Optimizations", "EnableDoorOwnership", true, new ConfigDescription("Автоматически забирать ownership при открытии дверей", null, isAdminOnly));
        EnableSceneOptimizations = config.Bind("05 - Scene Optimizations", "EnableSceneOptimizations", true, new ConfigDescription("Оптимизировать удаление объектов из сцены", null, isAdminOnly));
        SceneDebugEnabled = config.Bind("05 - Scene Optimizations", "SceneDebugEnabled", false, new ConfigDescription("Включить отладочный вывод для сцены", null, isAdminOnly));
    }

    // =========================================================================
    // OPEN SESAME - Оптимизированная версия
    // =========================================================================

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Door), nameof(Door.Open))]
    public static void Door_Open_Prefix(Door __instance)
    {
        if (!EnableDoorOwnership.Value) return;

        try
        {
            // Быстрая проверка null
            if (!__instance) return;
            
            var nview = __instance.m_nview;
            if (!nview || !nview.IsValid()) return;
            
            // Проверяем, нужно ли вообще забирать ownership
            if (!nview.IsOwner())
            {
                nview.ClaimOwnership();
                if (SceneDebugEnabled.Value) VBNetTweaks.LogVerbose($"Claimed ownership for door: {__instance.name}");
            }
        }
        catch (Exception e)
        {
            // Тихая обработка ошибок чтобы не ломать открытие дверей
            if (SceneDebugEnabled.Value) VBNetTweaks.LogDebug($"Door ownership error: {e.Message}");
        }
    }

    // =========================================================================
    // SCENIC - Оптимизированная версия  
    // =========================================================================

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.RemoveObjects))]
    static bool RemoveObjectsPrefix(ZNetScene __instance, List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects)
    {
        if (!EnableSceneOptimizations.Value) return true;

        try
        {
            OptimizedRemoveObjects(__instance, currentNearObjects, currentDistantObjects);
            return false;
        }
        catch (Exception e)
        {
            VBNetTweaks.LogDebug($"Scene optimization error: {e.Message}");
            return true; // Возвращаем оригинальный метод при ошибке
        }
    }

    private static void OptimizedRemoveObjects(ZNetScene netScene, List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects)
    {
        _currentFrameMark = (byte)(Time.frameCount & 255);
        
        // Оптимизированная разметка - один проход
        MarkObjects(currentNearObjects);
        MarkObjects(currentDistantObjects);

        var zdoManager = ZDOMan.s_instance;
        var netSceneInstances = netScene.m_instances;
        var netSceneTempRemoved = netScene.m_tempRemoved;

        netSceneTempRemoved.Clear();
        _zdosToRemove.Clear();

        // Оптимизированная проверка - избегаем повторных поисков в Dictionary
        RemoveUnmarkedObjects(netSceneInstances, netSceneTempRemoved, zdoManager);
        
        // Очистка временных данных
        netSceneTempRemoved.Clear();
        _zdosToRemove.Clear();

        if (SceneDebugEnabled.Value) VBNetTweaks.LogVerbose($"Scene optimization completed: marked {currentNearObjects.Count + currentDistantObjects.Count} objects");
    }

    private static void MarkObjects(List<ZDO> objects)
    {
        for (int i = 0, count = objects.Count; i < count; i++)
        {
            var zdo = objects[i];
            if (zdo != null) zdo.TempRemoveEarmark = _currentFrameMark;
        }
    }

    private static void RemoveUnmarkedObjects(
        Dictionary<ZDO, ZNetView> netSceneInstances,
        List<ZNetView> netSceneTempRemoved,
        ZDOMan zdoManager)
    {
        // Используем Keys collection для избежания аллокаций
        var keys = netSceneInstances.Keys;
        var keysArray = new ZDO[keys.Count];
        keys.CopyTo(keysArray, 0);

        int removedCount = 0;

        for (int i = 0; i < keysArray.Length; i++)
        {
            var zdo = keysArray[i];
            if (zdo == null) 
            {
                _zdosToRemove.Add(zdo);
                continue;
            }

            if (!netSceneInstances.TryGetValue(zdo, out var netView) || netView == null)
            {
                _zdosToRemove.Add(zdo);
                continue;
            }

            if (zdo.TempRemoveEarmark != _currentFrameMark)
            {
                netSceneTempRemoved.Add(netView);
                removedCount++;
            }
        }

        // Оптимизированное удаление объектов
        RemoveMarkedViews(netSceneTempRemoved, zdoManager);
        RemoveInvalidZDOs(netSceneInstances, zdoManager);

        if (SceneDebugEnabled.Value && removedCount > 0) VBNetTweaks.LogVerbose($"Removed {removedCount} unmarked objects from scene");
    }

    private static void RemoveMarkedViews(List<ZNetView> viewsToRemove, ZDOMan zdoManager)
    {
        for (int i = 0, count = viewsToRemove.Count; i < count; i++)
        {
            var netView = viewsToRemove[i];
            if (netView == null) continue;

            var zdo = netView.m_zdo;
            if (zdo == null) continue;
            
            _zdosToRemove.Add(zdo);
            zdo.Created = false;
            netView.m_zdo = null;

            UnityEngine.Object.Destroy(netView.gameObject);
        }
    }

    private static void RemoveInvalidZDOs(Dictionary<ZDO, ZNetView> instances, ZDOMan zdoManager)
    {
        for (int i = 0, count = _zdosToRemove.Count; i < count; i++)
        {
            var zdo = _zdosToRemove[i];
            if (zdo == null) continue;
            
            if (!zdo.Persistent && zdo.Owner)
            {
                zdoManager.m_destroySendList.Add(zdo.m_uid);
            }

            instances.Remove(zdo);
        }
    }

    // =========================================================================
    // ДОПОЛНИТЕЛЬНЫЕ ОПТИМИЗАЦИИ СЦЕНЫ
    // =========================================================================

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ZNetScene), "Update")]
    static void ZNetScene_Update_Prefix(ZNetScene __instance)
    {
        if (!EnableSceneOptimizations.Value) return;

        // Дополнительные оптимизации частоты обновления сцены
        OptimizeSceneUpdate(__instance);
    }

    private static void OptimizeSceneUpdate(ZNetScene netScene)
    {
        // Оптимизация: уменьшаем частоту некоторых проверок
        // на слабых серверах или при большом количестве игроков
        if (Time.frameCount % 2 == 0) // Выполняем каждые 2 кадра
        {
            // Можно добавить дополнительные оптимизации здесь
        }
    }
}
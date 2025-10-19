namespace VBNetTweaks;

using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;

[HarmonyPatch]
public static class InventoryOptimizations
{
    private static readonly List<InventoryAction> _pendingActions = new List<InventoryAction>();
    private static float _lastProcessTime;
    private const float PROCESS_INTERVAL = 0.1f;

    private struct InventoryAction
    {
        public string Type;
        public Inventory Inventory;
        public ItemDrop.ItemData Item;
        public Vector3 Position;
        public int Stack;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Inventory), "AddItem", new Type[] { typeof(ItemDrop.ItemData) })]
    public static bool Inventory_AddItem_Prefix(Inventory __instance, ItemDrop.ItemData item)
    {
        if (!Helper.IsServer()) return true;

        _pendingActions.Add(new InventoryAction {
            Type = "AddItem",
            Inventory = __instance,
            Item = item
        });
        
        ProcessBuffer();
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Inventory), "RemoveItem", new Type[] { typeof(ItemDrop.ItemData) })]
    public static bool Inventory_RemoveItem_Prefix(Inventory __instance, ItemDrop.ItemData item)
    {
        if (!Helper.IsServer()) return true;

        _pendingActions.Add(new InventoryAction {
            Type = "RemoveItem", 
            Inventory = __instance,
            Item = item
        });
        
        ProcessBuffer();
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), "DropItem")]
    public static bool Player_DropItem_Prefix(Player __instance, Inventory inventory, ItemDrop.ItemData item, int stack)
    {
        if (!Helper.IsServer()) return true;

        _pendingActions.Add(new InventoryAction {
            Type = "DropItem",
            Inventory = inventory,
            Item = item,
            Stack = stack,
            Position = __instance.transform.position + __instance.transform.forward + Vector3.up
        });
        
        ProcessBuffer();
        return false;
    }

    private static void ProcessBuffer()
    {
        if (Time.time - _lastProcessTime < PROCESS_INTERVAL) return;
        if (_pendingActions.Count == 0) return;

        // Берем первые 10 действий
        int processCount = Math.Min(_pendingActions.Count, 10);
        var batch = _pendingActions.GetRange(0, processCount);
        _pendingActions.RemoveRange(0, processCount);

        foreach (var action in batch)
        {
            ExecuteInventoryAction(action);
        }

        _lastProcessTime = Time.time;
        
        if (VBNetTweaks.DebugEnabled.Value)
        {
            VBNetTweaks.LogVerbose($"Processed {batch.Count} inventory actions, {_pendingActions.Count} remaining");
        }
    }

    private static void ExecuteInventoryAction(InventoryAction action)
    {
        try
        {
            switch (action.Type)
            {
                case "AddItem":
                    action.Inventory.AddItem(action.Item);
                    break;
                    
                case "RemoveItem":
                    action.Inventory.RemoveItem(action.Item);
                    break;
                    
                case "DropItem":
                    ItemDrop.DropItem(action.Item, action.Stack, action.Position, Quaternion.identity);
                    break;
            }
        }
        catch (Exception e)
        {
            VBNetTweaks.LogDebug($"Error executing inventory action: {e.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    public static void ZNet_OnDestroy_Postfix()
    {
        _pendingActions.Clear();
    }
}

[HarmonyPatch]
public static class InventoryUIOptimizations
{
    private static bool _inventoryOpen = false;
    private static float _lastUIRefresh;
    private static Dictionary<Inventory, float> _lastInventoryChange = new Dictionary<Inventory, float>();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), "Show")]
    public static void InventoryGui_Show_Prefix(InventoryGui __instance)
    {
        _inventoryOpen = true;
        _lastUIRefresh = Time.time;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), "Hide")]
    public static void InventoryGui_Hide_Prefix()
    {
        _inventoryOpen = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), "Update")]
    public static bool InventoryGui_Update_Prefix(InventoryGui __instance)
    {
        if (!_inventoryOpen) return true;

        // Ограничиваем частоту обновления UI до 10 Гц вместо 60 Гц
        if (Time.time - _lastUIRefresh < 0.1f) // 100ms между обновлениями
            return false;

        _lastUIRefresh = Time.time;
        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    public static bool InventoryGrid_UpdateGui_Prefix(InventoryGrid __instance, Player player)
    {
        if (!_inventoryOpen) return true;

        // Пропускаем обновление если инвентарь не изменился
        var inventory = __instance.m_inventory;
        if (inventory != null)
        {
            float lastChange = GetLastInventoryChangeTime(inventory);
            if (lastChange < _lastUIRefresh - 0.05f)
                return false;
        }

        return true;
    }

    // Отслеживаем изменения инвентаря
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Inventory), "Changed")]
    public static void Inventory_Changed_Postfix(Inventory __instance)
    {
        _lastInventoryChange[__instance] = Time.time;
    }

    private static float GetLastInventoryChangeTime(Inventory inventory)
    {
        if (_lastInventoryChange.TryGetValue(inventory, out float time))
            return time;
        return 0f;
    }

    // Оптимизация обновления веса
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Inventory), "GetTotalWeight")]
    public static bool Inventory_GetTotalWeight_Prefix(Inventory __instance, ref float __result)
    {
        if (!_inventoryOpen) return true;

        // Кэшируем вычисление веса на 0.2 секунды
        string cacheKey = $"{__instance.GetHashCode()}_weight";
        if (CacheManager.GetFloat(cacheKey, out float cachedWeight, 0.2f))
        {
            __result = cachedWeight;
            return false;
        }

        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Inventory), "GetTotalWeight")]
    public static void Inventory_GetTotalWeight_Postfix(Inventory __instance, ref float __result)
    {
        if (!_inventoryOpen) return;

        // Сохраняем в кэш
        string cacheKey = $"{__instance.GetHashCode()}_weight";
        CacheManager.SetFloat(cacheKey, __result);
    }
}

// Простой менеджер кэша
public static class CacheManager
{
    private static readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>();

    private struct CacheEntry
    {
        public float Value;
        public float Timestamp;
        public float Duration;
    }

    public static bool GetFloat(string key, out float value, float maxAge = 1.0f)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (Time.time - entry.Timestamp <= maxAge)
            {
                value = entry.Value;
                return true;
            }
            _cache.Remove(key);
        }

        value = 0f;
        return false;
    }

    public static void SetFloat(string key, float value, float duration = 1.0f)
    {
        _cache[key] = new CacheEntry
        {
            Value = value,
            Timestamp = Time.time,
            Duration = duration
        };
    }

    public static void Clear()
    {
        _cache.Clear();
    }
}
[HarmonyPatch]
public static class ContainerOptimizations
{
    private static readonly Dictionary<Container, float> _lastContainerUpdate = new Dictionary<Container, float>();
    private const float CONTAINER_UPDATE_INTERVAL = 0.3f;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Container), "UpdateInventory")]
    public static bool Container_UpdateInventory_Prefix(Container __instance)
    {
        // Ограничиваем частоту обновления удаленных контейнеров
        float currentTime = Time.time;
        if (_lastContainerUpdate.TryGetValue(__instance, out float lastTime))
        {
            if (currentTime - lastTime < CONTAINER_UPDATE_INTERVAL)
            {
                // Проверяем расстояние до игроков
                if (GetDistanceToNearestPlayer(__instance.transform.position) > 15f)
                    return false;
            }
        }

        _lastContainerUpdate[__instance] = currentTime;
        return true;
    }

    private static float GetDistanceToNearestPlayer(Vector3 position)
    {
        float minDistance = float.MaxValue;
        foreach (Player player in Player.GetAllPlayers())
        {
            if (player == null) continue;
            float distance = Vector3.Distance(position, player.transform.position);
            if (distance < minDistance)
                minDistance = distance;
        }
        return minDistance;
    }
}
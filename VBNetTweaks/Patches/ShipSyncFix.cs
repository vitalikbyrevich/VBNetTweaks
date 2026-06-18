namespace VBNetTweaks.Patches;

using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

public static class ShipSyncFix
{
    private static readonly Dictionary<long, Vector3> _playerPosVelocities = new Dictionary<long, Vector3>();
    private static FieldInfo m_sendRudderTimeField = AccessTools.Field(typeof(Ship), "m_sendRudderTime");
    
    [HarmonyPatch(typeof(Ship), nameof(Ship.ApplyControlls))]
    [HarmonyPostfix]
    static void FasterRudder(Ship __instance, Vector3 dir)
    {
        if (!__instance.m_nview.IsOwner()) return;
        
        float sendRudderTime = (float)m_sendRudderTimeField.GetValue(__instance);
        if (Time.time - sendRudderTime > 0.05f)  // 20 Гц вместо 5
        {
            m_sendRudderTimeField.SetValue(__instance, Time.time);
            __instance.m_nview.InvokeRPC("Rudder", __instance.m_rudderValue);
        }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.Awake))]
    [HarmonyPostfix]
    public static void EnableShipInterpolation(Ship __instance)
    {
        if (__instance.m_body)
        {
            __instance.m_body.interpolation = RigidbodyInterpolation.Interpolate;
            __instance.m_body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }
    
    [HarmonyPatch(typeof(Player), nameof(Player.UpdateAttach))]
    [HarmonyPostfix]
    public static void SmoothAttachedPlayer(Player __instance)
    {
        if (!__instance.m_attached || !__instance.m_attachedToShip) return;
        if (!__instance.m_attachPoint) return;

        long playerId = __instance.GetPlayerID();
        Transform targetTransform = __instance.m_attachPoint;
        Transform playerTransform = __instance.transform;

        if (!_playerPosVelocities.ContainsKey(playerId))
        {
            _playerPosVelocities[playerId] = Vector3.zero;
            playerTransform.position = targetTransform.position;
            playerTransform.rotation = targetTransform.rotation;
            return;
        }

        Vector3 targetPos = targetTransform.position;
        Quaternion targetRot = targetTransform.rotation;

        float posSmoothTime = 0.08f; 
        
        Vector3 currentVel = _playerPosVelocities[playerId];
        
        playerTransform.position = Vector3.SmoothDamp(playerTransform.position, targetPos, ref currentVel, posSmoothTime);
        
        _playerPosVelocities[playerId] = currentVel;

        playerTransform.rotation = Quaternion.Slerp(playerTransform.rotation, targetRot, Time.deltaTime / 0.1f);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.AttachStop))]
    [HarmonyPrefix]
    public static void ClearCacheOnDetach(Player __instance) => RemoveFromCache(__instance.GetPlayerID());

    [HarmonyPatch(typeof(Player), nameof(Player.OnDestroy))]
    [HarmonyPrefix]
    public static void ClearCacheOnDestroy(Player __instance) => RemoveFromCache(__instance.GetPlayerID());

    private static void RemoveFromCache(long id)
    {
        if (_playerPosVelocities.ContainsKey(id)) _playerPosVelocities.Remove(id);
    }
}
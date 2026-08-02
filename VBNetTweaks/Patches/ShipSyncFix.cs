namespace VBNetTweaks.Patches;

using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

public static class ShipSyncFix
{
    private static readonly Dictionary<long, Vector3> _playerPosVelocities = new Dictionary<long, Vector3>();

    [HarmonyPatch(typeof(Ship), nameof(Ship.ApplyControlls))]
    [HarmonyPrefix]
    static bool ApplyControlls_Prefix(Ship __instance, Vector3 dir)
    {
        if (!VBNetTweaks.c_ModuleShipSync.Value) return true;

        bool forward = dir.z > 0.5;
        bool backward = dir.z < -0.5;

        if (forward && !__instance.m_forwardPressed) __instance.Forward();

        if (backward && !__instance.m_backwardPressed) __instance.Backward();

        __instance.m_forwardPressed = forward;
        __instance.m_backwardPressed = backward;

        float fixedDeltaTime = Time.fixedDeltaTime;

        float num = Mathf.Lerp(0.5f, 1f, Mathf.Abs(__instance.m_rudderValue));

        __instance.m_rudder = dir.x * num;
        __instance.m_rudderValue += __instance.m_rudder * __instance.m_rudderSpeed * fixedDeltaTime;
        __instance.m_rudderValue = Mathf.Clamp(__instance.m_rudderValue, -1f, 1f);

        if (Time.time - __instance.m_sendRudderTime > 0.05f)
        {
            __instance.m_sendRudderTime = Time.time;
            __instance.m_nview.InvokeRPC("Rudder", __instance.m_rudderValue);
        }

        return false;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UpdateAttach))]
    [HarmonyPostfix]
    public static void SmoothAttachedPlayer(Player __instance)
    {
        if (!VBNetTweaks.c_ModuleShipSync.Value) return;
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
    public static void ClearCacheOnDetach(Player __instance) 
    {
        if (!VBNetTweaks.c_ModuleShipSync.Value) return;
        RemoveFromCache(__instance.GetPlayerID());
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDestroy))]
    [HarmonyPrefix]
    public static void ClearCacheOnDestroy(Player __instance)
    {
        if (!VBNetTweaks.c_ModuleShipSync.Value) return;
        RemoveFromCache(__instance.GetPlayerID());
    }

    private static void RemoveFromCache(long id)
    {
        if (_playerPosVelocities.ContainsKey(id)) _playerPosVelocities.Remove(id);
    }
}
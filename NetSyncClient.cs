// VBNetTweaks.ClientDelta.cs
namespace VBNetTweaks;

[HarmonyPatch]
public static class NetSyncClient
{
    private static readonly Dictionary<ZDOID, RingBuffer<StatePoint>> _history = new(8192);
    private static float _interpDelay = 0.12f;
    private static bool _isConnected;
    private static ZRpc m_serverRpc;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), "Awake")]
    public static void ZNet_Awake_Postfix(ZNet __instance)
    {
        try
        {
            if (!__instance) return;
            
            // Проверяем, включена ли новая система
            bool netSyncEnabled = VBNetTweaks.EnableNetSync?.Value ?? true;
            if (!netSyncEnabled) return;
            
            if (!__instance.IsServer())
            {
                // Получаем серверный RPC через отражение
                m_serverRpc = GetServerRPC(__instance);
                if (m_serverRpc != null)
                {
                    m_serverRpc.Register<ZPackage>("VBNT_DeltaBatch", OnDeltaBatch);
                    _isConnected = true;
                    VBNetTweaks.LogDebug("NetSyncClient initialized successfully");
                }
                else
                {
                    VBNetTweaks.LogDebug("Failed to get server RPC");
                }
            }
        }
        catch (Exception e)
        {
            VBNetTweaks.LogDebug($"Error initializing NetSyncClient: {e.Message}");
        }
    }

    // Вспомогательный метод для получения серверного RPC
    private static ZRpc GetServerRPC(ZNet znet)
    {
        try
        {
            // Попробуем разные способы получить серверный RPC
            var field = typeof(ZNet).GetField("m_serverRpc", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                return field.GetValue(znet) as ZRpc;
            }
            
            // Альтернативный способ через свойство
            var prop = typeof(ZNet).GetProperty("m_serverRpc", BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
            {
                return prop.GetValue(znet) as ZRpc;
            }
            
            VBNetTweaks.LogDebug("Could not find m_serverRpc field/property");
            return null;
        }
        catch (Exception e)
        {
            VBNetTweaks.LogDebug($"Error getting server RPC: {e.Message}");
            return null;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    public static void ZNet_OnDestroy_Postfix()
    {
        _history.Clear();
        _isConnected = false;
        m_serverRpc = null;
        VBNetTweaks.LogDebug("NetSyncClient cleared on disconnect");
    }

    // Отслеживание статуса подключения
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNet), "Update")]
    public static void ZNet_Update_Postfix(ZNet __instance)
    {
        if (__instance.IsServer()) return;
        
        bool currentlyConnected = __instance.IsConnected(0);
        if (_isConnected && !currentlyConnected)
        {
            _history.Clear();
            m_serverRpc = null;
            VBNetTweaks.LogDebug("Client disconnected, cleared sync data");
        }
        _isConnected = currentlyConnected;
    }

    private static void OnDeltaBatch(ZRpc zRpc, ZPackage pkg)
    {
        if (!_isConnected) return;

        try
        {
            int processedCount = 0;
            
            // Читаем данные напрямую из ZPackage
            while (pkg.GetPos() < pkg.Size() - 8)
            {
                // Читаем ZDOID
                long userID = pkg.ReadLong();
                uint id = pkg.ReadUInt();
                var zdoId = new ZDOID(userID, id);
                
                if (pkg.GetPos() >= pkg.Size() - 2) break;
                
                byte mask = pkg.ReadByte();
                byte zone = pkg.ReadByte();

                var sp = new StatePoint { 
                    Stamp = Time.time,
                    Zone = (NetSyncServer.ZoneClass)zone
                };

                // Читаем поля на основе mask
                if ((mask & 1) != 0 && pkg.GetPos() <= pkg.Size() - 6)
                {
                    short x = pkg.ReadShort();
                    short y = pkg.ReadShort();
                    short z = pkg.ReadShort();
                    sp.Pos = new Vector3(x * 0.01f, y * 0.01f, z * 0.01f);
                }
                
                if ((mask & 2) != 0 && pkg.GetPos() <= pkg.Size() - 4)
                {
                    float x = (pkg.ReadByte() / 255f) * 2f - 1f;
                    float y = (pkg.ReadByte() / 255f) * 2f - 1f;
                    float z = (pkg.ReadByte() / 255f) * 2f - 1f;
                    float w = (pkg.ReadByte() / 255f) * 2f - 1f;
                    sp.Rot = new Quaternion(x, y, z, w).normalized;
                }
                
                if ((mask & 4) != 0 && pkg.GetPos() <= pkg.Size() - 6)
                {
                    short x = pkg.ReadShort();
                    short y = pkg.ReadShort();
                    short z = pkg.ReadShort();
                    sp.Vel = new Vector3(x * 0.01f, y * 0.01f, z * 0.01f);
                }
                
                if ((mask & 8) != 0 && pkg.GetPos() <= pkg.Size() - 4)
                    sp.HP = pkg.ReadInt();
                    
                if ((mask & 16) != 0 && pkg.GetPos() <= pkg.Size() - 4)
                    sp.Flags = pkg.ReadUInt();

                if (!_history.TryGetValue(zdoId, out var buf))
                {
                    buf = new RingBuffer<StatePoint>(GetBufferSizeByZone(sp.Zone));
                    _history[zdoId] = buf;
                }
                buf.Push(sp);
                processedCount++;
            }
            
            if (VBNetTweaks.DebugEnabled.Value && VBNetTweaks.VerboseLogging.Value && processedCount > 0)
            {
                VBNetTweaks.LogVerbose($"Processed {processedCount} delta entries, total tracked: {_history.Count}");
            }
        }
        catch (Exception e)
        {
            VBNetTweaks.LogDebug($"Error processing delta batch: {e.Message}");
        }
    }

    // Остальные методы без изменений...
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZNetScene), "Update")]
    public static void ZNetScene_Update_Postfix(ZNetScene __instance)
    {
        if (!_isConnected) return;

        var instances = __instance.m_instances;
        if (instances == null || instances.Count == 0) return;

        float targetTime = Time.time - _interpDelay;
        int interpolatedCount = 0;
        int extrapolatedCount = 0;

        foreach (var kv in instances)
        {
            var zdo = kv.Key;
            var view = kv.Value;
            if (zdo == null || view == null || view.transform == null) continue;

            if (_history.TryGetValue(zdo.m_uid, out var buf))
            {
                if (buf.TryGetLerpPair(targetTime, out var a, out var b))
                {
                    float t = Mathf.InverseLerp(a.Stamp, b.Stamp, targetTime);
                    var targetPos = Vector3.Lerp(a.Pos, b.Pos, t);
                    var targetRot = Quaternion.Slerp(a.Rot, b.Rot, t);

                    float smoothFactor = GetSmoothFactorByZone(a.Zone) * Time.deltaTime * 10f;
                    view.transform.position = Vector3.Lerp(view.transform.position, targetPos, smoothFactor);
                    view.transform.rotation = Quaternion.Slerp(view.transform.rotation, targetRot, smoothFactor);
                    
                    interpolatedCount++;
                }
                else if (buf.TryGetLast(out var last))
                {
                    float timeDiff = Time.time - last.Stamp;
                    var predictedPos = last.Pos + last.Vel * timeDiff;
                    
                    float smoothFactor = GetSmoothFactorByZone(last.Zone) * 0.5f * Time.deltaTime * 10f;
                    view.transform.position = Vector3.Lerp(view.transform.position, predictedPos, smoothFactor);
                    view.transform.rotation = Quaternion.Slerp(view.transform.rotation, last.Rot, smoothFactor * 0.8f);
                    
                    extrapolatedCount++;
                }
            }
        }

        if (VBNetTweaks.DebugEnabled.Value && VBNetTweaks.VerboseLogging.Value && Time.frameCount % 200 == 0)
        {
            VBNetTweaks.LogVerbose($"Sync: {interpolatedCount} interp, {extrapolatedCount} extrap, total: {_history.Count} objects");
        }
    }

    private static int GetBufferSizeByZone(NetSyncServer.ZoneClass zone)
    {
        return zone switch
        {
            NetSyncServer.ZoneClass.Near => 16,
            NetSyncServer.ZoneClass.Mid => 8,
            NetSyncServer.ZoneClass.Far => 4,
            _ => 8
        };
    }

    private static float GetSmoothFactorByZone(NetSyncServer.ZoneClass zone)
    {
        return zone switch
        {
            NetSyncServer.ZoneClass.Near => 0.8f,
            NetSyncServer.ZoneClass.Mid => 0.5f,
            NetSyncServer.ZoneClass.Far => 0.3f,
            _ => 0.5f
        };
    }

    private struct StatePoint
    {
        public float Stamp;
        public Vector3 Pos;
        public Quaternion Rot;
        public Vector3 Vel;
        public int HP;
        public uint Flags;
        public NetSyncServer.ZoneClass Zone;
    }

    private class RingBuffer<T>
    {
        private readonly T[] _data;
        private int _count;
        private int _head;

        public RingBuffer(int capacity) 
        { 
            _data = new T[capacity]; 
            _count = 0; 
            _head = 0; 
        }

        public void Push(T value)
        {
            _data[_head] = value;
            _head = (_head + 1) % _data.Length;
            _count = Math.Min(_count + 1, _data.Length);
        }

        public bool TryGetLast(out T value)
        {
            if (_count == 0)
            {
                value = default;
                return false;
            }
            int lastIndex = (_head - 1 + _data.Length) % _data.Length;
            value = _data[lastIndex];
            return true;
        }

        public bool TryGetLerpPair(float targetTime, out StatePoint a, out StatePoint b)
        {
            a = default;
            b = default;

            if (_count < 2) return false;

            for (int i = 0; i < _count - 1; i++)
            {
                int indexA = (_head - _count + i + _data.Length) % _data.Length;
                int indexB = (indexA + 1) % _data.Length;

                var pointA = (StatePoint)(object)_data[indexA];
                var pointB = (StatePoint)(object)_data[indexB];

                if (pointA.Stamp <= targetTime && targetTime <= pointB.Stamp)
                {
                    a = pointA;
                    b = pointB;
                    return true;
                }
            }
            return false;
        }
    }
}
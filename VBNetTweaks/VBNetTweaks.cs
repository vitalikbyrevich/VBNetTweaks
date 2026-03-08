namespace VBNetTweaks
{
    public enum CompressionAlgorithm
    {
        Deflate,
        Zstd
    }
    
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInIncompatibility("CacoFFF.valheim.LeanNet")]
    [BepInIncompatibility("redseiko.valheim.scenic")]
    [BepInIncompatibility("Searica.Valheim.NetworkTweaks")]
    [BepInIncompatibility("Searica.Valheim.OpenSesame")]
    [BepInIncompatibility("org.bepinex.plugins.network")]
    [BepInIncompatibility("CW_Jesse.BetterNetworking")]
    public class VBNetTweaks : BaseUnityPlugin
    {
        private const string ModName = "VBNetTweaks";
        private const string ModVersion = "0.1.8";
        private const string ModGUID = "VitByr.VBNetTweaks";
        
        
        public static ConfigEntry<bool> EnableAILOD;
        public static ConfigEntry<float> AILODNearDistance;
        public static ConfigEntry<float> AILODFarDistance;
        public static ConfigEntry<float> AILODThrottleFactor;

        public static ConfigEntry<bool> EnableZDOThrottling;
        public static ConfigEntry<float> ZDOThrottleDistance;

        public static ConfigEntry<bool> EnablePlayerPositionBoost;
        public static ConfigEntry<float> PlayerPositionUpdateMultiplier;
        public static ConfigEntry<bool> EnableClientInterpolation;
        public static ConfigEntry<bool> EnablePlayerPrediction;

        public static ConfigEntry<bool> EnableMonsterAiPatches;
        public static ConfigEntry<bool> EnableSteamSendRate;
        public static ConfigEntry<int> SteamSendRateMinKB;
        public static ConfigEntry<int> SteamSendRateMaxKB;
        public static ConfigEntry<int> SteamSendBufferSize;
        
        public static ConfigEntry<bool> EnableNetworkCompression;
        public static ConfigEntry<string> CompressionAlgorithm;
        public static ConfigEntry<int> m_CompressionLevel;

        public static ConfigEntry<bool> DebugEnabled;
        public static ConfigEntry<bool> VerboseLogging;
        public static ConfigEntry<float> SendInterval;
        public static ConfigEntry<int> PeersPerUpdate;
        public static ConfigEntry<bool> SceneDebugEnabled;
        public static ConfigEntry<bool> EnableNetSync;

        public static double NetTime;
        public static float DeltaTimeFixedPhysics = 0.02f;
        public static float DeltaTimePhysics = 0.01f;

        private Harmony _harmony;
        private static bool _serverConfigsInitialized;

        private void Awake()
        {
            DebugEnabled = Config.Bind("01 - General", "DebugEnabled", false, new ConfigDescription("Включить отладочный вывод"));
            VerboseLogging = Config.Bind("01 - General", "VerboseLogging", false, new ConfigDescription("Включить подробное логирование успешных операций"));
                
            EnableNetworkCompression = Config.Bind("Network", "EnableCompression", true, "Enable network compression (safe, negotiated between peers)");
            CompressionAlgorithm = Config.Bind("Network", "CompressionAlgorithm", "Deflate", "Deflate (built-in) or Zstd (requires ZstdSharp)");
            m_CompressionLevel = Config.Bind("Network", "CompressionLevel", 1, "The higher the load on the processor increases. Max 10");

             if (ZRoutedRpc.instance != null)
             {
                 ZRoutedRpc.instance.Register<ZPackage>("VBNT_RPCBatch", RpcBatcher.HandleBatch); 
                 Logger.LogInfo("VBNetTweaks: VBNT_RPCBatch registered");
             }
    
             if (EnableNetworkCompression.Value)
             {
                 ZDONetworkOptimizer.Initialize();
             }

            _harmony = new Harmony(ModGUID);
            _harmony.PatchAll(typeof(ZDONetworkOptimizer)); 
            _harmony.PatchAll(typeof(SteamOptimizations));
            _harmony.PatchAll(typeof(ShipSyncSystem));
            _harmony.PatchAll(typeof(PlayerSyncSystem));
            _harmony.PatchAll(typeof(ObjectPool));
            _harmony.PatchAll(typeof(PlayerCache));
            _harmony.PatchAll(typeof(WearNTear_ClearCachedSupport_Patch));
            _harmony.PatchAll(typeof(WearNTear_OnDestroy_Patch));
            _harmony.PatchAll(typeof(WearNTear_UpdateWear_Patch));
            _harmony.PatchAll(typeof(WearNTear_GetSupport_Patch));
            _harmony.PatchAll(typeof(WearNTear_RPC_Damage_Patch));
            _harmony.PatchAll(typeof(WearNTear_Destroy_Patch));
            _harmony.PatchAll(typeof(ZoneSystem_Update_Patch));
            _harmony.PatchAll(typeof(ZNet_RPC_CharacterID_Patch));
            _harmony.PatchAll(typeof(ZNet_OnNewConnection_Patch));
            _harmony.PatchAll(typeof(ZNet_Disconnect_Patch));

            // Серверные патчи — через корутину
            StartCoroutine(DelayedServerPatchInit());

            // Отложенная инициализация серверных настроек
            StartCoroutine(DelayedServerConfigInit());

            Logger.LogInfo("VBNetTweaks загружен!");
            if (DebugEnabled.Value) Logger.LogInfo("Режим отладки включен");
        }
        
        private System.Collections.IEnumerator DelayedServerPatchInit() 
        { 
            float timeout = 30f; // секунд
            float elapsed = 0f;
            while (!ZNet.instance && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (!ZNet.instance)
            {
                Logger.LogWarning("ZNet.instance не появился за 30 секунд — серверные патчи не применены.");
                yield break;
            }

            if (!Helper.IsServer()) yield break; 
            if (EnableAILOD?.Value == true) _harmony.PatchAll(typeof(AILODPatches)); 
            if (EnableMonsterAiPatches?.Value == true) _harmony.PatchAll(typeof(MonsterAiPatches)); 
            Logger.LogInfo("VBNetTweaks: серверные патчи успешно применены."); 
        }

        private System.Collections.IEnumerator DelayedServerConfigInit()
        {
            int maxAttempts = 100;
            for (int i = 0; i < maxAttempts; i++)
            {
                if (ZNet.instance) break;
                yield return new WaitForSeconds(0.25f);
            }

            if (EnableNetworkCompression.Value)
            {
                ZDONetworkOptimizer.Initialize();
            }

            if (Helper.IsServer())
            {
                EnableNetSync = Config.BindConfig("02 - Network", "EnableNetSync", true, "Включить новую систему синхронизации NetSync", synced: true);
                SendInterval = Config.BindConfig("02 - Network", "SendInterval", 0.05f, "Интервал отправки данных (секунды) - ТОЛЬКО СЕРВЕР", synced: true);
                PeersPerUpdate = Config.BindConfig("02 - Network", "PeersPerUpdate", 20, "Количество пиров для обработки за один апдейт - ТОЛЬКО СЕРВЕР", synced: true);
                EnableZDOThrottling = Config.BindConfig("02 - Network", "EnableZDOThrottling", true, "Снижать частоту обновления для дальних ZDO (только для сервера).", synced: true);
                ZDOThrottleDistance = Config.BindConfig("02 - Network", "ZDOThrottleDistance", 500f, "Дистанция (в метрах), за пределами которой ZDO обновляются реже.", synced: true);
                
                EnableSteamSendRate = Config.Bind("02 - Network", "EnableSteamSendRateOverride", true, "Применять настройки скорости отправки Steam при запуске.");
                SteamSendRateMinKB = Config.Bind("02 - Network", "SteamSendRateMinKB", 256, "Минимальная скорость отправки (КБ/с).");
                SteamSendRateMaxKB = Config.Bind("02 - Network", "SteamSendRateMaxKB", 1024, "Максимальная скорость отправки (КБ/с).");
                SteamSendBufferSize = Config.Bind("02 - Network", "SteamSendBufferBytes", 100_000_000, "Размер буфера отправки Steam (в байтах).");
                
                SceneDebugEnabled = Config.BindConfig("03 - Scene Optimizations", "SceneDebugEnabled", false, "Включить отладочный вывод для сцены", synced: true);

                EnableAILOD = Config.BindConfig("04 - AI", "EnableAILOD", true, "Уменьшать частоту обновления AI для дальних существ (только для сервера).", synced: true);
                AILODNearDistance = Config.BindConfig("04 - AI", "AILODNearDistance", 100f, "Дистанция (в метрах), в пределах которой AI работает на полной скорости.", synced: true);
                AILODFarDistance = Config.BindConfig("04 - AI", "AILODFarDistance", 300f, "Дистанция (в метрах), за пределами которой AI замедляется.", synced: true);
                AILODThrottleFactor = Config.BindConfig("04 - AI", "AILODThrottleFactor", 0.5f, "Коэффициент замедления для дальнего AI (0.5 = половинная скорость).", synced: true);
                
                EnablePlayerPositionBoost = Config.BindConfig("05 - Player Sync", "EnableHighFrequencyPositionUpdates", true, "Повысить приоритет обновления позиций игроков на сервере.", synced: true);
                PlayerPositionUpdateMultiplier = Config.BindConfig("05 - Player Sync", "PositionUpdateMultiplier", 2.5f, "Множитель приоритета синхронизации игроков (1.0 = стандарт, 2.5 = рекомендовано).", synced: true);
                EnableClientInterpolation = Config.Bind("05 - Player Sync", "EnableClientInterpolation", true, "Сглаживать движения других игроков на клиенте (убирает рывки).");
                EnablePlayerPrediction = Config.Bind("05 - Player Sync", "EnableClientPrediction", true, "Прогнозировать движения других игроков между сетевыми обновлениями (плавность в бою).");
                
                EnableMonsterAiPatches = Config.Bind("06 - Gameplay", "EnableMonsterAiPatches", true, "Использовать всех игроков вместо локального для событий и спавна монстров.");
                
                var zoneOwnerSection = "07 - Zone Owner Manager";
                ZoneOwnerManager.Enabled = Config.BindConfig(zoneOwnerSection, "Enabled", true, "Включить автоматическую передачу владения зоной на основе пинга", synced: true);
                ZoneOwnerManager.PingThreshold = Config.BindConfig(zoneOwnerSection, "PingThreshold", 100, 
                    "Порог пинга (мс). Если пинг владельца выше этого значения - возможна передача.", synced: true);
                ZoneOwnerManager.Hysteresis = Config.BindConfig(zoneOwnerSection, "Hysteresis", 20, 
                    "Гистерезис (мс). Новый кандидат должен быть как минимум на столько мс лучше текущего владельца.", synced: true);
                ZoneOwnerManager.TransferCooldown = Config.BindConfig(zoneOwnerSection, "TransferCooldown", 5f, 
                    "Минимальное время (сек) между передачами владения одной зоны.", synced: true);
                ZoneOwnerManager.OwnerUpdateInterval = Config.BindConfig(zoneOwnerSection, "OwnerUpdateInterval", 2f, 
                    "Как часто (сек) проверять необходимость передачи владения.", synced: true);

                _serverConfigsInitialized = true;
                Logger.LogInfo("Серверные настройки VBNetTweaks инициализированы");
                
                ZoneOwnerManager.Initialize();
            }
            else Logger.LogInfo("VBNetTweaks работает в клиентском режиме");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        public static void LogDebug(string message)
        {
            if (DebugEnabled.Value) Debug.Log($"[VBNetTweaks] {message}");
        }

        public static void LogVerbose(string message)
        {
            if (DebugEnabled.Value && VerboseLogging.Value) Debug.Log($"[VBNetTweaks] {message}");
        }

        public static bool GetSceneDebugEnabled()
        {
            try
            {
                return SceneDebugEnabled?.Value ?? false;
            }
            catch
            {
                return false;
            }
        }
        
        public static float GetEffectiveSendInterval()
        {
            float cfg = (!_serverConfigsInitialized) ? 0.05f : (SendInterval?.Value ?? 0.05f);
            return AdaptiveThrottler.GetInterval(cfg);
        }

        public static int GetPeersPerUpdate() => (!_serverConfigsInitialized) ? 20 : (PeersPerUpdate?.Value ?? 20);
    }
}
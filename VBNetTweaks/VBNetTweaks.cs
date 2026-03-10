namespace VBNetTweaks
{
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
        private const string ModVersion = "0.1.95";
        private const string ModGUID = "VitByr.VBNetTweaks";
        
        // Главный выключатель
        public static ConfigEntry<bool> ModEnabled;

        // Отладка
        public static ConfigEntry<bool> DebugEnabled;
        public static ConfigEntry<bool> VerboseLogging;
        public static ConfigEntry<bool> SceneDebugEnabled;

        // Модули - все в Awake() чтобы были и на клиенте, и на сервере
        public static ConfigEntry<bool> ModuleCompression;      // Сжатие трафика
        public static ConfigEntry<bool> ModuleZDOThrottling;    // Троттлинг ZDO
        public static ConfigEntry<bool> ModuleAILOD;            // LOD для AI
        public static ConfigEntry<bool> ModuleMonsterAI;        // Патчи AI монстров
        public static ConfigEntry<bool> ModuleSteamOptimizations; // Оптимизации Steam
        public static ConfigEntry<bool> ModulePlayerSync;       // Синхронизация игроков
        public static ConfigEntry<bool> ModuleShipSync;         // Синхронизация кораблей
        public static ConfigEntry<bool> ModuleZoneOwner;        // Управление владельцами зон
        public static ConfigEntry<bool> ModuleSupportCache;     // Кэш поддержки построек
        public static ConfigEntry<bool> ModuleRpcBatcher;       // Пакетная обработка RPC

        // Параметры модулей (тоже в Awake)
        public static ConfigEntry<CompressionAlgorithm> m_CompressionAlgorithm;
        public static ConfigEntry<int> CompressionLevel;
        public static ConfigEntry<float> ZDOThrottleDistance;
        public static ConfigEntry<float> AILODNearDistance;
        public static ConfigEntry<float> AILODFarDistance;
        public static ConfigEntry<float> AILODThrottleFactor;
        public static ConfigEntry<int> SteamSendRateMinKB;
        public static ConfigEntry<int> SteamSendRateMaxKB;
        public static ConfigEntry<int> SteamSendBufferSize;
        public static ConfigEntry<bool> EnableClientInterpolation;
        public static ConfigEntry<bool> EnablePlayerPrediction;
    
        // Серверные параметры (остаются в DelayedServerConfigInit)
        public static ConfigEntry<float> SendInterval;
        public static ConfigEntry<int> PeersPerUpdate;
        public static ConfigEntry<bool> EnableNetSync;

        public static double NetTime;
        public static float DeltaTimeFixedPhysics = 0.02f;
        public static float DeltaTimePhysics = 0.01f;

        private Harmony _harmony;
        private static bool _serverConfigsInitialized;

        private void Awake()
    {
        // Главный выключатель
        ModEnabled = Config.Bind("00 - Master", "ModEnabled", true, "Полностью включить/выключить мод VBNetTweaks");
        if (!ModEnabled.Value) return;

        // Отладка
        var debugSection = "01 - Debug";
        DebugEnabled = Config.Bind(debugSection, "DebugEnabled", false, "Включить отладочный вывод");
        VerboseLogging = Config.Bind(debugSection, "VerboseLogging", false, "Включить подробное логирование");

        // Модули - все здесь, чтобы были доступны и на клиенте, и на сервере
        var modulesSection = "02 - Modules";
        ModuleCompression = Config.Bind(modulesSection, "Compression", true, "Сжатие сетевого трафика");
        ModuleZDOThrottling = Config.Bind(modulesSection, "ZDOThrottling", true, "Троттлинг дальних ZDO объектов");
        ModuleAILOD = Config.Bind(modulesSection, "AILOD", true, "LOD для AI существ (снижение частоты обновления)");
        ModuleMonsterAI = Config.Bind(modulesSection, "MonsterAI", true, "Оптимизация AI монстров и событий");
        ModuleSteamOptimizations = Config.Bind(modulesSection, "SteamOptimizations", true, "Оптимизации Steam сокета");
        ModulePlayerSync = Config.Bind(modulesSection, "PlayerSync", true, "Синхронизация и сглаживание игроков");
        ModuleShipSync = Config.Bind(modulesSection, "ShipSync", true, "Синхронизация кораблей");
        ModuleZoneOwner = Config.Bind(modulesSection, "ZoneOwner", true, "Автоматическая передача владения зонами");
        ModuleSupportCache = Config.Bind(modulesSection, "SupportCache", true, "Кэш расчетов поддержки построек");
        ModuleRpcBatcher = Config.Bind(modulesSection, "RpcBatcher", true, "Пакетная обработка RPC вызовов");

        // Параметры модулей
        var compressionSection = "03 - Compression Settings";
        m_CompressionAlgorithm = Config.Bind(compressionSection, "Algorithm", CompressionAlgorithm.Deflate, 
            "Алгоритм сжатия: Deflate (встроенный) или Zstd (требует ZstdSharp)");
        CompressionLevel = Config.Bind(compressionSection, "Level", 2, 
            new ConfigDescription("Уровень сжатия (1-10)", new AcceptableValueRange<int>(1, 10)));

        var zdoSection = "04 - ZDO Throttling Settings";
        ZDOThrottleDistance = Config.Bind(zdoSection, "Distance", 500f, 
            "Дистанция (м), после которой ZDO обновляются реже");

        var aiSection = "05 - AI LOD Settings";
        AILODNearDistance = Config.Bind(aiSection, "NearDistance", 100f, 
            "Дистанция (м) полной скорости AI");
        AILODFarDistance = Config.Bind(aiSection, "FarDistance", 300f, 
            "Дистанция (м) максимального замедления AI");
        AILODThrottleFactor = Config.Bind(aiSection, "ThrottleFactor", 0.5f, 
            new ConfigDescription("Коэффициент замедления AI (0.25-0.75)", 
                new AcceptableValueRange<float>(0.25f, 0.75f)));

        var steamSection = "06 - Steam Settings";
        SteamSendRateMinKB = Config.Bind(steamSection, "MinRateKB", 256, 
            "Минимальная скорость отправки Steam (КБ/с)");
        SteamSendRateMaxKB = Config.Bind(steamSection, "MaxRateKB", 1024, 
            "Максимальная скорость отправки Steam (КБ/с)");
        SteamSendBufferSize = Config.Bind(steamSection, "BufferSize", 100_000_000, 
            "Размер буфера отправки Steam (байт)");

        var playerSyncSection = "07 - Player Sync Settings";
        EnableClientInterpolation = Config.Bind(playerSyncSection, "Interpolation", true, 
            "Сглаживать движения других игроков (убирает рывки)");
        EnablePlayerPrediction = Config.Bind(playerSyncSection, "Prediction", true, 
            "Прогнозировать движения игроков между обновлениями");

        // Инициализация модулей
        if (ModuleCompression.Value) ZDONetworkOptimizer.Initialize();

        // Регистрация RPC
        if (ModuleRpcBatcher.Value && ZRoutedRpc.instance != null)
        {
            ZRoutedRpc.instance.Register<ZPackage>("VBNT_RPCBatch", RpcBatcher.HandleBatch);
            Logger.LogInfo("VBNetTweaks: VBNT_RPCBatch registered");
        }

        // Патчи - применяем только включенные модули
        _harmony = new Harmony(ModGUID);
        
        if (ModuleCompression.Value) _harmony.PatchAll(typeof(ZDONetworkOptimizer));
        if (ModuleSteamOptimizations.Value) _harmony.PatchAll(typeof(SteamOptimizations));
        if (ModuleShipSync.Value) _harmony.PatchAll(typeof(ShipSyncSystem));
        if (ModulePlayerSync.Value) _harmony.PatchAll(typeof(PlayerSyncSystem));
        if (ModuleRpcBatcher.Value) _harmony.PatchAll(typeof(RpcBatcher));
        if (ModuleSupportCache.Value)
        {
            _harmony.PatchAll(typeof(WearNTear_ClearCachedSupport_Patch));
            _harmony.PatchAll(typeof(WearNTear_OnDestroy_Patch));
            _harmony.PatchAll(typeof(WearNTear_UpdateWear_Patch));
            _harmony.PatchAll(typeof(WearNTear_GetSupport_Patch));
            _harmony.PatchAll(typeof(WearNTear_RPC_Damage_Patch));
            _harmony.PatchAll(typeof(WearNTear_Destroy_Patch));
        }
        if (ModuleZoneOwner.Value) _harmony.PatchAll(typeof(ZoneOwnerManager));
        
        // Общие патчи (всегда нужны)
        _harmony.PatchAll(typeof(ObjectPool));
        _harmony.PatchAll(typeof(PlayerCache));

        StartCoroutine(DelayedServerConfigInit());
        StartCoroutine(DelayedServerPatchInit());

        Logger.LogInfo("VBNetTweaks загружен!");
        if (DebugEnabled.Value) Logger.LogInfo("Режим отладки включен");
    }
        
        private System.Collections.IEnumerator DelayedServerPatchInit()
        {
            float timeout = 30f;
            float elapsed = 0f;
            while (!ZNet.instance && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        
            if (!ZNet.instance)
            {
                Logger.LogWarning("ZNet.instance не появился за 30 секунд");
                yield break;
            }

            if (!Helper.IsServer()) yield break;

            // Серверные патчи (применяются только на сервере)
            if (ModuleAILOD.Value) _harmony.PatchAll(typeof(AILODPatches));
            if (ModuleMonsterAI.Value) _harmony.PatchAll(typeof(MonsterAiPatches));

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

        if (Helper.IsServer())
        {
            var serverSection = "08 - Server Settings";
            EnableNetSync = Config.BindConfig(serverSection, "EnableNetSync", true, 
                "Включить новую систему синхронизации NetSync", synced: true);
            SendInterval = Config.BindConfig(serverSection, "SendInterval", 0.05f, 
                "Интервал отправки данных (секунды)", synced: true);
            PeersPerUpdate = Config.BindConfig(serverSection, "PeersPerUpdate", 20, 
                "Количество пиров за один апдейт", synced: true);
            
            // Дополнительные серверные настройки ZoneOwnerManager
            if (ModuleZoneOwner.Value)
            {
                ZoneOwnerManager.PingThreshold = Config.BindConfig(serverSection, "ZonePingThreshold", 100, 
                    "Порог пинга для смены владельца зоны (мс)", synced: true);
                ZoneOwnerManager.Hysteresis = Config.BindConfig(serverSection, "ZoneHysteresis", 20, 
                    "Гистерезис для смены владельца (мс)", synced: true);
                ZoneOwnerManager.TransferCooldown = Config.BindConfig(serverSection, "ZoneTransferCooldown", 5f, 
                    "Задержка между сменами владельца (сек)", synced: true);
                ZoneOwnerManager.OwnerUpdateInterval = Config.BindConfig(serverSection, "ZoneUpdateInterval", 2f, 
                    "Частота проверки владельцев зон (сек)", synced: true);
                
                ZoneOwnerManager.Initialize();
            }

            _serverConfigsInitialized = true;
            Logger.LogInfo("Серверные настройки VBNetTweaks инициализированы");
        }
        else
        {
            Logger.LogInfo("VBNetTweaks работает в клиентском режиме");
        }
    }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        public static void LogDebug(string message)
        {
            if (DebugEnabled.Value) Debug.LogWarning($"[VBNetTweaks] {message}");
        }

        public static void LogVerbose(string message)
        {
            if (VerboseLogging.Value) Debug.Log($"[VBNetTweaks] {message}");
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
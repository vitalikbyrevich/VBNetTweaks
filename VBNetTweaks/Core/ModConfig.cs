namespace VBNetTweaks.Core
{
    public static class ModConfig
    {
        public static ConfigEntry<bool> ModEnabled { get; set; }
        public static ConfigEntry<bool> DebugEnabled { get; private set; }
        public static ConfigEntry<bool> VerboseLogging { get; private set; }
        public static ConfigEntry<bool> SceneDebugEnabled { get; private set; }

        public static ConfigEntry<bool> ModuleCompression { get; private set; }
        public static ConfigEntry<bool> ModuleZDOThrottling { get; private set; }
        public static ConfigEntry<bool> ModuleAILOD { get; private set; }
        public static ConfigEntry<bool> ModuleMonsterAI { get; private set; }
        public static ConfigEntry<bool> ModuleSteamOptimizations { get; private set; }
        public static ConfigEntry<bool> ModulePlayerSync { get; private set; }
        public static ConfigEntry<bool> ModuleShipSync { get; private set; }
        public static ConfigEntry<bool> ModuleZoneOwner { get; private set; }
        public static ConfigEntry<bool> ModuleSupportCache { get; private set; }
        public static ConfigEntry<bool> ModuleRpcBatcher { get; private set; }

        public static ConfigEntry<int> SteamSendRateMinKB { get; private set; }
        public static ConfigEntry<int> SteamSendRateMaxKB { get; private set; }
        public static ConfigEntry<int> SteamSendBufferSize { get; private set; }

        public static ConfigEntry<int> CompressionLevel { get; private set; }
        
        public static ConfigEntry<float> ZDOThrottleDistance { get; private set; }
        
        public static ConfigEntry<float> AILODNearDistance { get; private set; }
        public static ConfigEntry<float> AILODFarDistance { get; private set; }
        public static ConfigEntry<float> AILODThrottleFactor { get; private set; }
        
        public static ConfigEntry<bool> EnableClientInterpolation { get; private set; }
        public static ConfigEntry<bool> EnablePlayerPrediction { get; private set; }
        
        public static ConfigEntry<float> SendInterval { get; private set; }
        public static ConfigEntry<int> PeersPerUpdate { get; private set; }
        public static ConfigEntry<bool> EnableNetSync { get; private set; }

        private static ConfigFile _serverConfig;
        public static bool _serverConfigsInitialized;

        public static void Initialize(ConfigFile clientConfig, ConfigFile serverConfig)
        {
            _serverConfig = serverConfig;
            
            InitClientConfigs(clientConfig);
            InitServerConfigs(serverConfig);
        }

        private static void InitClientConfigs(ConfigFile config)
        {
            var debugSection = "01 - Debug";
            DebugEnabled = config.Bind(debugSection, "DebugEnabled", false, "Включить отладочный вывод");
            VerboseLogging = config.Bind(debugSection, "VerboseLogging", false, "Включить подробное логирование");

            var modulesSection = "02 - Modules";
            ModulePlayerSync = config.Bind(modulesSection, "PlayerSync", true, "Синхронизация игроков");
            ModuleShipSync = config.Bind(modulesSection, "ShipSync", true, "Синхронизация кораблей");
            ModuleSupportCache = config.Bind(modulesSection, "SupportCache", true, "Кэш поддержки построек");

            var playerSyncSection = "09 - Player Sync Settings";
            EnableClientInterpolation = config.Bind(playerSyncSection, "Interpolation", true, "Сглаживание игроков");
            EnablePlayerPrediction = config.Bind(playerSyncSection, "Prediction", true, "Предсказание движения");
        }

        private static void InitServerConfigs(ConfigFile config)
        {
            var modulesSection = "02 - Modules";
            ModuleAILOD = config.BindConfig(modulesSection, "AILOD", true, "LOD для AI существ", synced: true);
            ModuleSteamOptimizations = config.BindConfig(modulesSection, "SteamOptimizations", true, "Оптимизации Steam сокета", synced: true);
            ModuleMonsterAI = config.BindConfig(modulesSection, "MonsterAI", true, "Оптимизация AI монстров", synced: true);
            ModuleZoneOwner = config.BindConfig(modulesSection, "ZoneOwner", true, "Автоматическая передача владения зонами", synced: true);
            ModuleRpcBatcher = config.BindConfig(modulesSection, "RpcBatcher", true, "Пакетная обработка RPC", synced: true);

            var steamSection = "03 - Steam Settings";
            SteamSendRateMinKB = config.BindConfig(steamSection, "MinRateKB", 256, "Минимальная скорость Steam", synced: true);
            SteamSendRateMaxKB = config.BindConfig(steamSection, "MaxRateKB", 8192, "Максимальная скорость Steam", synced: true);
            SteamSendBufferSize = config.BindConfig(steamSection, "BufferSize", 128_000_000, "Размер буфера Steam", synced: true);
            
            var compressionSection = "04 - Compression Settings";
            CompressionController.Algorithm = config.BindConfig(compressionSection, "Algorithm", CompressionAlgorithm.Deflate, "Алгоритм сжатия: Deflate или Zstd", synced: true);
            CompressionController.Level = config.BindConfig(compressionSection, "Level", 3, "Уровень сжатия (1-10 для Deflate, 1-22 для Zstd)", acceptableValues: new AcceptableValueRange<int>(1, 22), synced: true);

            var serverSection = "05 - Server Settings";
            SendInterval = config.BindConfig(serverSection, "SendInterval", 0.05f, "Интервал отправки данных (секунды)", synced: true);
            PeersPerUpdate = config.BindConfig(serverSection, "PeersPerUpdate", 20, "Количество пиров за один апдейт", synced: true);
            EnableNetSync = config.BindConfig(serverSection, "EnableNetSync", true, "Включить новую систему синхронизации NetSync", synced: true);

            var zdoSection = "06 - ZDO Throttling Settings";
            ZDOThrottleDistance = config.BindConfig(zdoSection, "Distance", 100f, "Дистанция для троттлинга ZDO", synced: true);

            var zoneSection = "07 - Zone Owner Settings";
            ZoneOwnerManager.PingThreshold = config.BindConfig(zoneSection, "PingThreshold", 60, "Порог пинга для смены владельца зоны (мс)", synced: true);
            ZoneOwnerManager.Hysteresis = config.BindConfig(zoneSection, "Hysteresis", 20, "Гистерезис для смены владельца (мс)", synced: true);
            ZoneOwnerManager.TransferCooldown = config.BindConfig(zoneSection, "TransferCooldown", 5f, "Задержка между сменами владельца (сек)", synced: true);
            ZoneOwnerManager.OwnerUpdateInterval = config.BindConfig(zoneSection, "UpdateInterval", 2f, "Частота проверки владельцев зон (сек)", synced: true);

            var aiSection = "08 - AI LOD Settings";
            AILODNearDistance = config.BindConfig(aiSection, "NearDistance", 100f, "Дистанция полной скорости AI", synced: true);
            AILODFarDistance = config.BindConfig(aiSection, "FarDistance", 120f, "Дистанция замедления AI", synced: true);
            AILODThrottleFactor = config.BindConfig(aiSection, "ThrottleFactor", 0.5f, "Коэффициент замедления AI", acceptableValues: new AcceptableValueRange<float>(0.25f, 0.75f), synced: true);
        }

        public static bool IsServerConfigInitialized => _serverConfigsInitialized;
        public static void SetServerConfigInitialized() => _serverConfigsInitialized = true;
    }
}
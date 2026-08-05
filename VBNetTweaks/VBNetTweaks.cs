using BepInEx.Logging;

namespace VBNetTweaks
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    
    [BepInIncompatibility("CacoFFF.valheim.LeanNet")]
    [BepInIncompatibility("redseiko.valheim.scenic")]
    [BepInIncompatibility("Searica.Valheim.NetworkTweaks")]
    [BepInIncompatibility("Searica.Valheim.OpenSesame")]
    [BepInIncompatibility("org.bepinex.plugins.network")]
    [BepInIncompatibility("CW_Jesse.BetterNetworking")]
    [BepInIncompatibility("com.Fire.FiresGhettoNetworkMod")]
    [BepInIncompatibility("sighsorry.SkadiNet")]
    [BepInIncompatibility("redseiko.valheim.returntosender")]
    [BepInIncompatibility("com.maxsch.valheim.TimeoutLimit")]
    [BepInIncompatibility("dzk.warheimnetwork")]
    
    public class VBNetTweaks : BaseUnityPlugin
    {
        private const string ModName = "VBNetTweaks";
        private const string ModVersion = "0.4.0";
        private const string ModGUID = "VitByr.VBNetTweaks";
        public static VBNetTweaks Instance { get; private set; }
        public CustomRPC _configSyncRPC;
        private ConfigFile _clientConfig;
        public new static ManualLogSource Logger;
        
        public static ConfigEntry<Language> c_ConfigLanguage;
        public static ConfigEntry<bool> c_ModEnabled;

        public static ConfigEntry<bool> c_DebugEnabled;
        public static ConfigEntry<bool> c_VerboseLogging;

        public static ConfigEntry<bool> c_ModuleSteamOptimizations;
        public static ConfigEntry<bool> c_ModuleZDOOptimization;
        public static ConfigEntry<bool> c_ModuleShipSync;
        public static ConfigEntry<bool> c_ModuleZSyncTransformOptimization;
        public static ConfigEntry<bool> c_ModuleMapPositionSync;

        public static ConfigEntry<int> c_SteamSendRateMaxKB;
        public static ConfigEntry<int> c_SteamSendBufferSizeKB;
        public static ConfigEntry<float> c_SteamTimeoutConnected;
        public static ConfigEntry<float> c_SteamTimeoutKeepalive;
        public static ConfigEntry<int> c_SteamRecvMaxMessageSize;
        
        public static ConfigEntry<int> c_SteamSendRateMaxKB_S;
        public static ConfigEntry<int> c_SteamSendBufferSizeKB_S;

        public static ConfigEntry<int> c_ZDOQueueLimit;
        
        public static ConfigEntry<float> c_SendInterval_S;
        public static ConfigEntry<int> c_PeersPerUpdate_S;
        public static ConfigEntry<int> c_ZDOQueueLimit_S;
        public static ConfigEntry<float> c_FlushThresholdPercent_S;
        
        public static ConfigEntry<float> c_SmoothPosition;
        public static ConfigEntry<float> c_SmoothRotation;
        public static ConfigEntry<float> c_MicroThreshold;
        public static ConfigEntry<float> c_ClientDistanceThreshold;
        public static ConfigEntry<float> c_TeleportDistanceThreshold;
        public static ConfigEntry<float> c_TeleportRotationThreshold;
        
        public static ConfigEntry<float> c_MapPositionSendInterval;
        public static ConfigEntry<float> c_MapInterpolationDelay;
        public static ConfigEntry<float> c_MapMaxPredictionSpeed;
        public static ConfigEntry<float> c_MapMaxPredictionTime;
        public static ConfigEntry<float> c_MapTeleportThreshold;
        
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger; 
            _clientConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, "VitByr/VBNetTweaks/MainConfig.cfg"), true);
            SynchronizationManager.Instance.RegisterCustomConfig(_clientConfig);
            
            InitClientConfigs();
            InitServerConfigs();

            c_ModEnabled = _clientConfig.BindConfig("00 - Master", "ModEnabled", true, c_ConfigLanguage.Value == Language.Russian ? "Полностью включить/выключить мод VBNetTweaks" : "Completely enable/disable VBNetTweaks mod", synced: true);

            if (!c_ModEnabled.Value) return;

            _configSyncRPC = NetworkManager.Instance.AddRPC("VBNetTweaks_ConfigSync", OnAdminConfigSync, OnClientConfigSync);
            SynchronizationManager.Instance.AddInitialSynchronization(_configSyncRPC, () => BuildConfigPackage());

            CreateConfigWatcher();

            _harmony = new Harmony(ModGUID);
            
            if (c_ModuleMapPositionSync.Value)
            {
                MapPositionSync.Initialize();
                _harmony.PatchAll(typeof(MapPositionSync));
            }
            _harmony.PatchAll(typeof(ZSteamSocket_Patchs));
            _harmony.PatchAll(typeof(ShipSyncFix));
            _harmony.PatchAll(typeof(NetworkSyncPatches));
            _harmony.PatchAll(typeof(ZDONetworkOptimizer));

            Helper.LogDebug("Режим отладки включен");
        }

        private void InitClientConfigs()
        {
            var languageSection = "00 - Language";
            c_ConfigLanguage = Config.Bind(languageSection, "Language", Language.Russian, new ConfigDescription("Select interface language / Выберите язык интерфейса\nRequired Restart / Требуется рестарт"));

            var debugSection = "01 - Debug";
            c_DebugEnabled = Config.Bind(debugSection, "DebugEnabled", false, c_ConfigLanguage.Value == Language.Russian ? "Включить отладочный вывод" : "Enable debug output");
            c_VerboseLogging = Config.Bind(debugSection, "VerboseLogging", false, c_ConfigLanguage.Value == Language.Russian ? "Включить подробное логирование" : "Enable verbose logging");


            var modulesSection = "02 - Modules";
            c_ModuleSteamOptimizations = _clientConfig.BindConfig(modulesSection, "SteamOptimizations", true,
                c_ConfigLanguage.Value == Language.Russian ? "Оптимизации Steam сокета" : "Steam socket optimizations", synced: true);
            c_ModuleZDOOptimization = _clientConfig.BindConfig(modulesSection, "ZDOOptimization", true, c_ConfigLanguage.Value == Language.Russian ? "Оптимизация ZDO отправок" : "Optimization ZDO sender",
                synced: true);
            c_ModuleShipSync = _clientConfig.BindConfig(modulesSection, "ShipSync", true, c_ConfigLanguage.Value == Language.Russian ? "Синхронизация на кораблях" : "On ship synchronization", synced: true);
            c_ModuleZSyncTransformOptimization = _clientConfig.BindConfig(modulesSection, "ZSyncTransformOptimization", true,
                c_ConfigLanguage.Value == Language.Russian ? "Оптимизация движения игроков и мобов" : "Optimizing the movement of players and mobs", synced: true);
            c_ModuleMapPositionSync = _clientConfig.BindConfig(modulesSection, "MapPositionSync", true,
                c_ConfigLanguage.Value == Language.Russian ? "Включить плавные маркеры игроков на карте\nУлучшает отображение позиций игроков" : "Enable smooth player markers on map\nImproves display of player positions", synced: true);


            var steamSection = "03 - Client Steam Settings";
            c_SteamSendRateMaxKB = _clientConfig.BindConfig(steamSection, "MaxRateKB", 2048,
                c_ConfigLanguage.Value == Language.Russian ? "Максимальная скорость отправки Steam (vanilla = 150 KB/s)" : "Maximum Steam send rate (vanilla = 150 KB/s)", synced: true);

            c_SteamSendBufferSizeKB = _clientConfig.BindConfig(steamSection, "SendBufferSizeKB", 2048,
                c_ConfigLanguage.Value == Language.Russian
                    ? "Размер буфера отправки Steam в KB (vanilla = ~260KB). Рекомендуется 1024-4096"
                    : "Steam send buffer size in KB (vanilla = ~260KB). Recommended 1024-4096", synced: true);

            c_SteamTimeoutConnected = _clientConfig.BindConfig(steamSection, "TimeoutConnected", 120000f,
                c_ConfigLanguage.Value == Language.Russian
                    ? "Таймаут соединения Steam (миллисекунды)\n" + "Если соединение неактивно дольше этого времени — оно будет разорвано\n" + "vanilla: 30000 (30 секунд), рекомендуется: 60000-180000"
                    : "Steam connection timeout (milliseconds)\n" + "If connection is idle longer than this — it will be closed\n" + "vanilla: 30000 (30 sec), recommended: 60000-180000", synced: true);

            c_SteamTimeoutKeepalive = _clientConfig.BindConfig(steamSection, "TimeoutKeepalive", 30000f,
                c_ConfigLanguage.Value == Language.Russian
                    ? "Интервал Keep-Alive Steam (миллисекунды)\n" + "Как часто отправлять пинг для поддержания соединения\n" + "vanilla: 30000 (30 секунд), рекомендуется: 15000-60000"
                    : "Steam Keep-Alive interval (milliseconds)\n" + "How often to send ping to keep connection alive\n" + "vanilla: 30000 (30 sec), recommended: 15000-60000", synced: true);

            c_SteamRecvMaxMessageSize = _clientConfig.BindConfig(steamSection, "RecvMaxMessageSize", 8,
                c_ConfigLanguage.Value == Language.Russian
                    ? "Максимальный размер принимаемого сообщения Steam (мегабайты)\n" + "Большие пакеты будут отклонены\n" + "vanilla: ~1-2 MB, рекомендуется: 4-16 MB"
                    : "Maximum Steam receive message size (megabytes)\n" + "Large packets will be rejected\n" + "vanilla: ~1-2 MB, recommended: 4-16 MB", synced: true);


            var serverSection = "04 - Client ZDO Settings";

            c_ZDOQueueLimit = _clientConfig.BindConfig(serverSection, "ZDOQueueLimit", 10240,
                c_ConfigLanguage.Value == Language.Russian ? "Размер буфера отправки ZDO пакетов (vanilla = 10240 байт)" : "ZDO packet send buffer size (vanilla = 10240 bytes)", synced: true);


            var transformSection = "05 - Transform Settings";
            c_SmoothPosition = _clientConfig.BindConfig(transformSection, "SmoothPosition", 0.1f,
                c_ConfigLanguage.Value == Language.Russian ? "Сглаживание позиции (выше = плавнее, но больше задержка) (vanilla: 0.20)" : "Position smoothing value (vanilla: 0.20)", synced: true);

            c_SmoothRotation = _clientConfig.BindConfig(transformSection, "SmoothRotation", 0.3f,
                c_ConfigLanguage.Value == Language.Russian ? "Значение сглаживания поворота (vanilla: 0.50)" : "Rotation smoothing value (vanilla: 0.50)", synced: true);

            c_MicroThreshold = _clientConfig.BindConfig(transformSection, "MicroThreshold", 0.002f,
                c_ConfigLanguage.Value == Language.Russian ? "Порог микро-движений (выше = меньше обновлений) (vanilla: 0.001)" : "Micro-movement threshold (vanilla: 0.001)", synced: true);

            c_ClientDistanceThreshold = _clientConfig.BindConfig(transformSection, "ClientDistanceThreshold", 0.005f,
                c_ConfigLanguage.Value == Language.Russian ? "Порог дистанции для клиентской синхронизации (vanilla: 0.01)" : "Client distance threshold (vanilla: 0.01)", synced: true);

            c_TeleportDistanceThreshold = _clientConfig.BindConfig(transformSection, "TeleportDistanceThreshold", 3f,
                c_ConfigLanguage.Value == Language.Russian
                    ? "Порог дистанции для мгновенного телепорта (метры)\n" + "Если объект сместился больше этого значения — телепорт без сглаживания\n" + "vanilla: 5, рекомендуется: 5-20"
                    : "Distance threshold for instant teleport (meters)\n" + "If object moves beyond this value — teleport without smoothing\n" + "vanilla: 5, recommended: 5-20", synced: true);

            c_TeleportRotationThreshold = _clientConfig.BindConfig(transformSection, "TeleportRotationThreshold", 35f,
                c_ConfigLanguage.Value == Language.Russian
                    ? "Порог угла для мгновенного телепорта поворота (градусы)\n" + "Если объект повернулся больше этого значения — телепорт без сглаживания\n" + "vanilla: 45, рекомендуется: 30-90"
                    : "Angle threshold for instant rotation teleport (degrees)\n" + "If object rotates beyond this value — teleport without smoothing\n" + "vanilla: 45, recommended: 30-90", synced: true);

            var mapSection = "08 - Map Positions";

            c_MapPositionSendInterval = _clientConfig.BindConfig(mapSection, "SendInterval", 0.5f,
                c_ConfigLanguage.Value == Language.Russian
                    ? "Интервал отправки позиций игроков (сек)\nМеньше = плавнее, но больше трафик\nvanilla: 2.0, рекомендуется: 0.2-0.5" : "Player position send interval (sec)\nLower = smoother, but more traffic\nvanilla: 2.0, recommended: 0.2-0.5", synced: true);

            c_MapInterpolationDelay = _clientConfig.BindConfig(mapSection, "InterpolationDelay", 0.1f,
                c_ConfigLanguage.Value == Language.Russian
                    ? "Задержка интерполяции маркеров (сек)\nМаркеры будут отставать на это значение для плавности" : "Marker interpolation delay (sec)\nMarkers will lag by this value for smoothness", synced: true);

            c_MapMaxPredictionSpeed = _clientConfig.BindConfig(mapSection, "MaxPredictionSpeed", 40f,
                c_ConfigLanguage.Value == Language.Russian
                    ? "Максимальная скорость предсказания движения (м/с)\nОграничивает рывки при экстраполяции" : "Maximum movement prediction speed (m/s)\nLimits jerks during extrapolation", synced: true);

            c_MapMaxPredictionTime = _clientConfig.BindConfig(mapSection, "MaxPredictionTime", 0.05f,
                c_ConfigLanguage.Value == Language.Russian
                    ? "Максимальное время предсказания движения (сек)\nКак долго маркер двигается по инерции" : "Maximum movement prediction time (sec)\nHow long marker moves by inertia", synced: true);

            c_MapTeleportThreshold = _clientConfig.BindConfig(mapSection, "TeleportThreshold", 50f,
                c_ConfigLanguage.Value == Language.Russian
                    ? "Порог телепорта для маркеров (метры)\nЕсли игрок сместился дальше — маркер прыгнет мгновенно" : "Teleport threshold for markers (meters)\nIf player moves beyond — marker jumps instantly", synced: true);
        }

        private void InitServerConfigs()
        {
            var steamSection = "06 - Server Steam Settings";
            c_SteamSendRateMaxKB_S = _clientConfig.BindConfig(steamSection, "MaxRateKB", 4096, 
                c_ConfigLanguage.Value == Language.Russian ? "Максимальная скорость отправки Steam (vanilla = 150 KB/s)" : "Maximum Steam send rate (vanilla = 150 KB/s)", synced: false);
                
            c_SteamSendBufferSizeKB_S = _clientConfig.BindConfig(steamSection, "SendBufferSizeKB", 4096, 
                c_ConfigLanguage.Value == Language.Russian ? "Размер буфера отправки Steam в KB (vanilla = ~260KB). Рекомендуется 1024-4096" : "Steam send buffer size in KB (vanilla = ~260KB). Recommended 1024-4096", synced: false);

            
            var serverSection = "07 - Server ZDO Settings";
            c_SendInterval_S = _clientConfig.BindConfig(serverSection, "SendInterval", 0.02f, 
                c_ConfigLanguage.Value == Language.Russian ? "Интервал отправки данных (vanilla = 0.05)" : "Data send interval (vanilla = 0.05)", synced: false);
                
            c_PeersPerUpdate_S = _clientConfig.BindConfig(serverSection, "PeersPerUpdate", 10, 
                c_ConfigLanguage.Value == Language.Russian ? "Количество пиров за один апдейт (vanilla = 1). Лучше ставить значение равное максимальному количеству слотов сервера." : "Peers per update (vanilla = 1). Better set equal to max server slots.", synced: false);
                
            c_ZDOQueueLimit_S = _clientConfig.BindConfig(serverSection, "ZDOQueueLimit", 20480, 
                c_ConfigLanguage.Value == Language.Russian ? "Размер буфера отправки ZDO пакетов (vanilla = 10240 байт)" : "ZDO packet send buffer size (vanilla = 10240 bytes)", synced: false);
                
            c_FlushThresholdPercent_S = _clientConfig.BindConfig(serverSection, "FlushThresholdPercent", 0.2f, 
                c_ConfigLanguage.Value == Language.Russian ? 
                    "Процент от ZDOQueueLimit для активации flush (0.0-1.0)\n" + "0.1 = редкий flush (экономия трафика, но задержки)\n" + "0.3 = оптимальный баланс (рекомендуется)\n" + "0.5 = частый flush (меньше задержек, больше трафика)" :
                    "Percentage of ZDOQueueLimit for flush activation (0.0-1.0)\n" + "0.1 = rare flush (traffic saving, but delays)\n" + "0.3 = optimal balance (recommended)\n" + "0.5 = frequent flush (less delays, more traffic)", synced: false);
        }

        public ZPackage BuildConfigPackage()
        {
            ZPackage pkg = new ZPackage();
            try
            {
                pkg.Write(c_ModEnabled.Value);
                pkg.Write(c_ModuleSteamOptimizations.Value);
                pkg.Write(c_ModuleZDOOptimization.Value);
                pkg.Write(c_ModuleShipSync.Value);
                pkg.Write(c_ModuleZSyncTransformOptimization.Value);
                
                pkg.Write(c_SteamSendRateMaxKB.Value);
                pkg.Write(c_SteamSendBufferSizeKB.Value);
                pkg.Write(c_SteamTimeoutConnected.Value);
                pkg.Write(c_SteamTimeoutKeepalive.Value);
                pkg.Write(c_SteamRecvMaxMessageSize.Value);
                
                pkg.Write(c_ZDOQueueLimit.Value);
                
                pkg.Write(c_SmoothPosition.Value);
                pkg.Write(c_SmoothRotation.Value);
                pkg.Write(c_MicroThreshold.Value);
                pkg.Write(c_ClientDistanceThreshold.Value);
                pkg.Write(c_TeleportDistanceThreshold.Value);
                pkg.Write(c_TeleportRotationThreshold.Value);
                
                pkg.Write(c_MapPositionSendInterval.Value);
                pkg.Write(c_MapInterpolationDelay.Value);
                pkg.Write(c_MapMaxPredictionSpeed.Value);
                pkg.Write(c_MapMaxPredictionTime.Value);
                pkg.Write(c_MapTeleportThreshold.Value);
            }
            catch (Exception e)
            {
                Helper.LogDebug($"Error building config package: {e.Message}");
                return new ZPackage();
            }
            return pkg;
        }

        private void ApplyConfigFromPackage(ZPackage pkg)
        {
            if (pkg == null || pkg.GetArray().Length == 0)
            {
                Helper.LogDebug("Received empty config package");
                return;
            }

            try
            {
                pkg.SetPos(0);

                c_ModEnabled.Value = pkg.ReadBool();
                c_ModuleSteamOptimizations.Value = pkg.ReadBool();
                c_ModuleZDOOptimization.Value = pkg.ReadBool();
                c_ModuleShipSync.Value = pkg.ReadBool();
                c_ModuleZSyncTransformOptimization.Value = pkg.ReadBool();
        
                c_SteamSendRateMaxKB.Value = pkg.ReadInt();
                c_SteamSendBufferSizeKB.Value = pkg.ReadInt();
                c_SteamTimeoutConnected.Value = pkg.ReadSingle();
                c_SteamTimeoutKeepalive.Value = pkg.ReadSingle();
                c_SteamRecvMaxMessageSize.Value = pkg.ReadInt();
                
                c_ZDOQueueLimit.Value = pkg.ReadInt();
                
                c_SmoothPosition.Value = pkg.ReadSingle();
                c_SmoothRotation.Value = pkg.ReadSingle();
                c_MicroThreshold.Value = pkg.ReadSingle();
                c_ClientDistanceThreshold.Value = pkg.ReadSingle();
                c_TeleportDistanceThreshold.Value = pkg.ReadSingle();
                c_TeleportRotationThreshold.Value = pkg.ReadSingle();
                
                c_MapPositionSendInterval.Value = pkg.ReadSingle();
                c_MapInterpolationDelay.Value = pkg.ReadSingle();
                c_MapMaxPredictionSpeed.Value = pkg.ReadSingle();
                c_MapMaxPredictionTime.Value = pkg.ReadSingle();
                c_MapTeleportThreshold.Value = pkg.ReadSingle();
            }
            catch (Exception e)
            {
                Helper.LogDebug($"Error applying config package: {e.Message}");
            }
        }

        private IEnumerator OnAdminConfigSync(long sender, ZPackage pkg)
        {
            if (Helper.IsServer())
            {
                ZPackage serverConfigPkg = BuildConfigPackage();
                byte[] data = serverConfigPkg.GetArray();

                foreach (var peer in ZNet.instance.GetPeers())
                {
                    ZPackage copyPkg = new ZPackage(data);
                    _configSyncRPC.SendPackage(new List<ZNetPeer> { peer }, copyPkg);
                }
                Helper.LogDebug("Server config broadcast to all clients");
            }

            yield break;
        }

        public IEnumerator OnClientConfigSync(long sender, ZPackage pkg)
        {
            Helper.LogDebug($"Клиент получил конфиг от сервера {sender}");

            ApplyConfigFromPackage(pkg);
            
            _clientConfig.SetSaveOnConfigSet(true);
            _clientConfig.Save();
            
            yield break;
        }

        private void CreateConfigWatcher()
        {
            ConfigFileWatcher GeneralConfigWatcher = new ConfigFileWatcher(_clientConfig, reloadDelay: 1000);
            GeneralConfigWatcher.OnConfigFileReloaded += () =>
            {
                if (!Helper.IsServer()) return;

                Helper.LogDebug("Server config changed, broadcasting to all clients");
                StartCoroutine(ApplyServerConfigChanges());
            };
        }

        public IEnumerator ApplyServerConfigChanges()
        {
            yield return null;
    
            ZPackage pkg = BuildConfigPackage();
            if (pkg.GetArray().Length > 0)
            {
                byte[] data = pkg.GetArray();
                foreach (var peer in ZNet.instance.GetPeers())
                {
                    ZPackage copyPkg = new ZPackage(data);
                    _configSyncRPC.SendPackage(new List<ZNetPeer> { peer }, copyPkg);
                }
                Helper.LogDebug("Server config broadcast to all clients");
            }
        }

        private void OnDestroy()
        {
            _clientConfig?.Save();
            _harmony?.UnpatchSelf();
        }
    }
}
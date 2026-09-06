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
    [BepInIncompatibility("r4v9n1.terramizerserver")]
    [BepInIncompatibility("com.bfv.CombatPrio")]
    
    public class VBNetTweaks : BaseUnityPlugin
    {
        private const string ModName = "VBNetTweaks";
        private const string ModVersion = "0.4.1.21";
        private const string ModGUID = "VitByr.VBNetTweaks";
        public static VBNetTweaks Instance { get; private set; }
        public CustomRPC _configSyncRPC;
        private ConfigFile _clientConfig;
        public new static ManualLogSource Logger;
        
        public static ConfigEntry<Language> c_ConfigLanguage;
        public static ConfigEntry<bool> c_ModEnabled;

        public static ConfigEntry<bool> c_DebugEnabled;
        public static ConfigEntry<bool> c_VerboseLogging;
        public static ConfigEntry<bool> c_NetStatsLogging;
        public static ConfigEntry<int> c_NetStatsInterval;
        public static ConfigEntry<int> c_TargetFPS;

        public static ConfigEntry<bool> c_ModuleSteamOptimizations;
        public static ConfigEntry<bool> c_ModuleZDOOptimization;
        public static ConfigEntry<bool> c_ModuleShipSync;
        public static ConfigEntry<bool> c_ModuleMapPositionSync;
        public static ConfigEntry<bool> c_ModuleRevisionOptimization;
        
        
        public static ConfigEntry<int> c_SteamSendRateMaxKB;
        public static ConfigEntry<int> c_SteamSendBufferSizeKB;
        public static ConfigEntry<float> c_SteamTimeoutConnected;
        public static ConfigEntry<int> c_ZDOQueueLimit;
        
        public static ConfigEntry<float> c_MapPositionSendInterval;
        public static ConfigEntry<float> c_MapMaxPredictionSpeed;
        
        public static ConfigEntry<float> c_SendInterval;
        public static ConfigEntry<int> c_PeerCycleDivisor;
        
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger; 
            _clientConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, "VitByr/VBNetTweaks/MainConfig.cfg"), true);
            SynchronizationManager.Instance.RegisterCustomConfig(_clientConfig);
            
            InitClientConfigs();

            c_ModEnabled = _clientConfig.BindConfig("00 - Master", "ModEnabled", true, c_ConfigLanguage.Value == Language.Russian ? "Полностью включить/выключить мод VBNetTweaks" : "Completely enable/disable VBNetTweaks mod", synced: true);
            c_TargetFPS = _clientConfig.BindConfig("00 - Master", "TargetFPS", 120, c_ConfigLanguage.Value == Language.Russian ? "Целевой FPS для сервера (только для выделенного сервера)" : "Target FPS for server (dedicated only)", synced: false);
            
            if (!c_ModEnabled.Value) return;

            _configSyncRPC = NetworkManager.Instance.AddRPC("VBNetTweaks_ConfigSync", OnAdminConfigSync, OnClientConfigSync);
            SynchronizationManager.Instance.AddInitialSynchronization(_configSyncRPC, () => BuildConfigPackage());

            CreateConfigWatcher();

            _harmony = new Harmony(ModGUID);
            
            _harmony.PatchAll(typeof(MiniMap_Patch));
            _harmony.PatchAll(typeof(NetStats_Patch));
            _harmony.PatchAll(typeof(Ship_Patch));
            _harmony.PatchAll(typeof(ZDOMan_Patch));
            _harmony.PatchAll(typeof(ZNet_Patch));
            _harmony.PatchAll(typeof(ZSteamSocket_Patch));

            Helper.LogDebug("Режим отладки включен");
        }

        private void InitClientConfigs()
        {
            var languageSection = "00 - Language";
            c_ConfigLanguage = Config.Bind(languageSection, "Language", Language.Russian, new ConfigDescription("Select interface language / Выберите язык интерфейса\nRequired Restart / Требуется рестарт"));

            var debugSection = "01 - Debug";
            c_DebugEnabled = Config.Bind(debugSection, "DebugEnabled", false, c_ConfigLanguage.Value == Language.Russian ? "Включить отладочный вывод" : "Enable debug output");
            c_VerboseLogging = Config.Bind(debugSection, "VerboseLogging", false, c_ConfigLanguage.Value == Language.Russian ? "Включить подробное логирование" : "Enable verbose logging");
            c_NetStatsLogging = Config.Bind(debugSection, "NetStatsLogging", true, c_ConfigLanguage.Value == Language.Russian
                ? "Логирование статистики отправки/приёма пакетов (раз в интервал)." : "Log send/receive packet statistics (once per interval).");
            c_NetStatsInterval = Config.Bind(debugSection, "NetStatsIntervalSec", 10, c_ConfigLanguage.Value == Language.Russian ? "Интервал дампа статистики, сек." : "Statistics dump interval, sec.");


            var modulesSection = "02 - Modules";
            c_ModuleSteamOptimizations = _clientConfig.BindConfig(modulesSection, "SteamOptimizations", true, c_ConfigLanguage.Value == Language.Russian ? "Оптимизации Steam сокета" : "Steam socket optimizations", synced: true);
            c_ModuleZDOOptimization = _clientConfig.BindConfig(modulesSection, "ZDOOptimization", true, c_ConfigLanguage.Value == Language.Russian ? "Оптимизация ZDO отправок" : "Optimization ZDO sender", synced: true);
            c_ModuleShipSync = _clientConfig.BindConfig(modulesSection, "ShipSync", true, c_ConfigLanguage.Value == Language.Russian ? "Синхронизация на кораблях" : "On ship synchronization", synced: true);
            c_ModuleMapPositionSync = _clientConfig.BindConfig(modulesSection, "MapPositionSync", true, c_ConfigLanguage.Value == Language.Russian 
                ? "Включить плавные маркеры игроков на карте\nУлучшает отображение позиций игроков" : "Enable smooth player markers on map\nImproves display of player positions", synced: true);
            c_ModuleRevisionOptimization = _clientConfig.BindConfig(modulesSection, "RevisionOptimization", true, c_ConfigLanguage.Value == Language.Russian 
                ? "Оптимизация частоты обновления ZDO (снижает трафик)" : "Optimize ZDO update frequency (reduces traffic)", synced: true);
            
            var steamSection = "03 - Steam Settings";
            c_SteamSendRateMaxKB = _clientConfig.BindConfig(steamSection, "MaxRateKB", 2048, c_ConfigLanguage.Value == Language.Russian 
                ? "Максимальная скорость отправки Steam. Vanilla = ~150KB" : "Maximum Steam send rate. Vanilla = ~150KB", synced: true);

            c_SteamSendBufferSizeKB = _clientConfig.BindConfig(steamSection, "SendBufferSizeKB", 1024, c_ConfigLanguage.Value == Language.Russian
                ? "Размер буфера отправки Steam в KB. Vanilla = ~512KB" : "Steam send buffer size in KB. Vanilla = ~512KB", synced: true);

            c_SteamTimeoutConnected = _clientConfig.BindConfig(steamSection, "TimeoutConnected", 60000f, c_ConfigLanguage.Value == Language.Russian 
                ? "Таймаут соединения Steam (миллисекунды)\n" + "Если соединение неактивно дольше этого времени — оно будет разорвано\n" + "Vanilla = 30000 (30 секунд)"
                : "Steam connection timeout (milliseconds)\n" + "If connection is idle longer than this — it will be closed\n" + "Vanilla = 30000 (30 sec)", synced: true);
            
            
            var serverSection = "04 - ZDO Settings";
            c_SendInterval = _clientConfig.BindConfig(serverSection, "SendInterval", 0.04f, c_ConfigLanguage.Value == Language.Russian 
                ? "Интервал отправки данных. Vanilla = 0.05" : "Data send interval. Vanilla = 0.05", synced: true);
                
            c_PeerCycleDivisor = _clientConfig.BindConfig(serverSection, "PeerCycleDivisor", 3, c_ConfigLanguage.Value == Language.Russian 
                    ? "Делитель для расчёта пиров за цикл. Формула: ceil(всего_пиров / делитель).\n" + "Больше значение → больше циклов на полный оборот → реже обновление каждого пира.\n" + "Примеры: 2 = оборот за 2 цикла, 3 = за 3 цикла, 5 = за 5 циклов." 
                    : "Divisor for peers-per-cycle calculation. Formula: ceil(total_peers / divisor).\n" + "Higher value → more cycles per full rotation → less frequent updates per peer.\n" + "Examples: 2 = rotation in 2 cycles, 3 = in 3 cycles, 5 = in 5 cycles.", synced: true);
            
            c_ZDOQueueLimit = _clientConfig.BindConfig(serverSection, "ZDOQueueLimit", 30720, c_ConfigLanguage.Value == Language.Russian 
                ? "Размер буфера отправки ZDO пакетов (vanilla = 10240 байт). Требуется рестарт" : "ZDO packet send buffer size (vanilla = 10240 bytes). Required Restart", synced: true);
            
            
            var mapSection = "05 - Map Positions";
            c_MapPositionSendInterval = _clientConfig.BindConfig(mapSection, "SendInterval", 0.5f, c_ConfigLanguage.Value == Language.Russian
                ? "Интервал отправки позиций игроков (сек)\nМеньше = плавнее, но больше трафик. Vanilla: 2.0" 
                : "Player position send interval (sec)\nLower = smoother, but more traffic. Vanilla: 2.0", synced: true);

            c_MapMaxPredictionSpeed = _clientConfig.BindConfig(mapSection, "MaxPredictionSpeed", 50f, c_ConfigLanguage.Value == Language.Russian
                ? "Максимальная скорость движения маркера игрока на карте (м/с).\nТелепорты (порталы, спавн) обрабатываются отдельно – маркер перепрыгивает мгновенно." 
                : "Maximum movement speed of player marker on map (m/s).\nTeleports (portals, spawn) are handled separately – the marker jumps instantly.", synced: true);
            
            
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
                pkg.Write(c_ModuleMapPositionSync.Value);
                
                pkg.Write(c_SteamSendRateMaxKB.Value);
                pkg.Write(c_SteamSendBufferSizeKB.Value);
                pkg.Write(c_SteamTimeoutConnected.Value);
                
                pkg.Write(c_MapPositionSendInterval.Value);
                pkg.Write(c_MapMaxPredictionSpeed.Value);
                
                pkg.Write(c_SendInterval.Value);
                pkg.Write(c_PeerCycleDivisor.Value);
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
                c_ModuleMapPositionSync.Value = pkg.ReadBool();
                
                c_SteamSendRateMaxKB.Value = pkg.ReadInt();
                c_SteamSendBufferSizeKB.Value = pkg.ReadInt();
                c_SteamTimeoutConnected.Value = pkg.ReadSingle();
                
                c_MapPositionSendInterval.Value = pkg.ReadSingle();
                c_MapMaxPredictionSpeed.Value = pkg.ReadSingle();
                
                c_SendInterval.Value = pkg.ReadSingle();
                c_PeerCycleDivisor.Value = pkg.ReadInt();
            }
            catch (Exception e)
            {
                Helper.LogDebug($"Error applying config package: {e.Message}");
            }
        }

        private IEnumerator OnAdminConfigSync(long sender, ZPackage pkg)
        {
            if (Helper.IsDedicated())
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
                if (!Helper.IsDedicated()) return;

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
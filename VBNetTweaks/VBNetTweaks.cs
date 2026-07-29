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
        private const string ModVersion = "0.4.0.1";
        private const string ModGUID = "VitByr.VBNetTweaks";
        public static VBNetTweaks Instance { get; private set; }
        public CustomRPC _configSyncRPC;
        private ConfigFile _serverConfig;
        
        public static ConfigEntry<Language> ConfigLanguage;
        public static ConfigEntry<bool> ModEnabled;

        public static ConfigEntry<bool> DebugEnabled;
        public static ConfigEntry<bool> VerboseLogging;

        public static ConfigEntry<bool> ModuleSteamOptimizations;
        public static ConfigEntry<bool> ModuleShipSync;

        public static ConfigEntry<int> SteamSendRateMaxKB;
        public static ConfigEntry<int> SteamSendBufferSizeKB;

        public static ConfigEntry<float> SendInterval;
        public static ConfigEntry<int> PeersPerUpdate;
        public static ConfigEntry<int> ZDOQueueLimit;
        public static ConfigEntry<float> FlushThresholdPercent;
        
        public static ConfigEntry<float> SmoothPosition;
        public static ConfigEntry<float> SmoothRotation;
        public static ConfigEntry<float> MicroThreshold;
        public static ConfigEntry<float> ClientDistanceThreshold;
        
        private Harmony _harmony;

        private void Awake()
        {
            _serverConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, "VitByr/VBNetTweaks/ServerConfig.cfg"), true);
            SynchronizationManager.Instance.RegisterCustomConfig(_serverConfig);
            Instance = this;
            
            InitClientConfigs();

            ModEnabled = _serverConfig.BindConfig("00 - Master", "ModEnabled", true, VBNetTweaks.ConfigLanguage.Value == Language.Russian ? "Полностью включить/выключить мод VBNetTweaks": "Completely enable/disable VBNetTweaks mod", synced: true);
            
            if (!ModEnabled.Value) return;
            
            InitServerConfigs();

            _configSyncRPC = NetworkManager.Instance.AddRPC("VBNetTweaks_ConfigSync", OnAdminConfigSync, OnClientConfigSync);
            SynchronizationManager.Instance.AddInitialSynchronization(_configSyncRPC, () => BuildConfigPackage());
            
            CreateConfigWatcher();

            _harmony = new Harmony(ModGUID);

            if (ModuleSteamOptimizations.Value) _harmony.PatchAll(typeof(ZSteamSocket_Patchs));
            if (ModuleShipSync.Value)
            {
              _harmony.PatchAll(typeof(ShipSyncFix));
              _harmony.PatchAll(typeof(ShipWaterDamagePatch));
            }
            
            _harmony.PatchAll(typeof(ZNet_Paths));
            _harmony.PatchAll(typeof(NetworkSyncPatches));
            _harmony.PatchAll(typeof(ZDONetworkOptimizer));
            
            Helper.LogDebug("Режим отладки включен");
        }
        
        private void InitClientConfigs()
        {
            var languageSection = "00 - Language";
            ConfigLanguage = Config.Bind(languageSection, "Language", Language.Russian, new ConfigDescription("Select interface language / Выберите язык интерфейса\nRequired Restart / Требуется рестарт"));
            
            var debugSection = "01 - Debug";
            DebugEnabled = Config.Bind(debugSection, "DebugEnabled", false, VBNetTweaks.ConfigLanguage.Value == Language.Russian ? "Включить отладочный вывод" : "Enable debug output");
            VerboseLogging = Config.Bind(debugSection, "VerboseLogging", false, VBNetTweaks.ConfigLanguage.Value == Language.Russian ? "Включить подробное логирование" : "Enable verbose logging");
        }

        private void InitServerConfigs()
        {
            var modulesSection = "02 - Modules";
            ModuleSteamOptimizations = _serverConfig.BindConfig(modulesSection, "SteamOptimizations", true, VBNetTweaks.ConfigLanguage.Value == Language.Russian ? "Оптимизации Steam сокета" : "Steam socket optimizations", synced: true);
            ModuleShipSync = _serverConfig.BindConfig(modulesSection, "ShipSync", true, VBNetTweaks.ConfigLanguage.Value == Language.Russian ? "Синхронизация кораблей" : "Ship synchronization", synced: true);
        
            
            var steamSection = "03 - Steam Settings";
            SteamSendRateMaxKB = _serverConfig.BindConfig(steamSection, "MaxRateKB", 4096, 
                VBNetTweaks.ConfigLanguage.Value == Language.Russian ? "Максимальная скорость отправки Steam (vanilla = 150 KB/s)" : "Maximum Steam send rate (vanilla = 150 KB/s)", synced: true);
                
            SteamSendBufferSizeKB = _serverConfig.BindConfig(steamSection, "SendBufferSizeKB", 2048, 
                VBNetTweaks.ConfigLanguage.Value == Language.Russian ? "Размер буфера отправки Steam в KB (vanilla = ~260KB). Рекомендуется 1024-4096" : "Steam send buffer size in KB (vanilla = ~260KB). Recommended 1024-4096", synced: true);

            
            var serverSection = "04 - Server Settings";
            SendInterval = _serverConfig.BindConfig(serverSection, "SendInterval", 0.025f, 
                VBNetTweaks.ConfigLanguage.Value == Language.Russian ? "Интервал отправки данных (vanilla = 0.05)" : "Data send interval (vanilla = 0.05)", synced: true);
                
            PeersPerUpdate = _serverConfig.BindConfig(serverSection, "PeersPerUpdate", 50, 
                VBNetTweaks.ConfigLanguage.Value == Language.Russian ? "Количество пиров за один апдейт (vanilla = 1). Лучше ставить значение равное максимальному количеству слотов сервера." : "Peers per update (vanilla = 1). Better set equal to max server slots.", synced: true);
                
            ZDOQueueLimit = _serverConfig.BindConfig(serverSection, "ZDOQueueLimit", 30720, 
                VBNetTweaks.ConfigLanguage.Value == Language.Russian ? "Размер буфера отправки ZDO пакетов (vanilla = 10240 байт)" : "ZDO packet send buffer size (vanilla = 10240 bytes)", synced: true);
                
            FlushThresholdPercent = _serverConfig.BindConfig(serverSection, "FlushThresholdPercent", 0.35f, 
                VBNetTweaks.ConfigLanguage.Value == Language.Russian ? 
                    "Процент от ZDOQueueLimit для активации flush (0.0-1.0)\n" + "0.1 = редкий flush (экономия трафика, но задержки)\n" + "0.3 = оптимальный баланс (рекомендуется)\n" + "0.5 = частый flush (меньше задержек, больше трафика)" :
                    "Percentage of ZDOQueueLimit for flush activation (0.0-1.0)\n" + "0.1 = rare flush (traffic saving, but delays)\n" + "0.3 = optimal balance (recommended)\n" + "0.5 = frequent flush (less delays, more traffic)", synced: true);
            
            
            var transformSection = "05 - Transform Settings";
            SmoothPosition = _serverConfig.BindConfig(transformSection, "SmoothPosition", 0.15f,
                ConfigLanguage.Value == Language.Russian ? "Сглаживание позиции (выше = плавнее, но больше задержка) (vanilla: 0.20)" : "Position smoothing value (vanilla: 0.20)", synced: true);

            SmoothRotation = _serverConfig.BindConfig(transformSection, "SmoothRotation", 0.35f,
                ConfigLanguage.Value == Language.Russian ? "Значение сглаживания поворота (vanilla: 0.50)" : "Rotation smoothing value (vanilla: 0.50)", synced: true);

            MicroThreshold = _serverConfig.BindConfig(transformSection, "MicroThreshold", 0.002f,
                ConfigLanguage.Value == Language.Russian ? "Порог микро-движений (выше = меньше обновлений) (vanilla: 0.001)" : "Micro-movement threshold (vanilla: 0.001)", synced: true);

            ClientDistanceThreshold = _serverConfig.BindConfig(transformSection, "ClientDistanceThreshold", 0.005f,
                ConfigLanguage.Value == Language.Russian ? "Порог дистанции для клиентской синхронизации (vanilla: 0.01)" : "Client distance threshold (vanilla: 0.01)", synced: true);
        }

        public ZPackage BuildConfigPackage()
        {
            ZPackage pkg = new ZPackage();
            try
            {
                pkg.Write(ModEnabled.Value);
                pkg.Write(ModuleSteamOptimizations.Value);
                pkg.Write(ModuleShipSync.Value);
                
                pkg.Write(SteamSendRateMaxKB.Value);
                pkg.Write(SteamSendBufferSizeKB.Value);
                
                pkg.Write(SendInterval.Value);
                pkg.Write(PeersPerUpdate.Value);
                pkg.Write(ZDOQueueLimit.Value);
                pkg.Write(FlushThresholdPercent.Value);
                
                pkg.Write(SmoothPosition.Value);
                pkg.Write(SmoothRotation.Value);
                pkg.Write(MicroThreshold.Value);
                pkg.Write(ClientDistanceThreshold.Value);
            }
            catch (Exception e)
            {
                Helper.LogDebug($"[VBNetTweaks] Error building config package: {e.Message}");
                return new ZPackage();
            }
            return pkg;
        }

        private void ApplyConfigFromPackage(ZPackage pkg)
        {
            if (pkg == null || pkg.GetArray().Length == 0)
            {
                Helper.LogDebug("[VBNetTweaks] Received empty config package");
                return;
            }

            try
            {
                pkg.SetPos(0);

                ModEnabled.Value = pkg.ReadBool();
                ModuleSteamOptimizations.Value = pkg.ReadBool();
                ModuleShipSync.Value = pkg.ReadBool();
        
                SteamSendRateMaxKB.Value = pkg.ReadInt();
                SteamSendBufferSizeKB.Value = pkg.ReadInt();
                
                SendInterval.Value = pkg.ReadSingle();
                PeersPerUpdate.Value = pkg.ReadInt();
                ZDOQueueLimit.Value = pkg.ReadInt();
                FlushThresholdPercent.Value = pkg.ReadSingle();
                
                SmoothPosition.Value = pkg.ReadSingle();
                SmoothRotation.Value = pkg.ReadSingle();
                MicroThreshold.Value = pkg.ReadSingle();
                ClientDistanceThreshold.Value = pkg.ReadSingle();
            }
            catch (Exception e)
            {
                Helper.LogDebug($"[VBNetTweaks] Error applying config package: {e.Message}");
            }
        }

        private IEnumerator OnAdminConfigSync(long sender, ZPackage pkg)
        {
            if (ZNet.instance && ZNet.instance.IsServer())
            {
                ZPackage serverConfigPkg = BuildConfigPackage();
                byte[] data = serverConfigPkg.GetArray();

                foreach (var peer in ZNet.instance.GetPeers())
                {
                    ZPackage copyPkg = new ZPackage(data);
                    _configSyncRPC.SendPackage(new List<ZNetPeer> { peer }, copyPkg);
                }

                Helper.LogDebug("[VBNetTweaks] Server config broadcast to all clients");
            }

            yield break;
        }

        public IEnumerator OnClientConfigSync(long sender, ZPackage pkg)
        {
            Helper.LogDebug($"[VBNetTweaks] Клиент получил конфиг от сервера {sender}");

            ApplyConfigFromPackage(pkg);
            
            _serverConfig.SetSaveOnConfigSet(true);
            _serverConfig.Save();
            
            yield break;
        }

        private void CreateConfigWatcher()
        {
            ConfigFileWatcher adminConfigWatcher = new ConfigFileWatcher(_serverConfig, reloadDelay: 1000);
            adminConfigWatcher.OnConfigFileReloaded += () =>
            {
                if (!ZNet.instance || !ZNet.instance.IsServer()) return;

                Helper.LogDebug("[VBNetTweaks] Server config changed, broadcasting to all clients");
                StartCoroutine(ApplyServerConfigChanges());
            };
        }

        public IEnumerator ApplyServerConfigChanges()
        {
            yield return null;
    
            if (SendInterval.Value <= 0.001f) SendInterval.Value = 0.03f;
            if (PeersPerUpdate.Value <= 0) PeersPerUpdate.Value = 30;
        
            ZPackage pkg = BuildConfigPackage();
            if (pkg.GetArray().Length > 0)
            {
                byte[] data = pkg.GetArray();
                foreach (var peer in ZNet.instance.GetPeers())
                {
                    ZPackage copyPkg = new ZPackage(data);
                    _configSyncRPC.SendPackage(new List<ZNetPeer> { peer }, copyPkg);
                }
                Helper.LogDebug("[VBNetTweaks] Server config broadcast to all clients");
            }
        }

        private void OnDestroy()
        {
            _serverConfig.Save();
            _harmony.UnpatchSelf();
        }
    }
}
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
    public class VBNetTweaks : BaseUnityPlugin
    {
        private const string ModName = "VBNetTweaks";
        private const string ModVersion = "0.3.1";
        private const string ModGUID = "VitByr.VBNetTweaks";
        public static VBNetTweaks Instance { get; private set; }
        public CustomRPC _configSyncRPC;
        private ConfigFile _serverConfig;

        public static ConfigEntry<bool> ModEnabled;

        public static ConfigEntry<bool> DebugEnabled;
        public static ConfigEntry<bool> VerboseLogging;

        public static ConfigEntry<bool> ModuleSteamOptimizations;
        public static ConfigEntry<bool> ModuleShipSync;
        public static ConfigEntry<bool> ModuleCompression;
     //   public static ConfigEntry<bool> EnableClientCompression;

        public static ConfigEntry<int> SteamSendRateMaxKB;
      //  public static ConfigEntry<int> SteamSendBufferSizeKB;

        public static ConfigEntry<int> CompressionLevel;
        public static ConfigEntry<int> CompressionMinSize;
        public static ConfigEntry<float> CompressionMinRatio;

        public static ConfigEntry<float> SendInterval;
        public static ConfigEntry<int> PeersPerUpdate;
        public static ConfigEntry<int> ZDOQueueLimit;

        private Harmony _harmony;

        private void Awake()
        {
            _serverConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, "VitByr/VBNetTweaks/ServerConfig.cfg"), true);
            SynchronizationManager.Instance.RegisterCustomConfig(_serverConfig);
            Instance = this;
            ModEnabled = _serverConfig.BindConfig("00 - Master", "ModEnabled", true, "Полностью включить/выключить мод VBNetTweaks", synced: true);
            if (!ModEnabled.Value) return;

            InitClientConfigs();
            InitServerConfigs();

            _configSyncRPC = NetworkManager.Instance.AddRPC("VBNetTweaks_ConfigSync", OnAdminConfigSync, OnClientConfigSync);
            SynchronizationManager.Instance.AddInitialSynchronization(_configSyncRPC, () => BuildConfigPackage());
            
            CreateConfigWatcher();

            _harmony = new Harmony(ModGUID);

            StartCoroutine(DelayedInit());
            if (ModuleSteamOptimizations.Value)
            {
                _harmony.PatchAll(typeof(ZSteamSocket_Patchs));
            }

            if (ModuleShipSync.Value) _harmony.PatchAll(typeof(ShipSyncSystem));

            _harmony.PatchAll(typeof(PlayerCache));
            _harmony.PatchAll(typeof(ZNet_Paths));
            _harmony.PatchAll(typeof(NetworkSyncPatches));
            _harmony.PatchAll(typeof(ZDONetworkOptimizer));

            Logger.LogInfo("VBNetTweaks загружен!");
            if (DebugEnabled.Value) Logger.LogInfo("Режим отладки включен");
        }

        private IEnumerator DelayedInit()
        {
            yield return new WaitForSeconds(2f);
            if (ModuleCompression.Value)
            {
                ZDONetworkOptimizer.Initialize();
                InvokeRepeating("CheckCompressionStatus", 5f, 30f);
            }
        }

        private void InitClientConfigs()
        {
            var debugSection = "01 - Debug";
            DebugEnabled = Config.Bind(debugSection, "DebugEnabled", false, "Включить отладочный вывод");
            VerboseLogging = Config.Bind(debugSection, "VerboseLogging", false, "Включить подробное логирование");
        }

        private void InitServerConfigs()
        {
            var modulesSection = "02 - Modules";
            ModuleSteamOptimizations = _serverConfig.BindConfig(modulesSection, "SteamOptimizations", true, "Оптимизации Steam сокета", synced: true);
            ModuleShipSync = _serverConfig.BindConfig(modulesSection, "ShipSync", true, "Синхронизация кораблей", synced: true);
            ModuleCompression = _serverConfig.BindConfig(modulesSection, "Compression", defaultValue: true, "Сжатие сетевого трафика на сервере");
 
            var steamSection = "04 - Steam Settings";
            SteamSendRateMaxKB = _serverConfig.BindConfig(steamSection, "MaxRateKB", 4096, "Максимальная скорость отправки Steam (vanilla = 150 KB/s)", acceptableValues: new AcceptableValueRange<int>(256, 10240), synced: true);
           // SteamSendBufferSizeKB = _serverConfig.BindConfig(steamSection, "SendBufferSizeKB", 2048, "Размер буфера отправки Steam в KB (vanilla = ~260KB). Рекомендуется 1024-4096", acceptableValues: new AcceptableValueRange<int>(512, 8192), synced: true);

            var compress = "05 - Compress Settings";
            CompressionLevel = _serverConfig.BindConfig(compress, "Level", 3, "Уровень сжатия Deflate: 1 = Fastest, 9 = Smallest", synced: true, null, new AcceptableValueRange<int>(1, 9));
            CompressionMinSize = _serverConfig.BindConfig(compress, "MinSizeBytes", 1024, "Минимальный размер пакета для сжатия. Пакеты меньше не сжимаются (экономия CPU). Рекомендуется 1024-2048.", synced: true, null, new AcceptableValueRange<int>(64, 8192));
            CompressionMinRatio = _serverConfig.BindConfig(compress, "MinUsefulRatio", 0.75f, "Минимальный полезный коэффициент сжатия (0.5-1.0). При 0.85 — сжатие применяется только если результат < 85% от оригинала.", synced: true, null, new AcceptableValueRange<float>(0.5f, 1f));
            
            var serverSection = "06 - Server Settings";
            SendInterval = _serverConfig.BindConfig(serverSection, "SendInterval", 0.03f, "Интервал отправки данных (vanilla = 0.05)", acceptableValues: new AcceptableValueRange<float>(0.01f, 0.5f), synced: true);
            PeersPerUpdate = _serverConfig.BindConfig(serverSection, "PeersPerUpdate", 30, "Количество пиров за один апдейт (vanilla = 1)", acceptableValues: new AcceptableValueRange<int>(1, 200), synced: true);
            ZDOQueueLimit = _serverConfig.BindConfig(serverSection, "ZDOQueueLimit", 20480, "Размер буфера отправки ZDO пакетов (vanilla = 10240 Kb)", synced: true);
        }

        public ZPackage BuildConfigPackage()
        {
            ZPackage pkg = new ZPackage();
            try
            {
                pkg.Write(ModEnabled.Value);
                pkg.Write(ModuleSteamOptimizations.Value);
                pkg.Write(ModuleShipSync.Value);
                pkg.Write(ModuleCompression.Value);
                pkg.Write(CompressionLevel.Value);
                pkg.Write(CompressionMinSize.Value);
                pkg.Write(CompressionMinRatio.Value);
                pkg.Write(SendInterval.Value);
                pkg.Write(PeersPerUpdate.Value);
                pkg.Write(ZDOQueueLimit.Value);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VBNetTweaks] Error building config package: {e.Message}");
                return new ZPackage();
            }
            return pkg;
        }

        private void ApplyConfigFromPackage(ZPackage pkg)
        {
            if (pkg == null || pkg.GetArray().Length == 0)
            {
                Debug.LogWarning("[VBNetTweaks] Received empty config package");
                return;
            }

            try
            {
                pkg.SetPos(0);

                ModEnabled.Value = pkg.ReadBool();
                ModuleSteamOptimizations.Value = pkg.ReadBool();
                ModuleShipSync.Value = pkg.ReadBool();
        
                // Читаем настройки компрессии (если они есть в пакете)
                if (pkg.Size() - pkg.GetPos() >= 1)
                {
                    ModuleCompression.Value = pkg.ReadBool();
                }
                if (pkg.Size() - pkg.GetPos() >= 12) // int + int + float
                {
                    CompressionLevel.Value = pkg.ReadInt();
                    CompressionMinSize.Value = pkg.ReadInt();
                    CompressionMinRatio.Value = pkg.ReadSingle();
                }
        
                SendInterval.Value = pkg.ReadSingle();
                PeersPerUpdate.Value = pkg.ReadInt();
                ZDOQueueLimit.Value = pkg.ReadInt();
                
                if (ModuleCompression.Value)
                {
                    ZDONetworkOptimizer.ReinitializeCompressor();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[VBNetTweaks] Error applying config package: {e.Message}");
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

                Logger.LogInfo("[VBNetTweaks] Server config broadcast to all clients");
            }

            yield break;
        }

        public IEnumerator OnClientConfigSync(long sender, ZPackage pkg)
        {
            Logger.LogInfo($"[VBNetTweaks] Клиент получил конфиг от сервера {sender}");

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

                Debug.Log("[VBNetTweaks] Server config changed, broadcasting to all clients");
                StartCoroutine(ApplyServerConfigChanges());
            };
        }

        public IEnumerator ApplyServerConfigChanges()
        {
            yield return null;
    
            if (SendInterval.Value <= 0.001f) SendInterval.Value = 0.03f;
            if (PeersPerUpdate.Value <= 0) PeersPerUpdate.Value = 30;
            if (CompressionLevel.Value < 1 || CompressionLevel.Value > 9) CompressionLevel.Value = 3;
            if (CompressionMinSize.Value < 64) CompressionMinSize.Value = 1024;
            if (CompressionMinRatio.Value < 0.5f || CompressionMinRatio.Value > 1f) CompressionMinRatio.Value = 0.85f;
    
            ZPackage pkg = BuildConfigPackage();
            if (pkg.GetArray().Length > 0)
            {
                byte[] data = pkg.GetArray();
                foreach (var peer in ZNet.instance.GetPeers())
                {
                    ZPackage copyPkg = new ZPackage(data);
                    _configSyncRPC.SendPackage(new List<ZNetPeer> { peer }, copyPkg);
                }
                Logger.LogInfo("[VBNetTweaks] Server config broadcast to all clients");
            }
    
            if (ModuleCompression.Value)
            {
                ZDONetworkOptimizer.ReinitializeCompressor();
            }
        }

        private void OnDestroy()
        {
            _serverConfig.Save();
            _harmony.UnpatchSelf();
        }
    }
}
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
        private const string ModVersion = "0.2.6";
        private const string ModGUID = "VitByr.VBNetTweaks";

        private CustomRPC _configSyncRPC;
        private ConfigFile _serverConfig;

        public static ConfigEntry<bool> ModEnabled;

        public static ConfigEntry<bool> DebugEnabled;
        public static ConfigEntry<bool> VerboseLogging;

        public static ConfigEntry<bool> ModuleCompression;
        public static ConfigEntry<bool> EnableClientCompression;
        public static ConfigEntry<bool> ModuleSteamOptimizations;
        public static ConfigEntry<bool> ModuleShipSync;

        public static ConfigEntry<CompressionAlgorithm> m_CompressionAlgorithm;
        public static ConfigEntry<int> CompressionLevel;
        public static ConfigEntry<int> SteamSendRateMinKB;
        public static ConfigEntry<int> SteamSendRateMaxKB;
        public static ConfigEntry<int> SteamSendBufferSize;

        public static ConfigEntry<float> SendInterval;
        public static ConfigEntry<int> PeersPerUpdate;
        public static ConfigEntry<int> ZDOQueueLimit;

        private Harmony _harmony;

        private void Awake()
        {
            _serverConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, "VitByr/VBNetTweaks/ServerConfig.cfg"), true);
            SynchronizationManager.Instance.RegisterCustomConfig(_serverConfig);

            ModEnabled = _serverConfig.BindConfig("00 - Master", "ModEnabled", true, "Полностью включить/выключить мод VBNetTweaks", synced: true);
            if (!ModEnabled.Value) return;

            InitClientConfigs();
            InitServerConfigs();

            _configSyncRPC = NetworkManager.Instance.AddRPC("VBNetTweaks_ConfigSync", OnAdminConfigSync, OnClientConfigSync);

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
            _harmony.PatchAll(typeof(StatusEffectVFXFix));
            _harmony.PatchAll(typeof(NetworkSyncPatches));
            _harmony.PatchAll(typeof(ZDONetworkOptimizer));

            Logger.LogInfo("VBNetTweaks загружен!");
            if (DebugEnabled.Value) Logger.LogInfo("Режим отладки включен");
        }

        private void Start()
        {
            StartCoroutine(WaitForLocalPlayer());
        }
        
        private void Update()
        {
            if (Time.frameCount % 90 == 0) StatusEffectVFXManager.Maintenance();
        }
        
        private IEnumerator DelayedInit()
        {
            yield return new WaitForSeconds(2f);
    
            if (ModuleCompression.Value)
            {
                ZDONetworkOptimizer.Initialize();
                InvokeRepeating(nameof(Helper.CheckCompressionStatus), 5f, 30f);
            }
        }
        
        private void InitClientConfigs()
        {
            var debugSection = "01 - Debug";
            DebugEnabled = Config.Bind(debugSection, "DebugEnabled", false, "Включить отладочный вывод");
            VerboseLogging = Config.Bind(debugSection, "VerboseLogging", false, "Включить подробное логирование");

            var modulesSection = "02 - Modules";
            EnableClientCompression = Config.Bind(modulesSection, "ClientCompression", true, "Сжимать данные на клиенте (может вызывать проблемы с визуальными эффектами)");
        }

        private void InitServerConfigs()
        {
            var modulesSection = "02 - Modules";
            ModuleSteamOptimizations = _serverConfig.BindConfig(modulesSection, "SteamOptimizations", true, "Оптимизации Steam сокета", synced: true);
            ModuleShipSync = _serverConfig.BindConfig(modulesSection, "ShipSync", true, "Синхронизация кораблей", synced: true);
            ModuleCompression = _serverConfig.BindConfig(modulesSection, "Compression", true, "Сжатие сетевого трафика на сервере", synced: true);

            var compressionSection = "03 - Compression Settings";
            m_CompressionAlgorithm = _serverConfig.BindConfig(compressionSection, "Algorithm", CompressionAlgorithm.Vanilla, "Алгоритм сжатия: Deflate, Vanilla (встроенная компрессия игры)", synced: true);
            CompressionLevel = _serverConfig.BindConfig(compressionSection, "Level", 3, "Уровень сжатия (1-9 для Deflate)", acceptableValues: new AcceptableValueRange<int>(1, 9), synced: true);

            var steamSection = "04 - Steam Settings";
            SteamSendRateMinKB = _serverConfig.BindConfig(steamSection, "MinRateKB", 256, "Минимальная скорость Steam (vanilla = 150 Kb/s)", synced: true);
            SteamSendRateMaxKB = _serverConfig.BindConfig(steamSection, "MaxRateKB", 4096, "Максимальная скорость Steam (vanilla = 150 Kb/s)", synced: true);
            SteamSendBufferSize = _serverConfig.BindConfig(steamSection, "BufferSize", 100_000_000, "Размер буфера Steam  (vanilla = 260000 B)", synced: true);

            var serverSection = "05 - Server Settings";
            SendInterval = _serverConfig.BindConfig(serverSection, "SendInterval", 0.03f, "Интервал отправки данных (vanilla = 0.05)", acceptableValues: new AcceptableValueRange<float>(0.01f, 0.5f), synced: true);

            PeersPerUpdate = _serverConfig.BindConfig(serverSection, "PeersPerUpdate", 30, "Количество пиров за один апдейт (vanilla = 1)", acceptableValues: new AcceptableValueRange<int>(1, 200), synced: true);
            ZDOQueueLimit = _serverConfig.BindConfig(serverSection, "ZDOQueueLimit", 20480, "Размер буфера отправки ZDO пакетов (vanilla = 10240 Kb)", synced: true);
        }
        
        private IEnumerator WaitForLocalPlayer()
        {
            while (!Player.m_localPlayer) yield return null;

            Logger.LogInfo("Локальный игрок найден - клиент подключен");

            if (!ZNet.instance.IsServer()) _serverConfig.Reload();
            else
            {
                _serverConfig.Reload();
                Logger.LogInfo("Сервер запущен, админ-конфиг загружен");
            }
        }

        private ZPackage BuildConfigPackage()
        {
            ZPackage pkg = new ZPackage();
            try
            {
                pkg.Write(1);
        
                pkg.Write(ModEnabled.Value);
                pkg.Write(ModuleSteamOptimizations.Value);
                pkg.Write(ModuleShipSync.Value);
                pkg.Write(ModuleCompression.Value);
                pkg.Write((int)m_CompressionAlgorithm.Value);
                pkg.Write(CompressionLevel.Value);
                pkg.Write(SendInterval.Value);
                pkg.Write(PeersPerUpdate.Value);
                pkg.Write(ZDOQueueLimit.Value);
                pkg.Write(EnableClientCompression.Value);
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
        
                int version = pkg.ReadInt();
                if (version != 1)
                {
                    Debug.LogWarning($"[VBNetTweaks] Unknown config version {version}, skipping");
                    return;
                }

                ModEnabled.Value = pkg.ReadBool();
                ModuleSteamOptimizations.Value = pkg.ReadBool();
                ModuleShipSync.Value = pkg.ReadBool();
                ModuleCompression.Value = pkg.ReadBool();
                m_CompressionAlgorithm.Value = (CompressionAlgorithm)pkg.ReadInt();
                CompressionLevel.Value = pkg.ReadInt();
                SendInterval.Value = pkg.ReadSingle();
                PeersPerUpdate.Value = pkg.ReadInt();
                ZDOQueueLimit.Value = pkg.ReadInt();
                EnableClientCompression.Value = pkg.ReadBool();
        
                Debug.Log($"[VBNetTweaks] Config applied: Algorithm={m_CompressionAlgorithm.Value}, Level={CompressionLevel.Value}, Interval={SendInterval.Value}, Peers={PeersPerUpdate.Value}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VBNetTweaks] Error applying config package: {e.Message}");
            }
        }

        private IEnumerator OnAdminConfigSync(long sender, ZPackage pkg)
        {
            Logger.LogInfo($"[VBNetTweaks] Server received config sync request from {sender}");
    
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
            Debug.Log($"[VBNetTweaks] Client received config from server {sender}");
            ApplyConfigFromPackage(pkg);
            yield break;
        }

        private void CreateConfigWatcher()
        {
            ConfigFileWatcher configFileWatcher = new ConfigFileWatcher(Config, reloadDelay: 1000);
            configFileWatcher.OnConfigFileReloaded += () =>
            {
                if (ZNet.instance)
                {
                    StartCoroutine(ApplyClientConfigChanges());
                }
            };

            ConfigFileWatcher adminConfigWatcher = new ConfigFileWatcher(_serverConfig, reloadDelay: 1000);
            adminConfigWatcher.OnConfigFileReloaded += () =>
            {
                if (!ZNet.instance || !ZNet.instance.IsServer()) return;

                StartCoroutine(ApplyServerConfigChanges());
            };
        }
        

        private IEnumerator ApplyClientConfigChanges()
        {
            yield return null;
        }
        
        private IEnumerator ApplyServerConfigChanges()
        {
            yield return null;

            _serverConfig.Reload();
    
            Logger.LogInfo($"[VBNetTweaks] Server config reloaded: Alg={m_CompressionAlgorithm.Value}, Level={CompressionLevel.Value}, Interval={SendInterval.Value}, Peers={PeersPerUpdate.Value}");
    
            if (SendInterval.Value <= 0.001f)
            {
                Logger.LogWarning("[VBNetTweaks] SendInterval was 0, resetting to default 0.03f");
                SendInterval.Value = 0.03f;
            }
    
            if (PeersPerUpdate.Value <= 0)
            {
                Logger.LogWarning("[VBNetTweaks] PeersPerUpdate was 0, resetting to default 30");
                PeersPerUpdate.Value = 30;
            }
    
            if (CompressionLevel.Value < 1 || CompressionLevel.Value > 9)
            {
                Logger.LogWarning($"[VBNetTweaks] CompressionLevel {CompressionLevel.Value} out of range, resetting to 3");
                CompressionLevel.Value = 3;
            }

            ZPackage pkg = BuildConfigPackage();
            if (pkg.GetArray().Length > 0)
            {
                byte[] data = pkg.GetArray();
                foreach (var peer in ZNet.instance.GetPeers())
                {
                    ZPackage copyPkg = new ZPackage(data);
                    _configSyncRPC.SendPackage(new List<ZNetPeer> { peer }, copyPkg);
                }

                Logger.LogInfo("[VBNetTweaks] AdminConfig changed, data sent to clients");
            }
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
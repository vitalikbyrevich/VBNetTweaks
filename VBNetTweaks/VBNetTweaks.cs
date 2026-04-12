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
        private const string ModVersion = "0.2.7";
        private const string ModGUID = "VitByr.VBNetTweaks";
        public static VBNetTweaks Instance { get; private set; }
        public CustomRPC _configSyncRPC;
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
            Instance = this;
            ModEnabled = _serverConfig.BindConfig("00 - Master", "ModEnabled", true, "Полностью включить/выключить мод VBNetTweaks / Enable/disable the VBNetTweaks mod completely", synced: true);
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
            _harmony.PatchAll(typeof(StatusEffectVFXFix));
            _harmony.PatchAll(typeof(NetworkSyncPatches));
            _harmony.PatchAll(typeof(ZDONetworkOptimizer));

            Logger.LogInfo("VBNetTweaks загружен!");
            if (DebugEnabled.Value) Logger.LogInfo("Режим отладки включен");
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
            DebugEnabled = Config.Bind(debugSection, "DebugEnabled", false, "Включить отладочный вывод / Enable debug output");
            VerboseLogging = Config.Bind(debugSection, "VerboseLogging", false, "Включить подробное логирование / Enable verbose logging");

            var modulesSection = "02 - Modules";
            EnableClientCompression = Config.Bind(modulesSection, "ClientCompression", true, "Сжимать данные на клиенте (может вызывать проблемы с визуальными эффектами) / Compress data on the client (may cause visual issues)");
        }

        private void InitServerConfigs()
        {
            var modulesSection = "02 - Modules";
            ModuleSteamOptimizations = _serverConfig.BindConfig(modulesSection, "SteamOptimizations", true, "Оптимизации Steam сокета / Steam socket optimizations", synced: true);
            ModuleShipSync = _serverConfig.BindConfig(modulesSection, "ShipSync", true, "Синхронизация кораблей / Synchronization of ships", synced: true);
            ModuleCompression = _serverConfig.BindConfig(modulesSection, "Compression", true, "Сжатие сетевого трафика на сервере / Network traffic compression on the server", synced: true);

            var compressionSection = "03 - Compression Settings";
            m_CompressionAlgorithm = _serverConfig.BindConfig(compressionSection, "Algorithm", CompressionAlgorithm.Vanilla, "Алгоритм сжатия: Deflate, Vanilla (встроенная компрессия игры) / Compression algorithm: Deflate, Vanilla (built-in game compression)", synced: true);
            CompressionLevel = _serverConfig.BindConfig(compressionSection, "Level", 3, "Уровень сжатия (1-9 для Deflate) / Compression level (1-9 for Deflate)", acceptableValues: new AcceptableValueRange<int>(1, 9), synced: true);

            var steamSection = "04 - Steam Settings";
            SteamSendRateMinKB = _serverConfig.BindConfig(steamSection, "MinRateKB", 256, "Минимальная скорость Steam (vanilla = 150 Kb/s) / Minimum Steam speed (vanilla = 150 Kbps)", synced: true);
            SteamSendRateMaxKB = _serverConfig.BindConfig(steamSection, "MaxRateKB", 4096, "Максимальная скорость Steam (vanilla = 150 Kb/s) / Steam Max Speed ​​(vanilla = 150 Kbps)", synced: true);
            SteamSendBufferSize = _serverConfig.BindConfig(steamSection, "BufferSize", 100_000_000, "Размер буфера Steam  (vanilla = 260000 B) / Steam Buffer Size (vanilla = 260000 B)", synced: true);

            var serverSection = "05 - Server Settings";
            SendInterval = _serverConfig.BindConfig(serverSection, "SendInterval", 0.03f, "Интервал отправки данных (vanilla = 0.05) / Data sending interval (vanilla = 0.05)", acceptableValues: new AcceptableValueRange<float>(0.01f, 0.5f),
                synced: true);

            PeersPerUpdate = _serverConfig.BindConfig(serverSection, "PeersPerUpdate", 30, "Количество пиров за один апдейт (vanilla = 1) / Number of peers per update (vanilla = 1)", acceptableValues: new AcceptableValueRange<int>(1, 200),
                synced: true);
            ZDOQueueLimit = _serverConfig.BindConfig(serverSection, "ZDOQueueLimit", 20480, "Размер буфера отправки ZDO пакетов (vanilla = 10240 Kb) / ZDO packet sending buffer size (vanilla = 10240 KB)", synced: true);
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
                pkg.Write(SendInterval.Value);
                pkg.Write(PeersPerUpdate.Value);
                pkg.Write(ZDOQueueLimit.Value);
                pkg.Write(EnableClientCompression.Value);
                pkg.Write((int)m_CompressionAlgorithm.Value);
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
                ModuleCompression.Value = pkg.ReadBool();
                CompressionLevel.Value = pkg.ReadInt();
                SendInterval.Value = pkg.ReadSingle();
                PeersPerUpdate.Value = pkg.ReadInt();
                ZDOQueueLimit.Value = pkg.ReadInt();
                EnableClientCompression.Value = pkg.ReadBool();
                m_CompressionAlgorithm.Value = (CompressionAlgorithm)pkg.ReadInt();
                
                Debug.Log($"[VBNetTweaks] Server config applied: Algorithm={m_CompressionAlgorithm.Value}, Level={CompressionLevel.Value}");
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
            
            ZDONetworkOptimizer.ReinitializeCompressor();

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
            
            if (SendInterval.Value <= 0.001f) SendInterval.Value = 0.03f;
            if (PeersPerUpdate.Value <= 0) PeersPerUpdate.Value = 30;
            if (CompressionLevel.Value < 1 || CompressionLevel.Value > 9) CompressionLevel.Value = 3;

            ZDONetworkOptimizer.ReinitializeCompressor();
        }

        private void OnDestroy()
        {
            _serverConfig.Save();
            _harmony.UnpatchSelf();
        }
    }
}

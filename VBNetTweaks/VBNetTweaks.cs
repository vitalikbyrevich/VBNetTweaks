using System.Collections;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using Paths = BepInEx.Paths;

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
        private const string ModVersion = "0.2.0";
        private const string ModGUID = "VitByr.VBNetTweaks";

        private CustomRPC _configSyncRPC;
        private ConfigFile _serverConfig;

        public static ConfigEntry<bool> ModEnabled;

        public static ConfigEntry<bool> DebugEnabled;
        public static ConfigEntry<bool> VerboseLogging;
        public static ConfigEntry<bool> SceneDebugEnabled;

        public static ConfigEntry<bool> ModuleCompression;
        public static ConfigEntry<bool> EnableClientCompression;
        public static ConfigEntry<bool> ModuleZDOThrottling;
        public static ConfigEntry<bool> ModuleAILOD;
        public static ConfigEntry<bool> ModuleMonsterAI;
        public static ConfigEntry<bool> ModuleSteamOptimizations;
        public static ConfigEntry<bool> ModulePlayerSync;
        public static ConfigEntry<bool> ModuleShipSync;
        public static ConfigEntry<bool> ModuleZoneOwner;
        public static ConfigEntry<bool> ModuleSupportCache;
        public static ConfigEntry<bool> ModuleRpcBatcher;

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

            _serverConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, "VitByr/VBNetTweaks/ServerConfig.cfg"), true);
            SynchronizationManager.Instance.RegisterCustomConfig(_serverConfig);

            ModEnabled = _serverConfig.BindConfig("00 - Master", "ModEnabled", true, "Полностью включить/выключить мод VBNetTweaks", synced: true);
            if (!ModEnabled.Value) return;

            InitClientConfigs();
            InitServerConfigs();

            _configSyncRPC = NetworkManager.Instance.AddRPC("VBNetTweaks_ConfigSync", OnAdminConfigSync, OnClientConfigSync);

            CreateConfigWatcher();

            if (ModuleRpcBatcher.Value && ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.Register<ZPackage>("VBNT_RPCBatch", RpcBatcher.HandleBatch);
                Logger.LogInfo("VBNetTweaks: VBNT_RPCBatch registered");
            }

            _harmony = new Harmony(ModGUID);

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

            _harmony.PatchAll(typeof(ObjectPool));
            _harmony.PatchAll(typeof(PlayerCache));

            StartCoroutine(DelayedServerConfigInit());
            StartCoroutine(DelayedServerPatchInit());

            Logger.LogInfo("VBNetTweaks загружен!");
            if (DebugEnabled.Value) Logger.LogInfo("Режим отладки включен");
        }

        private void InitClientConfigs()
        {
            var debugSection = "01 - Debug";
            DebugEnabled = Config.Bind(debugSection, "DebugEnabled", false, "Включить отладочный вывод");
            VerboseLogging = Config.Bind(debugSection, "VerboseLogging", false, "Включить подробное логирование");

            var modulesSection = "02 - Modules";
            EnableClientCompression = Config.Bind(modulesSection, "ClientCompression", false, "Сжимать данные на клиенте (может вызывать проблемы с визуальными эффектами)");
            ModuleAILOD = Config.Bind(modulesSection, "AILOD", true, "LOD для AI существ");
            ModuleMonsterAI = Config.Bind(modulesSection, "MonsterAI", true, "Оптимизация AI монстров");
            ModuleSteamOptimizations = Config.Bind(modulesSection, "SteamOptimizations", true, "Оптимизации Steam сокета");
            ModulePlayerSync = Config.Bind(modulesSection, "PlayerSync", true, "Синхронизация игроков");
            ModuleShipSync = Config.Bind(modulesSection, "ShipSync", true, "Синхронизация кораблей");
            ModuleZoneOwner = Config.Bind(modulesSection, "ZoneOwner", true, "Автоматическая передача владения зонами");
            ModuleSupportCache = Config.Bind(modulesSection, "SupportCache", true, "Кэш поддержки построек");
            ModuleRpcBatcher = Config.Bind(modulesSection, "RpcBatcher", true, "Пакетная обработка RPC");

            var compressionSection = "03 - Compression Settings";
            m_CompressionAlgorithm = Config.Bind(compressionSection, "Algorithm", CompressionAlgorithm.Deflate, "Алгоритм сжатия: Deflate или Zstd");
            CompressionLevel = Config.Bind(compressionSection, "Level", 2, new ConfigDescription("Уровень сжатия (1-10)", new AcceptableValueRange<int>(1, 10)));

            var zdoSection = "04 - ZDO Throttling Settings";
            ZDOThrottleDistance = Config.Bind(zdoSection, "Distance", 500f, "Дистанция для троттлинга ZDO");

            var aiSection = "05 - AI LOD Settings";
            AILODNearDistance = Config.Bind(aiSection, "NearDistance", 100f, "Дистанция полной скорости AI");
            AILODFarDistance = Config.Bind(aiSection, "FarDistance", 300f, "Дистанция замедления AI");
            AILODThrottleFactor = Config.Bind(aiSection, "ThrottleFactor", 0.5f, new ConfigDescription("Коэффициент замедления AI", new AcceptableValueRange<float>(0.25f, 0.75f)));

            var steamSection = "06 - Steam Settings";
            SteamSendRateMinKB = Config.Bind(steamSection, "MinRateKB", 256, "Минимальная скорость Steam");
            SteamSendRateMaxKB = Config.Bind(steamSection, "MaxRateKB", 1024, "Максимальная скорость Steam");
            SteamSendBufferSize = Config.Bind(steamSection, "BufferSize", 100_000_000, "Размер буфера Steam");

            var playerSyncSection = "07 - Player Sync Settings";
            EnableClientInterpolation = Config.Bind(playerSyncSection, "Interpolation", true, "Сглаживание игроков");
            EnablePlayerPrediction = Config.Bind(playerSyncSection, "Prediction", true, "Предсказание движения");
        }

        private void InitServerConfigs()
        {
            var serverSection = "08 - Server Settings";
            ModuleCompression = _serverConfig.BindConfig(serverSection, "Compression", true, "Сжатие сетевого трафика на сервере", synced: true);
            ModuleZDOThrottling = Config.BindConfig(serverSection, "ZDOThrottling", true, "Троттлинг дальних ZDO объектов", synced: true);

            SendInterval = _serverConfig.BindConfig(serverSection, "SendInterval", 0.05f, "Интервал отправки данных (секунды)", synced: true);
            PeersPerUpdate = _serverConfig.BindConfig(serverSection, "PeersPerUpdate", 20, "Количество пиров за один апдейт", synced: true);
            EnableNetSync = _serverConfig.BindConfig(serverSection, "EnableNetSync", true, "Включить новую систему синхронизации NetSync", synced: true);

            var zoneSection = "09 - Zone Owner Settings";
            ZoneOwnerManager.PingThreshold = _serverConfig.BindConfig(zoneSection, "PingThreshold", 100, "Порог пинга для смены владельца зоны (мс)", synced: true);
            ZoneOwnerManager.Hysteresis = _serverConfig.BindConfig(zoneSection, "Hysteresis", 20, "Гистерезис для смены владельца (мс)", synced: true);
            ZoneOwnerManager.TransferCooldown = _serverConfig.BindConfig(zoneSection, "TransferCooldown", 5f, "Задержка между сменами владельца (сек)", synced: true);
            ZoneOwnerManager.OwnerUpdateInterval = _serverConfig.BindConfig(zoneSection, "UpdateInterval", 2f, "Частота проверки владельцев зон (сек)", synced: true);

            ModEnabled.SettingChanged += OnModEnabledChanged;
        }

        private ZPackage BuildConfigPackage()
        {
            ZPackage pkg = new ZPackage();
            try
            {
                pkg.Write(ModEnabled.Value);
                pkg.Write(ModuleCompression.Value);
                pkg.Write(ModuleZDOThrottling.Value);
                pkg.Write(SendInterval.Value);
                pkg.Write(PeersPerUpdate.Value);
                pkg.Write(EnableNetSync.Value);
                pkg.Write(ZoneOwnerManager.PingThreshold.Value);
                pkg.Write(ZoneOwnerManager.Hysteresis.Value);
                pkg.Write(ZoneOwnerManager.TransferCooldown.Value);
                pkg.Write(ZoneOwnerManager.OwnerUpdateInterval.Value);
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

                bool newModEnabled = pkg.ReadBool();
                ModuleCompression.Value = pkg.ReadBool();
                ModuleZDOThrottling.Value = pkg.ReadBool();
                SendInterval.Value = pkg.ReadSingle();
                PeersPerUpdate.Value = pkg.ReadInt();
                EnableNetSync.Value = pkg.ReadBool();
                ZoneOwnerManager.PingThreshold.Value = pkg.ReadInt();
                ZoneOwnerManager.Hysteresis.Value = pkg.ReadInt();
                ZoneOwnerManager.TransferCooldown.Value = pkg.ReadSingle();
                ZoneOwnerManager.OwnerUpdateInterval.Value = pkg.ReadSingle();

                if (ModEnabled.Value != newModEnabled)
                {
                    ModEnabled.Value = newModEnabled;
                    OnModEnabledStateChanged();
                }
                else
                {
                    // Если мод уже был включен, но изменились другие настройки
                    ReinitializeAllModules();
                }

                Debug.Log("[VBNetTweaks] Config applied successfully");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VBNetTweaks] Error applying config package: {e.Message}");
            }
        }

        private void OnModEnabledChanged(object sender, EventArgs e)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) return;

            Logger.LogInfo($"[VBNetTweaks] ModEnabled изменен на {ModEnabled.Value}");

            StartCoroutine(ApplyServerConfigChanges());
        }

        private void OnModEnabledStateChanged()
        {
            Logger.LogInfo($"[VBNetTweaks] ModEnabled на клиенте изменен на {ModEnabled.Value}");

            if (ModEnabled.Value)
            {
                if (ModuleCompression.Value && EnableClientCompression.Value)
                {
                    ZDONetworkOptimizer.Initialize();
                    Logger.LogInfo("[VBNetTweaks] Client compression initialized");
                }

                if (ModuleSteamOptimizations.Value)
                {
                    Logger.LogInfo("[VBNetTweaks] Steam optimizations active");
                }

                if (ModuleShipSync.Value)
                {
                    Logger.LogInfo("[VBNetTweaks] Ship sync system active");
                }

                if (ModulePlayerSync.Value)
                {
                    Logger.LogInfo("[VBNetTweaks] Player sync system active");
                }

                if (ModuleZoneOwner.Value && Helper.IsServer())
                {
                    ZoneOwnerManager.Initialize();
                    Logger.LogInfo("[VBNetTweaks] Zone owner manager initialized");
                }

                if (ModuleSupportCache.Value)
                {
                    SupportManager.SupportRecalcInterval = 5f;
                    SupportManager.SupportCacheDuration = 1f;
                    Logger.LogInfo("[VBNetTweaks] Support cache system active");
                }

                if (ModuleRpcBatcher.Value && ZRoutedRpc.instance != null)
                {
                    Logger.LogInfo("[VBNetTweaks] RPC batcher active");
                }

                if (ModuleZDOThrottling.Value)
                {
                    Logger.LogInfo($"[VBNetTweaks] ZDO throttling active at {ZDOThrottleDistance.Value}m");
                }

                if (ModuleAILOD.Value && Helper.IsServer())
                {
                    Logger.LogInfo($"[VBNetTweaks] AI LOD active (near:{AILODNearDistance.Value}m, far:{AILODFarDistance.Value}m)");
                }

                if (ModuleMonsterAI.Value && Helper.IsServer())
                {
                    Logger.LogInfo("[VBNetTweaks] Monster AI patches active");
                }
            }
            else
            {
                Logger.LogInfo("[VBNetTweaks] Mod disabled - disabling all systems");

                if (ModuleCompression.Value && EnableClientCompression.Value)
                {
                    Logger.LogInfo("[VBNetTweaks] Client compression disabled");
                }

                SupportManager.Clear(null);
                Logger.LogInfo("[VBNetTweaks] All systems disabled");
            }
        }

        private void ReinitializeAllModules()
        {
            Logger.LogInfo("[VBNetTweaks] Reinitializing all modules...");

            if (!ModEnabled.Value) return;

            if (ModuleCompression.Value)
            {
                if (Helper.IsServer() || EnableClientCompression.Value)
                {
                    ZDONetworkOptimizer.Initialize();
                }
            }

            if (ModuleZoneOwner.Value && Helper.IsServer())
            {
                ZoneOwnerManager.Initialize();
            }

            if (ModuleSupportCache.Value)
            {
                SupportManager.SupportRecalcInterval = 5f;
                SupportManager.SupportCacheDuration = 1f;
            }

            Logger.LogInfo("[VBNetTweaks] Modules reinitialized");
        }

        private IEnumerator OnAdminConfigSync(long sender, ZPackage pkg)
        {
            Logger.LogInfo($"[VBNetTweaks] Сервер получил конфиг от администратора {sender}");

            ApplyConfigFromPackage(pkg);
            _serverConfig.Save();

            if (ZNet.instance && ZNet.instance.IsServer())
            {
                byte[] data = pkg.GetArray();
                foreach (var peer in ZNet.instance.GetPeers())
                {
                    if (peer.m_uid != sender)
                    {
                        ZPackage copyPkg = new ZPackage(data);
                        _configSyncRPC.SendPackage(new List<ZNetPeer> { peer }, copyPkg);
                    }
                }
            }

            yield break;
        }

        private IEnumerator OnClientConfigSync(long sender, ZPackage pkg)
        {
            Logger.LogInfo($"[VBNetTweaks] Клиент получил конфиг от сервера {sender}");

            ApplyConfigFromPackage(pkg);
            ApplySyncedConfigChanges();

            yield break;
        }

        private void ApplySyncedConfigChanges()
        {
            if (ModuleCompression.Value)
            {
                ZDONetworkOptimizer.Initialize();
            }

            if (ModuleZoneOwner.Value)
            {
                ZoneOwnerManager.Initialize();
            }
        }

        private void CreateConfigWatcher()
        {
            ConfigFileWatcher clientWatcher = new ConfigFileWatcher(Config, reloadDelay: 1000);
            clientWatcher.OnConfigFileReloaded += () =>
            {
                if (ZNet.instance)
                {
                    StartCoroutine(ApplyClientConfigChanges());
                }
            };

            ConfigFileWatcher serverWatcher = new ConfigFileWatcher(_serverConfig, reloadDelay: 1000);
            serverWatcher.OnConfigFileReloaded += () =>
            {
                if (!ZNet.instance || !ZNet.instance.IsServer()) return;

                bool oldValue = ModEnabled.Value;
                _serverConfig.Reload();

                if (oldValue != ModEnabled.Value)
                {
                    Logger.LogInfo($"[VBNetTweaks] ModEnabled changed from {oldValue} to {ModEnabled.Value} via file watcher");
                }

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

            ZPackage pkg = BuildConfigPackage();
            if (pkg.GetArray().Length > 0)
            {
                byte[] data = pkg.GetArray();
                int sentCount = 0;

                foreach (var peer in ZNet.instance.GetPeers())
                {
                    ZPackage copyPkg = new ZPackage(data);
                    _configSyncRPC.SendPackage(new List<ZNetPeer> { peer }, copyPkg);
                    sentCount++;
                }

                Logger.LogInfo($"[VBNetTweaks] Серверный конфиг изменён, данные отправлены {sentCount} клиентам");

                if (!ModEnabled.Value)
                {
                    Logger.LogInfo("[VBNetTweaks] Мод отключен на сервере");
                }
            }
        }

        private IEnumerator DelayedServerPatchInit()
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

            if (ModuleAILOD.Value) _harmony.PatchAll(typeof(AILODPatches));
            if (ModuleMonsterAI.Value) _harmony.PatchAll(typeof(MonsterAiPatches));

            Logger.LogInfo("VBNetTweaks: серверные патчи успешно применены.");
        }

        private IEnumerator DelayedServerConfigInit()
        {
            int maxAttempts = 100;
            for (int i = 0; i < maxAttempts; i++)
            {
                if (ZNet.instance) break;
                yield return new WaitForSeconds(0.25f);
            }

            if (Helper.IsServer())
            {
                if (ModuleCompression.Value)
                {
                    ZDONetworkOptimizer.Initialize();
                    _harmony.PatchAll(typeof(ZDONetworkOptimizer));
                }

                _serverConfigsInitialized = true;
                Logger.LogInfo("Серверные настройки VBNetTweaks инициализированы");

                if (ZNet.instance && ZNet.instance.IsServer() && ZNet.instance.GetPeers().Count > 0)
                {
                    StartCoroutine(ApplyServerConfigChanges());
                }
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
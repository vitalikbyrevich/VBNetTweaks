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
        private const string ModVersion = "0.2.4";
        private const string ModGUID = "VitByr.VBNetTweaks";

        private CustomRPC _configSyncRPC;
        private ConfigFile _serverConfig;
        private Harmony _harmony;
        private bool _isInitialized;

        private void Awake()
        {
            _serverConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, "VitByr/VBNetTweaks/ServerConfig.cfg"), true);
            SynchronizationManager.Instance.RegisterCustomConfig(_serverConfig);

            ModConfig.ModEnabled = _serverConfig.BindConfig("00 - Master", "ModEnabled", true, "Полностью включить/выключить мод VBNetTweaks", synced: true);
            if (!ModConfig.ModEnabled.Value) return;

            ModConfig.Initialize(Config, _serverConfig);

            _configSyncRPC = NetworkManager.Instance.AddRPC("VBNetTweaks_ConfigSync", OnAdminConfigSync, OnClientConfigSync);

            CreateConfigWatcher();

            if (ModConfig.ModuleRpcBatcher.Value && ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.Register<ZPackage>("VBNT_RPCBatch", (long sender, ZPackage pkg) => { RpcBatcher.HandleBatch(sender, pkg); });
                Logger.LogInfo("VBNetTweaks: VBNT_RPCBatch registered");
            }

            StartCoroutine(DelayedServerConfigInit());
            StartCoroutine(DelayedServerPatchInit());

            _harmony = new Harmony(ModGUID);

            if (ModConfig.ModuleSteamOptimizations.Value) _harmony.PatchAll(typeof(SteamOptimizations));
            if (ModConfig.ModuleShipSync.Value) _harmony.PatchAll(typeof(ShipSyncSystem));
            if (ModConfig.ModulePlayerSync.Value) _harmony.PatchAll(typeof(PlayerSyncSystem));
            if (ModConfig.ModuleRpcBatcher.Value) _harmony.PatchAll(typeof(RpcBatcher));
            if (ModConfig.ModuleZoneOwner.Value) _harmony.PatchAll(typeof(ZoneOwnerManager));

            _harmony.PatchAll(typeof(ObjectPool));
            _harmony.PatchAll(typeof(PlayerCache));
            _harmony.PatchAll(typeof(ZNetOptimizations));

            Logger.LogInfo("VBNetTweaks загружен!");
            if (ModConfig.DebugEnabled.Value) Logger.LogInfo("Режим отладки включен");
        }

        private ZPackage BuildConfigPackage()
        {
            ZPackage pkg = new ZPackage();
            try
            {
                pkg.Write(ModConfig.ModEnabled.Value);
                pkg.Write(ModConfig.ModuleCompression.Value);
                pkg.Write(ModConfig.ModuleZDOThrottling.Value);
                pkg.Write(ModConfig.SteamSendRateMinKB.Value);
                pkg.Write(ModConfig.SteamSendRateMaxKB.Value);
                pkg.Write(ModConfig.SteamSendBufferSize.Value);
                pkg.Write((int)CompressionController.Algorithm.Value);
                pkg.Write(CompressionController.Level.Value);
                pkg.Write(CompressionController.MinSize.Value);
                pkg.Write(CompressionController.Adaptive.Value);
                pkg.Write(CompressionController.TargetRatio.Value);
                pkg.Write(ModConfig.SendInterval.Value);
                pkg.Write(ModConfig.PeersPerUpdate.Value);
                pkg.Write(ModConfig.EnableNetSync.Value);
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
                ModConfig.ModuleCompression.Value = pkg.ReadBool();
                ModConfig.ModuleZDOThrottling.Value = pkg.ReadBool();
                ModConfig.SteamSendRateMinKB.Value = pkg.ReadInt();
                ModConfig.SteamSendRateMaxKB.Value = pkg.ReadInt();
                ModConfig.SteamSendBufferSize.Value = pkg.ReadInt();
                CompressionController.Algorithm.Value = (CompressionAlgorithm)pkg.ReadInt();
                CompressionController.Level.Value = pkg.ReadInt();
                CompressionController.MinSize.Value = pkg.ReadInt();
                CompressionController.Adaptive.Value = pkg.ReadBool();
                CompressionController.TargetRatio.Value = pkg.ReadSingle();
                ModConfig.SendInterval.Value = pkg.ReadSingle();
                ModConfig.PeersPerUpdate.Value = pkg.ReadInt();
                ModConfig.EnableNetSync.Value = pkg.ReadBool();
                ZoneOwnerManager.PingThreshold.Value = pkg.ReadInt();
                ZoneOwnerManager.Hysteresis.Value = pkg.ReadInt();
                ZoneOwnerManager.TransferCooldown.Value = pkg.ReadSingle();
                ZoneOwnerManager.OwnerUpdateInterval.Value = pkg.ReadSingle();

                if (ModConfig.ModEnabled.Value != newModEnabled)
                {
                    ModConfig.ModEnabled.Value = newModEnabled;
                    OnModEnabledStateChanged();
                }
                else if (!_isInitialized)
                {
                    ReinitializeAllModules();
                }
                else
                {
                    ReinitializeModulesOnConfigChange();
                }

                _isInitialized = true;
                Debug.Log("[VBNetTweaks] Config applied successfully");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VBNetTweaks] Error applying config package: {e.Message}");
            }
        }

        private void ReinitializeModulesOnConfigChange()
        {
            // Переинициализируем только модули, которые могут измениться без перезагрузки мода
            if (ModConfig.ModuleCompression.Value)
            {
                ZDONetworkOptimizer.Shutdown();
                ZDONetworkOptimizer.Initialize();
                Logger.LogInfo("[VBNetTweaks] Compression reinitialized with new settings");
            }

            if (ModConfig.ModuleZoneOwner.Value && Helper.IsServer())
            {
                ZoneOwnerManager.Shutdown();
                ZoneOwnerManager.Initialize();
                Logger.LogInfo("[VBNetTweaks] Zone owner manager reinitialized");
            }
        }

        private void OnModEnabledStateChanged()
        {
            Logger.LogInfo($"[VBNetTweaks] ModEnabled на клиенте изменен на {ModConfig.ModEnabled.Value}");

            if (ModConfig.ModEnabled.Value)
            {
                if (ModConfig.ModuleCompression.Value)
                {
                    ZDONetworkOptimizer.Initialize();
                    Logger.LogInfo("[VBNetTweaks] Compression system initialized");
                }

                if (ModConfig.ModuleSteamOptimizations.Value)
                {
                    Logger.LogInfo("[VBNetTweaks] Steam optimizations active");
                }

                if (ModConfig.ModuleShipSync.Value)
                {
                    Logger.LogInfo("[VBNetTweaks] Ship sync system active");
                }

                if (ModConfig.ModulePlayerSync.Value)
                {
                    Logger.LogInfo("[VBNetTweaks] Player sync system active");
                }

                if (ModConfig.ModuleZoneOwner.Value && Helper.IsServer())
                {
                    ZoneOwnerManager.Initialize();
                    Logger.LogInfo("[VBNetTweaks] Zone owner manager initialized");
                }

                if (ModConfig.ModuleRpcBatcher.Value && ZRoutedRpc.instance != null)
                {
                    Logger.LogInfo("[VBNetTweaks] RPC batcher active");
                }

                if (ModConfig.ModuleZDOThrottling.Value)
                {
                    Logger.LogInfo($"[VBNetTweaks] ZDO throttling active at {ModConfig.ZDOThrottleDistance.Value}m");
                }
            }
            else
            {
                Logger.LogInfo("[VBNetTweaks] All systems disabled");
            }
        }

        private void ReinitializeAllModules()
        {
            Logger.LogInfo("[VBNetTweaks] Reinitializing all modules...");

            if (!ModConfig.ModEnabled.Value) return;

            if (ModConfig.ModuleCompression.Value)
            {
                ZDONetworkOptimizer.Initialize();
            }

            if (ModConfig.ModuleZoneOwner.Value && Helper.IsServer())
            {
                ZoneOwnerManager.Initialize();
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

            ModConfig.SetServerConfigInitialized();

            yield break;
        }

        private void ApplySyncedConfigChanges()
        {
            // Применяем изменения после получения конфига от сервера
            if (ModConfig.ModuleCompression.Value)
            {
                ZDONetworkOptimizer.Initialize();
                Logger.LogInfo("[VBNetTweaks] Compression initialized with server settings");
            }

            if (ModConfig.ModuleZoneOwner.Value && Helper.IsServer())
            {
                ZoneOwnerManager.Initialize();
            }

            if (ModConfig.ModuleSteamOptimizations.Value)
            {
                Logger.LogInfo("[VBNetTweaks] Steam optimizations active with server settings");
            }
        }

        private void CreateConfigWatcher()
        {
            ConfigFileWatcher clientWatcher = new ConfigFileWatcher(Config, reloadDelay: 1000);
            clientWatcher.OnConfigFileReloaded += () =>
            {
                if (ZNet.instance && !ZNet.instance.IsServer())
                {
                    StartCoroutine(ApplyClientConfigChanges());
                }
            };

            ConfigFileWatcher serverWatcher = new ConfigFileWatcher(_serverConfig, reloadDelay: 1000);
            serverWatcher.OnConfigFileReloaded += () =>
            {
                if (!ZNet.instance || !ZNet.instance.IsServer()) return;

                bool oldValue = ModConfig.ModEnabled.Value;
                _serverConfig.Reload();

                if (oldValue != ModConfig.ModEnabled.Value)
                {
                    Logger.LogInfo($"[VBNetTweaks] ModEnabled changed from {oldValue} to {ModConfig.ModEnabled.Value} via file watcher");
                }

                StartCoroutine(ApplyServerConfigChanges());
            };
        }

        private IEnumerator ApplyClientConfigChanges()
        {
            yield return null;
            // Клиентские настройки не требуют синхронизации с сервером
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

                if (!ModConfig.ModEnabled.Value)
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

            Logger.LogInfo("VBNetTweaks: серверные патчи успешно применены.");
        }

        private IEnumerator DelayedServerConfigInit()
        {
            int maxAttempts = 100;
            for (int i = 0; i < maxAttempts; i++)
            {
                if (ZNet.instance && ZNet.instance.IsServer())
                {
                    break;
                }

                yield return new WaitForSeconds(0.25f);
            }

            if (!ZNet.instance)
            {
                Logger.LogWarning("ZNet.instance не появился за 25 секунд, пропускаем инициализацию");
                yield break;
            }

            try
            {
                if (_harmony == null)
                {
                    Logger.LogError("Harmony instance is null в DelayedServerConfigInit");
                    yield break;
                }

                if (ModConfig.ModuleCompression != null && ModConfig.ModuleCompression.Value)
                {
                    try
                    {
                        ZDONetworkOptimizer.Initialize();
                        _harmony.PatchAll(typeof(ZDONetworkOptimizer));
                        Logger.LogInfo("ZDONetworkOptimizer инициализирован и заплачен");
                    }
                    catch (Exception e)
                    {
                        Logger.LogError($"Ошибка при инициализации компрессии: {e.Message}");
                    }
                }

                if (Helper.IsServer())
                {
                    ModConfig.SetServerConfigInitialized();
                    Logger.LogInfo("Серверные настройки VBNetTweaks инициализированы");
                }

                // Отправляем конфиг всем подключенным клиентам после инициализации
                if (Helper.IsServer() && ZNet.instance.IsServer())
                {
                    var peers = ZNet.instance.GetPeers();
                    if (peers != null && peers.Count > 0)
                    {
                        StartCoroutine(SendConfigToClientsWithDelay(peers));
                    }
                    else
                    {
                        Logger.LogInfo("Нет подключенных пиров, отправка конфига отложена");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"Ошибка в DelayedServerConfigInit: {e.Message}\n{e.StackTrace}");
            }
        }

        private IEnumerator SendConfigToClientsWithDelay(List<ZNetPeer> peers)
        {
            // Небольшая задержка, чтобы клиенты успели полностью подключиться
            yield return new WaitForSeconds(2f);

            ZPackage pkg = BuildConfigPackage();
            if (pkg.GetArray().Length == 0) yield break;

            byte[] data = pkg.GetArray();
            int sentCount = 0;

            foreach (var peer in peers)
            {
                if (peer != null && peer.IsReady())
                {
                    ZPackage copyPkg = new ZPackage(data);
                    _configSyncRPC.SendPackage(new List<ZNetPeer> { peer }, copyPkg);
                    sentCount++;
                    Logger.LogInfo($"[VBNetTweaks] Отправлен конфиг клиенту {peer.m_uid}");
                }
            }

            Logger.LogInfo($"[VBNetTweaks] Конфиг отправлен {sentCount} клиентам");
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
        [HarmonyPostfix]
        private void OnNewConnection(ZNet __instance, ZNetPeer peer)
        {
            if (!ModConfig.ModEnabled.Value) return;
            if (!__instance.IsServer()) return;

            // Отправляем конфиг новому клиенту после его полной инициализации
            __instance.StartCoroutine(SendConfigToNewClient(peer));
        }

        private IEnumerator SendConfigToNewClient(ZNetPeer peer)
        {
            // Ждем, пока клиент полностью инициализируется
            float timeout = 10f;
            float elapsed = 0f;
            
            while (!peer.IsReady() && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return new WaitForSeconds(0.5f);
            }

            if (!peer.IsReady())
            {
                Logger.LogInfo($"[VBNetTweaks] Client {peer.m_uid} not ready after {timeout}s, skipping config send");
                yield break;
            }

            var instance = GetInstance();
            if (!instance) yield break;

            ZPackage pkg = instance.BuildConfigPackage();
            if (pkg.GetArray().Length == 0) yield break;

            instance._configSyncRPC.SendPackage(new List<ZNetPeer> { peer }, pkg);
            Helper.LogDebug($"[VBNetTweaks] Отправлен конфиг новому клиенту {peer.m_uid}");
        }

        private static VBNetTweaks GetInstance()
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                var instance = go.GetComponent<VBNetTweaks>();
                if (instance) return instance;
            }
            return null;
        }

        private void OnDestroy()
        {
            ZoneOwnerManager.Shutdown();
            ZDONetworkOptimizer.Shutdown();
            _harmony?.UnpatchSelf();
        }
    }
}
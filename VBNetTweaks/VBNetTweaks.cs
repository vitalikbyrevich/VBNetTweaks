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
        private const string ModVersion = "0.2.2";
        private const string ModGUID = "VitByr.VBNetTweaks";

        private CustomRPC _configSyncRPC;
        private ConfigFile _serverConfig;

        private Harmony _harmony;

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
                // Регистрируем обработчик батча
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
            if (ModConfig.ModuleSupportCache.Value)
            {
                _harmony.PatchAll(typeof(WearNTear_ClearCachedSupport_Patch));
                _harmony.PatchAll(typeof(WearNTear_OnDestroy_Patch));
                _harmony.PatchAll(typeof(WearNTear_UpdateWear_Patch));
                _harmony.PatchAll(typeof(WearNTear_GetSupport_Patch));
                _harmony.PatchAll(typeof(WearNTear_RPC_Damage_Patch));
                _harmony.PatchAll(typeof(WearNTear_Destroy_Patch));
            }

            if (ModConfig.ModuleZoneOwner.Value) _harmony.PatchAll(typeof(ZoneOwnerManager));

            _harmony.PatchAll(typeof(ObjectPool));
            _harmony.PatchAll(typeof(PlayerCache));
            _harmony.PatchAll(typeof(ZNetOptimizations));

            if (ModConfig.ModuleAILOD.Value) _harmony.PatchAll(typeof(AILODPatches));
            if (ModConfig.ModuleMonsterAI.Value) _harmony.PatchAll(typeof(MonsterAiPatches));

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
                pkg.Write(ModConfig.AILODNearDistance.Value);
                pkg.Write(ModConfig.AILODFarDistance.Value);
                pkg.Write(ModConfig.AILODThrottleFactor.Value);
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
                ModConfig.AILODNearDistance.Value = pkg.ReadSingle();
                ModConfig.AILODFarDistance.Value = pkg.ReadSingle();
                ModConfig.AILODThrottleFactor.Value = pkg.ReadSingle();

                if (ModConfig.ModEnabled.Value != newModEnabled)
                {
                    ModConfig.ModEnabled.Value = newModEnabled;
                    OnModEnabledStateChanged();
                }
                else
                {
                    ReinitializeAllModules();
                }

                Debug.Log("[VBNetTweaks] Config applied successfully");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VBNetTweaks] Error applying config package: {e.Message}");
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

                if (ModConfig.ModuleZoneOwner.Value/* && Helper.IsServer()*/)
                {
                    ZoneOwnerManager.Initialize();
                    Logger.LogInfo("[VBNetTweaks] Zone owner manager initialized");
                }

                if (ModConfig.ModuleSupportCache.Value)
                {
                    SupportManager.SupportRecalcInterval = 5f;
                    SupportManager.SupportCacheDuration = 1f;
                    Logger.LogInfo("[VBNetTweaks] Support cache system active");
                }

                if (ModConfig.ModuleRpcBatcher.Value && ZRoutedRpc.instance != null)
                {
                    Logger.LogInfo("[VBNetTweaks] RPC batcher active");
                }

                if (ModConfig.ModuleZDOThrottling.Value)
                {
                    Logger.LogInfo($"[VBNetTweaks] ZDO throttling active at {ModConfig.ZDOThrottleDistance.Value}m");
                }

                if (ModConfig.ModuleAILOD.Value/* && Helper.IsServer()*/)
                {
                    Logger.LogInfo($"[VBNetTweaks] AI LOD active (near:{ModConfig.AILODNearDistance.Value}m, far:{ModConfig.AILODFarDistance.Value}m)");
                }

                if (ModConfig.ModuleMonsterAI.Value/* && Helper.IsServer()*/)
                {
                    Logger.LogInfo("[VBNetTweaks] Monster AI patches active");
                }
            }
            else
            {
                Logger.LogInfo("[VBNetTweaks] Mod disabled - disabling all systems");

                SupportManager.Clear(null);
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

            if (ModConfig.ModuleZoneOwner.Value/* && Helper.IsServer()*/)
            {
                ZoneOwnerManager.Initialize();
            }

            if (ModConfig.ModuleSupportCache.Value)
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

            ModConfig._serverConfigsInitialized = true;

            yield break;
        }

        private void ApplySyncedConfigChanges()
        {
            if (ModConfig.ModuleCompression.Value)
            {
                ZDONetworkOptimizer.Initialize();
                Logger.LogInfo("[VBNetTweaks] Compression reinitialized with new settings");
            }

            if (ModConfig.ModuleZoneOwner.Value)
            {
                ZoneOwnerManager.Initialize();
            }

            if (ModConfig.SendInterval != null)
            {
                // AdaptiveThrottler сам подхватит новое значение при следующем Update
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
                    ModConfig._serverConfigsInitialized = true;
                    Logger.LogInfo("Серверные настройки VBNetTweaks инициализированы");
                }

                if (Helper.IsServer() && ZNet.instance.IsServer())
                {
                    var peers = ZNet.instance.GetPeers();
                    if (peers != null && peers.Count > 0)
                    {
                        StartCoroutine(ApplyServerConfigChanges());
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

        private void OnDestroy()
        {
            ZoneOwnerManager.Shutdown();
            _harmony?.UnpatchSelf();
        }
    }
}
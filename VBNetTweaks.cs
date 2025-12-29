using VBNetTweaks.Utils;

namespace VBNetTweaks
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class VBNetTweaks : BaseUnityPlugin
    {
        private const string ModName = "VBNetTweaks";
        private const string ModVersion = "0.1.1";
        private const string ModGUID = "VitByr.VBNetTweaks";

        // В VBNetTweaks.cs — добавить поля конфигов
        public static ConfigEntry<bool> EnableAILOD;
        public static ConfigEntry<float> AILODNearDistance;
        public static ConfigEntry<float> AILODFarDistance;
        public static ConfigEntry<float> AILODThrottleFactor;

        public static ConfigEntry<bool> EnableZDOThrottling;
        public static ConfigEntry<float> ZDOThrottleDistance;

        public static ConfigEntry<bool> EnablePlayerPositionBoost;
        public static ConfigEntry<float> PlayerPositionUpdateMultiplier;
        public static ConfigEntry<bool> EnableClientInterpolation;
        public static ConfigEntry<bool> EnablePlayerPrediction;

        public static ConfigEntry<bool> EnableMonsterAiPatches;
        public static ConfigEntry<bool> EnableSteamSendRate;
        public static ConfigEntry<int> SteamSendRateMinKB;
        public static ConfigEntry<int> SteamSendRateMaxKB;
        public static ConfigEntry<int> SteamSendBufferSize;


        public static ConfigEntry<bool> DebugEnabled;
        public static ConfigEntry<bool> VerboseLogging;
        public static ConfigEntry<float> SendInterval;
        public static ConfigEntry<int> PeersPerUpdate;
        public static ConfigEntry<bool> SceneDebugEnabled;
        public static ConfigEntry<bool> EnableNetSync;

        public static double NetTime;
        public static float DeltaTimeFixedPhysics = 0.02f;
        public static float DeltaTimePhysics = 0.01f;

        private Harmony _harmony;
        private static bool _serverConfigsInitialized;

        private void Awake()
        {
            DebugEnabled = Config.Bind("01 - General", "DebugEnabled", false, new ConfigDescription("Включить отладочный вывод"));
            VerboseLogging = Config.Bind("01 - General", "VerboseLogging", false, new ConfigDescription("Включить подробное логирование успешных операций"));
            
            _harmony = new Harmony(ModGUID);
            _harmony.PatchAll(typeof(UnifiedZDOTranspiler)); 
            _harmony.PatchAll(typeof(UnifiedSceneOptimizations)); 
            _harmony.PatchAll(typeof(UnifiedSteamOptimizations));

            if (Helper.IsClient())
            {
                if (EnableClientInterpolation?.Value == true || EnablePlayerPrediction?.Value == true) _harmony.PatchAll(typeof(PlayerPositionSyncPatches));
            } 
            // Серверные патчи — через корутину
            StartCoroutine(DelayedServerPatchInit());

            // Отложенная инициализация серверных настроек
            StartCoroutine(DelayedServerConfigInit());

            Logger.LogInfo("VBNetTweaks загружен!");
            if (DebugEnabled.Value) Logger.LogInfo("Режим отладки включен");
        }
        
        private System.Collections.IEnumerator DelayedServerPatchInit() 
        { 
            float timeout = 30f; // секунд
            float elapsed = 0f;
            while (!ZNet.instance && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (!ZNet.instance)
            {
                Logger.LogWarning("ZNet.instance не появился за 30 секунд — серверные патчи не применены.");
                yield break;
            }

            if (!Helper.IsServer()) yield break; 
            if (EnableAILOD?.Value == true) _harmony.PatchAll(typeof(AILODPatches)); 
            if (EnableMonsterAiPatches?.Value == true) _harmony.PatchAll(typeof(MonsterAiPatches)); 
            Logger.LogInfo("VBNetTweaks: серверные патчи успешно применены."); 
        }

        private System.Collections.IEnumerator DelayedServerConfigInit()
        {
            // Ждем пока ZNet инициализируется
            int maxAttempts = 100;
            for (int i = 0; i < maxAttempts; i++)
            {
                if (ZNet.instance) break;
                yield return new WaitForSeconds(0.25f);
            }

            // Инициализируем серверные настройки
            if (Helper.IsServer())
            {
                EnableNetSync = Config.BindConfig("02 - Network", "EnableNetSync", true, "Включить новую систему синхронизации NetSync", synced: true);
                SendInterval = Config.BindConfig("02 - Network", "SendInterval", 0.05f, "Интервал отправки данных (секунды) - ТОЛЬКО СЕРВЕР", synced: true);
                PeersPerUpdate = Config.BindConfig("02 - Network", "PeersPerUpdate", 20, "Количество пиров для обработки за один апдейт - ТОЛЬКО СЕРВЕР", synced: true);
                SceneDebugEnabled = Config.BindConfig("03 - Scene Optimizations", "SceneDebugEnabled", false, "Включить отладочный вывод для сцены", synced: true);

                EnableAILOD = Config.BindConfig("04 - AI", "EnableAILOD", true, "Throttle distant AI update frequency (server-only).", synced: true);
                AILODNearDistance = Config.BindConfig("04 - AI", "AILODNearDistance", 100f, "Full-speed AI within this distance (m).", synced: true);
                AILODFarDistance = Config.BindConfig("04 - AI", "AILODFarDistance", 300f, "Throttle AI beyond this distance (m).", synced: true);
                AILODThrottleFactor = Config.BindConfig("04 - AI", "AILODThrottleFactor", 0.5f, "Throttle factor (0.5 = half speed).", synced: true);

                EnableZDOThrottling = Config.BindConfig("02 - Network", "EnableZDOThrottling", true, "Reduce update rate for distant ZDOs (server-only).", synced: true);
                ZDOThrottleDistance = Config.BindConfig("02 - Network", "ZDOThrottleDistance", 500f, "Distance beyond which ZDOs are throttled (m).", synced: true);

                EnablePlayerPositionBoost = Config.BindConfig("05 - Player Sync", "EnableHighFrequencyPositionUpdates", true, "Increase priority of player ZDOs.", synced: true);
                PlayerPositionUpdateMultiplier = Config.BindConfig("05 - Player Sync", "PositionUpdateMultiplier", 2.5f, "Multiplier for player position priority.", synced: true);

                EnableClientInterpolation = Config.Bind("05 - Player Sync", "EnableClientInterpolation", true, "Smooth remote players on client.");
                EnablePlayerPrediction = Config.Bind("05 - Player Sync", "EnableClientPrediction", true, "Extrapolate remote players for ultra-smooth look.");

                EnableMonsterAiPatches = Config.Bind("06 - Gameplay", "EnableMonsterAiPatches", true, "Use all players instead of m_localPlayer for events/spawn.");

                EnableSteamSendRate = Config.Bind("02 - Network", "EnableSteamSendRateOverride", true, "Apply Steamworks send rates on startup.");
                SteamSendRateMinKB = Config.Bind("02 - Network", "SteamSendRateMinKB", 256, "Min send rate in KB/s.");
                SteamSendRateMaxKB = Config.Bind("02 - Network", "SteamSendRateMaxKB", 1024, "Max send rate in KB/s.");
                SteamSendBufferSize = Config.Bind("02 - Network", "SteamSendBufferBytes", 100_000_000, "Global send buffer size in bytes.");
                
                _serverConfigsInitialized = true;
                Logger.LogInfo("Серверные настройки VBNetTweaks инициализированы");
            }
            else Logger.LogInfo("VBNetTweaks работает в клиентском режиме");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        // Дебаг-логирование
        public static void LogDebug(string message)
        {
            if (DebugEnabled.Value) Debug.Log($"[VBNetTweaks] {message}");
        }

        public static void LogVerbose(string message)
        {
            if (DebugEnabled.Value && VerboseLogging.Value) Debug.Log($"[VBNetTweaks] {message}");
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

        public static float GetSendInterval()
        {
            return (!_serverConfigsInitialized) ? 0.05f : (SendInterval?.Value ?? 0.05f);
        }

        public static int GetPeersPerUpdate()
        {
            return (!_serverConfigsInitialized) ? 20 : (PeersPerUpdate?.Value ?? 20);
        }
    }
}
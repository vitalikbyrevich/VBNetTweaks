using Jotunn.Extensions;

namespace VBNetTweaks
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class VBNetTweaks : BaseUnityPlugin
    {
        private const string ModName = "VBNetTweaks";
        private const string ModVersion = "0.0.7";
        private const string ModGUID = "VitByr.VBNetTweaks";

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

            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), ModGUID);
            
            // Отложенная инициализация серверных настроек
            StartCoroutine(DelayedServerConfigInit());
            
            Logger.LogInfo("VBNetTweaks загружен!");
            if (DebugEnabled.Value) Logger.LogInfo("Режим отладки включен");
        }

        private System.Collections.IEnumerator DelayedServerConfigInit()
        {
            // Ждем пока ZNet инициализируется
            int maxAttempts = 100;
            for (int i = 0; i < maxAttempts; i++)
            {
                if (ZNet.instance) break;
                yield return new WaitForSeconds(0.1f);
            }

            // Инициализируем серверные настройки
            if (Helper.IsServer())
            {
                EnableNetSync = Config.BindConfig("02 - Network", "EnableNetSync", true, "Включить новую систему синхронизации NetSync", synced: true);
                SendInterval = Config.BindConfig("02 - Network", "SendInterval", 0.05f, "Интервал отправки данных (секунды) - ТОЛЬКО СЕРВЕР", synced: true);
                PeersPerUpdate = Config.BindConfig("02 - Network", "PeersPerUpdate", 20, "Количество пиров для обработки за один апдейт - ТОЛЬКО СЕРВЕР", synced: true);
                SceneDebugEnabled = Config.BindConfig("03 - Scene Optimizations", "SceneDebugEnabled", false, "Включить отладочный вывод для сцены", synced: true);

                
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
        
        // Методы для безопасного доступа к серверным настройкам
        public static float GetSendInterval() => _serverConfigsInitialized ? SendInterval?.Value ?? 0.05f : 0.05f;
        public static int GetPeersPerUpdate() => _serverConfigsInitialized ? PeersPerUpdate?.Value ?? 35 : 35;
    }
}
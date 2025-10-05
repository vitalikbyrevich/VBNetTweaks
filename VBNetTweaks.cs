namespace VBNetTweaks
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class VBNetTweaks : BaseUnityPlugin
    {
        private const string ModName = "VBNetTweaks";
        private const string ModVersion = "0.0.1";
        private const string ModGUID = "VitByr.VBNetTweaks";

        // Конфигурационные параметры
        public static ConfigEntry<bool> DebugEnabled;
        public static ConfigEntry<bool> VerboseLogging;
        public static ConfigEntry<float> SendInterval;
        public static ConfigEntry<int> PeersPerUpdate;
        public static ConfigEntry<float> Vec3CullSize;
        public static ConfigEntry<float> NetRatePhysics;
        public static ConfigEntry<float> NetRateNPC;
        public static ConfigEntry<int> SteamSendBufferSize;
        public static ConfigEntry<int> SteamTransferRate;

        // Статические переменные
        public static double NetTime;
        public static float DeltaTimeFixedPhysics = 0.02f;
        public static float DeltaTimePhysics = 0.01f;
        public static float Vec3CullSizeSq = 0.00025f;


        private Harmony _harmony;

        private void Awake()
        {
            // Определяем, являемся ли мы сервером
         //   CheckServerStatus();

            // Всегда создаем основные настройки
            ConfigurationManagerAttributes isAdminOnly = new ConfigurationManagerAttributes { IsAdminOnly = true };
            
            DebugEnabled = Config.Bind("01 - General", "DebugEnabled", false, new ConfigDescription("Включить отладочный вывод"));
            VerboseLogging = Config.Bind("01 - General", "VerboseLogging", false, new ConfigDescription("Включить подробное логирование успешных операций"));

            // Настройки фильтрации (нужны и клиенту и серверу)
            Vec3CullSize = Config.Bind("02 - Filtering", "Vec3CullSize", 0.05f, new ConfigDescription("Минимальное изменение позиции для отправки"));
            NetRatePhysics = Config.Bind("02 - Filtering", "NetRatePhysics", 8f, new ConfigDescription("Частота обновления физических объектов"));
            NetRateNPC = Config.Bind("02 - Filtering", "NetRateNPC", 8f, new ConfigDescription("Частота обновления NPC"));

            SendInterval = Config.Bind("03 - Network", "SendInterval", 0.05f, new ConfigDescription("Интервал отправки данных (секунды) - ТОЛЬКО СЕРВЕР", null, isAdminOnly));
            PeersPerUpdate = Config.Bind("03 - Network", "PeersPerUpdate", 1, new ConfigDescription("Количество пиров для обработки за один апдейт - ТОЛЬКО СЕРВЕР", null, isAdminOnly));
            SteamSendBufferSize = Config.Bind("04 - Steam", "SendBufferSize", 100000000, new ConfigDescription("Размер буфера отправки Steam - ТОЛЬКО СЕРВЕР", null, isAdminOnly));
            SteamTransferRate = Config.Bind("04 - Steam", "TransferRate", 50000000, new ConfigDescription("Лимит передачи данных Steam - ТОЛЬКО СЕРВЕР", null, isAdminOnly));
            // Настройки сцены (новые!)
            UnifiedSceneOptimizations.BindSceneConfig(Config);
            
            Vec3CullSizeSq = Vec3CullSize.Value * Vec3CullSize.Value;

            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), ModGUID);
            Logger.LogInfo("VBNetTweaks загружен!");

            if (DebugEnabled.Value) Logger.LogInfo("Режим отладки включен");
            if (UnifiedSceneOptimizations.EnableDoorOwnership.Value) Logger.LogInfo("Оптимизация дверей включена");
            if (UnifiedSceneOptimizations.EnableSceneOptimizations.Value) Logger.LogInfo("Оптимизация сцены включена");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
        
        // Методы для обновления состояния
        public static void UpdateState(float deltaTime)
        {
            NetTime += deltaTime;
            if (deltaTime > 100f) NetTime -= deltaTime;
        }

        public static bool ShouldUpdateZDO(ZDO zdo, float netRate, float deltaTime)
        {
            double zdoTime = NetTime + 0.023 * (zdo.m_uid.ID & 0xFFFu);
            double nextTime = zdoTime + deltaTime;
            return Mathf.RoundToInt((float)(zdoTime * netRate)) != Mathf.RoundToInt((float)(nextTime * netRate));
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

        // Методы для безопасного доступа к серверным настройкам
        public static float GetSendInterval() => SendInterval?.Value ?? 0.05f;
        public static int GetPeersPerUpdate() => PeersPerUpdate?.Value ?? 1;
        public static int GetSteamSendBufferSize() => SteamSendBufferSize?.Value ?? 100000000;
        public static int GetSteamTransferRate() => SteamTransferRate?.Value ?? 50000000;
    }

    // Патч для обновления времени
    [HarmonyPatch(typeof(MonoUpdaters), "FixedUpdate")]
    public static class MonoUpdaters_FixedUpdate_Patch
    {
        private static void Prefix()
        {
            VBNetTweaks.DeltaTimeFixedPhysics = Time.fixedDeltaTime;
            VBNetTweaks.UpdateState(0f);
        }
    }

    [HarmonyPatch(typeof(MonoUpdaters), "LateUpdate")]
    public static class MonoUpdaters_LateUpdate_Patch
    {
        private static void Prefix()
        {
            VBNetTweaks.DeltaTimePhysics = Time.deltaTime;
            VBNetTweaks.UpdateState(VBNetTweaks.DeltaTimePhysics);
        }
    }
}
namespace VBNetTweaks
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class VBNetTweaks : BaseUnityPlugin
    {
        private const string ModName = "VBNetTweaks";
        private const string ModVersion = "0.0.1";

        private const string ModGUID = "VitByr.VBNetTweaks";

        // Конфигурационные параметры
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> DebugEnabled;
        public static ConfigEntry<float> SendInterval;
        public static ConfigEntry<int> PeersPerUpdate;
        public static ConfigEntry<float> Vec3CullSize;
        public static ConfigEntry<float> NetRatePhysics;
        public static ConfigEntry<float> NetRateNPC;
        public static ConfigEntry<int> SteamSendBufferSize;
        public static ConfigEntry<int> SteamTransferRate;

        // Статические переменные
        public static double NetTime = 0.0;
        public static float DeltaTimeFixedPhysics = 0.02f;
        public static float DeltaTimePhysics = 0.01f;
        public static float Vec3CullSizeSq = 0.00025f;

        private Harmony _harmony;

        private void Awake()
        {
            // Настройки общей сети
            Enabled = Config.Bind("General", "Enabled", true, "Включить оптимизации сети");
            DebugEnabled = Config.Bind("General", "DebugEnabled", false, "Включить отладочный вывод");
            SendInterval = Config.Bind("Network", "SendInterval", 0.05f, "Интервал отправки данных (секунды)");
            PeersPerUpdate = Config.Bind("Network", "PeersPerUpdate", 1, "Количество пиров для обработки за один апдейт");

            // Настройки фильтрации
            Vec3CullSize = Config.Bind("Filtering", "Vec3CullSize", 0.05f, "Минимальное изменение позиции для отправки");
            NetRatePhysics = Config.Bind("Filtering", "NetRatePhysics", 8f, "Частота обновления физических объектов");
            NetRateNPC = Config.Bind("Filtering", "NetRateNPC", 8f, "Частота обновления NPC");

            // Настройки Steam
            SteamSendBufferSize = Config.Bind("Steam", "SendBufferSize", 100000000, "Размер буфера отправки Steam");
            SteamTransferRate = Config.Bind("Steam", "TransferRate", 50000000, "Лимит передачи данных Steam");

            // Применяем настройки
            Vec3CullSizeSq = Vec3CullSize.Value * Vec3CullSize.Value;

            if (Enabled.Value)
            {
                _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), "com.yourname.UnifiedNetworkOptimizations");
                Logger.LogInfo("Unified Network Optimizations загружен!");

                if (DebugEnabled.Value) Logger.LogInfo("Режим отладки включен");
            }
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
            if (DebugEnabled.Value) Debug.Log($"[UNO] {message}");
        }
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
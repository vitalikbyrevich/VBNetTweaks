namespace VBNetTweaks.Patches
{
    [HarmonyPatch]
    public static class ZDOSortPriority
    {
        private const byte PLAYER     = 0;
        private const byte MOBILE     = 1; // Characters + Ships
        private const byte PROJECTILE = 2;
        private const byte REST       = 3;
        private const int BUCKET_COUNT = 4;

        private static readonly List<ZDO>[] Buckets = new List<ZDO>[BUCKET_COUNT]
        {
            new List<ZDO>(32),   // Player
            new List<ZDO>(64),   // Mobile
            new List<ZDO>(64),   // Projectile
            new List<ZDO>(512)   // Rest
        };

        private static readonly int[] Counts = new int[BUCKET_COUNT];
        private static readonly Dictionary<int, byte> _cache = new Dictionary<int, byte>(256);
        private static readonly int PlayerHash = "Player".GetStableHashCode();

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSortSendZDOS))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void ServerSortSendZDOS_Postfix(List<ZDO> objects)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return;
            Partition(objects);
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ClientSortSendZDOS))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void ClientSortSendZDOS_Postfix(List<ZDO> objects)
        {
            if (!VBNetTweaks.c_ModuleZDOOptimization.Value) return;
            Partition(objects);
        }

        private static void Partition(List<ZDO> objects)
        {
            if (objects == null || objects.Count < 2) return;

            for (int i = 0; i < BUCKET_COUNT; i++)
            {
                Buckets[i].Clear();
                Counts[i] = 0;
            }

            bool alreadySorted = true;
            byte prevRank = 0;

            for (int i = 0; i < objects.Count; i++)
            {
                ZDO zdo = objects[i];
                byte rank = Classify(zdo);
                if (rank < prevRank) alreadySorted = false;
                prevRank = rank;
                Buckets[rank].Add(zdo);
                Counts[rank]++;
            }

            if (!alreadySorted)
            {
                int idx = 0;
                for (int b = 0; b < BUCKET_COUNT; b++)
                {
                    var bucket = Buckets[b];
                    for (int i = 0; i < bucket.Count; i++) objects[idx++] = bucket[i];
                }
            }
        }

        private static byte Classify(ZDO zdo)
        {
            if (zdo == null) return REST;
            int prefab = zdo.GetPrefab();

            if (prefab == PlayerHash) return PLAYER;

            if (_cache.TryGetValue(prefab, out byte cached)) return cached;

            byte result = ClassifyUncached(zdo, prefab);
            // Кэшируем только если сцена готова (иначе GetPrefab вернёт null)
            if (ZNetScene.instance)
            {
                _cache[prefab] = result;
            }
            return result;
        }

        private static byte ClassifyUncached(ZDO zdo, int prefab)
        {
            if (!ZNetScene.instance) return REST;

            GameObject go = ZNetScene.instance.GetPrefab(prefab);
            if (!go)
            {
                // Если префаб не найден, но объект приоритетный — считаем мобильным
                return zdo.Type == ZDO.ObjectType.Prioritized ? MOBILE : REST;
            }

            // Снаряды
            if (go.GetComponent<Projectile>()) return PROJECTILE;

            // Мобы (все персонажи кроме игрока — игрок уже отфильтрован выше)
            if (go.GetComponent<Character>()) return MOBILE;

            // Корабли — критично для синхронизации
            if (go.GetComponent<Ship>()) return MOBILE;

            // Приоритетные объекты ванилы (корабли с игроками и т.п.)
            if (zdo.Type == ZDO.ObjectType.Prioritized) return MOBILE;

            return REST;
        }

        // Очистка кэша при выгрузке мира
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ShutDown))]
        [HarmonyPostfix]
        private static void ClearCache() => _cache.Clear();
    }
}
namespace VBNetTweaks.Utils
{
    [HarmonyPatch]
    public static class ObjectPool
    {
        private static class Pool<T>
        {
            public static readonly Stack<T> Stack = new();
            public static int MaxSize = 256;
        }

        private static readonly Stack<ZPackage> _pkgPool = new();
        private const int MaxPkgPoolSize = 128;
        
        public static List<T> RentList<T>()
        {
            if (Pool<List<T>>.Stack.Count > 0)
            {
                var list = Pool<List<T>>.Stack.Pop();
                list.Clear();
                return list;
            }
            
            LogAlloc($"List<{typeof(T).Name}>");
            return new List<T>();
        }

        public static void ReturnList<T>(List<T> list)
        {
            if (list == null) return;
            list.Clear();
            
            if (Pool<List<T>>.Stack.Count < Pool<List<T>>.MaxSize) Pool<List<T>>.Stack.Push(list);
        }

        public static ZPackage RentPackage()
        {
            if (_pkgPool.Count > 0)
            {
                var pkg = _pkgPool.Pop();
                pkg.Clear();
                LogReuse("ZPackage");
                return pkg;
            }

            LogAlloc("ZPackage");
            return new ZPackage();
        }

        public static void ReturnPackage(ZPackage pkg)
        {
            if (pkg == null) return;
            pkg.Clear();
            
            if (_pkgPool.Count < MaxPkgPoolSize) _pkgPool.Push(pkg);
        }

        [Conditional("DEBUG")]
        private static void LogAlloc(string type)
        {
            if (ModConfig.DebugEnabled.Value && ModConfig.VerboseLogging.Value) Helper.LogVerbose($"ObjectPool: allocated new {type}");
        }

        [Conditional("DEBUG")]
        private static void LogReuse(string type)
        {
            if (ModConfig.DebugEnabled.Value && ModConfig.VerboseLogging.Value) Helper.LogVerbose($"ObjectPool: reused {type}");
        }
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        [HarmonyPostfix]
        public static void OnSceneAwake()
        {
            _pkgPool.Clear();
            Pool<List<ZDO>>.Stack.Clear();
            Pool<List<Player>>.Stack.Clear();
            Pool<List<ZPackage>>.Stack.Clear();
        
            Helper.LogVerbose("ObjectPool cleared on scene load");
        }
    }
}
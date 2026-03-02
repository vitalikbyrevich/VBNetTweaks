namespace VBNetTweaks
{
    public static class RpcBatcher
    {
        private class RpcEntry
        {
            public string Name;
            public object[] Args;
            public int Priority;
        }

        private static readonly Dictionary<ZNetView, List<RpcEntry>> rpcQueue = new();
        private static readonly object _lock = new object();
        private static float lastSendTime;
        private const float SendInterval = 0.05f;

        public static void Enqueue(ZNetView nview, string rpcName, int priority, params object[] args)
        {
            if (!nview || !nview.IsValid()) return;

            lock (_lock) // Захватываем блокировку на время работы со словарем
            {
                if (!rpcQueue.TryGetValue(nview, out var list))
                {
                    list = new List<RpcEntry>();
                    rpcQueue[nview] = list;
                }

                list.Add(new RpcEntry
                {
                    Name = rpcName,
                    Args = args,
                    Priority = priority
                });
            }
        }

        public static void Update()
        {
            float now = Time.time;
            if (now - lastSendTime < SendInterval) return;
            lastSendTime = now;

            // Важно: блокируем только на время копирования данных
            Dictionary<ZNetView, List<RpcEntry>> queueCopy;
            lock (_lock)
            {
                if (rpcQueue.Count == 0) return;

                // Создаем копию очереди под блокировкой
                queueCopy = new Dictionary<ZNetView, List<RpcEntry>>(rpcQueue.Count);
                foreach (var kvp in rpcQueue)
                {
                    queueCopy[kvp.Key] = new List<RpcEntry>(kvp.Value);
                    kvp.Value.Clear(); // Очищаем оригинал
                }
            }

            // Отправляем без блокировки - это может занять время
            foreach (var kvp in queueCopy)
            {
                var nview = kvp.Key;
                var list = kvp.Value;

                if (!nview || !nview.IsValid()) continue;
                if (list.Count == 0) continue;

                list.Sort((a, b) => b.Priority.CompareTo(a.Priority));

                ZPackage pkg = ObjectPool.RentPackage();
                try
                {
                    pkg.Write(list.Count);
                    foreach (var entry in list)
                    {
                        pkg.Write(entry.Name);
                        pkg.Write(entry.Args.Length);
                        for (int i = 0; i < entry.Args.Length; i++) RpcSerializer.WriteArg(pkg, entry.Args[i]);
                    }
                    nview.InvokeRPC("VBNT_RPCBatch", pkg);
                }
                finally
                {
                    ObjectPool.ReturnPackage(pkg);
                }
            }
        }

        // Обработчик батча (регистрируется в ZRoutedRpc)
        public static void HandleBatch(long sender, ZPackage pkg)
        {
            int count = pkg.ReadInt();

            for (int i = 0; i < count; i++)
            {
                string rpcName = pkg.ReadString();
                int argCount = pkg.ReadInt();

                object[] args = new object[argCount];
                for (int j = 0; j < argCount; j++) args[j] = RpcSerializer.ReadArg(pkg);

                if (VBNetTweaks.DebugEnabled.Value && VBNetTweaks.VerboseLogging.Value) VBNetTweaks.LogDebug($"RpcBatcher: executing {rpcName} ({argCount} args)");

                ZRoutedRpc.instance.InvokeRoutedRPC(sender, rpcName, args);
            }
        }
    }
}
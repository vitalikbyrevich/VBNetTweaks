namespace VBNetTweaks
{
    public static class RpcBatcher
    {
        private class RpcEntry
        {
            public string Name;
            public ZDOID TargetZDO;
            public object[] Args;
            public int Priority;
        }

        private static readonly Dictionary<ZNetView, List<RpcEntry>> rpcQueue = new();
        private static readonly object _lock = new object();

        private static float lastSendTime;

        public static void Enqueue(ZNetView nview, string rpcName, int priority, params object[] args)
        {
            if (!nview || !nview.IsValid()) return;

            lock (_lock)
            {
                if (!rpcQueue.TryGetValue(nview, out var list))
                {
                    list = new List<RpcEntry>();
                    rpcQueue[nview] = list;
                }

                list.Add(new RpcEntry
                {
                    Name = rpcName,
                    TargetZDO = nview.m_zdo?.m_uid ?? ZDOID.None,
                    Args = args,
                    Priority = priority
                });
            }
        }

        public static void Update()
        {
           // if (!Helper.IsServer()) return;

            float now = Time.time;

            float baseInterval = ModConfig.SendInterval?.Value ?? 0.05f;
            float interval = AdaptiveThrottler.GetInterval(baseInterval);

            if (now - lastSendTime < interval) return;

            lastSendTime = now;

            Dictionary<ZNetView, List<RpcEntry>> queueCopy;

            lock (_lock)
            {
                if (rpcQueue.Count == 0) return;

                var deadKeys = new List<ZNetView>();
                foreach (var kvp in rpcQueue)
                {
                    if (!kvp.Key || !kvp.Key.IsValid()) deadKeys.Add(kvp.Key);
                }
                foreach (var dead in deadKeys)
                {
                    rpcQueue.Remove(dead);
                }

                if (rpcQueue.Count == 0)
                    return;

                queueCopy = new Dictionary<ZNetView, List<RpcEntry>>(rpcQueue.Count);
                foreach (var kvp in rpcQueue)
                {
                    queueCopy[kvp.Key] = new List<RpcEntry>(kvp.Value);
                    kvp.Value.Clear();
                }
            }

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
                        pkg.Write(entry.TargetZDO);
                        pkg.Write(entry.Args.Length);
                        
                        for (int i = 0; i < entry.Args.Length; i++)
                        {
                            RpcSerializer.WriteArg(pkg, entry.Args[i]);
                        }
                    }

                    nview.InvokeRPC("VBNT_RPCBatch", pkg);
                }
                finally
                {
                    ObjectPool.ReturnPackage(pkg);
                }
            }
        }

        public static void HandleBatch(long sender, ZPackage pkg)
        {
            var routedRpc = ZRoutedRpc.instance;
            if (routedRpc == null)
            {
                Helper.LogDebug("RpcBatcher: ZRoutedRpc.instance is null");
                return;
            }

            try
            {
                int count = pkg.ReadInt();
                
                if (ModConfig.DebugEnabled.Value && ModConfig.VerboseLogging.Value)
                {
                    Helper.LogDebug($"RpcBatcher: received batch with {count} RPCs from sender {sender}");
                }

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        string rpcName = pkg.ReadString();
                        ZDOID targetZDO = pkg.ReadZDOID();
                        int argCount = pkg.ReadInt();

                        object[] args = new object[argCount];
                        for (int j = 0; j < argCount; j++)
                        {
                            args[j] = RpcSerializer.ReadArg(pkg);
                        }

                        if (ModConfig.DebugEnabled.Value && ModConfig.VerboseLogging.Value)
                        {
                            Helper.LogDebug($"RpcBatcher: processing {rpcName} (targetZDO: {targetZDO}, {argCount} args)");
                        }

                        var rpcData = new ZRoutedRpc.RoutedRPCData
                        {
                            m_msgID = routedRpc.m_id + (routedRpc.m_rpcMsgID++),
                            m_senderPeerID = sender,
                            m_targetPeerID = 0L,
                            m_targetZDO = targetZDO,
                            m_methodHash = rpcName.GetStableHashCode()
                        };

                        var argsPkg = new ZPackage();
                        foreach (var arg in args)
                        {
                            if (arg is int intVal) argsPkg.Write(intVal);
                            else if (arg is float floatVal) argsPkg.Write(floatVal);
                            else if (arg is string strVal) argsPkg.Write(strVal);
                            else if (arg is bool boolVal) argsPkg.Write(boolVal);
                            else if (arg is Vector3 v3Val) argsPkg.Write(v3Val);
                            else if (arg is Quaternion qVal) argsPkg.Write(qVal);
                            else if (arg is long longVal) argsPkg.Write(longVal);
                            else if (arg is double doubleVal) argsPkg.Write(doubleVal);
                            else if (arg is ZDOID zdoIdVal) argsPkg.Write(zdoIdVal);
                            else if (arg is ZPackage pkgVal) argsPkg.Write(pkgVal);
                            else if (arg is byte[] bytesVal) argsPkg.Write(bytesVal);
                            else Helper.LogDebug($"RpcBatcher: unsupported arg type {arg?.GetType()} in {rpcName}");
                        }
                        argsPkg.SetPos(0);
                        rpcData.m_parameters = argsPkg;

                        routedRpc.HandleRoutedRPC(rpcData);
                    }
                    catch (Exception ex)
                    {
                        Helper.LogDebug($"RpcBatcher: error processing RPC #{i} in batch: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Helper.LogDebug($"RpcBatcher: error in HandleBatch: {ex.Message}");
            }
        }

        public static void SendImmediate(ZNetView nview, string rpcName, params object[] args)
        {
            if (!nview || !nview.IsValid()/* || !Helper.IsServer()*/) return;

            if (ModConfig.DebugEnabled.Value && ModConfig.VerboseLogging.Value)
            {
                Helper.LogDebug($"RpcBatcher: sending immediate RPC {rpcName}");
            }

            ZPackage pkg = ObjectPool.RentPackage();
            try
            {
                // Для немедленной отправки отправляем как одиночный батч
                pkg.Write(1); // Один RPC
                pkg.Write(rpcName);
                pkg.Write(nview.m_zdo?.m_uid ?? ZDOID.None);
                pkg.Write(args.Length);
                
                foreach (var arg in args)
                {
                    RpcSerializer.WriteArg(pkg, arg);
                }

                nview.InvokeRPC("VBNT_RPCBatch", pkg);
            }
            finally
            {
                ObjectPool.ReturnPackage(pkg);
            }
        }
    }
}
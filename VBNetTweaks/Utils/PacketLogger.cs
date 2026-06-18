using System.Text;

namespace VBNetTweaks.Utils
{
    public static class PacketLoggerConfig
    {
        public static bool IsEnabled = true;
        public static bool LogRawData = false;
        public static bool LogOnlyLargePackets = false;
        public static int LargePacketThreshold = 256;
    }

    public class PacketLogEntry
    {
        public DateTime Timestamp;
        public string Direction;
        public string PeerInfo;
        public string MethodName;
        public int MethodHash;
        public int Size;
        public string ContentType;
        public string Summary;
        public byte[] RawData;
        
        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss.fff}] {Direction,-4} | {PeerInfo,-20} | {Size,6} bytes | {MethodName ?? ContentType,-30} | {Summary ?? "-"}";
        }
        
        public string ToDetailedString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== Packet at {Timestamp:yyyy-MM-dd HH:mm:ss.fff} ===");
            sb.AppendLine($"Direction: {Direction}");
            sb.AppendLine($"Peer: {PeerInfo}");
            sb.AppendLine($"Size: {Size} bytes");
            sb.AppendLine($"Type: {ContentType}");
            if (MethodName != null)
                sb.AppendLine($"Method: {MethodName} (hash: {MethodHash})");
            sb.AppendLine($"Summary: {Summary ?? "-"}");
            
            if (PacketLoggerConfig.LogRawData && RawData != null && RawData.Length > 0)
            {
                sb.AppendLine("Raw data (hex):");
                sb.AppendLine(HexDump(RawData));
            }
            sb.AppendLine();
            return sb.ToString();
        }
        
        private static string HexDump(byte[] bytes, int maxBytes = 256)
        {
            int len = Math.Min(bytes.Length, maxBytes);
            var sb = new StringBuilder();
            for (int i = 0; i < len; i += 16)
            {
                int lineLen = Math.Min(16, len - i);
                for (int j = 0; j < lineLen; j++)
                    sb.Append($"{bytes[i + j]:X2} ");
                for (int j = lineLen; j < 16; j++)
                    sb.Append("   ");
                sb.Append(" | ");
                for (int j = 0; j < lineLen; j++)
                {
                    char c = (char)bytes[i + j];
                    sb.Append(char.IsControl(c) ? '.' : c);
                }
                sb.AppendLine();
            }
            if (bytes.Length > maxBytes)
                sb.AppendLine($"... and {bytes.Length - maxBytes} more bytes");
            return sb.ToString();
        }
    }

    public static class PacketLogger
    {
        private static StreamWriter m_writer;
        private static string m_logPath;
        private static readonly object m_lock = new object();
        
        public static void Initialize()
        {
            try
            {
                string configDir = BepInEx.Paths.ConfigPath;
                m_logPath = Path.Combine(configDir, "ValheimPacketLog.txt");
                
                if (File.Exists(m_logPath))
                {
                    string backup = Path.Combine(configDir, $"ValheimPacketLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.Move(m_logPath, backup);
                    Debug.Log($"[PacketLogger] Rotated old log to {backup}");
                }
                
                m_writer = new StreamWriter(m_logPath, false, Encoding.UTF8);
                m_writer.AutoFlush = true;
                
                WriteHeader();
                Debug.Log($"[PacketLogger] Initialized, logging to {m_logPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PacketLogger] Failed to initialize: {ex.Message}");
            }
        }
        
        private static void WriteHeader()
        {
            m_writer.WriteLine("=".PadRight(100, '='));
            m_writer.WriteLine($"Valheim Packet Log - Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            m_writer.WriteLine($"Large packet threshold: {PacketLoggerConfig.LargePacketThreshold} bytes");
            m_writer.WriteLine($"Raw data logging: {PacketLoggerConfig.LogRawData}");
            m_writer.WriteLine("=".PadRight(100, '='));
            m_writer.WriteLine();
            m_writer.WriteLine("Format: [TIME] DIR | PEER | SIZE | METHOD | SUMMARY");
            m_writer.WriteLine("-".PadRight(100, '-'));
        }
        
        public static void LogPacket(PacketLogEntry entry)
        {
            if (!PacketLoggerConfig.IsEnabled) return;
            
            if (PacketLoggerConfig.LogOnlyLargePackets && entry.Size < PacketLoggerConfig.LargePacketThreshold)
                return;
            
            lock (m_lock)
            {
                try
                {
                    if (m_writer == null) return;
                    
                    m_writer.WriteLine(entry.ToString());
                    
                    if (entry.Size > 2048)
                    {
                        m_writer.WriteLine(entry.ToDetailedString());
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PacketLogger] Failed to write log: {ex.Message}");
                }
            }
        }
        
        public static void Close()
        {
            lock (m_lock)
            {
                if (m_writer != null)
                {
                    m_writer.WriteLine();
                    m_writer.WriteLine($"Log closed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    m_writer.Close();
                    m_writer = null;
                }
            }
        }
    }

    // Патч для ZRpc - перехват отправляемых пакетов
    [HarmonyPatch(typeof(ZRpc))]
    static class ZRpcSendPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(ZRpc.SendPackage))]
        static void Prefix(ZRpc __instance, ZPackage pkg)
        {
            if (!PacketLoggerConfig.IsEnabled) return;
            
            try
            {
                if (pkg == null || pkg.Size() == 0) return;
                
                var entry = new PacketLogEntry
                {
                    Timestamp = DateTime.Now,
                    Direction = "SEND",
                    PeerInfo = GetPeerInfo(__instance),
                    Size = pkg.Size(),
                    RawData = PacketLoggerConfig.LogRawData ? pkg.GetArray() : null
                };
                
                AnalyzePackage(pkg, entry);
                PacketLogger.LogPacket(entry);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PacketLogger] Error in SendPatch: {ex.Message}");
            }
        }
        
        private static string GetPeerInfo(ZRpc rpc)
        {
            try
            {
                var socket = rpc.GetSocket();
                if (socket != null)
                {
                    string endpoint = socket.GetEndPointString();
                    if (!string.IsNullOrEmpty(endpoint))
                        return endpoint;
                }
                return "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
        
        private static void AnalyzePackage(ZPackage pkg, PacketLogEntry entry)
        {
            // Сохраняем позицию
            int originalPos = pkg.GetPos();
            pkg.SetPos(0);
            
            try
            {
                if (entry.Size >= 4)
                {
                    int methodHash = pkg.ReadInt();
                    entry.MethodHash = methodHash;
                    entry.ContentType = "RPC";
                    
                    // Определяем метод по хешу (известные хеши из кода Valheim)
                    entry.MethodName = GetMethodNameFromHash(methodHash);
                    entry.Summary = GetPacketSummary(pkg, methodHash);
                }
                else
                {
                    entry.ContentType = "PingPong";
                    entry.Summary = "Keepalive packet";
                }
            }
            catch
            {
                entry.ContentType = "Unknown";
                entry.Summary = "Failed to parse";
            }
            finally
            {
                pkg.SetPos(originalPos);
            }
        }
        
        private static string GetMethodNameFromHash(int hash)
        {
            // Известные хеши методов Valheim (собранные из анализа)
            // Используем int, так как GetStableHashCode возвращает int
            var knownHashes = new Dictionary<int, string>
            {
                // ZRoutedRpc
                { "RoutedRPC".GetStableHashCode(), "RoutedRPC" },
                { "DestroyZDO".GetStableHashCode(), "DestroyZDO" },
                { "RequestZDO".GetStableHashCode(), "RequestZDO" },
                { "SleepStart".GetStableHashCode(), "SleepStart" },
                { "SleepStop".GetStableHashCode(), "SleepStop" },
                { "Ping".GetStableHashCode(), "Ping" },
                { "Pong".GetStableHashCode(), "Pong" },
                
                // ZNet
                { "PeerInfo".GetStableHashCode(), "PeerInfo" },
                { "Disconnect".GetStableHashCode(), "Disconnect" },
                { "SavePlayerProfile".GetStableHashCode(), "SavePlayerProfile" },
                { "ServerHandshake".GetStableHashCode(), "ServerHandshake" },
                { "ClientHandshake".GetStableHashCode(), "ClientHandshake" },
                { "Kicked".GetStableHashCode(), "Kicked" },
                { "Error".GetStableHashCode(), "Error" },
                { "PlayerList".GetStableHashCode(), "PlayerList" },
                { "AdminList".GetStableHashCode(), "AdminList" },
                { "ServerSyncedPlayerData".GetStableHashCode(), "ServerSyncedPlayerData" },
                { "NetTime".GetStableHashCode(), "NetTime" },
                { "CharacterID".GetStableHashCode(), "CharacterID" },
                { "Kick".GetStableHashCode(), "Kick" },
                { "Ban".GetStableHashCode(), "Ban" },
                { "Unban".GetStableHashCode(), "Unban" },
                { "Save".GetStableHashCode(), "Save" },
                { "RemotePrint".GetStableHashCode(), "RemotePrint" },
                { "RemoteCommand".GetStableHashCode(), "RemoteCommand" },
                { "PrintBanned".GetStableHashCode(), "PrintBanned" },
                
                // ZDOMan
                { "ZDOData".GetStableHashCode(), "ZDOData" },
                
                // Player/Ship
                { "OnDeath".GetStableHashCode(), "OnDeath" },
                { "RPC_HitWhileDodging".GetStableHashCode(), "HitWhileDodging" },
                { "Message".GetStableHashCode(), "Message" },
                { "OnTargeted".GetStableHashCode(), "OnTargeted" },
                { "UseStamina".GetStableHashCode(), "UseStamina" },
                { "RequestControl".GetStableHashCode(), "RequestControl" },
                { "ReleaseControl".GetStableHashCode(), "ReleaseControl" },
                { "RequestRespons".GetStableHashCode(), "RequestRespons" },
                { "Stop".GetStableHashCode(), "Stop" },
                { "Forward".GetStableHashCode(), "Forward" },
                { "Backward".GetStableHashCode(), "Backward" },
                { "Rudder".GetStableHashCode(), "Rudder" },
                { "RPC_TeleportTo".GetStableHashCode(), "TeleportTo" },
                { "RPC_AddNoise".GetStableHashCode(), "AddNoise" },
                { "RPC_AddAdrenaline".GetStableHashCode(), "AddAdrenaline" },
                { "RPC_Damage".GetStableHashCode(), "Damage" },
                { "RPC_Heal".GetStableHashCode(), "Heal" },
                { "RPC_Stagger".GetStableHashCode(), "Stagger" },
                { "RPC_ResetCloth".GetStableHashCode(), "ResetCloth" },
                { "RPC_SetTamed".GetStableHashCode(), "SetTamed" },
                { "RPC_FreezeFrame".GetStableHashCode(), "FreezeFrame" },
            };
            
            if (knownHashes.TryGetValue(hash, out string name))
                return name;
            
            return $"hash_{hash:X8}";
        }
        
        private static string GetPacketSummary(ZPackage pkg, int methodHash)
        {
            // Сохраняем позицию
            int pos = pkg.GetPos();
            
            try
            {
                // Для известных методов пытаемся извлечь полезную информацию
                switch (methodHash)
                {
                    case var h when h == "ZDOData".GetStableHashCode():
                        {
                            int numInvalid = pkg.ReadInt();
                            return $"ZDO update, {numInvalid} invalid sectors";
                        }
                    case var h when h == "PlayerList".GetStableHashCode():
                        {
                            int playerCount = pkg.ReadInt();
                            return $"Player list update, {playerCount} players";
                        }
                    case var h when h == "RoutedRPC".GetStableHashCode():
                        return "Routed RPC (nested)";
                    default:
                        return $"RPC call ({GetMethodNameFromHash(methodHash)})";
                }
            }
            catch
            {
                return "Unable to parse summary";
            }
        }
    }

    // Патч для получения входящих пакетов
    [HarmonyPatch(typeof(ZRpc))]
    static class ZRpcRecvPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(ZRpc.HandlePackage))]
        static void Prefix(ZRpc __instance, ZPackage package)
        {
            if (!PacketLoggerConfig.IsEnabled) return;
            if (package == null || package.Size() == 0) return;
            
            try
            {
                var entry = new PacketLogEntry
                {
                    Timestamp = DateTime.Now,
                    Direction = "RECV",
                    PeerInfo = GetPeerInfo(__instance),
                    Size = package.Size(),
                    RawData = PacketLoggerConfig.LogRawData ? package.GetArray() : null
                };
                
                // Анализируем как в отправке
                int originalPos = package.GetPos();
                package.SetPos(0);
                
                if (entry.Size >= 4)
                {
                    int methodHash = package.ReadInt();
                    entry.MethodHash = methodHash;
                    entry.MethodName = GetMethodNameFromHashStatic(methodHash);
                    entry.ContentType = "RPC";
                    entry.Summary = $"Received {entry.MethodName ?? $"hash_{methodHash:X8}"}";
                }
                
                package.SetPos(originalPos);
                PacketLogger.LogPacket(entry);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PacketLogger] Error in RecvPatch: {ex.Message}");
            }
        }
        
        private static string GetPeerInfo(ZRpc rpc)
        {
            try
            {
                var socket = rpc.GetSocket();
                if (socket != null)
                {
                    string endpoint = socket.GetEndPointString();
                    if (!string.IsNullOrEmpty(endpoint))
                        return endpoint;
                }
                return "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
        
        private static string GetMethodNameFromHashStatic(int hash)
        {
            // Те же хеши, что и в отправке
            var knownHashes = new Dictionary<int, string>
            {
                { "RoutedRPC".GetStableHashCode(), "RoutedRPC" },
                { "ZDOData".GetStableHashCode(), "ZDOData" },
                { "PlayerList".GetStableHashCode(), "PlayerList" },
                { "PeerInfo".GetStableHashCode(), "PeerInfo" },
                { "Disconnect".GetStableHashCode(), "Disconnect" },
                { "Kicked".GetStableHashCode(), "Kicked" },
                { "Error".GetStableHashCode(), "Error" },
                { "NetTime".GetStableHashCode(), "NetTime" },
                { "CharacterID".GetStableHashCode(), "CharacterID" },
                { "Message".GetStableHashCode(), "Message" },
                { "RPC_Damage".GetStableHashCode(), "Damage" },
            };
            
            if (knownHashes.TryGetValue(hash, out string name))
                return name;
            
            return null;
        }
    }
    
    // Инициализация
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    static class ZNetInitPatch
    {
        static void Postfix()
        {
            PacketLogger.Initialize();
        }
    }
    
    // Закрытие лога при выходе
    [HarmonyPatch(typeof(Game), nameof(Game.OnApplicationQuit))]
    static class GameQuitPatch
    {
        static void Prefix()
        {
            PacketLogger.Close();
        }
    }
}
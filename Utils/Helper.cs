namespace VBNetTweaks.Utils;

public enum GameServerClientState
{
    Unknown,
    Client,
    Server
}

public static class Helper
{
    public static bool IsServer()
    {
        try
        {
            // Безопасная проверка ZNet.instance
            var znet = ZNet.instance;
            if (!znet) return false;
            
            return znet.IsServer();
        }
        catch
        {
            return false;
        }
    }

    public static bool IsClient()
    {
        try
        {
            var znet = ZNet.instance;
            if (!znet) return false;
            
            return !znet.IsServer();
        }
        catch
        {
            return false;
        }
    }

    public static GameServerClientState GetGameServerClientState()
    {
        try
        {
            var znet = ZNet.instance;
            if (!znet) return GameServerClientState.Unknown;
            
            return znet.IsServer() ? GameServerClientState.Server : GameServerClientState.Client;
        }
        catch
        {
            return GameServerClientState.Unknown;
        }
    }
}
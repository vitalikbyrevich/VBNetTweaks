namespace VBNetTweaks.Utils
{
    public static class Helper
    {
        private static int _lastFrame = -1;
        private static bool _cachedIsServer;
        private static bool _cachedIsClient;

        public static bool IsServer()
        {
            if (_lastFrame == Time.frameCount) return _cachedIsServer;

            try
            {
                var znet = ZNet.instance;
                _cachedIsServer = znet && znet.IsServer();
                _cachedIsClient = znet && !znet.IsServer();
                _lastFrame = Time.frameCount;
                return _cachedIsServer;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsClient()
        {
            if (_lastFrame == Time.frameCount) return _cachedIsClient;

            IsServer();
            return _cachedIsClient;
        }
    }
}
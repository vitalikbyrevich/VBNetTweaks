namespace VBNetTweaks.RPCUtills;

public static class RPCPositionContext
{
    [ThreadStatic]
    private static Vector3 _currentPosition;
    
    public static void SetCurrentPosition(Vector3 pos) => _currentPosition = pos;
    public static Vector3 GetCurrentPosition() => _currentPosition;
    public static void Clear() => _currentPosition = Vector3.zero;
}
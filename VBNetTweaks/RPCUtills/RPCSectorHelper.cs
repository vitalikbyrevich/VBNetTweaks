namespace VBNetTweaks.RPCUtills;

public static class RPCSectorHelper
{
    public const int SECTOR_SIZE = 64; // 64 метра на сектор
    
    public static int CalculateSectorDistance(Vector3 pos1, Vector3 pos2)
    {
        Vector2i sector1 = ZoneSystem.GetZone(pos1);
        Vector2i sector2 = ZoneSystem.GetZone(pos2);
        
        return Math.Max(Math.Abs(sector1.x - sector2.x), 
            Math.Abs(sector1.y - sector2.y));
    }
    
    public static bool IsInSectorRange(Vector3 origin, Vector3 target, int sectorRadius)
    {
        return CalculateSectorDistance(origin, target) <= sectorRadius;
    }
}
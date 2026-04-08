namespace MTEngine.World;

public interface IMapStateSource
{
    MapData? GetMapOverride(string mapId);
}

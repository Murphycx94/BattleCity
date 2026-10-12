using Tank.Core.Math;

namespace Tank.Core.World;

public readonly record struct SpawnPoint(int X, int Z, byte Team);

public readonly record struct EagleSpawn(int X, int Z, byte Team);

/// <summary>Immutable parsed map: terrain plus spawn and base positions (all in sub units, top-left).</summary>
public sealed class MapDefinition
{
    public MapDefinition(
        string name,
        int widthCells,
        int heightCells,
        byte[] cells,
        IReadOnlyList<SpawnPoint> playerSpawns,
        IReadOnlyList<SpawnPoint> enemySpawns,
        IReadOnlyList<EagleSpawn> eagles)
    {
        Name = name;
        WidthCells = widthCells;
        HeightCells = heightCells;
        Cells = cells;
        PlayerSpawns = playerSpawns;
        EnemySpawns = enemySpawns;
        Eagles = eagles;
    }

    public string Name { get; }
    public int WidthCells { get; }
    public int HeightCells { get; }
    public IReadOnlyList<byte> Cells { get; }
    public IReadOnlyList<SpawnPoint> PlayerSpawns { get; }
    public IReadOnlyList<SpawnPoint> EnemySpawns { get; }
    public IReadOnlyList<EagleSpawn> Eagles { get; }

    public GameMap CreateMap()
    {
        var map = new GameMap(WidthCells, HeightCells);
        var buffer = new byte[Cells.Count];
        for (int i = 0; i < buffer.Length; i++) buffer[i] = Cells[i];
        map.LoadCells(buffer);
        return map;
    }

    public IEnumerable<SpawnPoint> PlayerSpawnsFor(byte team) => PlayerSpawns.Where(s => s.Team == team);

    public Box EagleBox(int index) => new(Eagles[index].X, Eagles[index].Z, Units.Tile, Units.Tile);
}

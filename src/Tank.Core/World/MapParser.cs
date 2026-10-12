namespace Tank.Core.World;

/// <summary>
/// Parses the text map format. Each character is one half-tile (8px = 2x2 cells):
/// <code>
///   .  empty      #  brick     @  steel     ~  water    %  forest    -  ice
///   P  player spawn (team 0)   Q  player spawn (team 1)   S  enemy spawn
///   E  eagle (team 0)          F  eagle (team 1)
/// </code>
/// Spawn and eagle markers mark the top-left half of a 1-tile (2x2 halves) area.
/// Lines starting with ';' are comments.
/// </summary>
public static class MapParser
{
    private const int CellsPerHalf = Units.Half / Units.Cell;

    public static MapDefinition Parse(string name, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = text.Replace("\r", string.Empty)
            .Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0 && !l.StartsWith(';'))
            .ToArray();
        if (rows.Length == 0) throw new FormatException($"Map '{name}' is empty.");

        int widthHalves = rows[0].Length;
        if (rows.Any(r => r.Length != widthHalves))
            throw new FormatException($"Map '{name}' rows must all have {widthHalves} characters.");
        if (widthHalves % 2 != 0 || rows.Length % 2 != 0)
            throw new FormatException($"Map '{name}' must have an even number of rows and columns.");

        int widthCells = widthHalves * CellsPerHalf;
        int heightCells = rows.Length * CellsPerHalf;
        var cells = new byte[widthCells * heightCells];
        var playerSpawns = new List<SpawnPoint>();
        var enemySpawns = new List<SpawnPoint>();
        var eagles = new List<EagleSpawn>();

        for (int hz = 0; hz < rows.Length; hz++)
        {
            for (int hx = 0; hx < widthHalves; hx++)
            {
                char c = rows[hz][hx];
                int x = hx * Units.Half, z = hz * Units.Half;
                switch (c)
                {
                    case 'P': playerSpawns.Add(new SpawnPoint(x, z, 0)); break;
                    case 'Q': playerSpawns.Add(new SpawnPoint(x, z, 1)); break;
                    case 'S': enemySpawns.Add(new SpawnPoint(x, z, Teams.Enemy)); break;
                    case 'E': eagles.Add(new EagleSpawn(x, z, 0)); break;
                    case 'F': eagles.Add(new EagleSpawn(x, z, 1)); break;
                    default: FillHalf(cells, widthCells, hx, hz, ToCell(c, name)); break;
                }
            }
        }

        if (playerSpawns.Count == 0) throw new FormatException($"Map '{name}' has no player spawn (P).");
        return new MapDefinition(name, widthCells, heightCells, cells, playerSpawns, enemySpawns, eagles);
    }

    private static CellType ToCell(char c, string name) => c switch
    {
        '.' => CellType.Empty,
        '#' => CellType.Brick,
        '@' => CellType.Steel,
        '~' => CellType.Water,
        '%' => CellType.Forest,
        '-' => CellType.Ice,
        _ => throw new FormatException($"Map '{name}' contains unknown character '{c}'."),
    };

    private static void FillHalf(byte[] cells, int widthCells, int hx, int hz, CellType type)
    {
        for (int dz = 0; dz < CellsPerHalf; dz++)
            for (int dx = 0; dx < CellsPerHalf; dx++)
                cells[(hz * CellsPerHalf + dz) * widthCells + hx * CellsPerHalf + dx] = (byte)type;
    }
}

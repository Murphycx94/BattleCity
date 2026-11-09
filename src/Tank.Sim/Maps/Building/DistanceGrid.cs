namespace Tank.Sim.Maps.Building;

/// <summary>
/// One value per height-field sample — sample (i, j) at (i·<see cref="CellSize"/>, j·<see cref="CellSize"/>), index
/// j·<see cref="SamplesX"/> + i — of how far it is from the nearest line stamped into it (<see cref="Polyline.Stamp"/>),
/// how far along that line its nearest point is, and which line it was. Samples no line reached keep +∞, 0 and −1.
/// </summary>
public sealed class DistanceGrid
{
    public const int NoLine = -1;

    public DistanceGrid(int samplesX, int samplesZ, float cellSize)
    {
        if (samplesX < 2 || samplesZ < 2) throw new ArgumentOutOfRangeException(nameof(samplesX), "Need at least 2×2 samples.");
        if (!float.IsFinite(cellSize) || cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be finite and positive.");
        SamplesX = samplesX;
        SamplesZ = samplesZ;
        CellSize = cellSize;
        int count = samplesX * samplesZ;
        Distance = new float[count];
        Array.Fill(Distance, float.PositiveInfinity);
        Along = new float[count];
        Line = new int[count];
        Array.Fill(Line, NoLine);
    }

    public int SamplesX { get; }
    public int SamplesZ { get; }
    public float CellSize { get; }

    /// <summary>Distance to the nearest stamped line, m; +∞ where none reached.</summary>
    public float[] Distance { get; }

    /// <summary>How far along that line (from its start) the nearest point is, m.</summary>
    public float[] Along { get; }

    /// <summary>The id the nearest line was stamped with; <see cref="NoLine"/> where none reached.</summary>
    public int[] Line { get; }

    /// <summary>
    /// <see cref="Distance"/> interpolated bilinearly at (x, z), clamped to the grid; +∞ when a sample that weighs in was
    /// never reached.
    /// </summary>
    public float DistanceAt(float x, float z)
    {
        float fx = Math.Clamp(x / CellSize, 0, SamplesX - 1);
        float fz = Math.Clamp(z / CellSize, 0, SamplesZ - 1);
        int i = Math.Min((int)fx, SamplesX - 2);
        int j = Math.Min((int)fz, SamplesZ - 2);
        float u = fx - i, v = fz - j;
        int at = j * SamplesX + i;
        return Weigh(Distance[at], (1 - u) * (1 - v)) + Weigh(Distance[at + 1], u * (1 - v))
             + Weigh(Distance[at + SamplesX], (1 - u) * v) + Weigh(Distance[at + SamplesX + 1], u * v);
    }

    /// <summary>value × weight, except that a sample with no weight adds nothing even when it is +∞ (∞ · 0 is NaN).</summary>
    private static float Weigh(float value, float weight) => weight > 0 ? value * weight : 0f;
}

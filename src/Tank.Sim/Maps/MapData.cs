using Tank.Sim.Terrain;

namespace Tank.Sim.Maps;

/// <summary>
/// A battlefield's fixed shape as data (stage 4 design §5.4, riverland design §4.5): the terrain's heights in whole
/// centimetres, the props, the border, the ground's kinds and the water level in whole centimetres. A map generator gives
/// the same output on one platform, not necessarily on another (it uses the maths library's sines and cosines), so
/// clients never generate match maps: the server builds its battlefield from this and sends the same data to its
/// clients, who build theirs from it too, and every player has the same ground and the same props under the same ids.
/// Rounding the heights and the water level to centimetres happens once, in <see cref="Of"/>; building a battlefield
/// and taking its data again gives the same data back.
/// </summary>
public sealed class MapData
{
    public const float HeightUnit = 0.01f;

    /// <summary>The water level may be at most this many centimetres (10 km) above or below zero.</summary>
    public const int MaxWaterLevelCm = 1_000_000;

    /// <summary>A map of grass everywhere, without water.</summary>
    public MapData(int samplesX, int samplesZ, float cellSize, int[] heightsCm, Prop[] props, float border)
        : this(samplesX, samplesZ, cellSize, heightsCm, props, border, null, null)
    {
    }

    /// <param name="surface">The ground's kinds, one per terrain cell, row-major; null for grass everywhere.</param>
    /// <param name="waterLevelCm">The water level in centimetres; null for no water.</param>
    public MapData(int samplesX, int samplesZ, float cellSize, int[] heightsCm, Prop[] props, float border,
        SurfaceKind[]? surface, int? waterLevelCm)
    {
        if (samplesX < 2 || samplesZ < 2) throw new ArgumentException("A height field needs at least 2 × 2 samples.");
        if (!(cellSize > 0f) || !float.IsFinite(cellSize)) throw new ArgumentException("Cell size must be positive.", nameof(cellSize));
        if (heightsCm.Length != (long)samplesX * samplesZ)   // long: the product of two int sizes off the wire can overflow
            throw new ArgumentException($"Expected {(long)samplesX * samplesZ} heights, got {heightsCm.Length}.", nameof(heightsCm));
        int cells = (samplesX - 1) * (samplesZ - 1);   // fewer than the heights just checked: no overflow
        if (surface is not null && surface.Length != cells)
            throw new ArgumentException($"Expected {cells} surface cells, got {surface.Length}.", nameof(surface));
        if (surface is not null)
            for (int k = 0; k < surface.Length; k++)
                if (surface[k] > SurfaceMap.LastKind) throw new ArgumentException($"Surface cell {k} has unknown kind {(byte)surface[k]}.", nameof(surface));
        if (waterLevelCm is { } level && (level < -MaxWaterLevelCm || level > MaxWaterLevelCm))
            throw new ArgumentException($"Water level {level} cm is beyond ±{MaxWaterLevelCm} cm.", nameof(waterLevelCm));
        (SamplesX, SamplesZ, CellSize, HeightsCm, Props, Border) = (samplesX, samplesZ, cellSize, heightsCm, props, border);
        Surface = surface ?? new SurfaceKind[cells];   // all SurfaceKind.Grass (0)
        WaterLevelCm = waterLevelCm;
    }

    public int SamplesX { get; }
    public int SamplesZ { get; }
    public float CellSize { get; }

    /// <summary>Sample (i, j)'s height in centimetres at index <c>j * SamplesX + i</c>, as <see cref="HeightField"/> lays them out.</summary>
    public int[] HeightsCm { get; }

    public Prop[] Props { get; }
    public float Border { get; }

    /// <summary>Cell (i, j)'s kind of ground at index <c>j * (SamplesX − 1) + i</c>, as <see cref="SurfaceMap"/> lays them out.</summary>
    public SurfaceKind[] Surface { get; }

    /// <summary>The water level in centimetres; null for no water.</summary>
    public int? WaterLevelCm { get; }

    /// <summary>The data of <paramref name="field"/>, its heights and water level rounded to the nearest centimetre.</summary>
    public static MapData Of(Battlefield field)
    {
        HeightField terrain = field.Terrain;
        var heights = new int[terrain.SamplesX * terrain.SamplesZ];
        for (int j = 0; j < terrain.SamplesZ; j++)
        for (int i = 0; i < terrain.SamplesX; i++)
            heights[j * terrain.SamplesX + i] = (int)MathF.Round(terrain.Sample(i, j) / HeightUnit);
        int? waterLevelCm = field.WaterLevel is { } level ? (int)MathF.Round(level / HeightUnit) : null;
        return new MapData(terrain.SamplesX, terrain.SamplesZ, terrain.CellSize, heights, field.Props.ToArray(), field.Border,
            field.Surface.Cells.ToArray(), waterLevelCm);
    }

    /// <summary>A fresh battlefield (every prop standing at full hit points) from this data.</summary>
    public Battlefield Build()
    {
        var heights = new float[HeightsCm.Length];
        for (int k = 0; k < heights.Length; k++) heights[k] = HeightsCm[k] * HeightUnit;
        return new Battlefield(new HeightField(SamplesX, SamplesZ, CellSize, heights), Props, Border,
            new SurfaceMap(SamplesX - 1, SamplesZ - 1, CellSize, Surface), WaterLevelCm * HeightUnit);
    }
}

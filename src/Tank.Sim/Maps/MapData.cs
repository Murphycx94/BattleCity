using Tank.Sim.Terrain;

namespace Tank.Sim.Maps;

/// <summary>
/// A battlefield's fixed shape as data (stage 4 design §5.4): the terrain's heights in whole centimetres, the props and
/// the border. The server builds its battlefield from this and sends the same data to its clients, who build theirs
/// from it too, so every machine has the same ground and the same props under the same ids, whatever its floating
/// point does to the map generator. Rounding the heights to centimetres happens once, in <see cref="Of"/>; building a
/// battlefield and taking its data again gives the same data back.
/// </summary>
public sealed class MapData
{
    public const float HeightUnit = 0.01f;

    public MapData(int samplesX, int samplesZ, float cellSize, int[] heightsCm, Prop[] props, float border)
    {
        if (samplesX < 2 || samplesZ < 2) throw new ArgumentException("A height field needs at least 2 × 2 samples.");
        if (!(cellSize > 0f) || !float.IsFinite(cellSize)) throw new ArgumentException("Cell size must be positive.", nameof(cellSize));
        if (heightsCm.Length != (long)samplesX * samplesZ)   // long: the product of two int sizes off the wire can overflow
            throw new ArgumentException($"Expected {(long)samplesX * samplesZ} heights, got {heightsCm.Length}.", nameof(heightsCm));
        (SamplesX, SamplesZ, CellSize, HeightsCm, Props, Border) = (samplesX, samplesZ, cellSize, heightsCm, props, border);
    }

    public int SamplesX { get; }
    public int SamplesZ { get; }
    public float CellSize { get; }

    /// <summary>Sample (i, j)'s height in centimetres at index <c>j * SamplesX + i</c>, as <see cref="HeightField"/> lays them out.</summary>
    public int[] HeightsCm { get; }

    public Prop[] Props { get; }
    public float Border { get; }

    /// <summary>The data of <paramref name="field"/>, its heights rounded to the nearest centimetre.</summary>
    public static MapData Of(Battlefield field)
    {
        HeightField terrain = field.Terrain;
        var heights = new int[terrain.SamplesX * terrain.SamplesZ];
        for (int j = 0; j < terrain.SamplesZ; j++)
        for (int i = 0; i < terrain.SamplesX; i++)
            heights[j * terrain.SamplesX + i] = (int)MathF.Round(terrain.Sample(i, j) / HeightUnit);
        return new MapData(terrain.SamplesX, terrain.SamplesZ, terrain.CellSize, heights, field.Props.ToArray(), field.Border);
    }

    /// <summary>A fresh battlefield (every prop standing at full hit points) from this data.</summary>
    public Battlefield Build()
    {
        var heights = new float[HeightsCm.Length];
        for (int k = 0; k < heights.Length; k++) heights[k] = HeightsCm[k] * HeightUnit;
        return new Battlefield(new HeightField(SamplesX, SamplesZ, CellSize, heights), Props, Border);
    }
}

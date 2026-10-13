using Tank.Sim.Terrain;

namespace Tank.Sim.Tests;

internal static class TestWorld
{
    public const int Samples = 101;
    public const float Cell = 2f; // 200m × 200m
    public const float Size = (Samples - 1) * Cell;
    public const float Border = 4f;

    public static HeightField Flat(float height = 0f) =>
        new(Samples, Samples, Cell, Enumerable.Repeat(height, Samples * Samples).ToArray());

    /// <summary>Ground rising toward -Z (north) by <paramref name="grade"/> metres per metre.</summary>
    public static HeightField RampNorth(float grade)
    {
        var heights = new float[Samples * Samples];
        for (int j = 0; j < Samples; j++)
        for (int i = 0; i < Samples; i++)
            heights[j * Samples + i] = (Samples - 1 - j) * Cell * grade;
        return new HeightField(Samples, Samples, Cell, heights);
    }
}

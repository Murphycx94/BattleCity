using System.Numerics;

namespace Tank.Sim.Terrain;

/// <summary>
/// Regular grid of ground heights. Sample (i, j) sits at world (i·CellSize, j·CellSize): X grows with i, Z with j.
/// The surface between samples is bilinear; outside the grid the edge height continues.
/// </summary>
public sealed class HeightField
{
    private const int RefineSteps = 12;
    private const int MaxSteps = 4096;
    private readonly float[] _heights;

    public HeightField(int samplesX, int samplesZ, float cellSize, float[] heights)
    {
        if (samplesX < 2 || samplesZ < 2) throw new ArgumentOutOfRangeException(nameof(samplesX), "Need at least 2×2 samples.");
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive.");
        if (heights.Length != samplesX * samplesZ)
            throw new ArgumentException($"Expected {samplesX * samplesZ} heights, got {heights.Length}.", nameof(heights));
        SamplesX = samplesX;
        SamplesZ = samplesZ;
        CellSize = cellSize;
        _heights = (float[])heights.Clone();
    }

    public int SamplesX { get; }
    public int SamplesZ { get; }
    public float CellSize { get; }
    public float SizeX => (SamplesX - 1) * CellSize;
    public float SizeZ => (SamplesZ - 1) * CellSize;

    public float Sample(int i, int j) =>
        _heights[System.Math.Clamp(j, 0, SamplesZ - 1) * SamplesX + System.Math.Clamp(i, 0, SamplesX - 1)];

    public float HeightAt(float x, float z)
    {
        float fx = System.Math.Clamp(x / CellSize, 0, SamplesX - 1);
        float fz = System.Math.Clamp(z / CellSize, 0, SamplesZ - 1);
        int i = System.Math.Min((int)fx, SamplesX - 2);
        int j = System.Math.Min((int)fz, SamplesZ - 2);
        float u = fx - i, v = fz - j;
        float near = Sample(i, j) * (1 - u) + Sample(i + 1, j) * u;
        float far = Sample(i, j + 1) * (1 - u) + Sample(i + 1, j + 1) * u;
        return near * (1 - v) + far * v;
    }

    /// <summary>The ground's normal at (x, z), from the heights half a cell either side: smooth across sample lines.</summary>
    public Vector3 NormalAt(float x, float z)
    {
        float e = CellSize * 0.5f;
        float dx = HeightAt(x + e, z) - HeightAt(x - e, z);
        float dz = HeightAt(x, z + e) - HeightAt(x, z - e);
        return Vector3.Normalize(new Vector3(-dx, 2 * e, -dz));
    }

    /// <summary>
    /// The normal of the bilinear surface itself at (x, z), within the cell holding the point (the one after it on a
    /// sample line): where the slope bends at a sample line, as at a cliff's foot, it changes there and not over the
    /// metre either side that <see cref="NormalAt"/> spreads it across. Flat outside the grid, as the ground is there.
    /// </summary>
    public Vector3 SurfaceNormalAt(float x, float z)
    {
        float fx = x / CellSize, fz = z / CellSize;
        float cx = System.Math.Clamp(fx, 0, SamplesX - 1), cz = System.Math.Clamp(fz, 0, SamplesZ - 1);
        int i = System.Math.Min((int)cx, SamplesX - 2);
        int j = System.Math.Min((int)cz, SamplesZ - 2);
        float u = cx - i, v = cz - j;
        float h00 = Sample(i, j), h10 = Sample(i + 1, j), h01 = Sample(i, j + 1), h11 = Sample(i + 1, j + 1);
        float dx = fx == cx ? (h10 - h00) * (1 - v) + (h11 - h01) * v : 0f;
        float dz = fz == cz ? (h01 - h00) * (1 - u) + (h11 - h10) * u : 0f;
        return Vector3.Normalize(new Vector3(-dx, CellSize, -dz));
    }

    /// <summary>
    /// First point where segment <paramref name="from"/>→<paramref name="to"/> goes below ground, as t in [0, 1].
    /// Marches in half-cell steps; a ridge sharper than the step can be tunnelled by up to ~slopeChange·step/4.
    /// Fine for generated terrain; switch to an exact cell walk before hand-authored sharp ridges.
    /// Returns false if either endpoint is not finite.
    /// </summary>
    public bool Raycast(Vector3 from, Vector3 to, out float t)
    {
        t = 0;
        if (!IsFinite(from) || !IsFinite(to)) return false;
        if (Below(from)) return true;
        float wanted = MathF.Ceiling(Vector3.Distance(from, to) / (CellSize * 0.5f));
        int steps = (int)System.Math.Clamp(wanted, 1f, MaxSteps);
        float previous = 0;
        for (int s = 1; s <= steps; s++)
        {
            float current = (float)s / steps;
            if (Below(Vector3.Lerp(from, to, current)))
            {
                t = Refine(from, to, previous, current);
                return true;
            }
            previous = current;
        }
        return false;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private bool Below(Vector3 p) => p.Y < HeightAt(p.X, p.Z);

    private float Refine(Vector3 from, Vector3 to, float above, float below)
    {
        for (int k = 0; k < RefineSteps; k++)
        {
            float mid = (above + below) * 0.5f;
            if (Below(Vector3.Lerp(from, to, mid))) below = mid;
            else above = mid;
        }
        return below;
    }
}

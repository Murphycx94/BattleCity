using Tank.Sim.Maps;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Engine-neutral ground layers of a map with ground kinds (riverland design §6.2): one RGBA texel per surface cell,
/// R = not grass (dirt, sand or road) dilated by one cell, G = sand, B = road, each 3×3 box-blurred so the shader's
/// smoothsteps draw soft edges instead of 2 m steps. R takes the place of the sandbox's dirt mask, so the grass rules
/// (<see cref="GroundCover.GrassWeight"/>) read it unchanged; <c>World.DirtMask</c> wraps it for Godot.
/// </summary>
public sealed class GroundLayers
{
    private const int Channels = 4;
    private readonly byte[] _rgba;
    private readonly float _cellSize;

    /// <summary>Whether a map needs the layers: it has water, or some cell that isn't grass.</summary>
    public static bool Needed(SurfaceMap surface, float? waterLevel) => waterLevel.HasValue || !surface.IsUniform(SurfaceKind.Grass);

    public GroundLayers(SurfaceMap surface)
    {
        Width = surface.CellsX;
        Height = surface.CellsZ;
        _cellSize = surface.CellSize;
        _rgba = new byte[Width * Height * Channels];
        ReadOnlySpan<SurfaceKind> cells = surface.Cells;
        bool[] nearNotGrass = NearNotGrass(cells);
        for (int j = 0; j < Height; j++)
        for (int i = 0; i < Width; i++)
        {
            int notGrass = 0, sand = 0, road = 0;
            for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                int at = Math.Clamp(j + dj, 0, Height - 1) * Width + Math.Clamp(i + di, 0, Width - 1);
                if (nearNotGrass[at]) notGrass++;
                if (cells[at] == SurfaceKind.Sand) sand++;
                if (cells[at] == SurfaceKind.Road) road++;
            }
            int o = (j * Width + i) * Channels;
            _rgba[o] = Ninths(notGrass);
            _rgba[o + 1] = Ninths(sand);
            _rgba[o + 2] = Ninths(road);
        }
    }

    /// <summary>
    /// "Not grass" grown by one cell (any of the eight neighbours isn't grass). Blurred, it is then full (1) on every cell
    /// that isn't grass, edge cells included, so grass can't grow on a road's or a beach's edge whatever the patch noise
    /// (riverland design §6.2), and still 0 two cells out.
    /// </summary>
    private bool[] NearNotGrass(ReadOnlySpan<SurfaceKind> cells)
    {
        var near = new bool[cells.Length];
        for (int j = 0; j < Height; j++)
        for (int i = 0; i < Width; i++)
        {
            if (cells[j * Width + i] == SurfaceKind.Grass) continue;
            for (int nj = Math.Max(j - 1, 0); nj <= Math.Min(j + 1, Height - 1); nj++)
            for (int ni = Math.Max(i - 1, 0); ni <= Math.Min(i + 1, Width - 1); ni++)
                near[nj * Width + ni] = true;
        }
        return near;
    }

    /// <summary>Texels across (X), one per surface cell.</summary>
    public int Width { get; }

    /// <summary>Texels down (Z), one per surface cell.</summary>
    public int Height { get; }

    public float SizeX => Width * _cellSize;
    public float SizeZ => Height * _cellSize;

    /// <summary>The blurred layers, row-major (row = Z), four bytes per texel: R not grass (dilated by a cell), G sand, B road, A 0.</summary>
    public byte[] ToRgba() => (byte[])_rgba.Clone();

    /// <summary>
    /// The blurred "not grass" value at a point, 0..1, bilinear between texel centres (<c>(i + 0.5) · cell</c>) and clamped
    /// at the edges like the GPU's linear filter: the dirt-mask value <see cref="GroundCover.GrassWeight"/> takes.
    /// </summary>
    public float NotGrassAt(float x, float z)
    {
        float fx = Math.Clamp(x / _cellSize - 0.5f, 0f, Width - 1f);
        float fz = Math.Clamp(z / _cellSize - 0.5f, 0f, Height - 1f);
        int i = Math.Min((int)fx, Math.Max(Width - 2, 0)), j = Math.Min((int)fz, Math.Max(Height - 2, 0));
        float u = fx - i, v = fz - j;
        int i1 = Math.Min(i + 1, Width - 1), j1 = Math.Min(j + 1, Height - 1);
        float near = Lerp(R(i, j), R(i1, j), u), far = Lerp(R(i, j1), R(i1, j1), u);
        return Lerp(near, far, v);
    }

    private float R(int i, int j) => _rgba[(j * Width + i) * Channels] / 255f;

    /// <summary>How many of the nine box samples were set, as a byte (n · 255 / 9, rounded).</summary>
    private static byte Ninths(int count) => (byte)((count * 255 + 4) / 9);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

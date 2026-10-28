using System.Numerics;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;

namespace Tank.Client.Minimap;

/// <summary>
/// The map's still layer, baked once (stage 5 design §5): per pixel the ground's kind and grass (as the terrain shader
/// colours it), steep rock, water by depth, the forest by crown density with its canopy closed over the gaps between
/// crowns, all lit by a hillshade from the north-west.
/// Reads only what never changes in a battle (terrain, surface, water, the trees' places), so it may be baked on a worker
/// thread while the scene goes on; houses and walls are <see cref="MinimapImage"/>'s.
/// </summary>
public sealed class MinimapGround
{
    public const int DefaultPixels = 1024;

    private const int Channels = 4;
    private readonly byte[] _rgba;

    private MinimapGround(MinimapArea area, int pixels, byte[] rgba)
    {
        Area = area;
        Pixels = pixels;
        _rgba = rgba;
    }

    /// <summary>
    /// Paints <paramref name="field"/>'s ground, <paramref name="pixels"/> square over <see cref="MinimapArea.Of"/>, each
    /// pixel coloured at its centre. Single-threaded and deterministic; it reads the field and never changes it.
    /// </summary>
    /// <param name="notGrassAt">The bare-ground mask, 0 (grass) … 1 (dirt, sand or road) at (x, z): the scenery's own
    /// (<c>DirtMask.At</c>), so grass shows where the 3D ground has it.</param>
    public static MinimapGround Bake(Battlefield field, Func<float, float, float> notGrassAt, int pixels = DefaultPixels)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(notGrassAt);
        if (pixels < 1) throw new ArgumentOutOfRangeException(nameof(pixels), pixels, "The map needs at least one pixel.");
        MinimapArea area = MinimapArea.Of(field);
        float[] cover = ForestCover(field, area, pixels);
        float[] heights = Heights(field.Terrain, area, pixels);
        return new MinimapGround(area, pixels, Paint(new GroundPainter(field, notGrassAt, heights), area, pixels, heights, cover));
    }

    public MinimapArea Area { get; }
    public int Pixels { get; }

    /// <summary>Pixels × Pixels × 4 bytes, RGBA, sRGB, row-major from the top row (smallest z); alpha 255. Don't change it.</summary>
    public ReadOnlySpan<byte> Rgba => _rgba;

    /// <summary>The pixel centres' x (or z, from <paramref name="min"/>), the same on every row (column).</summary>
    private static float[] Centres(float min, float step, int pixels)
    {
        var centres = new float[pixels];
        for (int p = 0; p < pixels; p++) centres[p] = min + (p + 0.5f) * step;
        return centres;
    }

    /// <summary>The terrain's height at every pixel centre, row-major as the picture.</summary>
    private static float[] Heights(HeightField terrain, MinimapArea area, int pixels)
    {
        float step = area.MetresPerPixel(pixels);
        float[] columnX = Centres(area.MinX, step, pixels), rowZ = Centres(area.MinZ, step, pixels);
        var heights = new float[pixels * pixels];
        for (int py = 0; py < pixels; py++)
        for (int px = 0; px < pixels; px++)
            heights[py * pixels + px] = terrain.HeightAt(columnX[px], rowZ[py]);
        return heights;
    }

    /// <summary>Every pixel: the ground's colour, then the forest over it by its cover there.</summary>
    private static byte[] Paint(GroundPainter painter, MinimapArea area, int pixels, float[] heights, float[] cover)
    {
        var rgba = new byte[pixels * pixels * Channels];
        float step = area.MetresPerPixel(pixels);
        float[] columnX = Centres(area.MinX, step, pixels), rowZ = Centres(area.MinZ, step, pixels);
        for (int py = 0; py < pixels; py++)
        for (int px = 0; px < pixels; px++)
        {
            int k = py * pixels + px;
            Vector3 colour = painter.ColourAt(columnX[px], rowZ[py], heights[k]);
            if (cover[k] > 0f) colour = Vector3.Lerp(colour, MinimapStyle.Forest, MinimapStyle.ForestMix * cover[k]);
            rgba[k * Channels] = MinimapStyle.ToByte(colour.X);
            rgba[k * Channels + 1] = MinimapStyle.ToByte(colour.Y);
            rgba[k * Channels + 2] = MinimapStyle.ToByte(colour.Z);
            rgba[k * Channels + 3] = byte.MaxValue;
        }
        return rgba;
    }

    /// <summary>
    /// The forest's cover per pixel, 0–1: the crowns' density (<see cref="CrownDensity"/>, capped at 1), raised to
    /// <see cref="MinimapStyle.CanopyCover"/> wherever the canopy closes over the gaps between crowns. The canopy is the
    /// crowns' pixels closed (dilated, then eroded) by a disc of <see cref="MinimapStyle.CanopyGapMetres"/>: gaps narrower
    /// than about twice that between neighbouring crowns fill, a forest's outer edge stays where its outermost crowns are
    /// (it never reaches past the crowns dilated by the disc), and a lone tree stays the dot its crown is.
    /// </summary>
    private static float[] ForestCover(Battlefield field, MinimapArea area, int pixels)
    {
        float[] cover = CrownDensity(field, area, pixels);
        int radius = (int)MathF.Ceiling(MinimapStyle.CanopyGapMetres / area.MetresPerPixel(pixels));
        bool[] canopy = Close(cover, pixels, radius);
        for (int k = 0; k < cover.Length; k++)
            cover[k] = MathF.Max(MathF.Min(1f, cover[k]), canopy[k] ? MinimapStyle.CanopyCover : 0f);
        return cover;
    }

    /// <summary>
    /// The pixels with crown density, closed by a disc of <paramref name="radius"/> pixels: dilated (within the radius of
    /// a crown pixel), then eroded (more than the radius from every pixel the dilation left out). Both by exact Euclidean
    /// distance transforms, O(pixels²) whatever the radius. Ground off the picture counts as neither.
    /// </summary>
    private static bool[] Close(float[] density, int pixels, int radius)
    {
        float reach = (float)radius * radius;
        var distances = new float[pixels * pixels];
        var transform = new DistanceTransform(pixels);
        for (int k = 0; k < distances.Length; k++) distances[k] = density[k] > 0f ? 0f : DistanceTransform.Far;
        transform.SquaredDistances(distances);
        for (int k = 0; k < distances.Length; k++) distances[k] = distances[k] <= reach ? DistanceTransform.Far : 0f;   // the dilation's gaps seed the erosion
        transform.SquaredDistances(distances);
        var closed = new bool[distances.Length];
        for (int k = 0; k < distances.Length; k++) closed[k] = distances[k] > reach;
        return closed;
    }

    /// <summary>
    /// The trees' crown density per pixel: every tree, standing or not (the layer is baked once, plan deviation 2), adds a
    /// disc of its crown's radius, <see cref="MinimapStyle.CrownCentreDensity"/> at the middle falling linearly to
    /// <see cref="MinimapStyle.CrownEdgeDensity"/> at the rim. Each disc visits only the pixels its square covers.
    /// </summary>
    private static float[] CrownDensity(Battlefield field, MinimapArea area, int pixels)
    {
        var density = new float[pixels * pixels];
        foreach (Prop prop in field.Props)
            if (prop.Kind == PropKind.Tree) AddCrown(density, area, pixels, prop.Footprint.Center, prop.FoliageRadius);
        return density;
    }

    private static void AddCrown(float[] density, MinimapArea area, int pixels, Vector2 centre, float radius)
    {
        if (!(radius > 0f)) return;
        float step = area.MetresPerPixel(pixels);
        (int fromX, int toX) = PixelSpan(centre.X - area.MinX, radius, step, pixels);
        (int fromZ, int toZ) = PixelSpan(centre.Y - area.MinZ, radius, step, pixels);
        for (int py = fromZ; py <= toZ; py++)
        {
            float dz = area.MinZ + (py + 0.5f) * step - centre.Y;
            for (int px = fromX; px <= toX; px++)
            {
                float dx = area.MinX + (px + 0.5f) * step - centre.X;
                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > radius) continue;
                density[py * pixels + px] += MinimapStyle.CrownCentreDensity
                    + (MinimapStyle.CrownEdgeDensity - MinimapStyle.CrownCentreDensity) * (distance / radius);
            }
        }
    }

    /// <summary>The pixels along one axis whose centres may lie within <paramref name="radius"/> of <paramref name="offset"/> (m from the area's edge), clamped to the image.</summary>
    private static (int From, int To) PixelSpan(float offset, float radius, float step, int pixels)
    {
        float from = MathF.Floor((offset - radius) / step), to = MathF.Floor((offset + radius) / step);
        return ((int)Math.Clamp(from, 0f, pixels), (int)Math.Clamp(to, -1f, pixels - 1));
    }

    /// <summary>
    /// The exact squared Euclidean distance transform of a square grid (Felzenszwalb and Huttenlocher, "Distance
    /// Transforms of Sampled Functions"): a pass along every row, then along every column, each the lower envelope of
    /// parabolas; linear in the grid's size. Its line buffers are allocated once.
    /// </summary>
    private sealed class DistanceTransform
    {
        /// <summary>The value of a pixel that seeds nothing: far beyond any distance on the grid, small enough to add squares to.</summary>
        public const float Far = 1e20f;

        private readonly int _size;
        private readonly float[] _line, _out, _bounds;
        private readonly int[] _parabolas;

        public DistanceTransform(int size)
        {
            _size = size;
            _line = new float[size];
            _out = new float[size];
            _bounds = new float[size + 1];
            _parabolas = new int[size];
        }

        /// <summary>In place: <paramref name="grid"/> holds 0 at the seeds and <see cref="Far"/> elsewhere, and gets each pixel's squared distance to the nearest seed in pixels.</summary>
        public void SquaredDistances(float[] grid)
        {
            int n = _size;
            for (int row = 0; row < n; row++)
            {
                Array.Copy(grid, row * n, _line, 0, n);
                Envelope();
                Array.Copy(_out, 0, grid, row * n, n);
            }
            for (int column = 0; column < n; column++)
            {
                for (int i = 0; i < n; i++) _line[i] = grid[i * n + column];
                Envelope();
                for (int i = 0; i < n; i++) grid[i * n + column] = _out[i];
            }
        }

        /// <summary>Where the parabolas rooted at <paramref name="q"/> and <paramref name="p"/> (q &gt; p) cross.</summary>
        private float Meet(int q, int p) => (_line[q] + q * q - (_line[p] + p * p)) / (2f * (q - p));

        /// <summary>One line: <c>_out[q] = min over p of (q − p)² + _line[p]</c>.</summary>
        private void Envelope()
        {
            int n = _size, k = 0;
            _parabolas[0] = 0;
            _bounds[0] = float.NegativeInfinity;
            _bounds[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                // Drop the parabolas the new one is lower than from where they start; the first's bound is −∞, so it stays.
                float s = Meet(q, _parabolas[k]);
                while (s <= _bounds[k]) s = Meet(q, _parabolas[--k]);
                k++;
                _parabolas[k] = q;
                _bounds[k] = s;
                _bounds[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (_bounds[k + 1] < q) k++;
                int p = _parabolas[k];
                _out[q] = (q - p) * (q - p) + _line[p];
            }
        }
    }

    /// <summary>The colour of the ground at one point, before the forest: water, or bare ground, grass and rock under the hillshade.</summary>
    private sealed class GroundPainter
    {
        private readonly HeightField _terrain;
        private readonly SurfaceMap _surface;
        private readonly float? _waterLevel;
        private readonly Func<float, float, float> _notGrassAt;
        private readonly float _lowest, _heightSpan;

        /// <param name="heights">The ground's height at every pixel centre: its range is what the height shading spans.</param>
        public GroundPainter(Battlefield field, Func<float, float, float> notGrassAt, float[] heights)
        {
            _terrain = field.Terrain;
            _surface = field.Surface;
            _waterLevel = field.WaterLevel;
            _notGrassAt = notGrassAt;
            float lowest = float.MaxValue, highest = float.MinValue;
            foreach (float h in heights)
            {
                lowest = MathF.Min(lowest, h);
                highest = MathF.Max(highest, h);
            }
            _lowest = lowest;
            _heightSpan = highest - lowest;
        }

        /// <param name="height">The terrain's height at (x, z).</param>
        public Vector3 ColourAt(float x, float z, float height)
        {
            if (_waterLevel is { } level && height < level) return Water(level - height);
            Vector3 normal = _terrain.NormalAt(x, z);
            Vector3 colour = MinimapStyle.BareGround(_surface.At(x, z));
            colour = Vector3.Lerp(colour, MinimapStyle.Grass, GroundCover.GrassWeight(x, z, normal.Y, _notGrassAt(x, z)));
            colour = Vector3.Lerp(colour, Palette.Rock, GroundCover.RockWeight(x, z, normal.Y));
            return colour * Shade(normal, height);
        }

        /// <summary>Lambert light from <see cref="MinimapStyle.LightDirection"/> over an ambient floor, a little brighter the higher the ground.</summary>
        private float Shade(Vector3 normal, float height)
        {
            float shade = MinimapStyle.ShadeAmbient + MinimapStyle.ShadeDiffuse * MathF.Max(0f, Vector3.Dot(normal, MinimapStyle.LightDirection));
            if (_heightSpan <= 0f) return shade;
            float up = (height - _lowest) / _heightSpan;   // 0 at the map's lowest pixel, 1 at its highest
            return shade * (1f + MinimapStyle.HeightShade * (2f * up - 1f));
        }

        /// <summary>Shallow to deep with <paramref name="depth"/>, unlit; a foam-white line along the shore.</summary>
        private static Vector3 Water(float depth)
        {
            Vector3 colour = Vector3.Lerp(Palette.WaterShallow, Palette.WaterDeep,
                GroundCover.SmoothStep(MinimapStyle.ShallowDepth, MinimapStyle.DeepDepth, depth));
            return depth < MinimapStyle.ShoreBand ? Vector3.Lerp(colour, Palette.Foam, MinimapStyle.ShoreFoam) : colour;
        }
    }
}

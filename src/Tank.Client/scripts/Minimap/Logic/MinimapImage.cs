using System.Numerics;
using Tank.Sim.Maps;

namespace Tank.Client.Minimap;

/// <summary>
/// The ground plus the houses and walls as they stand (stage 5 design §5): standing ones in their colours with an
/// outline, fallen ones as faint rubble. Keeps every house's and wall's standing; <see cref="Refresh"/> repaints only the
/// tiles (<see cref="TileSize"/> pixels square) a prop that rose or fell touches, from the ground up, so the result is
/// always what a whole repaint would give. Reads the battlefield's prop state, so it belongs to the thread the scene runs on.
/// </summary>
public sealed class MinimapImage
{
    public const int TileSize = 32;

    private const int Channels = 4;

    private readonly Shape[] _shapes;        // the houses and walls on the map, in Battlefield.Props order
    private readonly bool[] _drawn;          // each shape's standing as last painted
    private readonly int[] _tileStart;       // tile t's shapes are _tileShapes[_tileStart[t] .. _tileStart[t + 1]), ascending
    private readonly int[] _tileShapes;
    private readonly bool[] _dirty;          // per tile, while a Refresh gathers them
    private readonly int[] _dirtyTiles;
    private readonly int _tilesPerRow;
    private readonly IReadOnlyList<Prop> _props;   // the map's props: a fresh battlefield of the same map shares them
    private readonly float _step;
    private readonly Pixel _roof, _rim, _brick, _stone;
    private Battlefield _field;
    private int _version;

    /// <summary>A house or wall as drawn: its footprint (a wall's widened to <see cref="MinimapStyle.WallMinPixels"/>) and the pixels it may touch, 1 pixel round it included.</summary>
    private readonly record struct Shape(int PropId, PropKind Kind, Vector2 Centre, Vector2 Right, Vector2 Forward, Vector2 Half,
        int MinPx, int MinPy, int MaxPx, int MaxPy)
    {
        public bool IsHouse => Kind == PropKind.House;

        public bool Contains(Vector2 point)
        {
            Vector2 d = point - Centre;
            return MathF.Abs(Vector2.Dot(d, Right)) <= Half.X && MathF.Abs(Vector2.Dot(d, Forward)) <= Half.Y;
        }
    }

    private readonly record struct Pixel(byte R, byte G, byte B)
    {
        public static Pixel Of(Vector3 colour) => new(MinimapStyle.ToByte(colour.X), MinimapStyle.ToByte(colour.Y), MinimapStyle.ToByte(colour.Z));
    }

    /// <summary>Paints <paramref name="ground"/> with <paramref name="field"/>'s houses and walls as they stand now.</summary>
    /// <exception cref="ArgumentException"><paramref name="field"/> is not the map <paramref name="ground"/> was baked from.</exception>
    public MinimapImage(MinimapGround ground, Battlefield field)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(field);
        if (MinimapArea.Of(field) != ground.Area)
            throw new ArgumentException($"The field's area {MinimapArea.Of(field)} isn't the ground's {ground.Area}: another map.", nameof(field));
        Ground = ground;
        _props = field.Props;
        _step = ground.Area.MetresPerPixel(ground.Pixels);
        _roof = Pixel.Of(MinimapStyle.HouseRoof);
        _rim = Pixel.Of(MinimapStyle.HouseRim);
        _brick = Pixel.Of(MinimapStyle.Wall(PropKind.BrickWall));
        _stone = Pixel.Of(MinimapStyle.Wall(PropKind.StoneWall));

        _shapes = ShapesOf(field);
        _drawn = new bool[_shapes.Length];
        for (int s = 0; s < _shapes.Length; s++) _drawn[s] = field.IsStanding(_shapes[s].PropId);

        _tilesPerRow = (Pixels + TileSize - 1) / TileSize;
        int tiles = _tilesPerRow * _tilesPerRow;
        (_tileStart, _tileShapes) = RegisterTiles(tiles);
        _dirty = new bool[tiles];
        _dirtyTiles = new int[tiles];

        Rgba = ground.Rgba.ToArray();
        for (int t = 0; t < tiles; t++)
            if (_tileStart[t + 1] > _tileStart[t]) PaintTile(t);
        _field = field;
        _version = field.StateVersion;
    }

    public MinimapGround Ground { get; }

    public int Pixels => Ground.Pixels;

    /// <summary>The picture the view uploads, laid out as <see cref="MinimapGround.Rgba"/>; changed in place by <see cref="Refresh"/>.</summary>
    public byte[] Rgba { get; }

    /// <summary>The tiles the last <see cref="Refresh"/> repainted (tests and the log).</summary>
    public int LastRepaintedTiles { get; private set; }

    /// <summary>
    /// Brings the picture up to <paramref name="field"/> (the same map; a resync may hand a new instance): nothing when
    /// its <see cref="Battlefield.StateVersion"/> and instance are the ones last seen, else the tiles of every house or
    /// wall whose standing changed. Returns whether any pixel may have changed. No allocation.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="field"/> is another map: a new instance must share this one's
    /// props (as <see cref="Battlefield.Fresh"/> copies do).</exception>
    public bool Refresh(Battlefield field)
    {
        ArgumentNullException.ThrowIfNull(field);
        bool sameField = ReferenceEquals(field, _field);
        if (sameField && field.StateVersion == _version)
        {
            LastRepaintedTiles = 0;
            return false;
        }
        if (!sameField && (!ReferenceEquals(field.Props, _props) || MinimapArea.Of(field) != Ground.Area))
            throw new ArgumentException("The field is another map than the one this picture was made for.", nameof(field));

        int dirty = 0;
        for (int s = 0; s < _shapes.Length; s++)
        {
            bool standing = field.IsStanding(_shapes[s].PropId);
            if (standing == _drawn[s]) continue;
            _drawn[s] = standing;
            dirty = MarkTiles(_shapes[s], dirty);
        }
        for (int i = 0; i < dirty; i++)
        {
            PaintTile(_dirtyTiles[i]);
            _dirty[_dirtyTiles[i]] = false;
        }
        _field = field;
        _version = field.StateVersion;
        LastRepaintedTiles = dirty;
        return dirty > 0;
    }

    /// <summary>Adds the tiles <paramref name="shape"/> touches to the dirty list, each once; returns the list's new length.</summary>
    private int MarkTiles(in Shape shape, int dirty)
    {
        for (int ty = shape.MinPy / TileSize; ty <= shape.MaxPy / TileSize; ty++)
        for (int tx = shape.MinPx / TileSize; tx <= shape.MaxPx / TileSize; tx++)
        {
            int t = ty * _tilesPerRow + tx;
            if (_dirty[t]) continue;
            _dirty[t] = true;
            _dirtyTiles[dirty++] = t;
        }
        return dirty;
    }

    /// <summary>The houses and walls, each with its drawn footprint and pixel box; any wholly off the picture left out.</summary>
    private Shape[] ShapesOf(Battlefield field)
    {
        var shapes = new List<Shape>();
        MinimapArea area = Ground.Area;
        float wallHalf = _step * MinimapStyle.WallMinPixels / 2f;
        foreach (Prop prop in field.Props)
        {
            if (prop.Kind is not (PropKind.House or PropKind.BrickWall or PropKind.StoneWall)) continue;
            Vector2 right = prop.Footprint.AxisRight, forward = prop.Footprint.AxisForward, half = prop.Footprint.HalfExtents;
            if (prop.Kind != PropKind.House) half = WidenThinSide(half, wallHalf);
            Vector2 centre = prop.Footprint.Center;
            float reachX = half.X * MathF.Abs(right.X) + half.Y * MathF.Abs(forward.X);
            float reachZ = half.X * MathF.Abs(right.Y) + half.Y * MathF.Abs(forward.Y);
            (int minPx, int maxPx) = PixelBox(centre.X - area.MinX, reachX);
            (int minPy, int maxPy) = PixelBox(centre.Y - area.MinZ, reachZ);
            if (minPx > maxPx || minPy > maxPy) continue;
            shapes.Add(new Shape(prop.Id, prop.Kind, centre, right, forward, half, minPx, minPy, maxPx, maxPy));
        }
        return shapes.ToArray();
    }

    /// <summary>
    /// A wall's half extents with the thinner one (its thickness, whichever axis that is) at least <paramref name="least"/>,
    /// so a wall thinner than a pixel still draws <see cref="MinimapStyle.WallMinPixels"/> wide; its length unchanged.
    /// </summary>
    private static Vector2 WidenThinSide(Vector2 half, float least) =>
        half.X < half.Y ? new Vector2(MathF.Max(half.X, least), half.Y) : new Vector2(half.X, MathF.Max(half.Y, least));

    /// <summary>The pixels along one axis whose centres may lie within <paramref name="reach"/> of <paramref name="offset"/> (m from the area's edge), 1 more each side, clamped to the picture; empty (From &gt; To) if none is on it.</summary>
    private (int From, int To) PixelBox(float offset, float reach)
    {
        float from = MathF.Floor((offset - reach) / _step) - 1, to = MathF.Floor((offset + reach) / _step) + 1;
        if (to < 0 || from > Pixels - 1) return (1, 0);
        return ((int)MathF.Max(from, 0f), (int)MathF.Min(to, Pixels - 1));
    }

    /// <summary>Tile → the shapes whose pixel boxes touch it, built once: counts, offsets, then the shape indices in order.</summary>
    private (int[] Start, int[] Shapes) RegisterTiles(int tiles)
    {
        var start = new int[tiles + 1];
        foreach (Shape shape in _shapes)
            ForEachTile(shape, t => start[t + 1]++);
        for (int t = 0; t < tiles; t++) start[t + 1] += start[t];
        var entries = new int[start[tiles]];
        var filled = new int[tiles];
        for (int s = 0; s < _shapes.Length; s++)
        {
            int shape = s;
            ForEachTile(_shapes[s], t => entries[start[t] + filled[t]++] = shape);
        }
        return (start, entries);
    }

    private void ForEachTile(in Shape shape, Action<int> visit)
    {
        for (int ty = shape.MinPy / TileSize; ty <= shape.MaxPy / TileSize; ty++)
        for (int tx = shape.MinPx / TileSize; tx <= shape.MaxPx / TileSize; tx++)
            visit(ty * _tilesPerRow + tx);
    }

    /// <summary>Tile <paramref name="t"/> from the ground up: the ground's pixels, then its shapes in order, each as it stands in <see cref="_drawn"/>.</summary>
    private void PaintTile(int t)
    {
        int x0 = t % _tilesPerRow * TileSize, y0 = t / _tilesPerRow * TileSize;
        int x1 = Math.Min(x0 + TileSize, Pixels) - 1, y1 = Math.Min(y0 + TileSize, Pixels) - 1;
        ReadOnlySpan<byte> ground = Ground.Rgba;
        int rowBytes = (x1 - x0 + 1) * Channels;
        for (int py = y0; py <= y1; py++)
        {
            int o = (py * Pixels + x0) * Channels;
            ground.Slice(o, rowBytes).CopyTo(Rgba.AsSpan(o, rowBytes));
        }
        for (int i = _tileStart[t]; i < _tileStart[t + 1]; i++)
            PaintShape(_tileShapes[i], x0, y0, x1, y1);
    }

    /// <summary>
    /// One shape over the pixels of [<paramref name="x0"/>, <paramref name="x1"/>] × [<paramref name="y0"/>, <paramref name="y1"/>]
    /// whose centres it covers: a standing house roof-red with a darker rim (the pixels with a side-neighbour outside), a
    /// standing wall in its colour, fallen ones a wash of rubble over what is there.
    /// </summary>
    private void PaintShape(int s, int x0, int y0, int x1, int y1)
    {
        ref readonly Shape shape = ref _shapes[s];
        bool standing = _drawn[s];
        int fromX = Math.Max(shape.MinPx, x0), toX = Math.Min(shape.MaxPx, x1);
        int fromY = Math.Max(shape.MinPy, y0), toY = Math.Min(shape.MaxPy, y1);
        for (int py = fromY; py <= toY; py++)
        for (int px = fromX; px <= toX; px++)
        {
            if (!shape.Contains(CentreOf(px, py))) continue;
            int o = (py * Pixels + px) * Channels;
            if (shape.IsHouse)
            {
                if (standing) Put(o, OnRim(shape, px, py) ? _rim : _roof);
                else Blend(o, MinimapStyle.Rubble, MinimapStyle.HouseRubbleMix);
            }
            else if (standing) Put(o, shape.Kind == PropKind.StoneWall ? _stone : _brick);
            else Blend(o, MinimapStyle.Rubble, MinimapStyle.WallRubbleMix);
        }
    }

    private bool OnRim(in Shape shape, int px, int py) =>
        !shape.Contains(CentreOf(px - 1, py)) || !shape.Contains(CentreOf(px + 1, py))
        || !shape.Contains(CentreOf(px, py - 1)) || !shape.Contains(CentreOf(px, py + 1));

    private Vector2 CentreOf(int px, int py) =>
        new(Ground.Area.MinX + (px + 0.5f) * _step, Ground.Area.MinZ + (py + 0.5f) * _step);

    private void Put(int o, Pixel colour)
    {
        Rgba[o] = colour.R;
        Rgba[o + 1] = colour.G;
        Rgba[o + 2] = colour.B;
    }

    private void Blend(int o, Vector3 colour, float mix)
    {
        Rgba[o] = MinimapStyle.ToByte(Lerp(Rgba[o], colour.X, mix));
        Rgba[o + 1] = MinimapStyle.ToByte(Lerp(Rgba[o + 1], colour.Y, mix));
        Rgba[o + 2] = MinimapStyle.ToByte(Lerp(Rgba[o + 2], colour.Z, mix));
    }

    private static float Lerp(byte from, float to, float mix) => from / 255f + (to - from / 255f) * mix;
}

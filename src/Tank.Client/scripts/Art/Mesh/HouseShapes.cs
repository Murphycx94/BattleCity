using System.Numerics;
using Tank.Sim.Maps;

namespace Tank.Client.Art.Meshes;

/// <summary>The four kinds of house (riverland design §3.6), by size: length × width × height to the ridge, metres.</summary>
public enum HouseStyle : byte { Cottage, Farmhouse, TwoStorey, Barn }   // 8×6×6.5, 12×7×7.5, 9×7×9, 14×9×8

/// <summary>
/// Cartoon houses and the heaps they collapse into (riverland design §6.5). A house is in its prop's own frame: X along
/// the ridge, Z across it with the door on the −Z face, Y up from the ground; it fills its box exactly, so a tank stops
/// where the walls are drawn. The walls stand <see cref="WallInset"/> inside the box, the door and window frames come out
/// to its faces and the roof's eaves and gable ends reach them, so nothing overhangs the box. Vertex colour RGB is each
/// part's colour (linear), from one of <see cref="ColourSets"/> picked by seed: plaster or plank walls, tiles or slates.
/// </summary>
public static class HouseShapes
{
    /// <summary>Looks per style: a house of style s gets seed s × Variants + 1 … Variants (<see cref="SeedFor"/>).</summary>
    public const int Variants = 3;
    /// <summary>How far a ruin heaps towards the way its house was pushed, m (<see cref="RuinOffset"/>).</summary>
    public const float RuinShift = 0.5f;
    private const uint VariantSeed = 6143u;
    /// <summary>How far the walls stand inside the box: the depth of the doors and window frames on them.</summary>
    private const float WallInset = 0.06f;
    private const float RoofThickness = 0.22f;
    private const float CapHeight = 0.18f, CapWidth = 0.36f;   // the board along the ridge
    private const float PlinthHeight = 0.45f, PlinthOut = 0.02f;
    private const float Bay = 2.6f;                             // a door or window per this much of the front
    private const float WindowWidth = 1f, WindowHeight = 1.1f, Sill = 1f;
    private const float FrameBar = 0.08f;
    private const float DoorWidth = 1.15f, DoorHeight = 2.1f, BarnDoorWidth = 3f, BarnDoorHeight = 3.2f;
    private const float ChimneySide = 0.7f, ChimneyBelowRidge = 0.25f, ChimneyCap = 0.12f;
    private const float RuinMinPeak = 1.4f, RuinPeakSpread = 0.6f, RuinFloor = -0.15f;
    private const int RuinChunks = 36, RuinRoofPieces = 10, RuinBeams = 3;

    private static readonly (HouseStyle Style, Vector3 Size)[] Sizes =
    {
        (HouseStyle.Cottage, new Vector3(8f, 6f, 6.5f)),
        (HouseStyle.Farmhouse, new Vector3(12f, 7f, 7.5f)),
        (HouseStyle.TwoStorey, new Vector3(9f, 7f, 9f)),
        (HouseStyle.Barn, new Vector3(14f, 9f, 8f)),
    };

    /// <summary>The height of the eaves (the roof's lower edge) as a share of the height to the ridge, per style.</summary>
    private static float EaveShare(HouseStyle style) => style switch
    {
        HouseStyle.Cottage => 0.46f,
        HouseStyle.Farmhouse => 0.45f,
        HouseStyle.TwoStorey => 0.62f,
        _ => 0.45f,
    };

    /// <summary>A house's colours: walls, roof and the trim round its windows.</summary>
    private readonly record struct Colours(Vector3 Wall, Vector3 Roof, Vector3 Trim);

    /// <summary>Plaster under tiles, planks under slates, plaster under slates, planks under tiles; picked by seed (sRGB, as <see cref="Palette"/>).</summary>
    private static readonly Colours[] ColourSetsSrgb =
    {
        new(Palette.Plaster, Palette.RoofTile, Palette.Planks),
        new(Palette.Planks, Palette.RoofSlate, Palette.Plaster),
        new(Palette.Plaster, Palette.RoofSlate, Palette.Planks),
        new(Palette.Planks, Palette.RoofTile, Palette.Plaster),
    };

    /// <summary><see cref="ColourSetsSrgb"/> in linear colour, as the shader takes vertex colours.</summary>
    private static readonly Colours[] ColourSets = ColourSetsSrgb.Select(c => new Colours(Lin(c.Wall), Lin(c.Roof), Lin(c.Trim))).ToArray();
    private static readonly Vector3 DoorColour = Lin(Palette.Door);
    private static readonly Vector3 GlassColour = Lin(Palette.WindowGlass);
    private static readonly Vector3 PlinthColour = Lin(Palette.Stone);
    private static readonly Vector3 ChimneyColour = Lin(Palette.Brick);
    private static readonly Vector3 SootColour = Lin(Palette.Charred);

    /// <summary>The style whose size is nearest (length, width, height).</summary>
    public static HouseStyle StyleFor(float length, float width, float height)
    {
        var size = new Vector3(length, width, height);
        HouseStyle best = HouseStyle.Cottage;
        float nearest = float.MaxValue;
        foreach (var (style, styleSize) in Sizes)
        {
            float distance = Vector3.DistanceSquared(size, styleSize);
            if (distance >= nearest) continue;
            (best, nearest) = (style, distance);
        }
        return best;
    }

    /// <summary>A house prop's box: length along the ridge (the footprint's X), width across it, height above the ground.</summary>
    public static (float Length, float Width, float Height) SizeOf(in Prop house) =>
        (2 * house.Footprint.HalfExtents.X, 2 * house.Footprint.HalfExtents.Y, house.HeightAboveGround());

    /// <summary>
    /// The seed a house prop is drawn with: its style (the nearest to its size) × <see cref="Variants"/> + 1 … Variants, the
    /// look picked by a hash of its id. The same on every machine; its colours are <see cref="ColoursOf"/> it.
    /// </summary>
    public static uint SeedFor(in Prop house)
    {
        var (length, width, height) = SizeOf(house);
        HouseStyle style = StyleFor(length, width, height);
        return (uint)style * Variants + SeededNoise.Hash(house.Id, 0, 0, VariantSeed) % Variants + 1;
    }

    /// <summary>The wall and roof colours (sRGB, as <see cref="Palette"/>) of a house and its ruin drawn with <paramref name="seed"/>.</summary>
    public static (Vector3 Wall, Vector3 Roof) ColoursOf(uint seed)
    {
        Colours colours = ColourSetsSrgb[seed % ColourSetsSrgb.Length];
        return (colours.Wall, colours.Roof);
    }

    /// <summary>How far a ruin is moved off its house's place: <see cref="RuinShift"/> m along the push, none without one.</summary>
    public static Vector2 RuinOffset(Vector2? push) =>
        push is { } way && way.LengthSquared() > 1e-6f ? Vector2.Normalize(way) * RuinShift : Vector2.Zero;

    /// <summary>
    /// A cartoon house filling the box exactly: x ∈ [−length/2, length/2] (the ridge runs along X), z ∈ [−width/2, width/2]
    /// with the door on the −Z face (the prop's AxisForward once the node turns by the footprint's yaw, as WallViews does),
    /// y ∈ [0, height] with the ridge at height; walls of plaster or planks, a gable roof of tiles or slates, door, windows,
    /// a chimney on some; vertex colours from one of several colour sets picked by seed.
    /// </summary>
    public static MeshData House(HouseStyle style, float length, float width, float height, uint seed)
    {
        float R(int salt) => SeededNoise.Hash01((int)style, salt, 11, seed);
        Colours colours = ColourSets[seed % ColourSets.Length];
        var roof = new Roof(length, width, height, height * EaveShare(style));
        var parts = new List<(MeshData, Matrix4x4)>();

        // The body: a five-sided block under the roof (its gable ends up to the roof's underside), on a stone plinth.
        float hz = width / 2 - WallInset, hx = length / 2 - WallInset;
        float wallTop = roof.UndersideAt(hz) - 0.02f, gableTop = roof.UndersideAt(0f) - 0.02f;
        var profile = new[] { new Vector2(-hz, 0f), new Vector2(hz, 0f), new Vector2(hz, wallTop), new Vector2(0f, gableTop), new Vector2(-hz, wallTop) };
        parts.Add(Part(Extrude.Build(profile, 2 * hx), colours.Wall, Matrix4x4.Identity));
        parts.Add(Part(Box.Build(new Vector3(2 * (hx + PlinthOut), PlinthHeight, 2 * (hz + PlinthOut))), PlinthColour,
            Matrix4x4.CreateTranslation(0f, PlinthHeight / 2, 0f)));

        // The roof: two slabs meeting at the ridge, out to the box on every side, and a board along the ridge.
        parts.Add(Part(roof.Slab(front: true), colours.Roof, Matrix4x4.Identity));
        parts.Add(Part(roof.Slab(front: false), colours.Roof, Matrix4x4.Identity));
        parts.Add(Part(Box.Build(new Vector3(length, CapHeight, CapWidth)), colours.Roof * 0.8f,
            Matrix4x4.CreateTranslation(0f, height - CapHeight / 2, 0f)));

        // The front: a door in one bay, a window in each other one; upstairs a window in every bay.
        int bays = Math.Max(2, (int)MathF.Round(2 * hx / Bay));
        float bay = 2 * hx / bays;
        bool barn = style == HouseStyle.Barn;
        // A barn's doors are in the middle; a house's in any bay but the end ones (either of two).
        int doorBay = barn ? bays / 2 : bays == 2 ? (int)(R(1) * 2f) : 1 + Math.Min((int)(R(1) * (bays - 2)), bays - 3);
        Matrix4x4 front = Matrix4x4.CreateTranslation(0f, 0f, -hz), back = front * Matrix4x4.CreateRotationY(MathF.PI);
        float upstairs = wallTop - 1.5f;
        for (int b = 0; b < bays; b++)
        {
            float x = -hx + bay * (b + 0.5f);
            if (b == doorBay)
            {
                float doorWidth = barn ? MathF.Min(BarnDoorWidth, bay * 0.9f) : DoorWidth;
                float doorHeight = barn ? MathF.Min(BarnDoorHeight, wallTop - 0.4f) : DoorHeight;
                Door(parts, doorWidth, doorHeight, colours.Trim, Matrix4x4.CreateTranslation(x, 0f, 0f) * front);
            }
            else if (!barn) Window(parts, WindowWidth, WindowHeight, colours.Trim, Matrix4x4.CreateTranslation(x, Sill + WindowHeight / 2, 0f) * front);
            if (style == HouseStyle.TwoStorey)
                Window(parts, WindowWidth, WindowHeight, colours.Trim, Matrix4x4.CreateTranslation(x, upstairs, 0f) * front);
            if (b % 2 == (int)(seed & 1) && !barn)   // the back: every other bay
                Window(parts, WindowWidth, WindowHeight, colours.Trim, Matrix4x4.CreateTranslation(-x, Sill + WindowHeight / 2, 0f) * back);
        }
        if (barn)   // small windows high on the front, either side of the doors
            foreach (int b in new[] { 0, bays - 1 })
                Window(parts, WindowWidth * 0.8f, WindowHeight * 0.6f, colours.Trim,
                    Matrix4x4.CreateTranslation(-hx + bay * (b + 0.5f), wallTop - 1f, 0f) * front);

        // The gable ends: a window low down and one in the gable, each side.
        foreach (float side in new[] { -1f, 1f })
        {
            Matrix4x4 gable = Matrix4x4.CreateTranslation(0f, 0f, -hx) * Matrix4x4.CreateRotationY(side * -MathF.PI / 2);
            if (!barn) Window(parts, WindowWidth, WindowHeight, colours.Trim, Matrix4x4.CreateTranslation(0f, Sill + WindowHeight / 2, 0f) * gable);
            float loft = wallTop + (gableTop - wallTop) * 0.35f;
            if (gableTop - wallTop > 1.8f)
                Window(parts, WindowWidth * 0.7f, WindowHeight * 0.7f, colours.Trim, Matrix4x4.CreateTranslation(0f, loft, 0f) * gable);
        }

        // A brick chimney on the back slope near one end, its top under the ridge.
        if (!barn && R(2) < 0.7f)
        {
            float x = (R(3) < 0.5f ? -1f : 1f) * (hx - 1.2f);
            float z = roof.HalfWidth * 0.4f;
            float top = height - ChimneyBelowRidge;
            float stack = top - ChimneyCap - wallTop;
            parts.Add(Part(Box.Build(new Vector3(ChimneySide, stack, ChimneySide)), ChimneyColour,
                Matrix4x4.CreateTranslation(x, wallTop + stack / 2, z)));
            parts.Add(Part(Box.Build(new Vector3(ChimneySide + 0.16f, ChimneyCap, ChimneySide + 0.16f)), SootColour,
                Matrix4x4.CreateTranslation(x, top - ChimneyCap / 2, z)));
        }
        return Merge.Build(parts);
    }

    /// <summary>
    /// A heap of chunks and roof pieces over (length + 1) × (width + 1), 1–2.2 m high: broken slabs of wall tipped over
    /// the ground, chunks piled highest in the middle, roof pieces and a few beams on top, in the colours
    /// <see cref="House"/> gives the same seed. Each chunk is shaded a little apart (vertex colour times 0.8 … 1).
    /// </summary>
    public static MeshData Ruin(float length, float width, uint seed)
    {
        float R(int k, int salt) => SeededNoise.Hash01(k, salt, 13, seed);
        Colours colours = ColourSets[seed % ColourSets.Length];
        var half = new Vector3(length / 2 + 0.5f, 0f, width / 2 + 0.5f);
        float peak = RuinMinPeak + RuinPeakSpread * R(0, 0);
        var heap = new Heap(half, peak);
        var parts = new List<(MeshData, Matrix4x4)>();

        // The floor of the heap: broken slabs of wall tipped every way, three rows of them, higher in the middle.
        int rows = 3, columns = Math.Max(3, (int)MathF.Round(length / 2.4f));
        for (int c = 0; c < columns; c++)
        for (int r = 0; r < rows; r++)
        {
            int k = c * rows + r;
            var size = new Vector3(2 * half.X / columns * (0.6f + 0.2f * R(k, 1)), 0.35f + 0.2f * R(k, 33), 2 * half.Z / rows * (0.6f + 0.2f * R(k, 2)));
            var at = new Vector2(-half.X + 2 * half.X * (c + 0.5f) / columns, -half.Z + 2 * half.Z * (r + 0.5f) / rows);
            Matrix4x4 turn = Matrix4x4.CreateFromYawPitchRoll((R(k, 3) - 0.5f) * 0.8f, (R(k, 4) - 0.5f) * 0.7f, (R(k, 5) - 0.5f) * 0.7f);
            parts.Add(heap.Place(Box.Build(size), colours.Wall * (0.75f + 0.15f * R(k, 6)), turn, at, heap.CrestAt(at) * 0.5f));
        }

        // Chunks of wall, more of them and higher towards the middle.
        for (int k = 0; k < RuinChunks; k++)
        {
            float s = 0.6f + 0.7f * R(k, 7);
            var size = new Vector3(s, s * (0.5f + 0.3f * R(k, 8)), s * (0.6f + 0.3f * R(k, 9)));
            var at = new Vector2((R(k, 10) + R(k, 11) - 1f) * half.X, (R(k, 12) + R(k, 13) - 1f) * half.Z);
            Matrix4x4 turn = Matrix4x4.CreateFromYawPitchRoll(R(k, 14) * MathF.Tau, (R(k, 15) - 0.5f) * 0.9f, (R(k, 16) - 0.5f) * 0.9f);
            parts.Add(heap.Place(Box.Build(size, s * 0.08f), colours.Wall * (0.8f + 0.2f * R(k, 17)), turn, at, heap.CrestAt(at) * (0.6f + 0.4f * R(k, 18))));
        }

        // Pieces of the roof leaning on the heap, the first one at its top.
        for (int k = 0; k < RuinRoofPieces; k++)
        {
            var size = new Vector3(1.2f + 0.8f * R(k, 19), 0.14f, 0.8f + 0.6f * R(k, 20));
            var at = k == 0 ? Vector2.Zero : new Vector2((R(k, 21) * 2 - 1) * half.X * 0.7f, (R(k, 22) * 2 - 1) * half.Z * 0.7f);
            Matrix4x4 turn = Matrix4x4.CreateFromYawPitchRoll(R(k, 23) * MathF.Tau, 0.2f + 0.4f * R(k, 24), (R(k, 25) - 0.5f) * 0.4f);
            parts.Add(heap.Place(Box.Build(size), colours.Roof * (0.85f + 0.15f * R(k, 26)), turn, at, k == 0 ? peak : heap.CrestAt(at)));
        }

        // A few beams lying across it.
        for (int k = 0; k < RuinBeams; k++)
        {
            var size = new Vector3(2.5f + 1f * R(k, 27), 0.2f, 0.2f);
            var at = new Vector2((R(k, 28) * 2 - 1) * half.X * 0.5f, (R(k, 29) * 2 - 1) * half.Z * 0.5f);
            Matrix4x4 turn = Matrix4x4.CreateFromYawPitchRoll(R(k, 30) * MathF.Tau, (R(k, 31) - 0.5f) * 0.6f, 0f);
            parts.Add(heap.Place(Box.Build(size), Lin(Palette.Planks) * (0.75f + 0.15f * R(k, 32)), turn, at, heap.CrestAt(at)));
        }
        return Merge.Build(parts);
    }

    /// <summary>A door (and a lintel over it in the trim colour) on a wall whose face is the z = 0 plane, outward −Z, centred at x = 0, standing on y = 0.</summary>
    private static void Door(List<(MeshData, Matrix4x4)> parts, float width, float height, Vector3 trim, Matrix4x4 place)
    {
        parts.Add(Part(Box.Build(new Vector3(width, height, WallInset)), DoorColour, Matrix4x4.CreateTranslation(0f, height / 2, -WallInset / 2) * place));
        parts.Add(Part(Box.Build(new Vector3(width + 2 * FrameBar, FrameBar * 1.5f, WallInset)), trim,
            Matrix4x4.CreateTranslation(0f, height + FrameBar * 0.75f, -WallInset / 2) * place));
    }

    /// <summary>
    /// A window on a wall whose face is the z = 0 plane, outward −Z, centred at the origin: the glass set back half the
    /// inset, a frame round it and a cross over it out to the box's face.
    /// </summary>
    private static void Window(List<(MeshData, Matrix4x4)> parts, float width, float height, Vector3 trim, Matrix4x4 place)
    {
        Matrix4x4 Bar(float x, float y) => Matrix4x4.CreateTranslation(x, y, -WallInset / 2) * place;
        parts.Add(Part(Box.Build(new Vector3(width, height, WallInset / 2)), GlassColour, Matrix4x4.CreateTranslation(0f, 0f, -WallInset / 4) * place));
        float outerW = width + 2 * FrameBar;
        parts.Add(Part(Box.Build(new Vector3(outerW, FrameBar, WallInset)), trim, Bar(0f, (height + FrameBar) / 2)));
        parts.Add(Part(Box.Build(new Vector3(outerW + 0.1f, FrameBar * 1.4f, WallInset)), trim, Bar(0f, -(height + FrameBar * 1.4f) / 2)));
        parts.Add(Part(Box.Build(new Vector3(FrameBar, height, WallInset)), trim, Bar(-(width + FrameBar) / 2, 0f)));
        parts.Add(Part(Box.Build(new Vector3(FrameBar, height, WallInset)), trim, Bar((width + FrameBar) / 2, 0f)));
        parts.Add(Part(Box.Build(new Vector3(FrameBar * 0.7f, height, WallInset)), trim, Bar(0f, 0f)));
        parts.Add(Part(Box.Build(new Vector3(width, FrameBar * 0.7f, WallInset)), trim, Bar(0f, 0f)));
    }

    private static (MeshData, Matrix4x4) Part(MeshData mesh, Vector3 colour, Matrix4x4 place) => (mesh.WithColor(new Vector4(colour, 1f)), place);

    private static Vector3 Lin(Vector3 srgb) => Palette.Linear(srgb);

    /// <summary>
    /// The gable roof over a box: its top surface runs from the eaves (<c>eave</c> high at z = ±width/2) up to the ridge
    /// (<c>height</c> at z = 0), <see cref="RoofThickness"/> thick measured square to the slope.
    /// </summary>
    private readonly struct Roof
    {
        private readonly float _length, _height, _eave, _drop;

        public Roof(float length, float width, float height, float eave)
        {
            (_length, HalfWidth, _height, _eave) = (length, width / 2, height, eave);
            float slope = (height - eave) / HalfWidth;
            _drop = RoofThickness * MathF.Sqrt(1f + slope * slope);   // the slab's thickness measured straight down
        }

        public float HalfWidth { get; }

        /// <summary>The height of the roof's underside |z| from the ridge.</summary>
        public float UndersideAt(float z) => _height - (_height - _eave) * MathF.Abs(z) / HalfWidth - _drop;

        /// <summary>One slab: the front (−Z) or back (+Z) side, cut square at the eaves and the ridge, the box's whole length.</summary>
        public MeshData Slab(bool front)
        {
            float side = front ? -1f : 1f;
            var profile = new[]
            {
                new Vector2(side * HalfWidth, _eave), new Vector2(0f, _height), new Vector2(0f, _height - _drop), new Vector2(side * HalfWidth, _eave - _drop),
            };
            return Extrude.Build(profile, _length);
        }
    }

    /// <summary>
    /// Lays the pieces of a ruin within its box: x ∈ ±half.X, z ∈ ±half.Z, from <see cref="RuinFloor"/> up to the peak,
    /// the crest falling from the peak in the middle to a third of it at the edges.
    /// </summary>
    private readonly struct Heap(Vector3 half, float peak)
    {
        public float CrestAt(Vector2 at)
        {
            float r = MathF.Max(MathF.Abs(at.X) / half.X, MathF.Abs(at.Y) / half.Z);
            return peak * (1f - 0.67f * Math.Clamp(r, 0f, 1f) * Math.Clamp(r, 0f, 1f));
        }

        /// <summary>
        /// The piece turned by <paramref name="turn"/>, moved so its top is at <paramref name="top"/> over
        /// <paramref name="at"/>, then pushed back inside the box (the floor wins over the peak).
        /// </summary>
        public (MeshData, Matrix4x4) Place(MeshData piece, Vector3 colour, Matrix4x4 turn, Vector2 at, float top)
        {
            MeshData turned = piece.Transformed(turn);
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (Vector3 p in turned.Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            float x = MathF.Min(MathF.Max(at.X, -half.X - min.X), half.X - max.X);
            float z = MathF.Min(MathF.Max(at.Y, -half.Z - min.Z), half.Z - max.Z);
            float y = MathF.Max(MathF.Min(top, peak) - max.Y, RuinFloor - min.Y);
            return Part(turned, colour, Matrix4x4.CreateTranslation(x, y, z));
        }
    }
}

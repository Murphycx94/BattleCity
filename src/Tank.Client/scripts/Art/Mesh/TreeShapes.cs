using System.Numerics;
using Tank.Sim.Maps;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Cartoon background trees, rooted at the origin and about 7–9 m tall at scale 1, leaning a little.
/// Broadleaf: a warm brown trunk under 3–5 smooth, slightly lumpy green balls. Conifer: 3–4 stacked cones whose rims
/// are bevelled round. Vertex colour RGB is the base colour (linear) already darkened towards the ground (cheap
/// ambient occlusion); A is how much the part sways in the wind (0 trunk, up to 1 at the crown).
/// The tree line draws them with the default trunks; the forests build theirs with a trunk to suit the crown (<see cref="TreeFit"/>).
/// </summary>
public static class TreeShapes
{
    public const int BlobSubdivisions = 1;
    private const int TierSides = 8;
    private const int TrunkSides = 6;
    private const float TreeHeight = 8.5f;
    private const float MaxLean = 0.06f;
    private const float AoFloor = 0.6f;                     // brightness at the foot of the tree
    private const float BlobLumps = 0.06f;
    private static readonly Vector3 Bark = Palette.Linear(Palette.Bark);
    private static readonly Vector3 Needles = Palette.Linear(Palette.Needles);
    private static readonly Vector3 Leaves = Palette.Linear(Palette.Leaves);

    /// <summary>The tree line's trunk radius at scale 1 for a conifer (<see cref="Conifer"/>'s default).</summary>
    public const float ConiferTrunk = 0.24f;
    /// <summary>The tree line's trunk radius at scale 1 for a broadleaf (<see cref="Broadleaf"/>'s default).</summary>
    public const float BroadleafTrunk = 0.28f;

    /// <summary>A tree's size at scale 1: its trunk's radius at the foot, its crown's radius and its height.</summary>
    public readonly record struct Shape(float TrunkRadius, float CrownRadius, float Height);

    /// <param name="trunkRadius">The trunk's radius at its foot; it tapers to 0.65 of that at the top. Only the trunk changes with it.</param>
    public static MeshData Conifer(uint seed, float trunkRadius = ConiferTrunk)
    {
        float Rnd(int a, int b) => SeededNoise.Hash01(a, b, 0, seed);
        var parts = new List<(MeshData, Matrix4x4)> { Part(Trunk(trunkRadius, 2.4f), Bark, 1f, 0f, Matrix4x4.Identity) };
        int tiers = 3 + (int)(SeededNoise.Hash(9, 9, 9, seed) % 2);
        float y = 1.6f, radius = 2.6f, height = 3.4f;
        for (int tier = 0; tier < tiers; tier++)
        {
            float r = radius * (0.9f + 0.2f * Rnd(tier, 0));
            var place = Matrix4x4.CreateRotationY(Rnd(tier, 1) * MathF.Tau)
                * Matrix4x4.CreateTranslation((Rnd(tier, 3) - 0.5f) * 0.3f, y, (Rnd(tier, 4) - 0.5f) * 0.3f);
            parts.Add(Part(Tier(r, height * (0.9f + 0.2f * Rnd(tier, 5))), Needles, 0.9f + 0.2f * Rnd(tier, 6), y, place));
            y += height * 0.55f;
            radius *= 0.75f;
            height *= 0.85f;
        }
        return Lean(parts, seed);
    }

    /// <param name="trunkRadius">The trunk's radius at its foot; it tapers to 0.65 of that at the top. Only the trunk changes with it.</param>
    public static MeshData Broadleaf(uint seed, float trunkRadius = BroadleafTrunk)
    {
        float Rnd(int a, int b) => SeededNoise.Hash01(a, b, 0, seed);
        var parts = new List<(MeshData, Matrix4x4)> { Part(Trunk(trunkRadius, 4.4f), Bark, 1f, 0f, Matrix4x4.Identity) };
        int blobs = 3 + (int)(SeededNoise.Hash(7, 7, 7, seed) % 3);
        for (int blob = 0; blob < blobs; blob++)
        {
            float r = blob == 0 ? 2.0f : 1.3f + 0.5f * Rnd(blob, 0);
            float angle = blob * MathF.Tau / Math.Max(blobs - 1, 1) + Rnd(blob, 1) * 0.8f;
            float spread = blob == 0 ? 0f : 1.2f + 0.4f * Rnd(blob, 3);
            float cy = blob == 0 ? 5.4f : 4.7f + 0.9f * Rnd(blob, 4);
            var offset = new Vector3(MathF.Cos(angle) * spread, cy, MathF.Sin(angle) * spread);
            parts.Add(Part(Blob(r, seed + (uint)blob * 31u), Leaves, 0.9f + 0.2f * Rnd(blob, 2), cy, Matrix4x4.CreateTranslation(offset)));
        }
        return Lean(parts, seed);
    }

    /// <summary>
    /// Measures <paramref name="tree"/> as it is: the crown's radius is the widest foliage vertex (colour A &gt; 0) from the
    /// Y axis, the height its highest vertex; the trunk radius is <paramref name="trunkRadius"/>, what it was built with.
    /// </summary>
    public static Shape Measure(MeshData tree, float trunkRadius)
    {
        float crown = 0f, height = float.NegativeInfinity;
        for (int i = 0; i < tree.VertexCount; i++)
        {
            Vector3 p = tree.Positions[i];
            height = MathF.Max(height, p.Y);
            if (tree.Colors[i].W > 0f) crown = MathF.Max(crown, MathF.Sqrt(p.X * p.X + p.Z * p.Z));
        }
        return new Shape(trunkRadius, crown, height);
    }

    /// <summary>One conifer tier of base radius <paramref name="radius"/>: a cone whose rim is bevelled round.</summary>
    internal static MeshData Tier(float radius, float height) => Lathe.Build(new[]
    {
        new Vector2(0, 0), new Vector2(radius * 0.85f, 0.03f * height), new Vector2(radius * 0.98f, 0.09f * height),
        new Vector2(radius * 0.97f, 0.15f * height), new Vector2(radius * 0.85f, 0.24f * height), new Vector2(0, height),
    }, TierSides, smoothAngleDeg: 75f);

    /// <summary>A smooth, softly lumpy ball for a broadleaf crown.</summary>
    private static MeshData Blob(float radius, uint seed)
    {
        MeshData sphere = Icosphere.Build(BlobSubdivisions);
        var positions = new Vector3[sphere.VertexCount];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 d = sphere.Positions[i];
            positions[i] = d * radius * new Vector3(1f, 0.85f, 1f) * (1f + BlobLumps * SeededNoise.Fbm(d * 1.4f, seed, 2));
        }
        Vector3[] normals = RockShape.SmoothNormals(positions, sphere.Indices);
        var b = new MeshBuilder();
        for (int i = 0; i < positions.Length; i++) b.AddVertex(positions[i], normals[i], sphere.Uvs[i]);
        for (int t = 0; t < sphere.Indices.Count; t += 3) b.AddTriangle(sphere.Indices[t], sphere.Indices[t + 1], sphere.Indices[t + 2]);
        return b.Build();
    }

    /// <summary>Colours a part: base colour × brightness variation × height-based darkening.</summary>
    private static (MeshData, Matrix4x4) Part(MeshData mesh, Vector3 baseColor, float variation, float partY, Matrix4x4 place)
    {
        bool isBark = baseColor == Bark;
        Vector3 tinted = baseColor * (isBark ? 1f : variation);
        MeshData coloured = mesh.WithColors(p =>
        {
            float height = Math.Clamp((partY + p.Y) / TreeHeight, 0f, 1f);
            float ao = AoFloor + (1f - AoFloor) * height;
            return new Vector4(tinted * ao, isBark ? 0f : Math.Clamp(height, 0.15f, 1f));
        });
        return (coloured, place);
    }

    private static MeshData Lean(List<(MeshData Mesh, Matrix4x4 Transform)> parts, uint seed)
    {
        float tilt = (SeededNoise.Hash01(0, 0, 7, seed) - 0.5f) * 2f * MaxLean;
        float bearing = SeededNoise.Hash01(0, 1, 7, seed) * MathF.Tau;
        var lean = Matrix4x4.CreateFromAxisAngle(new Vector3(MathF.Cos(bearing), 0, MathF.Sin(bearing)), tilt);
        return Merge.Build(parts.Select(p => (p.Mesh, p.Transform * lean)));
    }

    private static MeshData Trunk(float radius, float height) =>
        Lathe.Build(new[] { new Vector2(radius, -0.3f), new Vector2(radius * 0.65f, height) }, TrunkSides);
}

/// <summary>
/// Fits the tree meshes to the map's tree props (riverland design §6.4). A forest mesh has a trunk
/// <see cref="Prop.CrownPerTrunk"/> times thinner than its crown, so scaled to the prop's crown and height (<see cref="Scale"/>)
/// its trunk is the prop's trunk too, give or take the taper. Which kind a tree is follows from its id and where it stands
/// on its map (the wire carries neither, plan deviation 13), the same on every machine.
/// </summary>
public static class TreeFit
{
    /// <summary>Meshes per kind; a tree takes variant <c>Id % Variants</c>.</summary>
    public const int Variants = 2;
    /// <summary>The share of conifers outside a map's forests, and on a map without any.</summary>
    public const float ElsewhereShare = 0.4f;
    private const uint KindSeed = 6151u;

    /// <summary>
    /// A forest whose trees are conifers with likelihood <see cref="Share"/>: the rectangle (X world X, Y world Z) its
    /// ragged outline stays within.
    /// </summary>
    public readonly record struct ConiferRegion(Vector2 Min, Vector2 Max, float Share);

    // The riverland's forests' conifer shares, by name (riverland design §3.7).
    private static readonly Dictionary<string, float> RiverlandShares = new()
    {
        ["north"] = 0.8f, ["north-east"] = 0.8f, ["east"] = 0.5f, ["west"] = 0.3f, ["centre"] = 0.3f,
    };

    private static readonly ConiferRegion[] Riverland = RiverlandMap.Forests.Select(forest => new ConiferRegion(
        forest.Min - new Vector2(RiverlandMap.ForestJitter), forest.Max + new Vector2(RiverlandMap.ForestJitter),
        RiverlandShares.TryGetValue(forest.Name, out float share) ? share
            : throw new InvalidOperationException($"No conifer share for the riverland's {forest.Name} forest."))).ToArray();

    /// <summary>The forests of map <paramref name="mapId"/> (ignoring case): the riverland's five; none for any other or an unknown map.</summary>
    public static IReadOnlyList<ConiferRegion> ForestsOf(string? mapId) =>
        string.Equals(mapId, RiverlandMap.Id, StringComparison.OrdinalIgnoreCase) ? Riverland : Array.Empty<ConiferRegion>();

    /// <summary>
    /// Forest mesh <paramref name="variant"/> of a kind (the tree line's seeds), with its trunk radius set to its crown's
    /// radius / <see cref="Prop.CrownPerTrunk"/> (the crown doesn't depend on the trunk, so one measurement settles it), and
    /// its shape at scale 1.
    /// </summary>
    public static (MeshData Mesh, TreeShapes.Shape Shape) Variant(bool conifer, int variant)
    {
        uint seed = conifer ? 7u + (uint)variant * 13u : 11u + (uint)variant * 17u;
        MeshData Build(float trunk) => conifer ? TreeShapes.Conifer(seed, trunk) : TreeShapes.Broadleaf(seed, trunk);
        float defaultTrunk = conifer ? TreeShapes.ConiferTrunk : TreeShapes.BroadleafTrunk;
        float trunkRadius = TreeShapes.Measure(Build(defaultTrunk), defaultTrunk).CrownRadius / Prop.CrownPerTrunk;
        MeshData mesh = Build(trunkRadius);
        return (mesh, TreeShapes.Measure(mesh, trunkRadius));
    }

    /// <summary>
    /// The scale that draws a mesh of <paramref name="shape"/> the size of <paramref name="tree"/>: its crown radius
    /// (<see cref="Prop.FoliageRadius"/>) across, its height above the ground (<see cref="PropExtents.HeightAboveGround"/>) up.
    /// </summary>
    public static Vector3 Scale(in Prop tree, TreeShapes.Shape shape)
    {
        float across = tree.FoliageRadius / shape.CrownRadius;
        return new Vector3(across, tree.HeightAboveGround() / shape.Height, across);
    }

    /// <summary>
    /// How likely a tree at (x, z) is a conifer: the share of the first of <paramref name="forests"/> it is in (on the
    /// riverland the north and north-east forests 0.8, the east 0.5, the west and middle 0.3), else <see cref="ElsewhereShare"/>.
    /// </summary>
    public static float ConiferShare(IReadOnlyList<ConiferRegion> forests, float x, float z)
    {
        foreach (var (min, max, share) in forests)
            if (x >= min.X && x <= max.X && z >= min.Y && z <= max.Y) return share;
        return ElsewhereShare;
    }

    /// <summary>Whether <paramref name="tree"/> is drawn as a conifer: a hash of its id under the <see cref="ConiferShare"/> where it stands.</summary>
    public static bool IsConifer(in Prop tree, IReadOnlyList<ConiferRegion> forests) =>
        SeededNoise.Hash01(tree.Id, 0, 0, KindSeed) < ConiferShare(forests, tree.Footprint.Center.X, tree.Footprint.Center.Y);
}

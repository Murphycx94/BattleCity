using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>
/// Grass around a focus point (the player's tank, so orbiting the camera never rebuilds anything): the map is cut into
/// 16 m cells and each cell within <see cref="Radius"/> owns one pooled MultiMesh of clumps, in one of three detail
/// bands by distance. A cell's clumps are seeded by a hash of its coordinates, so they are the same every time it
/// reappears. Clumps grow only where the terrain shader's grass layer wins (<see cref="GroundCover"/> mirrors its slope and
/// dirt-mask rules) and outside rocks.
/// Streaming is budgeted by candidate count per frame and reuses its buffers; steady state allocates nothing managed.
/// </summary>
public partial class GrassField : Node3D
{
    public const float Radius = 90f;
    public const int DefaultCandidateBudget = 4000;
    private const float CellSize = 16f;
    private const float KeepRadius = Radius + CellSize;   // hysteresis so cells don't flicker at the edge
    private const uint CellSeedSalt = 0x5bd1e995u;

    // Level of detail by cell distance: full clumps close up, thinner and wider further out so the meadow stays continuous.
    private static readonly float[] LodFar = { 28f, 52f, float.MaxValue };
    private static readonly float[] LodDensity = { 6.5f, 2.4f, 0.9f };             // clumps per m²
    private static readonly float[] LodScale = { 1f, 1.3f, 1.7f };           // clump size multiplier
    private static readonly (int Segments, int Blades)[] LodShape = { (3, 7), (2, 5), (1, 3) };
    private const float LodHysteresis = 4f;
    private const float MinScale = 0.6f, MaxScale = 1.2f;
    private const float RandomTilt = 0.25f;
    private const float MinGrassWeight = 0.5f;   // the terrain's grass layer must be at least this strong for a clump to grow
    private const float RockMargin = 0.4f;
    private static readonly float CellReach = CellSize * 0.5f * MathF.Sqrt(2f);   // centre to corner of a grass cell
    private const float BucketSlack = 0.01f;   // m: no cell at the edge of a prop's reach is lost to rounding
    private const int Variants = 4;
    private const int FloatsPerInstance = 12 + 4;   // Transform3D rows (3x4) + custom data

    private static readonly Comparison<Wanted> ByDistance = (a, b) => a.Distance.CompareTo(b.Distance);

    private readonly Dictionary<Vector2I, Cell> _cells = new();
    private readonly Stack<Cell> _pool = new();
    private readonly List<Vector2I> _dropped = new();
    private readonly List<Wanted> _wanted = new();
    private readonly List<Obb2> _nearRocks = new();
    private List<Obb2>?[]? _rockBuckets;   // per grass cell (row-major), the grown solid footprints that may reach it; built on the first fill
    private int _bucketsX;
    private float[][] _buffers = Array.Empty<float[]>();   // one reusable upload buffer per detail band, sized for its candidate count
    private HeightField _terrain = null!;
    private DirtMask _dirt = null!;
    private IReadOnlyList<Prop> _props = Array.Empty<Prop>();
    private ArrayMesh[][] _clumps = Array.Empty<ArrayMesh[]>();
    private ShaderMaterial _material = null!;

    private sealed class Cell
    {
        public Cell(MultiMeshInstance3D node, MultiMesh multimesh) { Node = node; Multimesh = multimesh; }
        public MultiMeshInstance3D Node { get; }
        public MultiMesh Multimesh { get; }
        public int Lod { get; set; }
    }

    private readonly record struct Wanted(Vector2I Key, float Distance, int Lod);

    public int ActiveCells => _cells.Count;

    // Global shader parameters read by grass.gdshader (declared under [shader_globals] in project.godot). One tank for now; making these arrays is the way to support several.
    private const float OffMap = -100000f;                    // project.godot's default for tank_footprint
    private const string FootprintParam = "tank_footprint";   // vec4: centre x, centre z, half width, half length
    private const string AxesParam = "tank_axes";             // vec4: right x, right z, forward x, forward z
    private const string FocusParam = "grass_focus";          // vec2: where the far fade is centred

    /// <summary>Tells the grass shader where the tank is, so blades under and beside it lie flat instead of poking through the tracks.</summary>
    public void SetTankFootprint(Obb2 footprint)
    {
        RenderingServer.GlobalShaderParameterSet(FootprintParam, new Vector4(footprint.Center.X, footprint.Center.Y, footprint.HalfExtents.X, footprint.HalfExtents.Y));
        SN.Vector2 right = footprint.AxisRight, forward = footprint.AxisForward;
        RenderingServer.GlobalShaderParameterSet(AxesParam, new Vector4(right.X, right.Y, forward.X, forward.Y));
    }

    /// <summary>Moves the flattened patch off the map (the shader's own default), for a camera that follows no tank.</summary>
    public void ClearTankFootprint() => SetTankFootprint(new Obb2(new SN.Vector2(OffMap, OffMap), SN.Vector2.Zero, 0f));

    public void Build(HeightField terrain, DirtMask dirt, IReadOnlyList<Prop> props)
    {
        _terrain = terrain;
        _dirt = dirt;
        _props = props;
        _rockBuckets = null;
        _clumps = LodShape.Select(shape => Enumerable.Range(1, Variants)
            .Select(v => GrassClump.Build((uint)v * 7919u, shape.Segments, shape.Blades).ToArrayMesh()).ToArray()).ToArray();
        _buffers = LodDensity.Select(density => new float[(int)(CellSize * CellSize * density) * FloatsPerInstance]).ToArray();
        _material = Materials.FromShader("grass");
    }

    public override void _ExitTree()
    {
        foreach (Cell cell in _cells.Values) cell.Multimesh.Dispose();
        foreach (Cell cell in _pool) cell.Multimesh.Dispose();
    }

    /// <summary>
    /// Adds cells that came into range (nearest first), re-detail those that changed band, and recycles those that left.
    /// At most about <paramref name="candidateBudget"/> clump candidates are generated per call (one cell is always allowed).
    /// </summary>
    public void Update(Vector3 focus, int candidateBudget = DefaultCandidateBudget)
    {
        var center = new Vector2(focus.X, focus.Z);
        RenderingServer.GlobalShaderParameterSet(FocusParam, center);
        _dropped.Clear();
        foreach (var pair in _cells)
            if (CellCenter(pair.Key).DistanceTo(center) > KeepRadius) _dropped.Add(pair.Key);
        foreach (Vector2I key in _dropped)
        {
            Cell cell = _cells[key];
            _cells.Remove(key);
            cell.Node.Visible = false;
            _pool.Push(cell);
        }

        _wanted.Clear();
        int reach = (int)MathF.Ceiling(Radius / CellSize) + 1;
        int cx = (int)MathF.Floor(center.X / CellSize), cz = (int)MathF.Floor(center.Y / CellSize);
        int maxX = (int)MathF.Ceiling(_terrain.SizeX / CellSize), maxZ = (int)MathF.Ceiling(_terrain.SizeZ / CellSize);
        for (int z = Math.Max(0, cz - reach); z <= Math.Min(maxZ - 1, cz + reach); z++)
        for (int x = Math.Max(0, cx - reach); x <= Math.Min(maxX - 1, cx + reach); x++)
        {
            var key = new Vector2I(x, z);
            float distance = CellCenter(key).DistanceTo(center);
            if (_cells.TryGetValue(key, out Cell? cell))
            {
                int lod = LodBands.Select(distance, cell.Lod, LodFar, LodHysteresis);
                if (lod != cell.Lod) _wanted.Add(new Wanted(key, distance, lod));   // crossed into another detail band
            }
            else if (distance <= Radius + CellSize * 0.71f)
                _wanted.Add(new Wanted(key, distance, LodBands.Select(distance, -1, LodFar, LodHysteresis)));
        }
        _wanted.Sort(ByDistance);

        int spent = 0;
        foreach (Wanted want in _wanted)
        {
            int cost = (int)(CellSize * CellSize * LodDensity[want.Lod]);
            if (spent > 0 && spent + cost > candidateBudget) break;
            spent += cost;
            if (!_cells.TryGetValue(want.Key, out Cell? cell)) _cells[want.Key] = cell = _pool.Count > 0 ? _pool.Pop() : NewCell();
            cell.Lod = want.Lod;
            Fill(want.Key, cell);
        }
    }

    private static Vector2 CellCenter(Vector2I key) => new((key.X + 0.5f) * CellSize, (key.Y + 0.5f) * CellSize);

    private void Fill(Vector2I key, Cell cell)
    {
        int lod = cell.Lod;
        var rng = new XorShift(SeededNoise.Hash(key.X, key.Y, 0, CellSeedSalt));
        CollectNearbyRocks(key);
        float[] buffer = _buffers[lod];
        int candidates = buffer.Length / FloatsPerInstance;
        int count = 0;
        for (int k = 0; k < candidates; k++)
        {
            float x = (key.X + rng.NextFloat()) * CellSize, z = (key.Y + rng.NextFloat()) * CellSize;
            float yaw = rng.NextFloat() * Mathf.Tau, scale = (MinScale + rng.NextFloat() * (MaxScale - MinScale)) * LodScale[lod];
            float tiltAngle = rng.NextFloat() * Mathf.Tau, tilt = rng.NextFloat() * RandomTilt;
            float hueShift = rng.NextFloat(), phase = rng.NextFloat(), brightness = rng.NextFloat();
            if (x > _terrain.SizeX || z > _terrain.SizeZ) continue;
            SN.Vector3 n = VisualNormal(x, z);
            if (GroundCover.GrassWeight(x, z, n.Y, _dirt.At(x, z)) < MinGrassWeight || InsideRock(x, z)) continue;
            var scatter = new Basis(new Vector3(MathF.Cos(tiltAngle), 0, MathF.Sin(tiltAngle)), tilt);
            Basis basis = Lean(new Vector3(n.X, n.Y, n.Z)) * scatter * new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale);
            float y = TerrainMesh.VisualHeightAt(_terrain, x, z) - 0.02f;
            int o = count++ * FloatsPerInstance;
            // Godot's MultiMesh buffer: three rows of (basis row, origin component), then the custom data.
            buffer[o + 0] = basis.Row0.X; buffer[o + 1] = basis.Row0.Y; buffer[o + 2] = basis.Row0.Z; buffer[o + 3] = x;
            buffer[o + 4] = basis.Row1.X; buffer[o + 5] = basis.Row1.Y; buffer[o + 6] = basis.Row1.Z; buffer[o + 7] = y;
            buffer[o + 8] = basis.Row2.X; buffer[o + 9] = basis.Row2.Y; buffer[o + 10] = basis.Row2.Z; buffer[o + 11] = z;
            buffer[o + 12] = hueShift; buffer[o + 13] = phase; buffer[o + 14] = brightness; buffer[o + 15] = 0f;
        }

        cell.Node.Visible = count > 0;
        if (count == 0) return;
        // The MultiMesh always holds a full band's worth of instances (a buffer upload must match its size exactly); only the
        // first `count` are drawn. The unused tail repeats instance 0 so the culling bounds stay tight.
        for (int o = count * FloatsPerInstance; o < buffer.Length; o += FloatsPerInstance) Array.Copy(buffer, 0, buffer, o, FloatsPerInstance);
        MultiMesh multimesh = cell.Multimesh;
        multimesh.Mesh = _clumps[lod][(int)((uint)(key.X * 31 + key.Y * 17) % Variants)];
        if (multimesh.InstanceCount != candidates) multimesh.InstanceCount = candidates;
        multimesh.Buffer = buffer;
        multimesh.VisibleInstanceCount = count;
    }

    private Cell NewCell()
    {
        var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true };
        var node = new MultiMeshInstance3D
        {
            Multimesh = multimesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 1f, // wind sway reaches a little outside the instances' bounds
        };
        AddChild(node);
        return new Cell(node, multimesh);
    }

    /// <summary>Normal of the ground as drawn (eased at the map edge), from central differences of the visual height.</summary>
    private SN.Vector3 VisualNormal(float x, float z)
    {
        float e = _terrain.CellSize * 0.5f;
        float dx = TerrainMesh.VisualHeightAt(_terrain, x + e, z) - TerrainMesh.VisualHeightAt(_terrain, x - e, z);
        float dz = TerrainMesh.VisualHeightAt(_terrain, x, z + e) - TerrainMesh.VisualHeightAt(_terrain, x, z - e);
        return SN.Vector3.Normalize(new SN.Vector3(-dx, 2 * e, -dz));
    }

    /// <summary>Tilts a clump half-way towards the ground normal so it follows slopes without looking combed.</summary>
    private static Basis Lean(Vector3 normal)
    {
        Vector3 axis = Vector3.Up.Cross(normal);
        float length = axis.Length();
        return length < 1e-4f ? Basis.Identity : new Basis(axis / length, MathF.Asin(Math.Min(length, 1f)) * 0.5f);
    }

    /// <summary>
    /// Gathers the (grown) solid footprints that can touch this cell, once per fill: the cell's bucket, filtered by the same
    /// distance test a scan over every prop makes, so the list (and its order) is exactly what that scan would give.
    /// </summary>
    private void CollectNearbyRocks(Vector2I key)
    {
        _nearRocks.Clear();
        _rockBuckets ??= BucketRocks();
        if (_rockBuckets[key.Y * _bucketsX + key.X] is not { } bucket) return;
        var center = new SN.Vector2((key.X + 0.5f) * CellSize, (key.Y + 0.5f) * CellSize);
        foreach (Obb2 grown in bucket)
            if (SN.Vector2.Distance(grown.Center, center) <= CellReach + grown.HalfExtents.Length()) _nearRocks.Add(grown);
    }

    /// <summary>
    /// Puts every solid prop (bushes don't count: grass grows on under them), grown by <see cref="RockMargin"/>, into the
    /// bucket of each grass cell whose centre lies in the square around the prop's circumscribed circle widened by
    /// <see cref="CellReach"/>: every cell the distance test can accept. Props go in in their order, so buckets keep it.
    /// </summary>
    private List<Obb2>?[] BucketRocks()
    {
        int cellsX = (int)MathF.Ceiling(_terrain.SizeX / CellSize), cellsZ = (int)MathF.Ceiling(_terrain.SizeZ / CellSize);
        var buckets = new List<Obb2>?[cellsX * cellsZ];
        _bucketsX = cellsX;
        foreach (Prop prop in _props)
        {
            if (!prop.IsSolid) continue;
            Obb2 grown = prop.Footprint with { HalfExtents = prop.Footprint.HalfExtents + new SN.Vector2(RockMargin) };
            float reach = CellReach + grown.HalfExtents.Length() + BucketSlack;
            // Cell centres (k + 0.5) · CellSize within [centre − reach, centre + reach] on each axis, clamped to the map.
            int x0 = Math.Max(0, (int)MathF.Ceiling((grown.Center.X - reach) / CellSize - 0.5f));
            int x1 = Math.Min(cellsX - 1, (int)MathF.Floor((grown.Center.X + reach) / CellSize - 0.5f));
            int z0 = Math.Max(0, (int)MathF.Ceiling((grown.Center.Y - reach) / CellSize - 0.5f));
            int z1 = Math.Min(cellsZ - 1, (int)MathF.Floor((grown.Center.Y + reach) / CellSize - 0.5f));
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                (buckets[z * cellsX + x] ??= new List<Obb2>()).Add(grown);
        }
        return buckets;
    }

    private bool InsideRock(float x, float z)
    {
        var p = new SN.Vector2(x, z);
        foreach (Obb2 rock in _nearRocks)
            if (rock.Contains(p)) return true;
        return false;
    }
}

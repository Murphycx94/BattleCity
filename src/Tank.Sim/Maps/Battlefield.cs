using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;

namespace Tank.Sim.Maps;

/// <summary>Where a segment test stopped. <see cref="PropId"/> is <see cref="TerrainId"/> for the ground.</summary>
public readonly record struct RayHit(float T, Vector3 Point, int PropId)
{
    public const int TerrainId = 0;
}

/// <summary>
/// Terrain, what its ground is made of, the water over it, the props on it and the rectangle tanks may drive in. Props
/// keep their shapes; what changes as the battle goes on is their state: whether each still stands, and its hit points.
/// Only the simulation changes it (<see cref="Simulation"/> and the <see cref="Vehicles.TankMotion.Drive"/> it calls), or
/// a network client following the server's word (<see cref="SetDurability"/>), so a battlefield belongs to one battle:
/// it is not safe to share between simulations, nor to reuse once the battle is over (<see cref="Fresh"/> makes one for
/// the next, sharing everything that never changes). Every query here (rays, tank collision, bushes) sees the
/// props as they stand now; a grid index over the props (<see cref="PropIndex"/>, built here and never changed) keeps
/// them to the props nearby, with results identical to a scan of them all.
/// </summary>
public sealed class Battlefield
{
    private readonly Prop[] _props;
    private readonly Dictionary<int, int> _indexOf;
    private readonly int[] _durability;
    private readonly bool[] _standing;
    private readonly PropIndex _index;

    /// <summary>The side of a cell of the prop index, m.</summary>
    internal const float IndexCellSize = 8f;

    /// <summary>What callers give <see cref="Near"/> and <see cref="Along"/> to fill: enough for any one tank's or shell's surroundings.</summary>
    internal const int QueryBufferSize = 512;

    /// <summary>A ray may cross the whole map: <see cref="Raycast"/> gathers its candidates in a buffer this big.</summary>
    private const int RayBufferSize = 1024;

    /// <param name="surface">The ground's kinds, one per terrain cell; null for grass everywhere.</param>
    /// <param name="waterLevel">The height of the water's surface; null for no water.</param>
    public Battlefield(HeightField terrain, IEnumerable<Prop> props, float border, SurfaceMap? surface = null, float? waterLevel = null)
    {
        if (border < 0 || border * 2 >= MathF.Min(terrain.SizeX, terrain.SizeZ))
            throw new ArgumentOutOfRangeException(nameof(border), $"Border {border}m leaves no playable area.");
        int cellsX = terrain.SamplesX - 1, cellsZ = terrain.SamplesZ - 1;
        if (surface is not null && (surface.CellsX != cellsX || surface.CellsZ != cellsZ || surface.CellSize != terrain.CellSize))
            throw new ArgumentException(
                $"The surface's {surface.CellsX} × {surface.CellsZ} cells of {surface.CellSize} m don't match the terrain's {cellsX} × {cellsZ} of {terrain.CellSize} m.",
                nameof(surface));
        if (waterLevel is { } level && !float.IsFinite(level))
            throw new ArgumentException($"Water level must be finite, got {level}.", nameof(waterLevel));
        Terrain = terrain;
        Surface = surface ?? SurfaceMap.Uniform(cellsX, cellsZ, terrain.CellSize);
        WaterLevel = waterLevel;
        _props = props.ToArray();
        _indexOf = new Dictionary<int, int>(_props.Length);
        for (int i = 0; i < _props.Length; i++)
        {
            Prop prop = _props[i];
            if (prop.Id == RayHit.TerrainId)
                throw new ArgumentException($"Prop id {prop.Id} is reserved for terrain.", nameof(props));
            if (!_indexOf.TryAdd(prop.Id, i))
                throw new ArgumentException($"Duplicate prop id {prop.Id}.", nameof(props));
            if (prop.Height <= 0)
                throw new ArgumentException($"Prop {prop.Id} must have positive height, got {prop.Height}.", nameof(props));
            if (prop.IsRound && prop.Footprint.HalfExtents.X != prop.Footprint.HalfExtents.Y)
                throw new ArgumentException($"{prop.Kind} {prop.Id} must have a round footprint (equal half extents).", nameof(props));
        }
        _durability = _props.Select(p => p.MaxDurability).ToArray();
        _standing = _props.Select(_ => true).ToArray();
        _index = new PropIndex(_props, terrain.SizeX, terrain.SizeZ, IndexCellSize);
        Border = border;
    }

    /// <summary>
    /// <paramref name="source"/>'s ground, water and props (shared: none of them ever changes), with every prop standing
    /// at full hit points. The prop index and the id lookup are built from the props alone, so they are shared too.
    /// </summary>
    private Battlefield(Battlefield source)
    {
        Terrain = source.Terrain;
        Surface = source.Surface;
        WaterLevel = source.WaterLevel;
        Border = source.Border;
        _props = source._props;
        _indexOf = source._indexOf;
        _index = source._index;
        _durability = _props.Select(p => p.MaxDurability).ToArray();
        _standing = _props.Select(_ => true).ToArray();
    }

    public HeightField Terrain { get; }

    /// <summary>The ground's kinds, one per terrain cell (riverland design §2.1).</summary>
    public SurfaceMap Surface { get; }

    /// <summary>The height of the water's surface: ground below it is under water (riverland design §2.2). Null without water.</summary>
    public float? WaterLevel { get; }

    /// <summary>
    /// Goes up by one whenever a prop's standing or hit points really change (damage, knocking down, restoring, the
    /// server's word), so a view can tell whether anything needs redrawing without looking at every prop.
    /// </summary>
    public int StateVersion { get; private set; }

    /// <summary>
    /// A new battlefield for a new battle: the same terrain, surface, water and props (the same instances, exact to the
    /// bit), every prop standing at full hit points, <see cref="StateVersion"/> 0.
    /// </summary>
    public Battlefield Fresh() => new(this);

    /// <summary>The kind of ground under <paramref name="ground"/> (x, z).</summary>
    public SurfaceKind SurfaceAt(Vector2 ground) => Surface.At(ground.X, ground.Y);

    /// <summary>How deep the water stands over the ground at <paramref name="ground"/> (x, z): 0 on dry ground or without water.</summary>
    public float WaterDepthAt(Vector2 ground) =>
        WaterLevel is { } level ? MathF.Max(0f, level - Terrain.HeightAt(ground.X, ground.Y)) : 0f;

    /// <summary>The top-speed factor at <paramref name="ground"/> (x, z): the water's where there is water, else the ground's (<see cref="GroundSpeed"/>).</summary>
    public float SpeedFactorAt(Vector2 ground) => GroundSpeed.Factor(SurfaceAt(ground), WaterDepthAt(ground));
    public IReadOnlyList<Prop> Props => _props;
    public float Border { get; }

    /// <summary>Whether there is a prop with id <paramref name="propId"/> (<see cref="PropById"/> throws for one that is not).</summary>
    public bool HasProp(int propId) => _indexOf.ContainsKey(propId);

    /// <summary>The prop with id <paramref name="propId"/>; throws if there is none.</summary>
    public Prop PropById(int propId) => _props[IndexOf(propId)];

    /// <summary>True until the prop is destroyed (a wall turned to rubble, a fence knocked flat, a bush shot away). Rocks always stand.</summary>
    public bool IsStanding(int propId) => _standing[IndexOf(propId)];

    /// <summary>Hit points left: <see cref="Prop.MaxDurability"/> when intact, 0 once destroyed (and always 0 for a rock, which can't be).</summary>
    public int Durability(int propId) => _durability[IndexOf(propId)];

    /// <summary>
    /// Sets a destructible prop's hit points from outside the simulation (a client applying the server's prop state):
    /// 0 puts it down, anything else up to <see cref="Prop.MaxDurability"/> leaves it standing with that many left.
    /// </summary>
    public void SetDurability(int propId, int durability)
    {
        int index = IndexOf(propId);
        RequireDestructible(index);
        int max = _props[index].MaxDurability;
        if (durability < 0 || durability > max) throw new ArgumentOutOfRangeException(nameof(durability), durability, $"Prop {propId} has 0 … {max} hit points.");
        if (_durability[index] == durability && _standing[index] == (durability > 0)) return;
        _durability[index] = durability;
        _standing[index] = durability > 0;
        StateVersion++;
    }

    public Vector2 ClampToPlayable(Vector2 p) =>
        Vector2.Clamp(p, new Vector2(Border, Border), new Vector2(Terrain.SizeX - Border, Terrain.SizeZ - Border));

    /// <summary>
    /// Nearest hit of segment <paramref name="from"/>→<paramref name="to"/> against the ground, rocks and standing walls
    /// and houses. Fences, bushes, trees and rubble don't stop it.
    /// </summary>
    public bool Raycast(Vector3 from, Vector3 to, out RayHit hit)
    {
        bool found = Terrain.Raycast(from, to, out float best);
        int propId = RayHit.TerrainId;
        Span<int> buffer = stackalloc int[RayBufferSize];
        foreach (int i in Along(PropSet.ShellStoppers, new Vector2(from.X, from.Z), new Vector2(to.X, to.Z), 0f, buffer))
        {
            if (!_standing[i]) continue;
            ref readonly Prop prop = ref _props[i];
            if (Intersect.SegmentPrism(prop.Footprint, prop.Bottom, prop.Top, from, to, out float t) && (!found || t < best))
            {
                found = true;
                best = t;
                propId = prop.Id;
            }
        }
        hit = found ? new RayHit(best, Vector3.Lerp(from, to, best), propId) : default;
        return found;
    }

    /// <summary>
    /// Standing bushes whose edge comes within <paramref name="radius"/> of <paramref name="point"/> (their footprint is a
    /// circle of radius HalfExtents.X), in prop order, as they stand when called.
    /// </summary>
    public IEnumerable<Prop> BushesNear(Vector2 point, float radius)
    {
        var results = new List<Prop>();
        BushesNear(point, radius, results);
        return results;
    }

    /// <summary>Same as <see cref="BushesNear(Vector2, float)"/> but fills a caller-owned list (cleared first), without allocating.</summary>
    public void BushesNear(Vector2 point, float radius, List<Prop> results)
    {
        results.Clear();
        Span<int> buffer = stackalloc int[QueryBufferSize];
        foreach (int i in Near(PropSet.Foliage, point, radius, buffer))
            if (_props[i].Kind == PropKind.Bush && _standing[i] && IsNear(_props[i], point, radius))
                results.Add(_props[i]);
    }

    private static bool IsNear(in Prop bush, Vector2 point, float radius) =>
        Vector2.Distance(bush.Footprint.Center, point) - bush.Footprint.HalfExtents.X <= radius;

    // The simulation's view: props by index into Props, and the means to change their state.

    /// <summary>
    /// Ascending indices of every prop in <paramref name="set"/>: <see cref="PropSet.Blockers"/> (rocks, walls, fences,
    /// houses and trees: what stands in tanks' way while it stands), <see cref="PropSet.ShellStoppers"/> (rocks, walls and
    /// houses), <see cref="PropSet.Flimsy"/> (fences, bushes and trees: shells fly through them and knock them down),
    /// <see cref="PropSet.Walls"/> (brick and stone walls and houses: what HE splashes and what leaves rubble),
    /// <see cref="PropSet.Foliage"/> (bushes and trees: what hides a tank).
    /// </summary>
    internal ReadOnlySpan<int> All(PropSet set) => _index.All(set);

    /// <summary>
    /// Ascending, distinct indices of the props of <paramref name="set"/> whose registered circle (<see cref="PropIndex.Reach"/>)
    /// reaches the disc (<paramref name="center"/>, <paramref name="radius"/>): a superset of what an exact test there can
    /// hit. <see cref="All"/> when the index is off or <paramref name="buffer"/> is too small for the result.
    /// </summary>
    internal ReadOnlySpan<int> Near(PropSet set, Vector2 center, float radius, Span<int> buffer) =>
        UseIndex ? _index.Near(set, center, radius, buffer) : _index.All(set);

    /// <summary>The same as <see cref="Near"/> for the ground segment <paramref name="from"/> → <paramref name="to"/> widened by <paramref name="margin"/>.</summary>
    internal ReadOnlySpan<int> Along(PropSet set, Vector2 from, Vector2 to, float margin, Span<int> buffer) =>
        UseIndex ? _index.Along(set, from, to, margin, buffer) : _index.All(set);

    /// <summary>Tests only: false makes <see cref="Near"/> and <see cref="Along"/> return <see cref="All"/>, the full scan the index must agree with.</summary>
    internal bool UseIndex { get; set; } = true;

    internal int IndexOf(int propId) =>
        _indexOf.TryGetValue(propId, out int index) ? index : throw new ArgumentException($"No prop {propId}.", nameof(propId));

    internal ref readonly Prop PropAt(int index) => ref _props[index];

    internal bool StandingAt(int index) => _standing[index];

    internal int DurabilityAt(int index) => _durability[index];

    /// <summary>Takes <paramref name="amount"/> off a standing prop's hit points. At zero it is destroyed.</summary>
    /// <returns>True if this destroyed it.</returns>
    internal bool Damage(int index, int amount)
    {
        RequireDestructible(index);
        if (!_standing[index] || amount <= 0) return false;
        _durability[index] = System.Math.Max(0, _durability[index] - amount);
        StateVersion++;
        if (_durability[index] > 0) return false;
        _standing[index] = false;
        return true;
    }

    /// <summary>Knocks a prop down outright, whatever its hit points.</summary>
    /// <returns>True if it was standing.</returns>
    internal bool Destroy(int index)
    {
        RequireDestructible(index);
        if (!_standing[index]) return false;
        _durability[index] = 0;
        _standing[index] = false;
        StateVersion++;
        return true;
    }

    /// <summary>Puts a prop back as it was at the start: standing, at full hit points. On a rock, which never falls, it changes nothing.</summary>
    internal void Restore(int index)
    {
        int max = _props[index].MaxDurability;
        if (_durability[index] == max && _standing[index]) return;
        _durability[index] = max;
        _standing[index] = true;
        StateVersion++;
    }

    private void RequireDestructible(int index)
    {
        if (!_props[index].IsDestructible) throw new InvalidOperationException($"Prop {_props[index].Id} ({_props[index].Kind}) can't be destroyed.");
    }
}

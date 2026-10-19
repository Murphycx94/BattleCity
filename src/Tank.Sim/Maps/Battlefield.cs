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
/// Terrain, the props on it and the rectangle tanks may drive in. Props keep their shapes; what changes as the battle
/// goes on is their state: whether each still stands, and its hit points. Only the simulation changes it
/// (<see cref="Simulation"/> and the <see cref="Vehicles.TankMotion.Drive"/> it calls), or a network client following
/// the server's word (<see cref="SetDurability"/>), so a battlefield belongs to one battle: it is not safe to share
/// between simulations, nor to reuse once the battle is over. Every query here (rays, tank collision, bushes) sees the
/// props as they stand now.
/// </summary>
public sealed class Battlefield
{
    private readonly Prop[] _props;
    private readonly Dictionary<int, int> _indexOf;
    private readonly int[] _durability;
    private readonly bool[] _standing;
    private readonly int[] _shellStoppers;
    private readonly int[] _blockers;
    private readonly int[] _flimsy;
    private readonly int[] _walls;
    private readonly int[] _bushes;

    public Battlefield(HeightField terrain, IEnumerable<Prop> props, float border)
    {
        if (border < 0 || border * 2 >= MathF.Min(terrain.SizeX, terrain.SizeZ))
            throw new ArgumentOutOfRangeException(nameof(border), $"Border {border}m leaves no playable area.");
        Terrain = terrain;
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
            if (prop.Kind == PropKind.Bush && prop.Footprint.HalfExtents.X != prop.Footprint.HalfExtents.Y)
                throw new ArgumentException($"Bush {prop.Id} must have a round footprint (equal half extents).", nameof(props));
        }
        _durability = _props.Select(p => p.MaxDurability).ToArray();
        _standing = _props.Select(_ => true).ToArray();
        _shellStoppers = IndicesWhere(p => p.StopsShells);
        _blockers = IndicesWhere(p => p.IsSolid);
        _flimsy = IndicesWhere(p => p.IsFlimsy);
        _walls = IndicesWhere(p => p.IsWall);
        _bushes = IndicesWhere(p => p.Kind == PropKind.Bush);
        Border = border;
    }

    public HeightField Terrain { get; }
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
        _durability[index] = durability;
        _standing[index] = durability > 0;
    }

    public Vector2 ClampToPlayable(Vector2 p) =>
        Vector2.Clamp(p, new Vector2(Border, Border), new Vector2(Terrain.SizeX - Border, Terrain.SizeZ - Border));

    /// <summary>
    /// Nearest hit of segment <paramref name="from"/>→<paramref name="to"/> against the ground, rocks and standing walls.
    /// Fences, bushes and rubble don't stop it.
    /// </summary>
    public bool Raycast(Vector3 from, Vector3 to, out RayHit hit)
    {
        bool found = Terrain.Raycast(from, to, out float best);
        int propId = RayHit.TerrainId;
        foreach (int i in _shellStoppers)
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

    /// <summary>Standing bushes whose edge comes within <paramref name="radius"/> of <paramref name="point"/> (their footprint is a circle of radius HalfExtents.X).</summary>
    public IEnumerable<Prop> BushesNear(Vector2 point, float radius)
    {
        foreach (int i in _bushes)
            if (_standing[i] && IsNear(_props[i], point, radius))
                yield return _props[i];
    }

    /// <summary>Same as <see cref="BushesNear(Vector2, float)"/> but fills a caller-owned list (cleared first), without allocating.</summary>
    public void BushesNear(Vector2 point, float radius, List<Prop> results)
    {
        results.Clear();
        foreach (int i in _bushes)
            if (_standing[i] && IsNear(_props[i], point, radius))
                results.Add(_props[i]);
    }

    private static bool IsNear(in Prop bush, Vector2 point, float radius) =>
        Vector2.Distance(bush.Footprint.Center, point) - bush.Footprint.HalfExtents.X <= radius;

    // The simulation's view: props by index into Props, and the means to change their state.

    /// <summary>Indices of rocks, walls and fences: what blocks tanks while it stands.</summary>
    internal ReadOnlySpan<int> Blockers => _blockers;

    /// <summary>Indices of fences and bushes: shells fly through them and knock them down.</summary>
    internal ReadOnlySpan<int> Flimsy => _flimsy;

    /// <summary>Indices of brick and stone walls: what HE splashes and what leaves rubble.</summary>
    internal ReadOnlySpan<int> Walls => _walls;

    /// <summary>Indices of bushes: what hides a tank from view.</summary>
    internal ReadOnlySpan<int> Bushes => _bushes;

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
        return true;
    }

    /// <summary>Puts a prop back as it was at the start: standing, at full hit points. On a rock, which never falls, it changes nothing.</summary>
    internal void Restore(int index)
    {
        _durability[index] = _props[index].MaxDurability;
        _standing[index] = true;
    }

    private void RequireDestructible(int index)
    {
        if (!_props[index].IsDestructible) throw new InvalidOperationException($"Prop {_props[index].Id} ({_props[index].Kind}) can't be destroyed.");
    }

    private int[] IndicesWhere(Func<Prop, bool> predicate) =>
        Enumerable.Range(0, _props.Length).Where(i => predicate(_props[i])).ToArray();
}

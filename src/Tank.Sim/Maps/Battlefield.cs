using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;

namespace Tank.Sim.Maps;

/// <summary>Where a segment test stopped. <see cref="PropId"/> is <see cref="TerrainId"/> for the ground.</summary>
public readonly record struct RayHit(float T, Vector3 Point, int PropId)
{
    public const int TerrainId = 0;
}

/// <summary>Terrain plus static props, and the rectangle tanks may drive in.</summary>
public sealed class Battlefield
{
    private readonly Prop[] _props;

    public Battlefield(HeightField terrain, IEnumerable<Prop> props, float border)
    {
        if (border < 0 || border * 2 >= MathF.Min(terrain.SizeX, terrain.SizeZ))
            throw new ArgumentOutOfRangeException(nameof(border), $"Border {border}m leaves no playable area.");
        Terrain = terrain;
        _props = props.ToArray();
        var seen = new HashSet<int>();
        foreach (var prop in _props)
        {
            if (prop.Id == RayHit.TerrainId)
                throw new ArgumentException($"Prop id {prop.Id} is reserved for terrain.", nameof(props));
            if (!seen.Add(prop.Id))
                throw new ArgumentException($"Duplicate prop id {prop.Id}.", nameof(props));
            if (prop.Height <= 0)
                throw new ArgumentException($"Prop {prop.Id} must have positive height, got {prop.Height}.", nameof(props));
        }
        Border = border;
    }

    public HeightField Terrain { get; }
    public IReadOnlyList<Prop> Props => _props;
    public float Border { get; }

    public Vector2 ClampToPlayable(Vector2 p) =>
        Vector2.Clamp(p, new Vector2(Border, Border), new Vector2(Terrain.SizeX - Border, Terrain.SizeZ - Border));

    /// <summary>Nearest hit of segment <paramref name="from"/>→<paramref name="to"/> against ground and props.</summary>
    public bool Raycast(Vector3 from, Vector3 to, out RayHit hit)
    {
        bool found = Terrain.Raycast(from, to, out float best);
        int propId = RayHit.TerrainId;
        foreach (var prop in _props)
        {
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
}

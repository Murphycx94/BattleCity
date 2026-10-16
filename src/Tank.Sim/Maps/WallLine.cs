using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;

namespace Tank.Sim.Maps;

/// <summary>
/// A straight run of wall or fence from <see cref="Start"/> to <see cref="End"/> on the ground (X is world X, Y is world Z),
/// the way maps place walls (design §3.2). <see cref="Segments"/> lays it out as separate 2 m props, each with its own id.
/// </summary>
public readonly record struct WallLine(PropKind Kind, Vector2 Start, Vector2 End)
{
    public const float SegmentLength = 2f;

    /// <summary>
    /// Each segment's base sits this far under the ground at its centre. The ground is sampled only there, so this keeps a
    /// segment from floating only where the ground under it drops less than this from its centre: gentle ground, as in the
    /// sandbox.
    /// </summary>
    public const float Sink = 0.3f;

    private const float LengthTolerance = 0.01f;

    /// <summary>How thick a segment of <paramref name="kind"/> is: brick 0.6 m, stone 1 m, fence 0.2 m.</summary>
    public static float Thickness(PropKind kind) => kind switch
    {
        PropKind.BrickWall => 0.6f,
        PropKind.StoneWall => 1f,
        PropKind.Fence => 0.2f,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only walls and fences are laid in lines."),
    };

    /// <summary>How tall a segment of <paramref name="kind"/> stands above the ground: walls 3 m, fences 1.5 m.</summary>
    public static float Height(PropKind kind) => kind switch
    {
        PropKind.BrickWall or PropKind.StoneWall => 3f,
        PropKind.Fence => 1.5f,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only walls and fences are laid in lines."),
    };

    /// <summary>How many 2 m segments the line holds. Throws unless its length is a whole number of them (to within 1 cm).</summary>
    public int SegmentCount
    {
        get
        {
            float length = Vector2.Distance(Start, End);
            int count = (int)MathF.Round(length / SegmentLength);
            if (count < 1 || MathF.Abs(length - count * SegmentLength) > LengthTolerance)
                throw new InvalidOperationException($"Wall line {Start}→{End} is {length:F2} m long, not a whole number of {SegmentLength} m segments.");
            return count;
        }
    }

    /// <summary>
    /// The line's segments from <see cref="Start"/> on, numbered <paramref name="firstId"/>, <paramref name="firstId"/> + 1, …:
    /// footprints long along the line (<see cref="Obb2.AxisRight"/>) and <see cref="Thickness"/> across it, standing
    /// <see cref="Height"/> above the ground at their centre with <see cref="Sink"/> more below it. Laid out (and checked)
    /// right away, not as they are enumerated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The line is not of walls or fences.</exception>
    /// <exception cref="InvalidOperationException">Its length is not a whole number of segments.</exception>
    public IEnumerable<Prop> Segments(HeightField terrain, int firstId)
    {
        float thickness = Thickness(Kind), height = Height(Kind) + Sink;
        var segments = new Prop[SegmentCount];
        Vector2 along = Vector2.Normalize(End - Start);
        float yaw = MathF.Atan2(-along.Y, along.X);   // Angles.Right(yaw) == along
        var half = new Vector2(SegmentLength / 2, thickness / 2);
        for (int k = 0; k < segments.Length; k++)
        {
            Vector2 center = Start + along * (SegmentLength * (k + 0.5f));
            float ground = terrain.HeightAt(center.X, center.Y);
            segments[k] = new Prop(firstId + k, Kind, new Obb2(center, half, yaw), ground - Sink, height);
        }
        return segments;
    }
}

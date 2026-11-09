using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Modes;

/// <summary>Where the two of a duos team appear (stage 6 design §2.2): beside their team's point, <see cref="Spacing"/> apart, facing as it does.</summary>
public static class DuoSpawns
{
    /// <summary>How far apart the two hulls' centres are, m.</summary>
    public const float Spacing = 10f;

    /// <summary>A spot counts as clear with no prop's footprint within this of the hull's footprint, m.</summary>
    public const float PropClearance = 1f;

    /// <summary>
    /// Member <paramref name="member"/> (0 or 1) of the team whose point is <paramref name="point"/>: half <see cref="Spacing"/>
    /// to the left (member 0) or right (1) of the heading — or, when either of the two spots isn't clear on <paramref name="field"/>
    /// for a heavy's hull (a solid prop within <see cref="PropClearance"/>, water under it, or outside the playable area), behind (0)
    /// and ahead (1) instead, clear or not (the riverland's points never come to that: <c>DuoSpawnTests</c>). Same yaw and team as
    /// the point. Both members make the same choice, so they never end up on one spot. Deterministic; no allocation.
    /// </summary>
    public static SpawnPoint Member(in SpawnPoint point, int member, Battlefield field)
    {
        if (member is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(member), member, "A duos team has members 0 and 1.");
        float half = Spacing / 2, sign = member == 0 ? -1f : 1f;
        Vector2 side = Angles.Right(point.Yaw);   // (cos yaw, −sin yaw): to the right of the heading
        Vector2 left = point.Position - side * half, right = point.Position + side * half;
        if (IsClear(left, point.Yaw, field) && IsClear(right, point.Yaw, field))
            return point with { Position = member == 0 ? left : right };
        return point with { Position = point.Position + Angles.Forward(point.Yaw) * (sign * half) };
    }

    /// <summary>
    /// Whether a heavy's hull at <paramref name="at"/> facing <paramref name="yaw"/> keeps <see cref="PropClearance"/> from every
    /// solid prop's footprint (rocks, walls, fences, houses and trees; bushes stand in nobody's way), has dry ground under its
    /// corners and centre, and has its centre inside the playable area.
    /// </summary>
    private static bool IsClear(Vector2 at, float yaw, Battlefield field)
    {
        if (field.ClampToPlayable(at) != at) return false;
        Obb2 hull = TankMotion.Footprint(TankCatalog.Heavy, at, yaw);
        Span<Vector2> corners = stackalloc Vector2[4];
        Corners(hull, corners);
        if (field.WaterDepthAt(at) > 0) return false;
        foreach (Vector2 c in corners) if (field.WaterDepthAt(c) > 0) return false;

        Span<int> buffer = stackalloc int[Battlefield.QueryBufferSize];
        Span<Vector2> propCorners = stackalloc Vector2[4];
        foreach (int i in field.Near(PropSet.Blockers, at, hull.HalfExtents.Length() + PropClearance, buffer))
            if (Gap(hull, corners, field.PropAt(i), propCorners) < PropClearance) return false;
        return true;
    }

    /// <summary>
    /// The distance between the hull and the prop's footprint, 0 where they overlap. A round prop (a tree's trunk) is a circle:
    /// its centre's distance from the hull less its radius. Two rectangles that don't overlap are nearest at a corner of one
    /// or the other, so the least distance of each one's corners from the other box is exact.
    /// </summary>
    private static float Gap(in Obb2 hull, ReadOnlySpan<Vector2> hullCorners, in Prop prop, Span<Vector2> propCorners)
    {
        Obb2 box = prop.Footprint;
        if (prop.IsRound) return MathF.Max(0f, hull.DistanceToPoint(box.Center) - box.HalfExtents.X);
        if (Obb2.TryGetPenetration(hull, box, out _)) return 0f;
        Corners(box, propCorners);
        float gap = float.MaxValue;
        for (int k = 0; k < 4; k++)
            gap = MathF.Min(gap, MathF.Min(box.DistanceToPoint(hullCorners[k]), hull.DistanceToPoint(propCorners[k])));
        return gap;
    }

    private static void Corners(in Obb2 box, Span<Vector2> into)
    {
        Vector2 right = box.AxisRight * box.HalfExtents.X, forward = box.AxisForward * box.HalfExtents.Y;
        into[0] = box.Center - right - forward;
        into[1] = box.Center + right - forward;
        into[2] = box.Center + right + forward;
        into[3] = box.Center - right + forward;
    }
}

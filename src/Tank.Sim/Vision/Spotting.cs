using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Vision;

/// <summary>
/// What one look from an observer at a target found (stage 3 design §2.2): whether it is <see cref="Seen"/>, how far
/// away it is (ground distance between hull centres), the observer's <see cref="DetectionRange"/> against it, the
/// target's effective concealment and what went into it (foliage, a recent shot), and whether a sight line is clear.
/// <see cref="Bushes"/> counts bushes and tree crowns alike (riverland design §2.4).
/// </summary>
public readonly record struct Sighting(bool Seen, float Distance, float DetectionRange, float Camo, int Bushes, bool FiredRecently, bool LineClear);

/// <summary>
/// The spotting rules (design §6, stage 3 design §2.2), stateless. A target's effective concealment is its moving or
/// still <see cref="Concealment"/>, times <see cref="FirePenalty"/> within <see cref="FirePenaltyTicks"/> of a shot,
/// plus <see cref="BushBonus"/> for each bush or tree crown hiding it (at most <see cref="MaxBushes"/> of them together),
/// capped at <see cref="MaxCamo"/>.
/// An observer spots it within <see cref="DirectRange"/> whatever stands between them; further out, within its
/// <see cref="DetectionRange"/> and with one of two sight lines (its turret top to the target's hull centre, and to the
/// target's turret top) clear of the ground, rocks and standing walls.
/// </summary>
public static class Spotting
{
    public const float DirectRange = 15f;

    /// <summary>Faster than this (m/s) a tank counts as moving.</summary>
    public const float MovingSpeed = 0.5f;

    /// <summary>
    /// A hull that turned more than this (radians) since the previous <see cref="VisionTracker"/> update counts as moving
    /// too: over <see cref="VisionTracker.UpdateInterval"/> ticks, so about 2.5° a second.
    /// </summary>
    public const float TurningAngle = 0.5f * MathF.PI / 180f;

    public const int FirePenaltyTicks = 3 * SimConstants.TickRate;
    public const float FirePenalty = 0.25f;
    public const float BushBonus = 0.25f;
    public const int MaxBushes = 2;
    public const float MaxCamo = 0.9f;

    /// <summary>Bushes and crowns on the sight line count only this close to the target, m.</summary>
    public const float BushReach = 15f;

    /// <summary>
    /// A tank is in a bush (or under a tree) when its hull centre is within this share of the bush's (crown's) radius; one
    /// whose edge it only touches hides it when the sight line runs through that inner part of it.
    /// </summary>
    public const float InsideBush = 0.75f;

    public static float DetectionRange(float viewRange, float camo) => DirectRange + (viewRange - DirectRange) * (1f - camo);

    public static float EffectiveCamo(Concealment concealment, bool moving, bool firedRecently, int bushes) =>
        MathF.Min(MaxCamo, (moving ? concealment.Moving : concealment.Still) * (firedRecently ? FirePenalty : 1f)
            + BushBonus * System.Math.Min(bushes, MaxBushes));

    public static bool FiredRecently(in TankState tank, int tick) => tick - tank.LastFired < FirePenaltyTicks;

    /// <summary>Where a tank looks from, and is looked at: the top centre of its turret (or casemate) box.</summary>
    public static Vector3 TurretTop(in TankState tank, TankPose pose)
    {
        OrientedBox turret = pose.TurretBox(tank.Spec, tank.TurretYaw);
        return turret.Center + pose.Up * turret.HalfExtents.Y;
    }

    /// <summary>
    /// How many standing bushes and trees hide a target (up to <see cref="MaxBushes"/> together), each once. A tree hides
    /// by its crown, an upright cylinder of <see cref="Prop.FoliageRadius"/> from its bottom to its top; a bush by its own
    /// cylinder. Those count that the sight line from <paramref name="eye"/> to <paramref name="target"/> crosses within
    /// <see cref="BushReach"/> of the target, and those its hull centre <paramref name="ground"/> stands in (or under).
    /// Such a one counts only when the sight line, its end included, comes within <see cref="InsideBush"/> of its radius
    /// from its middle: a tank well inside it, or behind its middle, is hidden; one on its rim, or with the bush behind
    /// it, is not (the line would otherwise always end inside the bush it touches).
    /// </summary>
    public static int BushesHiding(Battlefield field, Vector3 eye, Vector3 target, Vector2 ground)
    {
        float length = Vector3.Distance(eye, target);
        Vector3 near = length > BushReach ? target + (eye - target) * (BushReach / length) : eye;
        int count = 0;
        Span<int> buffer = stackalloc int[Battlefield.QueryBufferSize];
        foreach (int i in field.Near(PropSet.Foliage, ground, BushReach, buffer))
        {
            if (!field.StandingAt(i)) continue;
            ref readonly Prop foliage = ref field.PropAt(i);
            Vector2 center = foliage.Footprint.Center;
            float radius = foliage.FoliageRadius;
            float apart = Vector2.Distance(center, ground);
            if (apart > BushReach + radius) continue;   // a cheap pre-check: nowhere near the last BushReach of the line
            bool hides = apart <= radius
                ? GroundDistance(center, near, target) <= InsideBush * radius
                : Intersect.SegmentCylinder(center, radius, foliage.Bottom, foliage.Top, near, target, out _);
            if (hides && ++count == MaxBushes) break;
        }
        return count;
    }

    /// <summary>
    /// Whether <paramref name="observer"/> spots <paramref name="target"/>: the same answer as <see cref="Look"/>'s
    /// <see cref="Sighting.Seen"/>, but it counts foliage and casts sight lines only when they can change it (within
    /// <see cref="DirectRange"/> or beyond the view range they can't; a target closer than the detection range at the
    /// highest concealment needs no bushes counted), and stops at the first clear line. For <see cref="VisionTracker"/>.
    /// </summary>
    public static bool Sees(in TankState observer, in TankState target, Battlefield field, int tick, bool targetMoving)
    {
        float distance = Vector2.Distance(observer.Position, target.Position);
        if (distance <= DirectRange) return true;
        float view = CrateRules.ViewRange(observer);
        if (distance > view) return false;
        TankPose seer = TankPose.Compute(observer, field.Terrain), seen = TankPose.Compute(target, field.Terrain);
        Vector3 eye = TurretTop(observer, seer), hull = seen.HullBox(target.Spec).Center;
        if (distance > DetectionRange(view, MaxCamo))
        {
            int bushes = BushesHiding(field, eye, hull, target.Position);
            float camo = EffectiveCamo(target.Spec.Camo, targetMoving, FiredRecently(target, tick), bushes);
            if (distance > DetectionRange(view, camo)) return false;
        }
        return !field.Raycast(eye, hull, out _) || !field.Raycast(eye, TurretTop(target, seen), out _);
    }

    /// <summary>The ground distance from <paramref name="p"/> to the segment from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static float GroundDistance(Vector2 p, Vector3 a, Vector3 b)
    {
        Vector2 from = new(a.X, a.Z), along = new Vector2(b.X, b.Z) - from;
        float t = along.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(p - from, along) / along.LengthSquared(), 0f, 1f) : 0f;
        return Vector2.Distance(p, from + along * t);
    }

    /// <summary>
    /// One look from <paramref name="observer"/> at <paramref name="target"/> on <paramref name="tick"/>;
    /// <paramref name="targetMoving"/> says whether the target counts as moving (see <see cref="MovingSpeed"/>).
    /// </summary>
    public static Sighting Look(in TankState observer, in TankState target, Battlefield field, int tick, bool targetMoving)
    {
        float distance = Vector2.Distance(observer.Position, target.Position);
        TankPose seer = TankPose.Compute(observer, field.Terrain), seen = TankPose.Compute(target, field.Terrain);
        Vector3 eye = TurretTop(observer, seer);
        Vector3 hull = seen.HullBox(target.Spec).Center, turret = TurretTop(target, seen);
        int bushes = BushesHiding(field, eye, hull, target.Position);
        bool fired = FiredRecently(target, tick);
        float camo = EffectiveCamo(target.Spec.Camo, targetMoving, fired, bushes);
        float range = DetectionRange(CrateRules.ViewRange(observer), camo);
        bool lineClear = !field.Raycast(eye, hull, out _) || !field.Raycast(eye, turret, out _);
        bool spotted = distance <= DirectRange || (distance <= range && lineClear);
        return new Sighting(spotted, distance, range, camo, bushes, fired, lineClear);
    }
}

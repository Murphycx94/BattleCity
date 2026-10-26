using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;

namespace Tank.Sim.Vehicles;

/// <summary>Ground-hugging hull kinematics (design §4.1–4.2).</summary>
public static class TankMotion
{
    /// <summary>Above this speed the hull counts as moving and turns slower.</summary>
    private const float MovingThreshold = 0.5f;
    private const float MovingTurnFactor = 0.75f;
    private const int CollisionIterations = 3;
    /// <summary>Extra clearance added to every push-out so float error never leaves a hull touching.</summary>
    private const float Skin = 1e-3f;
    /// <summary>Fraction of a slope's grade a step must descend to count as heading downhill (about 60 degrees off the fall line).</summary>
    private const float DescentAlongFallLine = 0.5f;
    /// <summary>Penetration differences below this count as equal when ranking wedged poses, so push-out noise cannot freeze the hull.</summary>
    private const float TieDepth = 0.01f;
    /// <summary>Minimum drop per metre travelled for a step to count as clearly downhill (an absolute margin would block the first, tiny steps from rest).</summary>
    private const float DescentGrade = 0.05f;

    /// <summary>If collisions leave less than this fraction of the intended step, the hull was blocked head-on and stalls.</summary>
    private const float StallRatio = 0.25f;

    /// <summary>Top speed on rubble, as a share of the usual (design §3.2).</summary>
    public const float RubbleSpeedFactor = 0.7f;

    /// <summary>Breaking through brick walls and houses costs this share of the hull's speed (once a tick, however many it breaks).</summary>
    private const float RamSpeedKept = 0.5f;

    public static Obb2 Footprint(TankSpec spec, Vector2 center, float yaw) =>
        new(center, new Vector2(spec.HullWidth / 2, spec.HullLength / 2), yaw);

    /// <summary>At most this many props give way to one hull in one tick (a hull spans a few 2 m segments at most).</summary>
    private const int MaxGivingWay = 16;

    /// <summary>
    /// A push-out iteration looks at the blockers this much beyond the hull's reach from where it started, room for the
    /// pushes within one iteration; should they carry the hull further, it looks again (see <see cref="Resolve"/>).
    /// </summary>
    private const float ResolveSlack = 4f;

    /// <summary>
    /// Applies throttle and steering, knocks down what gives way where the hull is going (any fence or tree trunk it
    /// touches, brick walls when it is faster than its <see cref="TankSpec.BrickWallRamSpeed"/> and houses when faster than
    /// its <see cref="TankSpec.HouseRamSpeed"/>, those two at the cost of half its speed; their ids are added to
    /// <paramref name="felled"/> if given), then moves the hull and pushes it out of standing solid props (never trees,
    /// which always give way) and <paramref name="obstacles"/>. Knocking props down changes <paramref name="field"/> for
    /// everyone using it, so only the simulation stepping the battle drives with <paramref name="knockDown"/>; a client
    /// predicting its own tank drives without it: the props that would give way are then driven through as if down (the
    /// speed still halved for a brick wall or house) but left standing, and whether they really fell is the server's to say.
    /// </summary>
    public static void Drive(ref TankState t, in InputCmd input, Battlefield field, ReadOnlySpan<Obb2> obstacles, List<int>? felled = null, bool knockDown = true)
    {
        TankSpec spec = t.Spec;
        const float dt = SimConstants.Dt;

        float oldYaw = t.Yaw;
        float turnRate = spec.HullTraverse * (MathF.Abs(t.Speed) > MovingThreshold ? MovingTurnFactor : 1f);
        t.Yaw = Angles.Wrap(t.Yaw - System.Math.Sign(input.Steer) * turnRate * dt);

        float target = TargetSpeed(t, input.Throttle, field);
        bool accelerating = target * t.Speed >= 0 && MathF.Abs(target) > MathF.Abs(t.Speed);
        t.Speed = Scalar.MoveTowards(t.Speed, target, (accelerating ? spec.Acceleration : spec.Braking) * dt);

        Vector2 start = t.Position;
        Vector2 intended = Angles.Forward(t.Yaw) * (t.Speed * dt);
        Span<int> ignored = stackalloc int[MaxGivingWay];
        int ignoredCount = KnockDown(ref t, Footprint(spec, start, oldYaw), Footprint(spec, start + intended, t.Yaw), field, felled, knockDown, ignored);
        ReadOnlySpan<int> through = ignored[..ignoredCount];
        Vector2 dest = field.ClampToPlayable(Resolve(Footprint(spec, start + intended, t.Yaw), field, obstacles, through));
        if (IsTooSteepToEnter(spec, field.Terrain, start, dest)) dest = start;
        (t.Position, t.Yaw) = SettleWithoutOverlap(spec, dest, t.Yaw, oldYaw, start, field, obstacles, through);

        // Pushed almost straight back (head-on into a rock, a standing wall or fence, a tank or the map edge): stall.
        // A glancing contact keeps its speed so the hull slides along the obstacle.
        float intendedLength = intended.Length();
        if (intendedLength > 0 && Vector2.Distance(t.Position, start) < intendedLength * StallRatio) t.Speed = 0f;
    }

    /// <summary>
    /// A casemate gun can't follow an aim outside its arc, so with no steering input the hull turns towards it, exactly
    /// as if steered by hand, until the aim is back inside the arc. Steering by hand always wins.
    /// </summary>
    /// <returns><paramref name="input"/>, with <c>Steer</c> set when the hull should turn by itself.</returns>
    public static InputCmd TraverseHull(in TankState t, in InputCmd input, HeightField terrain)
    {
        TankSpec spec = t.Spec;
        if (spec.HasTurret || input.Steer != 0) return input;
        TankPose pose = TankPose.Compute(t, terrain);
        float wantYaw = pose.ToHullAngles(Angles.Direction(input.AimYaw, input.AimPitch), t.TurretYaw).TurretYaw;
        if (MathF.Abs(wantYaw) <= spec.GunArc) return input;
        return input with { Steer = (sbyte)(wantYaw > 0 ? -1 : 1) };   // +yaw is to the left; Steer +1 turns right
    }

    /// <summary>
    /// Turns turret and gun toward the input's world aim direction at their rated speeds, within gun limits; a turret takes
    /// the shorter way round, a casemate gun stops at the edge of its arc and never swings round through the rear.
    /// </summary>
    public static void AimGun(ref TankState t, in InputCmd input, HeightField terrain)
    {
        TankSpec spec = t.Spec;
        TankPose pose = TankPose.Compute(t, terrain);
        var (wantYaw, wantPitch) = pose.ToHullAngles(Angles.Direction(input.AimYaw, input.AimPitch), t.TurretYaw);
        float step = spec.TurretTraverse * SimConstants.Dt;
        if (spec.HasTurret)
        {
            t.TurretYaw = Angles.MoveTowards(t.TurretYaw, wantYaw, step);
        }
        else
        {
            // No wrapping: the shorter way round the circle can lead through the rear, outside the arc.
            wantYaw = System.Math.Clamp(wantYaw, -spec.GunArc, spec.GunArc);
            t.TurretYaw = Scalar.MoveTowards(t.TurretYaw, wantYaw, step);
        }
        float pitch = System.Math.Clamp(wantPitch, -spec.GunDepression, spec.GunElevation);
        t.GunPitch = Scalar.MoveTowards(t.GunPitch, pitch, spec.GunPitchSpeed * SimConstants.Dt);
    }

    /// <summary>Hull pitch along its facing: positive when the nose points uphill.</summary>
    public static float ForwardSlope(in TankState t, HeightField terrain)
    {
        Vector2 half = Angles.Forward(t.Yaw) * (t.Spec.HullLength * 0.5f);
        float front = terrain.HeightAt(t.Position.X + half.X, t.Position.Y + half.Y);
        float back = terrain.HeightAt(t.Position.X - half.X, t.Position.Y - half.Y);
        return MathF.Atan2(front - back, t.Spec.HullLength);
    }

    /// <summary>
    /// What gives way to a hull about to move from <paramref name="from"/> onto <paramref name="hull"/> (design §4.2,
    /// riverland design §2.4–2.5): any fence, any tree whose trunk (the circle, not its square) it touches, brick walls
    /// when it is faster than its <see cref="TankSpec.BrickWallRamSpeed"/> and houses when faster than its
    /// <see cref="TankSpec.HouseRamSpeed"/>; breaking into either costs it half its speed (once, however many at a time).
    /// With <paramref name="knockDown"/> they go down before the hull is pushed out of what still stands, so it drives on
    /// through; without it they are left standing and their indices put in <paramref name="ignored"/> (as many as fit),
    /// for the collision to drive through them all the same; trees need no place there, as the collision never meets
    /// them. A standing brick wall or house the hull is already inside can only be one it broke into without knocking
    /// down (the break cost it half its speed, so it may now be under ram speed): it still gives way, at no further cost,
    /// or the hull would be pushed back out and stall where the server drives on.
    /// </summary>
    /// <returns>How many of <paramref name="ignored"/> were filled.</returns>
    private static int KnockDown(ref TankState t, in Obb2 from, in Obb2 hull, Battlefield field, List<int>? felled, bool knockDown, Span<int> ignored)
    {
        float speed = MathF.Abs(t.Speed);
        bool ramWall = speed > t.Spec.BrickWallRamSpeed, ramHouse = speed > t.Spec.HouseRamSpeed;
        bool broke = false;
        int count = 0;
        Span<int> buffer = stackalloc int[Battlefield.QueryBufferSize];
        foreach (int i in field.Near(PropSet.Blockers, hull.Center, hull.HalfExtents.Length(), buffer))
        {
            ref readonly Prop prop = ref field.PropAt(i);
            if (!field.StandingAt(i) || !Touches(hull, prop)) continue;
            bool breaks = prop.Kind is PropKind.BrickWall or PropKind.House;
            bool inside = breaks && Overlaps(from, prop.Footprint);
            bool givesWay = prop.Kind switch
            {
                PropKind.Fence or PropKind.Tree => true,
                PropKind.BrickWall => ramWall || (!knockDown && inside),
                PropKind.House => ramHouse || (!knockDown && inside),
                _ => false,
            };
            if (!givesWay) continue;
            if (knockDown)
            {
                field.Destroy(i);
                felled?.Add(prop.Id);
            }
            else if (prop.Kind != PropKind.Tree && count < ignored.Length)
            {
                ignored[count++] = i;
            }
            broke |= breaks && !inside;
        }
        if (broke) t.Speed *= RamSpeedKept;
        return count;
    }

    /// <summary>The hull overlaps the prop: a tree's trunk circle, any other prop's footprint.</summary>
    private static bool Touches(in Obb2 hull, in Prop prop) =>
        prop.Kind == PropKind.Tree ? hull.IntersectsCircle(prop.Footprint.Center, prop.Footprint.HalfExtents.X) : Overlaps(hull, prop.Footprint);

    /// <summary>
    /// Whether the collision pushes a hull out of the prop at <paramref name="index"/>: a standing one, not driven through
    /// (<paramref name="ignored"/>), and not a tree: a tree always gives way (<see cref="KnockDown"/>), so it never holds a
    /// hull up, even where it is left standing (in a client's prediction, or a trunk whose square the hull overlaps but
    /// not its circle).
    /// </summary>
    private static bool Blocks(Battlefield field, int index, ReadOnlySpan<int> ignored) =>
        field.StandingAt(index) && field.PropAt(index).Kind != PropKind.Tree && !IsIgnored(index, ignored);

    private static bool IsIgnored(int index, ReadOnlySpan<int> ignored)
    {
        foreach (int i in ignored) if (i == index) return true;
        return false;
    }

    /// <summary>The hull lies partly on the rubble of a wall.</summary>
    private static bool OnRubble(in Obb2 hull, Battlefield field)
    {
        Span<int> buffer = stackalloc int[Battlefield.QueryBufferSize];
        foreach (int i in field.Near(PropSet.Walls, hull.Center, hull.HalfExtents.Length(), buffer))
            if (!field.StandingAt(i) && Overlaps(hull, field.PropAt(i).RubbleFootprint)) return true;
        return false;
    }

    private static bool Overlaps(in Obb2 a, in Obb2 b)
    {
        float reach = a.HalfExtents.Length() + b.HalfExtents.Length();
        return Vector2.DistanceSquared(a.Center, b.Center) <= reach * reach && Obb2.TryGetPenetration(a, b, out _);
    }

    /// <summary>
    /// The speed the throttle asks for: scaled by the ground or water under the hull's centre
    /// (<see cref="Battlefield.SpeedFactorAt"/>), lower on rubble (<see cref="RubbleSpeedFactor"/>) and uphill.
    /// </summary>
    private static float TargetSpeed(in TankState t, sbyte throttle, Battlefield field)
    {
        TankSpec spec = t.Spec;
        float target = throttle > 0 ? spec.MaxForwardSpeed : throttle < 0 ? -spec.MaxReverseSpeed : 0f;
        if (target != 0) target *= field.SpeedFactorAt(t.Position);
        if (target != 0 && OnRubble(Footprint(spec, t.Position, t.Yaw), field)) target *= RubbleSpeedFactor;
        HeightField terrain = field.Terrain;
        float uphill = ForwardSlope(t, terrain) * MathF.Sign(target);
        if (uphill <= 0) return target;
        if (uphill >= spec.MaxClimb) return 0f;
        float c = MathF.Cos(uphill);
        return target * c * c;
    }

    /// <summary>
    /// A step is refused when the destination slope exceeds the climb limit and the step does not descend clearly:
    /// the drop must be at least <see cref="DescentGrade"/> per metre and <see cref="DescentAlongFallLine"/> of the slope's own grade.
    /// </summary>
    private static bool IsTooSteepToEnter(TankSpec spec, HeightField terrain, Vector2 start, Vector2 dest)
    {
        float slope = MathF.Acos(System.Math.Clamp(terrain.NormalAt(dest.X, dest.Y).Y, -1f, 1f));
        if (slope <= spec.MaxClimb) return false;
        float requiredDrop = MathF.Max(DescentGrade, DescentAlongFallLine * MathF.Tan(slope)) * Vector2.Distance(start, dest);
        return terrain.HeightAt(dest.X, dest.Y) > terrain.HeightAt(start.X, start.Y) - requiredDrop;
    }

    /// <summary>
    /// Picks the first pose that is clear of every standing solid prop and obstacle: new heading at the resolved position,
    /// old heading there, then the old pose. If none is clear (a wedged hull) the least-penetrating one wins, so the tank
    /// can still drive out (ties go to the earlier candidate).
    /// </summary>
    private static (Vector2 Position, float Yaw) SettleWithoutOverlap(
        TankSpec spec, Vector2 dest, float yaw, float oldYaw, Vector2 oldPosition, Battlefield field, ReadOnlySpan<Obb2> obstacles, ReadOnlySpan<int> ignored)
    {
        Span<(Vector2 Position, float Yaw)> candidates = stackalloc (Vector2, float)[3] { (dest, yaw), (dest, oldYaw), (oldPosition, oldYaw) };
        var best = candidates[0];
        float bestDepth = float.MaxValue;
        foreach (var candidate in candidates)
        {
            float depth = PenetrationDepth(Footprint(spec, candidate.Position, candidate.Yaw), field, obstacles, ignored);
            if (depth <= Skin * 0.5f) return candidate;
            if (depth < bestDepth - TieDepth) (best, bestDepth) = (candidate, depth);
        }
        return best;
    }

    /// <summary>Deepest overlap of <paramref name="box"/> with any standing solid prop that <see cref="Blocks"/> or obstacle; 0 when clear.</summary>
    private static float PenetrationDepth(in Obb2 box, Battlefield field, ReadOnlySpan<Obb2> obstacles, ReadOnlySpan<int> ignored)
    {
        float deepest = 0f;
        Span<int> buffer = stackalloc int[Battlefield.QueryBufferSize];
        foreach (int i in field.Near(PropSet.Blockers, box.Center, box.HalfExtents.Length(), buffer))
            if (Blocks(field, i, ignored) && Obb2.TryGetPenetration(box, field.PropAt(i).Footprint, out Vector2 push)) deepest = MathF.Max(deepest, push.Length());
        foreach (Obb2 other in obstacles)
            if (Obb2.TryGetPenetration(box, other, out Vector2 push)) deepest = MathF.Max(deepest, push.Length());
        return deepest;
    }

    /// <summary>
    /// Pushes <paramref name="box"/> out of the standing blockers that <see cref="Blocks"/> and
    /// <paramref name="obstacles"/>, each in turn, up to <see cref="CollisionIterations"/> times round. Each round looks
    /// at the blockers within <see cref="ResolveSlack"/> of the hull's reach where the round starts, and again round the
    /// hull, for the blockers after the one that pushed it, should it be pushed further than that: so every blocker it
    /// meets is met in the same order, and at the same place, as when looking at them all.
    /// </summary>
    private static Vector2 Resolve(Obb2 box, Battlefield field, ReadOnlySpan<Obb2> obstacles, ReadOnlySpan<int> ignored)
    {
        Span<int> buffer = stackalloc int[Battlefield.QueryBufferSize];
        for (int iteration = 0; iteration < CollisionIterations; iteration++)
        {
            bool pushed = false;
            Vector2 asked = box.Center;
            ReadOnlySpan<int> near = NearBlockers(box, field, buffer);
            for (int k = 0; k < near.Length; k++)
            {
                int i = near[k];
                // Rubble, fallen fences and trees don't block (bushes aren't blockers at all).
                if (!Blocks(field, i, ignored) || !PushOut(ref box, field.PropAt(i).Footprint)) continue;
                pushed = true;
                if (Vector2.DistanceSquared(box.Center, asked) <= ResolveSlack * ResolveSlack) continue;
                asked = box.Center;
                near = NearBlockers(box, field, buffer);
                int next = near.BinarySearch(i);
                k = (next >= 0 ? next + 1 : ~next) - 1;   // carry on after blocker i
            }
            foreach (Obb2 other in obstacles) pushed |= PushOut(ref box, other);
            if (!pushed) break;
        }
        return box.Center;
    }

    private static ReadOnlySpan<int> NearBlockers(in Obb2 box, Battlefield field, Span<int> buffer) =>
        field.Near(PropSet.Blockers, box.Center, box.HalfExtents.Length() + ResolveSlack, buffer);

    private static bool PushOut(ref Obb2 box, in Obb2 obstacle)
    {
        float reach = box.HalfExtents.Length() + obstacle.HalfExtents.Length();
        if (Vector2.DistanceSquared(box.Center, obstacle.Center) > reach * reach) return false;
        if (!Obb2.TryGetPenetration(box, obstacle, out Vector2 push)) return false;
        box = box.WithCenter(box.Center + push + Vector2.Normalize(push) * Skin);
        return true;
    }
}

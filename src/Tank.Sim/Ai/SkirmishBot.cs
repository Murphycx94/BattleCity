using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Ai;

/// <summary>
/// The simple bot that fills a room out (stage 4 design §4): it drives from waypoint to randomly chosen waypoint, and
/// when its team can see an enemy within <see cref="EngageRange"/> it stops, lays the gun on the nearest one's hull
/// (no lead) and fires AP whenever loaded and laid, until the enemy is lost from view; out of AP it drives on and
/// ignores enemies, as it does one above or below the gun's travel (rather than stand there unable to lay). It sees only what its team sees (<see cref="VisionTracker.IsVisible"/>): no cheating. Stuck for
/// <see cref="StuckSeconds"/> on the way somewhere (neither moving nor turning), it picks another waypoint. A wreck holds still and carries on once revived. Its choices come from its own seeded random
/// numbers, so a match replays the same.
/// </summary>
public sealed class SkirmishBot
{
    public const float EngageRange = 250f;

    /// <summary>A waypoint counts as reached this close, m.</summary>
    public const float WaypointRadius = 4f;

    public const float StuckSeconds = 3f;

    /// <summary>Moving less than this in <see cref="StuckSeconds"/> while driving counts as stuck, m …</summary>
    public const float StuckDistance = 1f;

    /// <summary>… unless it turned at least this much meanwhile (turning in place towards a waypoint behind it), radians.</summary>
    public static readonly float StuckTurn = Angles.Deg(20);

    /// <summary>The gun fires only once it is laid this close to the aim, in turret yaw and gun pitch (radians).</summary>
    public static readonly float OnTarget = Steering.OnTarget;

    private static readonly int StuckTicks = (int)(StuckSeconds * SimConstants.TickRate);

    private readonly IReadOnlyList<Vector2> _waypoints;
    private XorShift _rng;
    private Vector2 _lastChecked;
    private float _lastCheckedYaw;
    private int _checkAt = -1;

    public SkirmishBot(int tankId, IReadOnlyList<Vector2> waypoints, uint seed)
    {
        if (waypoints.Count < 2) throw new ArgumentException("A bot needs at least two waypoints.", nameof(waypoints));
        TankId = tankId;
        _waypoints = waypoints;
        _rng = new XorShift(seed);
        Waypoint = (int)(_rng.NextUInt() % (uint)waypoints.Count);
    }

    public int TankId { get; }

    /// <summary>The waypoint being driven to.</summary>
    public int Waypoint { get; private set; }

    /// <summary>The enemy being engaged, or 0.</summary>
    public int TargetId { get; private set; }

    /// <summary>The bot's command for <paramref name="tick"/>.</summary>
    public InputCmd Command(Simulation sim, VisionTracker vision, int tick)
    {
        int slot = sim.SlotOf(TankId);
        if (slot < 0) return new InputCmd(tick, 0, 0, 0f, 0f, false);
        ref readonly TankState me = ref sim.Tanks[slot];
        var hold = new InputCmd(tick, 0, 0, Angles.Wrap(me.Yaw + me.TurretYaw), me.GunPitch, false);
        if (me.IsDestroyed)
        {
            TargetId = 0;
            _checkAt = -1;
            return hold;
        }
        int target = NearestVisibleEnemy(sim, vision, me);
        if (target >= 0 && TryEngage(me, sim.Tanks[target], sim, tick, out InputCmd engage))
        {
            TargetId = sim.Tanks[target].Id;
            _checkAt = -1;   // standing still on purpose isn't being stuck
            return engage;
        }
        TargetId = 0;
        return Drive(me, tick, hold);
    }

    private int NearestVisibleEnemy(Simulation sim, VisionTracker vision, in TankState me)
    {
        if (me.ApLeft == 0) return -1;   // nothing to shoot with: don't stand in the open "engaging"
        int best = -1;
        float bestDistance = EngageRange;
        ReadOnlySpan<TankState> tanks = sim.Tanks;
        for (int i = 0; i < tanks.Length; i++)
        {
            ref readonly TankState t = ref tanks[i];
            if (t.Team == me.Team || t.IsDestroyed) continue;
            float distance = Vector2.Distance(t.Position, me.Position);
            if (distance > bestDistance || !vision.IsVisible(t.Id, me.Team)) continue;
            (best, bestDistance) = (i, distance);
        }
        return best;
    }

    /// <summary>Stops, lays the gun on the enemy's hull centre and fires once laid; false when the gun can't be laid that high or low.</summary>
    private static bool TryEngage(in TankState me, in TankState enemy, Simulation sim, int tick, out InputCmd cmd)
    {
        TankPose mine = TankPose.Compute(me, sim.Field.Terrain), theirs = TankPose.Compute(enemy, sim.Field.Terrain);
        Vector3 aim = theirs.HullBox(enemy.Spec).Center;
        var (yaw, pitch) = Angles.AimAngles(aim - mine.Trunnion(me.Spec), me.Yaw);
        var (turret, gun) = mine.ToHullAngles(Angles.Direction(yaw, pitch), me.TurretYaw);
        cmd = new InputCmd(tick, 0, 0, yaw, pitch, Steering.IsLaid(me, turret, gun), AmmoType.AP);
        return gun <= me.Spec.GunElevation && gun >= -me.Spec.GunDepression;
    }

    /// <summary>Drives to the waypoint, choosing the next when it gets there or gets stuck.</summary>
    private InputCmd Drive(in TankState me, int tick, in InputCmd hold)
    {
        if (Vector2.Distance(me.Position, _waypoints[Waypoint]) <= WaypointRadius) NextWaypoint(me);
        if (_checkAt < 0 || tick >= _checkAt)
        {
            bool stuck = Vector2.Distance(me.Position, _lastChecked) < StuckDistance && MathF.Abs(Angles.Wrap(me.Yaw - _lastCheckedYaw)) < StuckTurn;
            if (_checkAt >= 0 && stuck) NextWaypoint(me);
            _lastChecked = me.Position;
            _lastCheckedYaw = me.Yaw;
            _checkAt = tick + StuckTicks;
        }
        var (throttle, steer) = Steering.Toward(me, _waypoints[Waypoint], stopThere: false);
        return hold with { Throttle = throttle, Steer = steer, AimYaw = me.Yaw, AimPitch = 0f };
    }

    private void NextWaypoint(in TankState me)
    {
        int next = (int)(_rng.NextUInt() % (uint)(_waypoints.Count - 1));
        if (next >= Waypoint) next++;   // never the same one again
        Waypoint = next;
        _lastChecked = me.Position;
        _lastCheckedYaw = me.Yaw;
        _checkAt = -1;
    }
}

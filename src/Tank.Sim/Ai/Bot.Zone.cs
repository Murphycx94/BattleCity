using System.Numerics;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>
/// Running the zone in a royale (stage 6 design §3.4; stage 6A plan, task 6). The ring a bot must be in is the director's
/// <see cref="ZoneState.Target"/>; "in" is within max(radius, <see cref="BotDirector.ZoneArriveRadius"/>) of its centre.
/// Out of it, the bot runs (<see cref="RunningToZone"/>) for its point in the ring (<see cref="BotDirector.ZonePoint"/>) and
/// stops running once well inside (<see cref="BotDirector.InsideLimit"/>: <see cref="BotDirector.ZoneInsideShare"/> of the
/// radius, <see cref="BotDirector.ZoneArriveRadius"/> at least) or at the point: in a ring smaller than
/// <see cref="BotDirector.ZoneWanderRadius"/> it then holds where it is, otherwise it wanders on between the waypoints well
/// inside the ring (a random point in it when none is; a path there that leaves the ring is dropped for another point:
/// <see cref="WanderLeavesRing"/>). A point it can't reach is given up for the next (with no way to
/// it, after holding <see cref="BotDirector.ZoneRetryTicks"/>), and after <see cref="BotDirector.ZoneTries"/> it starts
/// over, holding first. A new
/// ring sends a running bot to its point in the new one, and a wandering bot whose waypoint lies outside it to another.
/// Running and out of time (reckoned to where the run stops), or outside the safe circle, it is <see cref="ZoneUrgent"/>,
/// and a run stays so to its end: it keeps driving with the gun on any target (firing once laid and loaded) and doesn't
/// back away from enemies too close; otherwise it stops to fight as ever and runs on afterwards.
/// </summary>
public sealed partial class Bot
{
    /// <summary>A wandering bot whose paths left the ring this many times running backs off (<see cref="WanderElsewhere"/>).</summary>
    private const int WanderOutsToBackOff = 3;

    private int _zoneRing;       // the ring (ZoneState.Ring) seen last; 0 before the first look
    private int _zoneAttempt;    // running: the point being tried for this ring (ZonePoint's attempt)
    private int _wanderOut;      // wandering: paths in a row dropped for leaving the ring

    /// <summary>Heading into the ring it isn't in.</summary>
    public bool RunningToZone { get; private set; }

    /// <summary>Running and out of time (or outside the safe circle): fires on the move, never stops to fight. Once set, it stays set for the rest of the run.</summary>
    public bool ZoneUrgent { get; private set; }

    /// <summary>Runs started this match.</summary>
    public int ZoneRuns { get; private set; }

    /// <summary>
    /// Once a command, before the arrival check (a royale with its rings only): a new ring changes where it heads; out of
    /// the ring a run starts, well inside one it ends; then whether it must hurry (a run that had to stays urgent).
    /// </summary>
    private void WatchZone(BotDirector director, in TankState me)
    {
        ZoneState zone = director.Zone;
        ZoneRing target = zone.Target;
        if (zone.Ring != _zoneRing)
        {
            (_zoneRing, _zoneAttempt) = (zone.Ring, 0);
            _wanderOut = 0;
            if (RunningToZone) Begin(Leg.Zone, director.ZonePoint(this, target, 0), keepPath: false);
            else if ((_leg is Leg.Waypoint or Leg.Zone) && !BotDirector.WellInside(Goal, target)) NextWaypoint(me);
            else if (_leg == Leg.Arrived && target.Radius >= BotDirector.ZoneWanderRadius) NextWaypoint(me);
        }
        if (!RunningToZone)
        {
            if (!BotDirector.InRing(me.Position, target))
            {
                (RunningToZone, ZoneRuns) = (true, ZoneRuns + 1);
                Begin(Leg.Zone, director.ZonePoint(this, target, _zoneAttempt), keepPath: false);
            }
        }
        else if (BotDirector.WellInside(me.Position, target)) StopRunning(me, target);
        ZoneUrgent = zone.IsOutside(me.Position) ||
                     (RunningToZone && (ZoneUrgent || BotDirector.MustRun(DistanceLeft(me, target), me.Spec.MaxForwardSpeed, zone.TicksToTarget, outsideCurrent: false)));
    }

    /// <summary>In: a ring smaller than <see cref="BotDirector.ZoneWanderRadius"/> is held where it is, a larger one wandered.</summary>
    private void StopRunning(in TankState me, in ZoneRing target)
    {
        RunningToZone = false;
        if (target.Radius < BotDirector.ZoneWanderRadius) Begin(Leg.Arrived, me.Position, keepPath: false);
        else NextWaypoint(me);
    }

    /// <summary>
    /// At the end of a <see cref="Leg.Zone"/>: running, it is in (or another point is tried when the point left it short);
    /// wandering, on to the next waypoint.
    /// </summary>
    private void ZoneReached(in TankState me, int tick)
    {
        if (!RunningToZone)
        {
            NextWaypoint(me);
            return;
        }
        ZoneRing target = _director.Zone.Target;
        if (BotDirector.InRing(me.Position, target)) StopRunning(me, target);
        else NextZonePoint(tick, noWay: false);
    }

    /// <summary>
    /// The run's point not reached (reached short of the ring, given up after getting stuck on the way, or no way to it):
    /// the next attempt's point, after <see cref="BotDirector.ZoneTries"/> of them the first again (batch C review
    /// follow-up: it used to hold where it was until the next ring, and after the last ring none comes). With no way to
    /// the point (<paramref name="noWay"/>: the ring is full, the tanks standing in it closing it to the searches, or the
    /// bot is boxed in), and before starting over, it holds <see cref="BotDirector.ZoneRetryTicks"/> where it is first:
    /// it waits for room rather than for one search after another.
    /// </summary>
    private void NextZonePoint(int tick, bool noWay)
    {
        _zoneAttempt = _zoneAttempt + 1 < BotDirector.ZoneTries ? _zoneAttempt + 1 : 0;
        Vector2 next = _director.ZonePoint(this, _director.Zone.Target, _zoneAttempt);
        if (!noWay && _zoneAttempt > 0)
        {
            Begin(Leg.Zone, next, keepPath: false);
            return;
        }
        Begin(Leg.Paused, next, keepPath: false);
        _retryAt = tick + BotDirector.ZoneRetryTicks;
    }

    /// <summary>
    /// A royale's wandering: a waypoint (not the current one) well inside the ring, drawn up to
    /// <see cref="BotDirector.ZoneTries"/> times with its own random numbers; none drawn, a random point in the ring
    /// (<see cref="BotDirector.ZoneWanderPoint"/>). A ring smaller than <see cref="BotDirector.ZoneWanderRadius"/> is held where it is.
    /// </summary>
    private void NextWaypointInZone(in TankState me)
    {
        ZoneRing target = _director.Zone.Target;
        if (target.Radius < BotDirector.ZoneWanderRadius)
        {
            Begin(Leg.Arrived, me.Position, keepPath: false);
            return;
        }
        int count = _waypoints.Count;
        for (int k = 0; k < BotDirector.ZoneTries && count > 0; k++)
        {
            int next = (int)(_rng.NextUInt() % (uint)count);
            if (next == _waypoint || !BotDirector.WellInside(_waypoints[next], target)) continue;
            _waypoint = next;
            Begin(Leg.Waypoint, _waypoints[next], keepPath: false);
            return;
        }
        Begin(Leg.Zone, _director.ZoneWanderPoint(this, target), keepPath: false);
    }

    /// <summary>
    /// Wandering in a royale, whether the path just come leaves the ring (<see cref="BotDirector.InRing"/>) it wanders in:
    /// a waypoint well inside it may be reached only the long way round, outside (round a cliff), and the ring may be
    /// shrinking (batch C review follow-up). The ring being convex, the points are all there is to look at.
    /// </summary>
    private bool WanderLeavesRing()
    {
        if (!_director.ZoneOn || RunningToZone || _leg is not (Leg.Waypoint or Leg.Zone)) return false;
        ZoneRing target = _director.Zone.Target;
        for (int k = 0; k < _pathCount; k++)
            if (!BotDirector.InRing(_path[k], target)) return true;
        return false;
    }

    /// <summary>
    /// A wander path that leaves the ring is dropped for another waypoint, asked for after
    /// <see cref="BotDirector.ZoneRetryTicks"/>; after <see cref="BotDirector.ZoneTries"/> in a row it holds where it is
    /// (in the ring) until the next ring. Every <see cref="WanderOutsToBackOff"/>th in a row it backs off meanwhile, as
    /// from a stuck (<see cref="ReverseSteer"/>): boxed in by the tanks round it, every way inside the ring passes
    /// them, and waiting where it stood for the next path, a bot in a small ring got nowhere for over 10 s.
    /// </summary>
    private void WanderElsewhere(in TankState me, int tick)
    {
        if (++_wanderOut >= BotDirector.ZoneTries)
        {
            _wanderOut = 0;
            Begin(Leg.Arrived, me.Position, keepPath: false);
            return;
        }
        NextWaypoint(me);
        _askAfter = tick + BotDirector.ZoneRetryTicks;
        if (_wanderOut % WanderOutsToBackOff != 0) return;
        _reverseSteer = ReverseSteer();
        (_reverseUntil, _checkAt) = (tick + BotDirector.ReverseTicks, -1);
    }

    /// <summary>
    /// How far it still has to go before the run stops: to the circle <see cref="BotDirector.InsideLimit"/> from
    /// <paramref name="target"/>'s centre, not to the point (<see cref="Goal"/>) further in (batch C review follow-up).
    /// Along the path when it holds the whole of one to its point (from the tank to the next point, then point to point,
    /// up to where the path first comes within that circle), otherwise straight to the circle × <see cref="BotDirector.ZoneDetour"/>.
    /// </summary>
    private float DistanceLeft(in TankState me, in ZoneRing target)
    {
        float limit = BotDirector.InsideLimit(target);
        if (!WholePath || _pathIndex >= _pathCount)
            return MathF.Max(0f, Vector2.Distance(me.Position, target.Centre) - limit) * BotDirector.ZoneDetour;
        Vector2 from = me.Position;
        float d = 0f;
        for (int k = _pathIndex; k < _pathCount; k++)
        {
            float leg = BotDirector.ToCircle(from, _path[k], target.Centre, limit), length = Vector2.Distance(from, _path[k]);
            if (leg < length) return d + leg;
            d += length;
            from = _path[k];
        }
        return d;
    }
}

using System.Numerics;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>
/// Keeping with the team-mate in duos (stage 6 design §5; stage 6C plan, task 5). A bot whose live team-mate is a person, or
/// a bot with a lower tank id, follows it (<see cref="BotDirector.Follows"/>): it doesn't wander — its next waypoint is to
/// hold where it is (<see cref="NextWaypoint"/>), the first one handed out too — and the zone's runs and its crate runs end
/// that way; it fetches only crates within <see cref="BotDirector.MateRange"/> of its team-mate. Once farther than
/// <see cref="BotDirector.MateRange"/> from a live team-mate in the ring it heads for, a bot heads for the team-mate
/// (<see cref="Leg.Mate"/>, <see cref="Regrouping"/>): not while running the zone, on a crate run or holding before trying
/// again (<see cref="Leg.Paused"/>), nor when the team-mate is a bot coming for it already; and a leader (whose team-mate is
/// the follower bot) leaves it to the follower to come when that one can (<see cref="CanRegroup"/>). A leader wandering
/// while its follower heads for it, farther off than <see cref="BotDirector.MateRange"/>, holds where it is until the
/// follower stops (batch C review follow-up: a slower follower chased a wandering leader for minutes); a run of the zone
/// ends the wait. On the way the goal is moved to where the team-mate is every <see cref="BotDirector.ReplanTicks"/> when
/// it has moved off the goal (and on reaching the old one), the old path followed until the new comes, as a retreat does;
/// it stops to fight as ever. Within <see cref="BotDirector.MateArrive"/> it is there (the regroup ends a braking distance
/// before, so that it stops about there): a follower holds, a leader wanders on.
/// The team-mate destroyed or out of the ring, a run of the zone begun, or a path there that leaves the ring, ends it: a
/// follower of a live team-mate holds, any other bot wanders as in a royale. A follower holding for its team-mate plays on
/// as in a royale once the team-mate is destroyed or gone. A regroup given up — stuck three times on the way, or three
/// searches finding no way (asked at least <see cref="BotDirector.ReplanTicks"/> apart: 10 s at least), or a way that leaves
/// the ring — is not begun again for <see cref="BotDirector.RegroupQuietTicks"/>. No random numbers: the old seeds draw as ever.
/// </summary>
public sealed partial class Bot
{
    private int _mateAimAt;      // Leg.Mate: the tick its goal is moved to where the team-mate is
    private int _mateQuietUntil; // a regroup given up: no other begun before this tick
    private bool _heldForMate;   // Leg.Arrived: a follower holding for its team-mate (cleared by Begin)
    private bool _waitingForMate; // Leg.Arrived: a leader holding while its follower heads for it (cleared by Begin)

    /// <summary>Duos: heading for its team-mate (<c>Leg.Mate</c>).</summary>
    public bool Regrouping => _leg == Leg.Mate;

    /// <summary>Times it began heading for its team-mate this match.</summary>
    public int Regroups { get; private set; }

    /// <summary>Free to head for its team-mate itself: not running the zone, on a crate run or holding before trying again.</summary>
    internal bool FreeToRegroup => !RunningToZone && _leg is not (Leg.Crate or Leg.Paused);

    /// <summary>Able to head for its team-mate at <paramref name="tick"/>: <see cref="FreeToRegroup"/>, and no regroup given up within <see cref="BotDirector.RegroupQuietTicks"/>.</summary>
    internal bool CanRegroup(int tick) => FreeToRegroup && tick >= _mateQuietUntil;

    /// <summary>Wandering: to a waypoint, or to a wander point in the ring.</summary>
    private bool Wandering => _leg == Leg.Waypoint || (_leg == Leg.Zone && !RunningToZone);

    /// <summary>
    /// Once a command in duos, after the zone's watch and before the arrival check: ends a regroup that is over, or moves its
    /// goal on; ends a hold for a team-mate that no longer needs it; begins a regroup when the team-mate is too far off;
    /// and stops a follower wandering, or a leader while its follower comes.
    /// </summary>
    private void WatchMate(BotDirector director, in TankState me, int tick)
    {
        int slot = director.MateSlotOf(me);
        bool alive = slot >= 0 && !director.Sim.Tanks[slot].IsDestroyed;
        Vector2 at = alive ? director.Sim.Tanks[slot].Position : default;
        bool reachable = alive && !RunningToZone && (!director.ZoneOn || BotDirector.InRing(at, director.Zone.Target));
        if (_leg == Leg.Mate)
        {
            if (!reachable || Vector2.Distance(me.Position, at) <= BotDirector.MateArrive + StoppingDistance(me)) NextWaypoint(me);
            else if (tick >= _mateAimAt) AimAt(at, tick);
            return;
        }
        bool follows = director.Follows(this, me);
        Bot? mate = alive ? director.Find(director.Sim.Tanks[slot].Id) : null;
        bool mateComing = mate is { Regrouping: true };
        if (_leg == Leg.Arrived && !RunningToZone &&
            ((_heldForMate && !follows) || (_waitingForMate && !mateComing)))
            NextWaypoint(me);   // the team-mate fell or left, or the follower stopped coming: on as in a royale
        if (reachable && CanRegroup(tick) && Vector2.Distance(me.Position, at) > BotDirector.MateRange &&
            !MateComes(mate, follows, tick))
        {
            Begin(Leg.Mate, at, keepPath: false);
            Regroups++;
            _mateAimAt = tick + BotDirector.ReplanTicks;
            return;
        }
        if (!Wandering) return;
        if (follows) HoldForMate(me);
        else if (mateComing && Vector2.Distance(me.Position, at) > BotDirector.MateRange)
        {
            Begin(Leg.Arrived, me.Position, keepPath: false);
            _waitingForMate = true;
        }
    }

    /// <summary>
    /// How far the tank rolls braking from its speed now, m: a regroup ends that much before <see cref="BotDirector.MateArrive"/>,
    /// so that the tank stops about there rather than coasting up to its team-mate (batch C review follow-up: a light at
    /// full speed rolled to 7 m of its waiting leader and, by the cliffs, shut the leader's only way out).
    /// </summary>
    private static float StoppingDistance(in TankState me) => me.Speed * me.Speed / (2f * me.Spec.Braking);

    /// <summary>A follower holds where it is for its team-mate (released once the team-mate falls or leaves: <see cref="WatchMate"/>).</summary>
    private void HoldForMate(in TankState me)
    {
        Begin(Leg.Arrived, me.Position, keepPath: false);
        _heldForMate = true;
    }

    /// <summary>
    /// Whether the team-mate <paramref name="mate"/> (null: a person, or none) is a bot that comes for this one itself:
    /// heading for it now, or — to a leader (not <paramref name="follower"/>) — able to at <paramref name="tick"/> (the
    /// follower comes, the leader doesn't turn back for it; one waiting out a regroup it gave up can't, so the leader does).
    /// </summary>
    private static bool MateComes(Bot? mate, bool follower, int tick) =>
        mate is not null && (mate.Regrouping || (!follower && mate.CanRegroup(tick)));

    /// <summary>
    /// Heading for the team-mate: the goal moved to where it is now when it has moved more than
    /// <see cref="BotDirector.WaypointRadius"/> off it (the old path followed until the new one comes; a team-mate standing
    /// still costs no search), looked at again <see cref="BotDirector.ReplanTicks"/> on.
    /// </summary>
    private void AimAt(Vector2 at, int tick)
    {
        if (Vector2.Distance(at, Goal) > BotDirector.WaypointRadius) (Goal, _needPath) = (at, true);
        _mateAimAt = tick + BotDirector.ReplanTicks;
    }

    /// <summary>Heading for the team-mate, the goal reached short of it (it moved on): the goal moved to where it is now (the regroup ends next command if it is gone).</summary>
    private void MateGoalReached(in TankState me, int tick)
    {
        int slot = _director.MateSlotOf(me);
        if (slot >= 0) AimAt(_director.Sim.Tanks[slot].Position, tick);
    }

    /// <summary>A regroup given up (stuck, no way, or a way that leaves the ring): none begun again for <see cref="BotDirector.RegroupQuietTicks"/>; a follower holds, a leader wanders.</summary>
    private void RegroupMissed(in TankState me, int tick)
    {
        _mateQuietUntil = tick + BotDirector.RegroupQuietTicks;
        NextWaypoint(me);
    }
}

using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Modes;
using Tank.Sim.Navigation;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>What a bot is for (stage 5 design §4.2). Values are not on the wire.</summary>
public enum BotRole : byte { Wander, Attack, Defend }

/// <summary>What a bot is doing this tick. Values are not on the wire.</summary>
public enum BotActivity : byte
{
    /// <summary>Standing where it is meant to be (or has nowhere to go, or its tank is gone or a wreck).</summary>
    Holding,
    /// <summary>Stopped, its path asked for and not come yet.</summary>
    Waiting,
    /// <summary>Following its path.</summary>
    Driving,
    /// <summary>Backing off after getting stuck, or away from an enemy too close to lay the gun on.</summary>
    Reversing,
    /// <summary>Stopped and firing at its target.</summary>
    Engaging,
}

/// <summary>
/// One bot (stage 5 design §4): what it is for, where it is heading and what it is doing, read-only outside the
/// <see cref="BotDirector"/>, which commands it once a tick.
/// </summary>
/// <remarks>
/// <para>Its way is a series of legs, each ending at <see cref="Goal"/>: a waypoint picked at random with its own
/// <see cref="XorShift"/> (wandering; another one once there, never the same twice running), or an attack lane's via
/// points one after another (passed within <see cref="BotDirector.ViaRadius"/>) and then a point to stop on and hold.</para>
/// <para>Each leg is driven along a path from the director's <see cref="PathService"/>, asked for from where the tank
/// is: when the leg begins, every <see cref="BotDirector.ReplanTicks"/> while driving (the old path is followed until the
/// new one comes), when a path cut short at <see cref="PathFinder.MaxPoints"/> runs out, and after backing off. The
/// searches steer round wrecks and tanks that have stood still (<see cref="PathService.Observe"/>). A path's points are
/// passed within <see cref="PassRadius"/>, or once the tank is past one both along the way in and along the way on.</para>
/// <para>Driving, it watches for getting stuck as the skirmish bot did: every <see cref="BotDirector.StuckTicks"/> of
/// driving (the watch waits while it stops to fire) it checks it has moved <see cref="BotDirector.StuckDistance"/> or
/// turned <see cref="BotDirector.StuckTurn"/>; if not, it backs off for <see cref="BotDirector.ReverseTicks"/>, the hull
/// turning one way and the next time the other, and asks again. A search that finds no path counts as stuck too, but
/// it doesn't back off: it asks again after <see cref="BotDirector.ReplanTicks"/>. The
/// <see cref="BotDirector.StuckRetries"/>th time on one leg it gives the leg up: another waypoint or the next via point.
/// A final leg is never given up short of its target (5C batch B review follow-up): an attacker (or a bot called back)
/// outside the circle its point lies in draws another point there and, after <see cref="BotDirector.RetryTicks"/>
/// holding, tries again (inside it, it holds where it is); one that stopped at the end of its path outside the circle
/// draws another at once; a defender tries the next cover point, and once every one has failed in a row holds its base
/// centre; a retreating bot holds and looks for its goal again with the next retarget.</para>
/// <para>In a standard battle (stage 5C task 7) a defender drives to its cover point and holds there facing the way the
/// enemy comes; called back (<see cref="FallingBack"/>) a bot drives to a point inside its own base and holds; badly hurt
/// (<see cref="Retreating"/>, below <see cref="BotDirector.RetreatHealth"/>) it drives to the nearest team mate or its own
/// base, whichever is nearer, looking again every <see cref="BotDirector.ReplanTicks"/>, and holds once within
/// <see cref="BotDirector.RetreatArrive"/> of the team mate or inside the base. Being called back comes before retreating,
/// retreating before its role; released, it takes its role up again from where it is. When its team has no attacker
/// left the director sends it forward (<see cref="SentForward"/>): it attacks from where it is, its retreat over, and
/// never retreats again (5C stalemate fix).</para>
/// <para>Whenever its <see cref="TargetPicker"/> has a target it stops and lays the gun on the aim point
/// (<see cref="Gunnery.Aim"/>: the hull centre, or the turret when the gun can't reach that), firing once laid, loaded
/// and slower than <see cref="BotDirector.FireSpeed"/>; one coming back or retreating keeps to its path instead and fires
/// once laid and loaded, however fast it goes (design §4.3). It fires AP, and HE once its AP is gone
/// (<see cref="Gunnery.AmmoFor"/>; every command asks for that round, so the gun isn't unloaded in between). Without a
/// target, but with an enemy too close to lay the gun on (<see cref="TargetPicker.TooClose"/>), it backs away from it,
/// the gun on it, until the picker finds it can (5C stalemate fix 2): straight away, or, inside the circle its final leg
/// must end in, sliding round the circle's edge rather than leaving it; each time that gets nowhere it turns 60° one
/// way, then the other. Holding before, it then drives back to where it held. One coming back or retreating keeps to
/// its path. A wreck holds still and forgets its target; revived, it starts its leg afresh from where it is, its retreat
/// over.</para>
/// <para>Allocation-free after construction; deterministic (its own random numbers and the simulation's state).</para>
/// </remarks>
public sealed partial class Bot
{
    /// <summary>A point of the path before its last counts as reached this close, m.</summary>
    public const float PassRadius = 4f;

    /// <summary>A defender holding on its cover point turns its hull until it faces the enemy's way within this, radians.</summary>
    private static readonly float FaceTolerance = Angles.Deg(4);

    private enum Leg : byte
    {
        /// <summary>Nowhere to go: no waypoints, or no role yet.</summary>
        None,
        /// <summary>Wandering to waypoint <see cref="_waypoint"/>.</summary>
        Waypoint,
        /// <summary>Driving past via point <see cref="_viaIndex"/>.</summary>
        Via,
        /// <summary>Driving to the end point, to stop there.</summary>
        End,
        /// <summary>At the end point (or as near as it got, where that will do), holding.</summary>
        Arrived,
        /// <summary>Gave its end up for now: holding until <see cref="_retryAt"/>, then driving for <see cref="Goal"/> (another point) again.</summary>
        Paused,
    }

    private readonly BotDirector _director;

    private readonly Vector2[] _path = new Vector2[BotDirector.MaxPathPoints];
    private int _pathCount, _pathIndex;
    private bool _pathCut;             // the path ends short of its goal (cut at MaxPoints)
    private Vector2 _pathGoal;         // the goal the path was asked for
    private Vector2 _askedGoal;
    private Vector2 _askedFrom, _pathFrom;   // where the tank was when it asked: for the request out, and for the path held
    private bool _asked;               // a request is out whose answer this bot hasn't seen
    private bool _needPath;            // ask as soon as it isn't backing off
    private int _replanAt;
    private int _askAfter;             // a search found no path: not asked again before this tick

    private XorShift _rng;
    private IReadOnlyList<Vector2> _waypoints = Array.Empty<Vector2>();
    private int _waypoint = -1;
    private IReadOnlyList<Vector2> _via = Array.Empty<Vector2>();
    private int _viaIndex;
    private Vector2 _end;              // Attack: the capture point; Defend: the cover point (or the base centre)
    private TeamBase _endBase;         // Attack: the base its capture point lies in (Radius 0: none known)
    private TeamBase _ownBase;         // called back: the base its point lies in
    private IReadOnlyList<Vector2> _covers = Array.Empty<Vector2>();   // Defend: the team's cover points
    private Vector2 _facing;           // Defend: the point it faces from its cover
    private Vector2 _home;             // Defend: its base centre, held once every cover point has failed in a row
    private int _coverFails;           // Defend: cover points given up in a row
    private int _retryAt;              // Paused: the tick it drives for its goal again
    private int _retreatAlly;          // the team mate a retreat heads for; 0: the own base, or nowhere
    private int _retargetAt;           // a retreat looks for its goal again at this tick
    private bool _retreatBegun;        // the retreat's goal is set (false: look for it at once)
    private Leg _leg;
    private int _legStucks;

    private int _checkAt = -1;         // the tick of the next stuck check, −1: none under way
    private Vector2 _checkedAt;
    private float _checkedYaw;
    private int _reverseUntil = -1;    // backing off up to (not including) this tick
    private sbyte _reverseSteer;       // the way the hull turns backing off: each stuck the other way
    private bool _backedOff;           // it backed away from an enemy too close since it last moved on its own
    private bool _backOffForward;      // backing off (or away) forward rather than in reverse
    private int _backOffStucks;        // times backing away from an enemy got nowhere: picks the way's turn
    private bool _wreck;

    internal Bot(BotDirector director, int tankId, uint seed)
    {
        _director = director;
        TankId = tankId;
        _rng = new XorShift(seed);
        Picker = new TargetPicker(tankId);
    }

    public int TankId { get; }

    public BotRole Role { get; private set; }

    /// <summary>Attack: the lane's index; otherwise −1.</summary>
    public int Lane { get; private set; } = -1;

    /// <summary>Defend: the index of its cover point (−1 without cover points, or once every one failed: it holds its base centre); otherwise −1 (stage 5C task 7).</summary>
    public int Cover { get; private set; } = -1;

    /// <summary>Called back to defend its own base (stage 5C task 7).</summary>
    public bool FallingBack { get; private set; }

    /// <summary>Pulling back to a team mate or its base, badly hurt (stage 5C task 7); stays so until revived.</summary>
    public bool Retreating { get; private set; }

    /// <summary>Sent forward because its team had no attacker left (5C stalemate fix): it attacks from then on and never retreats again.</summary>
    public bool SentForward { get; private set; }

    public BotActivity Activity { get; private set; } = BotActivity.Waiting;

    /// <summary>Where the current leg ends: where it is heading now.</summary>
    public Vector2 Goal { get; private set; }

    /// <summary>The enemy it is engaging, or 0.</summary>
    public int TargetId => Picker.TargetId;

    /// <summary>Paths asked for this match.</summary>
    public int PathRequests { get; private set; }

    /// <summary>Times it found itself stuck this match (a search that found no path counts too).</summary>
    public int Stucks { get; private set; }

    internal TargetPicker Picker { get; }

    /// <summary>Called back: the point inside its own base it was sent to.</summary>
    internal Vector2 FallBackPoint { get; private set; }

    /// <summary>Attack: its capture point; Defend: its cover point (or its base centre).</summary>
    internal Vector2 End => _end;

    /// <summary>Its own random numbers, in [0, 1): for the director's draws on its behalf (cover and capture points).</summary>
    internal float NextFloat() => _rng.NextFloat();

    /// <summary>Its own random numbers.</summary>
    internal uint NextUInt() => _rng.NextUInt();

    /// <summary>Wanders between <paramref name="waypoints"/>, starting with one picked at random (holds when there are none).</summary>
    internal void Wander(IReadOnlyList<Vector2> waypoints)
    {
        (Role, Lane, Cover, _waypoints) = (BotRole.Wander, -1, -1, waypoints);
        if (waypoints.Count == 0)
        {
            Begin(Leg.None, default, keepPath: false);
            return;
        }
        _waypoint = (int)(_rng.NextUInt() % (uint)waypoints.Count);
        Begin(Leg.Waypoint, waypoints[_waypoint], keepPath: false);
    }

    /// <summary>
    /// Drives lane <paramref name="lane"/>'s <paramref name="via"/> points in order, then to <paramref name="end"/> in
    /// base <paramref name="target"/> (default: none known), and holds there.
    /// </summary>
    internal void Attack(int lane, IReadOnlyList<Vector2> via, Vector2 end, TeamBase target)
    {
        (Role, Lane, Cover, _via, _viaIndex, _end, _endBase) = (BotRole.Attack, lane, -1, via, 0, end, target);
        if (via.Count > 0) Begin(Leg.Via, via[0], keepPath: false);
        else Begin(Leg.End, end, keepPath: false);
    }

    /// <summary>
    /// Holds cover point <paramref name="cover"/> of <paramref name="covers"/> (−1: none, then <paramref name="point"/>
    /// alone), facing <paramref name="facing"/>; three times stuck on the way, it tries the next cover point, and once
    /// every one has failed in a row it holds <paramref name="home"/>, its base centre.
    /// </summary>
    internal void Defend(IReadOnlyList<Vector2> covers, int cover, Vector2 point, Vector2 facing, Vector2 home)
    {
        (Role, Lane, Cover, _covers, _end, _facing, _home, _coverFails) = (BotRole.Defend, -1, cover, covers, point, facing, home, 0);
        Begin(Leg.End, point, keepPath: false);
    }

    /// <summary>
    /// Sent forward, its team having no attacker left (5C stalemate fix): attacks down lane <paramref name="lane"/> to
    /// <paramref name="end"/> in <paramref name="target"/> from <paramref name="at"/>, skipping the via points further from
    /// its capture point than it is; a retreat is over, and never starts again. Called back meanwhile, it carries on
    /// coming back and takes the lane up once released.
    /// </summary>
    internal void SendForward(int lane, IReadOnlyList<Vector2> via, Vector2 end, TeamBase target, Vector2 at)
    {
        (Role, Lane, Cover, _via, _viaIndex, _end, _endBase) = (BotRole.Attack, lane, -1, via, 0, end, target);
        (SentForward, Retreating) = (true, false);
        if (!FallingBack) ResumeRole(at);
    }

    /// <summary>Called back to <paramref name="point"/> inside its own base <paramref name="own"/>, to hold there: before retreating and its role.</summary>
    internal void FallBack(Vector2 point, TeamBase own)
    {
        (FallingBack, FallBackPoint, _ownBase) = (true, point, own);
        Begin(Leg.End, point, keepPath: false);
    }

    /// <summary>The base is quiet: back to retreating (looked at afresh), or to its role from <paramref name="at"/>.</summary>
    internal void Release(Vector2 at)
    {
        FallingBack = false;
        if (Retreating) _retreatBegun = false;
        else ResumeRole(at);
    }

    /// <summary>
    /// Takes its role up again from <paramref name="at"/>: an attacker its lane, skipping the via points further from
    /// its capture point than it is; a defender its cover point; a wanderer the waypoint it was heading for.
    /// </summary>
    private void ResumeRole(Vector2 at)
    {
        switch (Role)
        {
            case BotRole.Attack:
                float mine = Vector2.Distance(at, _end);
                int next = 0;
                while (next < _via.Count && Vector2.Distance(_via[next], _end) >= mine) next++;
                _viaIndex = next;
                if (next < _via.Count) Begin(Leg.Via, _via[next], keepPath: false);
                else Begin(Leg.End, _end, keepPath: false);
                break;
            case BotRole.Defend:
                Begin(Leg.End, _end, keepPath: false);
                break;
            default:
                if (_waypoint < 0) Begin(Leg.None, default, keepPath: false);
                else Begin(Leg.Waypoint, _waypoints[_waypoint], keepPath: false);
                break;
        }
    }

    /// <summary>
    /// Its tank is back at <paramref name="at"/> (a TankRevived event, or found alive again after ticks went unheard):
    /// the leg starts afresh, a retreat is over.
    /// </summary>
    internal void Revived(Vector2 at)
    {
        _wreck = false;
        Picker.Clear();
        (_pathCount, _asked, _checkAt, _reverseUntil, _legStucks, _askAfter) = (0, false, -1, -1, 0, 0);
        (_backedOff, _backOffStucks) = (false, 0);
        if (Retreating && !FallingBack)
        {
            Retreating = false;
            ResumeRole(at);
            return;
        }
        Retreating = false;
        if (_leg == Leg.Arrived) _leg = Leg.End;
        _needPath = _leg is Leg.Waypoint or Leg.Via or Leg.End;
    }

    /// <summary>After ticks went unheard: a wreck whose tank is alive again was revived meanwhile.</summary>
    internal void CatchUp(Simulation sim)
    {
        int slot = sim.SlotOf(TankId);
        if (_wreck && slot >= 0 && !sim.Tanks[slot].IsDestroyed) Revived(sim.Tanks[slot].Position);
    }

    /// <summary>The bot's command for <paramref name="tick"/> (the one being commanded).</summary>
    internal InputCmd Command(int tick)
    {
        BotDirector director = _director;
        Simulation sim = director.Sim;
        int slot = sim.SlotOf(TankId);
        if (slot < 0)
        {
            Picker.Clear();
            Stand(BotActivity.Holding);
            return new InputCmd(tick, 0, 0, 0f, 0f, false);
        }
        ref readonly TankState me = ref sim.Tanks[slot];
        var hold = new InputCmd(tick, 0, 0, Angles.Wrap(me.Yaw + me.TurretYaw), me.GunPitch, false);
        if (me.IsDestroyed)
        {
            if (!_wreck) Picker.Clear();
            _wreck = true;
            _reverseUntil = -1;
            Stand(BotActivity.Holding);
            return hold;
        }

        PathService paths = director.Paths;
        TakeAnswer(paths, me, tick);
        WatchHealth(director, me, tick);
        if (_leg == Leg.Paused && tick >= _retryAt) Begin(Leg.End, Goal, keepPath: false);
        CheckArrival(me);
        int target = Picker.Pick(sim, director.Vision, tick);
        HeightField terrain = sim.Field.Terrain;
        bool onTheWay = (FallingBack || Retreating) && _leg is Leg.Waypoint or Leg.Via or Leg.End;
        InputCmd cmd;
        if (target != 0)
        {
            ref readonly TankState enemy = ref sim.Tanks[sim.SlotOf(target)];
            cmd = onTheWay ? FireOnTheMove(me, enemy, terrain, Move(me, tick), !Picker.TargetLost)
                : EngageApart(me, enemy, terrain, tick, !Picker.TargetLost);
        }
        else if (!onTheWay && TooClose(sim, out int close)) cmd = BackOff(me, sim.Tanks[close], terrain, tick);
        else cmd = Move(me, tick);
        Ask(paths, me, tick);
        return cmd with { Ammo = Gunnery.AmmoFor(me) };
    }

    /// <summary>
    /// Retreating (unless called back): starts below <see cref="BotDirector.RetreatHealth"/> when there is somewhere to
    /// go and it isn't in either base, never once <see cref="SentForward"/> (it would turn back at once: most bots sent
    /// forward that way were below it already); looks for its goal again every <see cref="BotDirector.ReplanTicks"/> (a
    /// moved team mate, a nearer one, one gone), and holds once there.
    /// </summary>
    private void WatchHealth(BotDirector director, in TankState me, int tick)
    {
        if (FallingBack) return;
        if (!Retreating)
        {
            if (SentForward || me.Health >= BotDirector.RetreatHealth * me.Spec.MaxHealth || !director.MayRetreat(me) ||
                !director.RetreatTarget(me, out _, out _))
                return;
            (Retreating, _retreatBegun) = (true, false);
        }
        if (_retreatBegun && (tick < _retargetAt || _asked))
        {
            if (_leg == Leg.End && director.RetreatDone(me, _retreatAlly)) Begin(Leg.Arrived, Goal, keepPath: false);
            return;
        }
        _retargetAt = tick + BotDirector.ReplanTicks;
        bool somewhere = director.RetreatTarget(me, out Vector2 to, out _retreatAlly);
        if (!somewhere || director.RetreatDone(me, _retreatAlly))
        {
            if (_leg != Leg.Arrived) Begin(Leg.Arrived, somewhere ? to : me.Position, keepPath: false);
        }
        else if (!_retreatBegun || _leg is not Leg.End) Begin(Leg.End, to, keepPath: false);
        else if (to != Goal) (Goal, _needPath) = (to, true);   // the old path leads that way until the new one comes
        _retreatBegun = true;
    }

    /// <summary>
    /// Following its path (<paramref name="moving"/>) with the gun on the target, firing once laid and loaded (never at a
    /// target it is only keeping a moment, <paramref name="inSight"/> false).
    /// </summary>
    private static InputCmd FireOnTheMove(in TankState me, in TankState target, HeightField terrain, InputCmd moving, bool inSight)
    {
        Lay lay = Gunnery.Aim(me, target, terrain, out _);
        bool fire = inSight && lay.CanLay && lay.Laid && me.Reload <= 0f;
        return moving with { AimYaw = lay.Yaw, AimPitch = lay.Pitch, Fire = fire };
    }

    /// <summary>
    /// Stopped with the gun on the target's aim point, firing once laid, loaded and all but still (and the target
    /// <paramref name="inSight"/>). The stuck watch waits meanwhile: a short stop to fire doesn't start it afresh.
    /// </summary>
    private InputCmd Engage(in TankState me, in TankState target, HeightField terrain, int tick, bool inSight)
    {
        Lay lay = Gunnery.Aim(me, target, terrain, out _);
        bool fire = inSight && lay.CanLay && lay.Laid && me.Reload <= 0f && MathF.Abs(me.Speed) < BotDirector.FireSpeed;
        Activity = BotActivity.Engaging;
        if (_checkAt >= 0) _checkAt++;
        return new InputCmd(tick, 0, 0, lay.Yaw, lay.Pitch, fire);
    }

    /// <summary>
    /// Backing off, holding, waiting for a path or following it, the gun straight ahead. Having backed away from an
    /// enemy too close while holding, it first drives back to where it held.
    /// </summary>
    private InputCmd Move(in TankState me, int tick)
    {
        if (_backedOff)
        {
            (_backedOff, _backOffStucks) = (false, 0);
            if (_leg == Leg.Arrived && Vector2.Distance(me.Position, Goal) > Steering.StopRadius) Begin(Leg.End, Goal, keepPath: false);
        }
        var still = new InputCmd(tick, 0, 0, me.Yaw, 0f, false);
        if (tick < _reverseUntil)
        {
            Stand(BotActivity.Reversing);
            return still with { Throttle = -1, Steer = _reverseSteer };
        }
        if (_leg is Leg.None or Leg.Arrived or Leg.Paused)
        {
            Stand(BotActivity.Holding);
            return _leg != Leg.Paused && Role == BotRole.Defend && !FallingBack && !Retreating ? Face(me, still) : still;
        }
        AdvancePath(me);
        if (_pathIndex >= _pathCount)
        {
            Stand(BotActivity.Waiting);
            return still;
        }
        Vector2 point = _path[_pathIndex];
        bool last = _pathIndex == _pathCount - 1;
        var (throttle, steer) = Steering.Toward(me, AroundMates(me, point, out bool yield), stopThere: last && _leg == Leg.End && _pathGoal == Goal);
        if (yield) throttle = 0;
        Activity = BotActivity.Driving;
        if (IsStuck(me, tick))
        {
            OnStuck(me, tick);
            Stand(BotActivity.Reversing);
            return still with { Throttle = -1, Steer = _reverseSteer };
        }
        return still with { Throttle = throttle, Steer = steer };
    }

    /// <summary>A defender on its cover point: the hull turned on the spot, and the gun, towards the way the enemy comes.</summary>
    private InputCmd Face(in TankState me, in InputCmd still)
    {
        float yaw = Angles.YawOf(_facing - me.Position);
        float off = Angles.Wrap(yaw - me.Yaw);
        sbyte steer = (sbyte)(MathF.Abs(off) <= FaceTolerance ? 0 : off > 0 ? -1 : 1);   // +yaw is to the left; Steer +1 turns right
        return still with { Steer = steer, AimYaw = yaw };
    }

    /// <summary>
    /// Moves on past the path points reached: within <see cref="PassRadius"/>, or (missed, however widely) past the point
    /// both along the way on to the next point and along the way in from the one before (or from where the path was asked
    /// for): a point round a sharp bend is not cut while still coming up to it. The last one is used up once reached
    /// without the leg having ended there (a path cut short, or one for an earlier leg): then a new path is due.
    /// </summary>
    private void AdvancePath(in TankState me)
    {
        while (_pathIndex < _pathCount - 1)
        {
            Vector2 p = _path[_pathIndex], next = _path[_pathIndex + 1], before = _pathIndex > 0 ? _path[_pathIndex - 1] : _pathFrom;
            Vector2 off = me.Position - p;
            bool reached = off.LengthSquared() <= PassRadius * PassRadius;
            bool past = Vector2.Dot(off, next - p) > 0f && Vector2.Dot(off, p - before) >= 0f;
            if (!reached && !past) break;
            _pathIndex++;
        }
        if (_pathIndex == _pathCount - 1 && Vector2.Distance(me.Position, _path[_pathIndex]) <= PassRadius &&
            (_pathCut || _pathGoal != Goal))
        {
            _pathCount = 0;
            if (!_asked) _needPath = true;
        }
    }

    /// <summary>
    /// Whether the leg is over: the goal (or the end of a whole path to it) is reached. A final leg that stopped at the
    /// end of its path outside the circle its point lies in (the end moved off a wreck, say) draws another point at once.
    /// </summary>
    private void CheckArrival(in TankState me)
    {
        switch (_leg)
        {
            case Leg.Waypoint when Reached(me, BotDirector.WaypointRadius):
                NextWaypoint();
                break;
            case Leg.Via when Reached(me, BotDirector.ViaRadius):
                NextVia(keepPath: true);
                break;
            case Leg.End when Steering.StoppedAt(me, Goal) || (WholePath && Steering.StoppedAt(me, _path[_pathCount - 1])):
                if (OutsideItsCircle(me)) EndMissed(me, pause: false, tick: 0);
                else
                {
                    if (Role == BotRole.Defend && !FallingBack && !Retreating) _coverFails = 0;
                    Begin(Leg.Arrived, Goal, keepPath: false);
                }
                break;
        }
    }

    /// <summary>
    /// The circle a final leg must end in: called back, its own base; an attacker (not retreating), the base its capture
    /// point lies in; otherwise none (Radius 0).
    /// </summary>
    private TeamBase EndCircle => FallingBack ? _ownBase : Role == BotRole.Attack && !Retreating ? _endBase : default;

    /// <summary>Its final leg must end in a circle (<see cref="EndCircle"/>) and the tank is outside it.</summary>
    private bool OutsideItsCircle(in TankState me)
    {
        TeamBase circle = EndCircle;
        return circle.Radius > 0f && Vector2.DistanceSquared(me.Position, circle.Centre) > circle.Radius * circle.Radius;
    }

    /// <summary>Within <paramref name="radius"/> of the goal, or of the last point of a whole path to it (a goal off open ground snaps to the nearest open cell).</summary>
    private bool Reached(in TankState me, float radius) =>
        Vector2.Distance(me.Position, Goal) <= radius || (WholePath && Vector2.Distance(me.Position, _path[_pathCount - 1]) <= radius);

    /// <summary>The path held runs all the way to the current goal.</summary>
    private bool WholePath => _pathCount > 0 && !_pathCut && _pathGoal == Goal;

    private void NextWaypoint()
    {
        if (_waypoints.Count < 2)
        {
            Begin(Leg.Arrived, Goal, keepPath: false);
            return;
        }
        int next = (int)(_rng.NextUInt() % (uint)(_waypoints.Count - 1));
        if (next >= _waypoint) next++;   // never the same one again
        _waypoint = next;
        Begin(Leg.Waypoint, _waypoints[next], keepPath: false);
    }

    /// <summary>On to the next via point, or the end; the path to the one passed is kept until the next comes (it leads that way).</summary>
    private void NextVia(bool keepPath)
    {
        _viaIndex++;
        if (_viaIndex < _via.Count) Begin(Leg.Via, _via[_viaIndex], keepPath);
        else Begin(Leg.End, _end, keepPath);
    }

    private void Begin(Leg leg, Vector2 goal, bool keepPath)
    {
        (_leg, Goal, _legStucks, _askAfter) = (leg, goal, 0, 0);
        _needPath = leg is Leg.Waypoint or Leg.Via or Leg.End;
        if (!keepPath) _pathCount = 0;
    }

    /// <summary>Holds until <see cref="BotDirector.RetryTicks"/> from <paramref name="tick"/>, then drives for <paramref name="goal"/> afresh.</summary>
    private void Pause(Vector2 goal, int tick)
    {
        Begin(Leg.Paused, goal, keepPath: false);
        _retryAt = tick + BotDirector.RetryTicks;
    }

    /// <summary>
    /// Gives the leg up after <see cref="BotDirector.StuckRetries"/> stucks on it: the next waypoint or via point; a final
    /// leg is never given up short of its target (see the remarks).
    /// </summary>
    private void GiveUpLeg(in TankState me, int tick)
    {
        switch (_leg)
        {
            case Leg.Waypoint:
                NextWaypoint();
                break;
            case Leg.Via:
                NextVia(keepPath: false);
                break;
            case Leg.End when FallingBack || (Role == BotRole.Attack && !Retreating):
                EndMissed(me, pause: true, tick);
                break;
            case Leg.End when Role == BotRole.Defend && !Retreating:
                CoverMissed(tick);
                break;
            case Leg.End:   // retreating: it looks for its goal again with the next retarget
                Begin(Leg.Arrived, Goal, keepPath: false);
                break;
        }
    }

    /// <summary>
    /// An attacker's (or a bot called back's) end not reached: inside its circle it holds where it is; outside, it draws
    /// another point in the circle (with no circle known, it keeps its point) and drives for it, after
    /// <see cref="BotDirector.RetryTicks"/> holding when <paramref name="pause"/>.
    /// </summary>
    private void EndMissed(in TankState me, bool pause, int tick)
    {
        if (!OutsideItsCircle(me) && EndCircle.Radius > 0f)
        {
            Begin(Leg.Arrived, Goal, keepPath: false);
            return;
        }
        TeamBase circle = EndCircle;
        Vector2 next = circle.Radius > 0f ? _director.Redraw(this, circle) : Goal;
        if (FallingBack) FallBackPoint = next;
        else _end = next;
        if (pause) Pause(next, tick);
        else Begin(Leg.End, next, keepPath: false);
    }

    /// <summary>A defender's cover point not reached: the next one; once every one has failed in a row, its base centre; that failing too, a pause.</summary>
    private void CoverMissed(int tick)
    {
        if (Cover >= 0 && ++_coverFails < _covers.Count)
        {
            Cover = (Cover + 1) % _covers.Count;
            _end = _covers[Cover];
            Begin(Leg.End, _end, keepPath: false);
        }
        else if (_end != _home)
        {
            (Cover, _end) = (-1, _home);
            Begin(Leg.End, _home, keepPath: false);
        }
        else Pause(_home, tick);
    }

    /// <summary>Takes a path that has come; a search that found none counts as getting stuck (<see cref="NotFound"/>).</summary>
    private void TakeAnswer(PathService paths, in TankState me, int tick)
    {
        if (!_asked) return;
        switch (paths.StatusOf(TankId))
        {
            case PathStatus.Found:
                _pathCount = paths.TakePath(TankId, _path);
                _pathIndex = 0;
                (_pathGoal, _pathFrom) = (_askedGoal, _askedFrom);
                _pathCut = _pathCount == BotDirector.MaxPathPoints && _path[_pathCount - 1] != _askedGoal;
                _asked = false;
                break;
            case PathStatus.NoPath:
                _asked = false;
                if (_leg is Leg.Waypoint or Leg.Via or Leg.End) NotFound(me, tick);
                break;
        }
    }

    /// <summary>
    /// A search found no path: a stuck without backing off (there is nothing to back away from), asked again after
    /// <see cref="BotDirector.ReplanTicks"/>; the third on one leg gives the leg up.
    /// </summary>
    private void NotFound(in TankState me, int tick)
    {
        Stucks++;
        _legStucks++;
        (_askAfter, _needPath) = (tick + BotDirector.ReplanTicks, true);
        if (_legStucks >= BotDirector.StuckRetries) GiveUpLeg(me, tick);
    }

    /// <summary>Asks for a path from where the tank is: a new leg, a path used up, backed off, or every few seconds while driving.</summary>
    private void Ask(PathService paths, in TankState me, int tick)
    {
        if (_leg is Leg.None or Leg.Arrived or Leg.Paused || tick < _reverseUntil || tick < _askAfter) return;
        bool due = _needPath || (_pathCount == 0 && !_asked) || (Activity == BotActivity.Driving && !_asked && tick >= _replanAt);
        if (!due) return;
        paths.Request(TankId, me.Position, Goal);
        (_asked, _askedGoal, _askedFrom, _needPath, _replanAt) = (true, Goal, me.Position, false, tick + BotDirector.ReplanTicks);
        PathRequests++;
    }

    /// <summary>The stuck watch (driving; it waits while engaging): every StuckTicks, moved less than StuckDistance and turned less than StuckTurn?</summary>
    private bool IsStuck(in TankState me, int tick)
    {
        if (_checkAt < 0 || tick >= _checkAt)
        {
            bool stuck = _checkAt >= 0 && Vector2.Distance(me.Position, _checkedAt) < BotDirector.StuckDistance &&
                         MathF.Abs(Angles.Wrap(me.Yaw - _checkedYaw)) < BotDirector.StuckTurn;
            (_checkAt, _checkedAt, _checkedYaw) = (tick + BotDirector.StuckTicks, me.Position, me.Yaw);
            return stuck;
        }
        return false;
    }

    /// <summary>Backs off, the hull turning each time the other way, then asks again; the third time on one leg gives the leg up.</summary>
    private void OnStuck(in TankState me, int tick)
    {
        Stucks++;
        _legStucks++;
        _reverseSteer = (sbyte)(Stucks % 2 == 1 ? 1 : -1);
        (_reverseUntil, _checkAt, _needPath) = (tick + BotDirector.ReverseTicks, -1, true);
        if (_legStucks >= BotDirector.StuckRetries) GiveUpLeg(me, tick);
    }

    /// <summary>Not driving: no stuck watch under way.</summary>
    private void Stand(BotActivity activity)
    {
        Activity = activity;
        _checkAt = -1;
    }
}

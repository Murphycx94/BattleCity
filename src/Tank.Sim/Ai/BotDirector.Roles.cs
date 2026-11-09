using System.Numerics;
using System.Text;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Navigation;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>
/// A standard battle's roles (stage 5 design §4.2, stage 5C task 7): the lanes and cover points in use, the roles given
/// out at the start, and the team watch that calls bots back when their base starts to fall and lets them go once it is
/// quiet, and sends a team forward that has no attacker left; and what a retreating bot heads for.
/// </summary>
/// <remarks>
/// <para><b>Lanes and cover.</b> The map's (<see cref="BotMapData"/>): its lanes, team 1 driving them backwards, its cover
/// points and its flank lane. A map without lanes gets one straight to the enemy base (no via points, the flank); one
/// without cover points gets four round each base, <see cref="TeamBase.Radius"/> + 15 m from the centre at −45°, −15°,
/// +15° and +45° from the way to the other base, each moved to the nearest open cell within 12 m (dropped if none). The
/// way the enemy comes at a base is the mean of the lanes' via points nearest it (the other base without via points).</para>
/// <para><b>Roles</b>, once, each team on its own (its tanks counted with the players', its bots in ascending tank id):
/// every tank destroyer defends, at most <see cref="MaxDefenders"/>; a team of 3 tanks or more with no defender yet keeps
/// one back, a tank destroyer before a heavy before a medium before a light, then the lowest id. Light tanks attack down
/// the flank lane; the other attackers in turn take the lane with the fewest attackers so far (ties: the lower index). The
/// first defender picks a cover point with its own random numbers, the second the one half the list away. Each attacker
/// draws its capture point with its own random numbers (<see cref="DrawPoint"/>). A bot added once the roles are out
/// gets one by the same rules, as things stand (<see cref="GiveLateRole"/>). Roles change only one way after that (5C
/// stalemate fix): when a team has no live attacking tank (a bot attacking and not retreating, or any player), every live
/// bot of it attacks: defenders, and those retreating or holding where their retreat ended; their lanes and capture
/// points as at the start, each from where it is. One sent forward stays an attacker (being called back aside) and
/// never retreats again (<see cref="Bot.SentForward"/>).</para>
/// <para><b>The team watch</b>, every <see cref="Update"/>, team 0 then team 1: first the fall-back, then sending the team
/// forward if it has no attacker left (above). When the attackers' ticks on the team's base rose since the last update,
/// the base is falling: while fewer than <see cref="FallBackCount"/> live bots are on their way back, the nearest live
/// bots not in the enemy base (straight to the base centre; ties: the lower id) are called back to points drawn in their
/// own base, and the first call of a fall counts in <see cref="FallBacks"/>. They are released, back to their roles, once
/// for <see cref="ReleaseTicks"/> the score hasn't risen and no enemy the team sees has been within the base radius plus
/// <see cref="ReleaseClear"/> of its centre (stage 5C plan, deviation 6).</para>
/// <para><b>Retreat</b>: to the nearest live team mate that is nearer the own base than the bot by more than
/// <see cref="RetreatArrive"/> (or, got to already, within that of it and no further from home), or that is a bot holding
/// where it is, or to the own base centre, whichever is nearest; a team mate further forward is never followed (outside
/// a standard battle, the nearest team mate).</para>
/// <para>Only <see cref="AssignRoles"/>'s first call, <see cref="Add"/>, <see cref="Describe"/> and <see cref="Summary"/> may allocate.</para>
/// </remarks>
public sealed partial class BotDirector
{
    /// <summary>Capture and fall-back points: draws at most, the last one taken however near the others.</summary>
    private const int MaxDraws = 8;

    /// <summary>Cover points made round a base without bot data lie this far outside its circle, m.</summary>
    private const float MadeCoverOut = 15f;

    /// <summary>… and are moved to the nearest open cell within this, m.</summary>
    private const float MadeCoverSnap = 12f;

    private static readonly float[] MadeCoverTurns = { Angles.Deg(-45), Angles.Deg(-15), Angles.Deg(15), Angles.Deg(45) };
    private static readonly IReadOnlyList<Vector2> NoPoints = Array.Empty<Vector2>();

    private bool _standard;
    private readonly TeamBase[] _bases = new TeamBase[2];
    private readonly IReadOnlyList<Vector2>[][] _laneVia = { Array.Empty<IReadOnlyList<Vector2>>(), Array.Empty<IReadOnlyList<Vector2>>() };
    private readonly IReadOnlyList<Vector2>[] _cover = { NoPoints, NoPoints };
    private readonly Vector2[] _approach = new Vector2[2];
    private string[] _laneNames = Array.Empty<string>();

    private readonly int[] _lastOn = new int[2];       // the attackers' ticks on each team's base at the last update
    private readonly int[] _unquietAt = new int[2];    // the last tick that base's score rose or a seen enemy was near it
    private readonly bool[] _calledBack = new bool[2];
    private readonly int[] _fallBacks = new int[2];

    private readonly Bot[] _team = new Bot[Simulation.MaxTanks];          // one team's bots, while giving out roles
    private readonly int[] _teamLane = new int[Simulation.MaxTanks];      // their lanes (−1: not attacking)
    private readonly Vector2[] _taken = new Vector2[Simulation.MaxTanks]; // the points a team has drawn
    private readonly int[] _laneLoad = new int[BotMapData.MaxLanes];

    /// <summary>The lanes in use: the map's, or one straight to the enemy base without bot data; 0 outside a standard battle.</summary>
    public int LaneCount { get; private set; }

    /// <summary>The lane the light tanks take; −1 outside a standard battle.</summary>
    public int FlankLane { get; private set; } = -1;

    /// <summary>Team <paramref name="team"/>'s cover points in use (none outside a standard battle).</summary>
    /// <exception cref="ArgumentOutOfRangeException">A team other than 0 or 1.</exception>
    public IReadOnlyList<Vector2> CoverOf(byte team) => _cover[CheckTeam(team)];

    /// <summary>How many times team <paramref name="team"/>'s bots were called back: once a fall of its base.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A team other than 0 or 1.</exception>
    public int FallBacks(byte team) => _fallBacks[CheckTeam(team)];

    private static byte CheckTeam(byte team) => team <= 1
        ? team
        : throw new ArgumentOutOfRangeException(nameof(team), team, "Only teams 0 and 1 have lanes and cover points.");

    /// <summary>A standard battle: the lanes, the cover points and the way the enemy comes at each base.</summary>
    private void SetUpStandard()
    {
        if (Rules.Mode != BattleMode.Standard || Rules.Capture is null) return;
        _standard = true;
        foreach (TeamBase b in Rules.Capture.Bases) _bases[b.Team] = b;
        BotMapData data = Map.Bots;
        int lanes = data.Lanes.Count;
        LaneCount = lanes > 0 ? lanes : 1;
        FlankLane = lanes > 0 ? data.FlankLane(Rules.Capture.Bases) : 0;
        _laneNames = new string[LaneCount];
        for (int k = 0; k < LaneCount; k++) _laneNames[k] = lanes > 0 ? data.Lanes[k].Name : "direct";
        for (byte team = 0; team < 2; team++)
        {
            var via = new IReadOnlyList<Vector2>[LaneCount];
            for (int k = 0; k < LaneCount; k++) via[k] = lanes > 0 ? data.ViaFor(k, team) : NoPoints;
            _laneVia[team] = via;
            _approach[team] = ApproachTo(team);
            _cover[team] = data.CoverOf(team).Count > 0 ? data.CoverOf(team) : MakeCover(team);
        }
    }

    /// <summary>The way the enemy comes at team <paramref name="team"/>'s base: the mean of each lane's via point nearest it.</summary>
    private Vector2 ApproachTo(byte team)
    {
        Vector2 sum = Vector2.Zero;
        int n = 0;
        foreach (IReadOnlyList<Vector2> via in _laneVia[team])
        {
            if (via.Count == 0) continue;
            sum += via[0];   // the team's own order starts at its side
            n++;
        }
        return n > 0 ? sum / n : _bases[1 - team].Centre;
    }

    /// <summary>Four cover points round team <paramref name="team"/>'s base on the side facing the other base.</summary>
    private IReadOnlyList<Vector2> MakeCover(byte team)
    {
        TeamBase own = _bases[team];
        Vector2 way = _bases[1 - team].Centre - own.Centre;
        way = way.LengthSquared() > 0 ? Vector2.Normalize(way) : new Vector2(1, 0);
        var made = new List<Vector2>(MadeCoverTurns.Length);
        foreach (float turn in MadeCoverTurns)
        {
            (float sin, float cos) = MathF.SinCos(turn);
            var dir = new Vector2(way.X * cos - way.Y * sin, way.X * sin + way.Y * cos);
            if (TrySnap(own.Centre + dir * (own.Radius + MadeCoverOut), MadeCoverSnap, out Vector2 p)) made.Add(p);
        }
        return made.AsReadOnly();
    }

    /// <summary><paramref name="p"/> itself on an open cell, else the centre of the nearest open one within <paramref name="radius"/>.</summary>
    private bool TrySnap(Vector2 p, float radius, out Vector2 snapped)
    {
        snapped = p;
        if (Grid.IsOpenAt(p)) return true;
        if (!Grid.TryNearestOpen(p, radius, out int i, out int j)) return false;
        snapped = NavGrid.CentreOf(i, j);
        return true;
    }

    /// <summary>Every bot's role: in a standard battle each team's by the rules above, otherwise wandering.</summary>
    private void GiveOutRoles()
    {
        if (!_standard)
        {
            for (int b = 0; b < _bots.Count; b++) _bots[b].Wander(Map.Waypoints);
            return;
        }
        GiveOutRoles(0);
        GiveOutRoles(1);
    }

    private void GiveOutRoles(byte team)
    {
        int n = 0;
        for (int b = 0; b < _bots.Count; b++)
            if (TeamOf(_bots[b]) == team) _team[n++] = _bots[b];
        if (n == 0) return;
        int tanks = 0;
        foreach (ref readonly TankState t in Sim.Tanks)
            if (t.Team == team) tanks++;

        int defenders = 0, firstCover = -1;
        for (int k = 0; k < n && defenders < MaxDefenders; k++)
            if (RankOf(_team[k]) == TdRank) MakeDefender(_team[k], team, defenders++, ref firstCover);
        if (defenders == 0 && tanks >= 3)
        {
            int best = 0;
            for (int k = 1; k < n; k++)
                if (RankOf(_team[k]) > RankOf(_team[best])) best = k;
            MakeDefender(_team[best], team, defenders++, ref firstCover);
        }

        for (int k = 0; k < n; k++) _teamLane[k] = _team[k].Role == BotRole.Defend ? NotAttacking : ToAttack;
        SendDownLanes(team, n, forward: false);
    }

    private const int NotAttacking = -1, ToAttack = -2;   // _teamLane before the lanes are given out

    /// <summary>
    /// Gives the bots <c>_team[0..n)</c> marked <see cref="ToAttack"/> in <c>_teamLane</c> their lanes and capture points
    /// (see the remarks: light tanks the flank, the others in turn the lane with the fewest so far, ties the lower index;
    /// the points drawn apart), then sends them: at the start down their lanes from the first via point, sent forward
    /// (<paramref name="forward"/>) from where each is.
    /// </summary>
    private void SendDownLanes(byte team, int n, bool forward)
    {
        Array.Clear(_laneLoad);
        for (int k = 0; k < n; k++)
        {
            if (_teamLane[k] != ToAttack || RankOf(_team[k]) != LightRank) continue;
            _teamLane[k] = FlankLane;
            _laneLoad[FlankLane]++;
        }
        for (int k = 0; k < n; k++)
        {
            if (_teamLane[k] != ToAttack) continue;
            int lane = 0;
            for (int l = 1; l < LaneCount; l++)
                if (_laneLoad[l] < _laneLoad[lane]) lane = l;
            _teamLane[k] = lane;
            _laneLoad[lane]++;
        }

        TeamBase target = _bases[1 - team];
        int taken = 0;
        for (int k = 0; k < n; k++)
        {
            int lane = _teamLane[k];
            if (lane < 0) continue;
            Bot bot = _team[k];
            Vector2 point = DrawPoint(bot, target, _taken.AsSpan(0, taken));
            _taken[taken++] = point;
            if (!forward) bot.Attack(lane, _laneVia[team][lane], point, target);
            else
            {
                IsAlive(bot, out Vector2 at);
                bot.SendForward(lane, _laneVia[team][lane], point, target, at);
            }
        }
    }

    /// <summary>
    /// A bot added once the roles are out (<see cref="Add"/>): outside a standard battle it wanders; in one, by the rules
    /// above as the team stands now: a tank destroyer defends while the team has fewer than <see cref="MaxDefenders"/>,
    /// any bot does when the team (of 3 tanks or more) has none, its cover point half the list on from the first
    /// defender's; otherwise a light tank takes the flank lane and any other the lane with the fewest attackers (ties:
    /// the lower index), its capture point drawn apart from the team's.
    /// </summary>
    private void GiveLateRole(Bot bot)
    {
        byte team = TeamOf(bot);
        if (!_standard || team > 1)
        {
            bot.Wander(Map.Waypoints);
            return;
        }
        int defenders = 0, firstCover = -1, taken = 0, tanks = 0;
        Array.Clear(_laneLoad);
        for (int b = 0; b < _bots.Count; b++)
        {
            Bot other = _bots[b];
            if (other == bot || TeamOf(other) != team) continue;
            if (other.Role == BotRole.Defend && defenders++ == 0) firstCover = other.Cover;
            if (other.Role != BotRole.Attack) continue;
            _laneLoad[other.Lane]++;
            _taken[taken++] = other.End;
        }
        foreach (ref readonly TankState t in Sim.Tanks)
            if (t.Team == team) tanks++;
        int rank = RankOf(bot);
        if ((rank == TdRank && defenders < MaxDefenders) || (defenders == 0 && tanks >= 3))
        {
            MakeDefender(bot, team, defenders, ref firstCover);
            return;
        }
        int lane = 0;
        if (rank == LightRank) lane = FlankLane;
        else
            for (int l = 1; l < LaneCount; l++)
                if (_laneLoad[l] < _laneLoad[lane]) lane = l;
        TeamBase target = _bases[1 - team];
        bot.Attack(lane, _laneVia[team][lane], DrawPoint(bot, target, _taken.AsSpan(0, taken)), target);
    }

    /// <summary>The first defender's cover point is its own random pick, the second's half the list on from the first's.</summary>
    private void MakeDefender(Bot bot, byte team, int nth, ref int firstCover)
    {
        IReadOnlyList<Vector2> cover = _cover[team];
        Vector2 home = _bases[team].Centre;
        if (cover.Count == 0)
        {
            bot.Defend(cover, -1, home, _approach[team], home);
            return;
        }
        int pick = nth == 0 ? (int)(bot.NextUInt() % (uint)cover.Count) : (firstCover + cover.Count / 2) % cover.Count;
        if (nth == 0) firstCover = pick;
        bot.Defend(cover, pick, cover[pick], _approach[team], home);
    }

    private const int LightRank = 0, TdRank = 3;

    /// <summary>Who stays at home first: a tank destroyer (3), a heavy (2), a medium (1), a light (0).</summary>
    private int RankOf(Bot bot)
    {
        string id = Sim.Tanks[Sim.SlotOf(bot.TankId)].Spec.Id;
        return id == TankCatalog.Td.Id ? TdRank : id == TankCatalog.Heavy.Id ? 2 : id == TankCatalog.Medium.Id ? 1 : LightRank;
    }

    /// <summary>The bot's team, or <see cref="MatchRules.NoTeam"/> when its tank is gone.</summary>
    private byte TeamOf(Bot bot)
    {
        int slot = Sim.SlotOf(bot.TankId);
        return slot < 0 ? MatchRules.NoTeam : Sim.Tanks[slot].Team;
    }

    /// <summary>
    /// A point in <paramref name="b"/>'s circle, drawn with <paramref name="bot"/>'s own random numbers: radius
    /// (R − <see cref="CaptureMargin"/>)·√u, angle 2π·u₂, on an open cell (the nearest within the snap radius); a draw
    /// with no open cell that near, or moved by the snap farther than R − <see cref="CaptureMargin"/> from the centre,
    /// fails (5C batch B review follow-up: the riverland has rocks on both bases' rims). Drawn again while failing or
    /// within <see cref="CaptureSpacing"/> of one <paramref name="taken"/>, at most <see cref="MaxDraws"/> times: the last
    /// good draw is taken however near the others, the centre when none was good (design §4.2: not all on the centre).
    /// </summary>
    internal Vector2 DrawPoint(Bot bot, TeamBase b, ReadOnlySpan<Vector2> taken)
    {
        float reach = MathF.Max(0f, b.Radius - CaptureMargin);
        Vector2 point = b.Centre;
        for (int draw = 0; draw < MaxDraws; draw++)
        {
            float r = reach * MathF.Sqrt(bot.NextFloat());
            (float sin, float cos) = MathF.SinCos(2f * MathF.PI * bot.NextFloat());
            if (!TrySnap(b.Centre + new Vector2(cos, sin) * r, PathFinder.SnapRadius, out Vector2 p) ||
                Vector2.DistanceSquared(p, b.Centre) > reach * reach)
                continue;
            point = p;
            if (ApartFrom(p, taken)) break;
        }
        return point;
    }

    /// <summary>
    /// Another point for <paramref name="bot"/>'s final leg in <paramref name="circle"/> (its end missed outside it),
    /// drawn as <see cref="DrawPoint"/> apart from its team's other points of the kind: capture points, or, called back,
    /// fall-back points.
    /// </summary>
    internal Vector2 Redraw(Bot bot, TeamBase circle)
    {
        byte team = TeamOf(bot);
        int taken = 0;
        for (int b = 0; b < _bots.Count; b++)
        {
            Bot other = _bots[b];
            if (other == bot || TeamOf(other) != team) continue;
            if (bot.FallingBack ? other.FallingBack : other.Role == BotRole.Attack) _taken[taken++] = bot.FallingBack ? other.FallBackPoint : other.End;
        }
        return DrawPoint(bot, circle, _taken.AsSpan(0, taken));
    }

    private static bool ApartFrom(Vector2 p, ReadOnlySpan<Vector2> taken)
    {
        foreach (Vector2 q in taken)
            if (Vector2.Distance(p, q) < CaptureSpacing) return false;
        return true;
    }

    /// <summary>The team watch (see the remarks), team 0 then team 1; standard battles only.</summary>
    private void WatchTeams(int tick)
    {
        BaseCapture capture = Rules.Capture!;
        for (byte team = 0; team < 2; team++)
        {
            int on = capture.TicksOn(team);
            bool rose = on > _lastOn[team];
            _lastOn[team] = on;
            if (rose)
            {
                _unquietAt[team] = tick;
                CallBack(team);
            }
            if (_calledBack[team])
            {
                if (EnemyNear(team)) _unquietAt[team] = tick;
                else if (tick - _unquietAt[team] >= ReleaseTicks) Release(team);
            }
            if (!HasAttacker(team)) SendForward(team);
        }
    }

    /// <summary>
    /// Whether team <paramref name="team"/> has a live attacking tank: a bot attacking (called back or not) and not
    /// retreating, or any player.
    /// </summary>
    private bool HasAttacker(byte team)
    {
        foreach (ref readonly TankState t in Sim.Tanks)
        {
            if (t.Team != team || t.IsDestroyed) continue;
            Bot? bot = Find(t.Id);
            if (bot is null || (bot.Role == BotRole.Attack && !bot.Retreating)) return true;
        }
        return false;
    }

    /// <summary>
    /// Team <paramref name="team"/> has no attacker left: every live bot of it (defenders, those retreating or holding
    /// where their retreat ended) attacks, its lane and capture point given as at the start, from where it is.
    /// </summary>
    private void SendForward(byte team)
    {
        int n = 0;
        for (int b = 0; b < _bots.Count; b++)
        {
            Bot bot = _bots[b];
            if (TeamOf(bot) != team || !IsAlive(bot, out _)) continue;
            _teamLane[n] = ToAttack;
            _team[n++] = bot;
        }
        if (n > 0) SendDownLanes(team, n, forward: true);
    }

    /// <summary>Tops the bots on their way back up to <see cref="FallBackCount"/>, nearest to the base first.</summary>
    private void CallBack(byte team)
    {
        TeamBase own = _bases[team], enemy = _bases[1 - team];
        int coming = 0, taken = 0;
        for (int b = 0; b < _bots.Count; b++)
        {
            Bot bot = _bots[b];
            if (!bot.FallingBack || TeamOf(bot) != team) continue;
            _taken[taken++] = bot.FallBackPoint;
            if (IsAlive(bot, out _)) coming++;
        }
        bool called = false;
        while (coming < FallBackCount)
        {
            Bot? pick = null;
            float nearest = float.MaxValue;
            for (int b = 0; b < _bots.Count; b++)
            {
                Bot bot = _bots[b];
                if (bot.FallingBack || TeamOf(bot) != team || !IsAlive(bot, out Vector2 at) || InCircle(at, enemy)) continue;
                float d = Vector2.Distance(at, own.Centre);
                if (d < nearest) (pick, nearest) = (bot, d);   // ascending ids: ties keep the lower
            }
            if (pick is null) break;
            Vector2 point = DrawPoint(pick, own, _taken.AsSpan(0, taken));
            _taken[taken++] = point;
            pick.FallBack(point, own);
            coming++;
            called = true;
        }
        if (!called) return;
        if (!_calledBack[team]) _fallBacks[team]++;
        _calledBack[team] = true;
    }

    /// <summary>The fall is over: everyone called back takes up what it was doing.</summary>
    private void Release(byte team)
    {
        for (int b = 0; b < _bots.Count; b++)
        {
            Bot bot = _bots[b];
            if (!bot.FallingBack || TeamOf(bot) != team) continue;
            bot.Release(IsAlive(bot, out Vector2 at) ? at : bot.Goal);
        }
        _calledBack[team] = false;
    }

    /// <summary>An enemy team <paramref name="team"/> sees within its base radius plus <see cref="ReleaseClear"/> of the centre.</summary>
    private bool EnemyNear(byte team)
    {
        TeamBase own = _bases[team];
        float reach = own.Radius + ReleaseClear;
        foreach (ref readonly TankState t in Sim.Tanks)
            if (t.Team != team && !t.IsDestroyed && Vector2.Distance(t.Position, own.Centre) <= reach && Vision.IsVisible(t.Id, team))
                return true;
        return false;
    }

    private bool IsAlive(Bot bot, out Vector2 at)
    {
        int slot = Sim.SlotOf(bot.TankId);
        at = slot >= 0 ? Sim.Tanks[slot].Position : default;
        return slot >= 0 && !Sim.Tanks[slot].IsDestroyed;
    }

    private static bool InCircle(Vector2 p, TeamBase b) => Vector2.DistanceSquared(p, b.Centre) <= b.Radius * b.Radius;

    /// <summary>A hurt bot may start to retreat: not while capturing the enemy base, nor at home in its own (standard battle).</summary>
    internal bool MayRetreat(in TankState me) =>
        !_standard || (!InCircle(me.Position, _bases[me.Team]) && !InCircle(me.Position, _bases[1 - me.Team]));

    /// <summary>
    /// Where a retreat heads: the nearest live team mate or the own base's centre (a standard battle), whichever is
    /// nearer; <paramref name="ally"/> is the team mate's id, 0 for the base. In a standard battle only a team mate nearer
    /// the own base than this bot by more than <see cref="RetreatArrive"/> (or within that of it already and no further
    /// from home), or a bot holding, counts: a retreat never heads further forward (5C batch B review follow-up). False:
    /// nowhere (a free-for-all).
    /// </summary>
    internal bool RetreatTarget(in TankState me, out Vector2 to, out int ally)
    {
        (to, ally) = (me.Position, 0);
        float nearest = float.MaxValue;
        Vector2 home = _standard ? _bases[me.Team].Centre : default;
        float mine = Vector2.Distance(home, me.Position);
        if (ModeRules.HasTeams(Rules.Mode))
        {
            foreach (ref readonly TankState t in Sim.Tanks)
            {
                if (t.Team != me.Team || t.Id == me.Id || t.IsDestroyed) continue;
                float d = Vector2.Distance(t.Position, me.Position);
                if (_standard && !Behind(t, d) && Find(t.Id) is not { Activity: BotActivity.Holding }) continue;
                if (d < nearest) (nearest, to, ally) = (d, t.Position, t.Id);
            }
        }
        if (_standard && mine < nearest) (nearest, to, ally) = (mine, home, 0);
        return nearest < float.MaxValue;

        // Nearer home by more than RetreatArrive, or got to already (within RetreatArrive, no further from home).
        bool Behind(in TankState t, float d)
        {
            float theirs = Vector2.Distance(home, t.Position);
            return theirs < mine - RetreatArrive || (d <= RetreatArrive && theirs <= mine);
        }
    }

    /// <summary>A retreat is over: within <see cref="RetreatArrive"/> of the live team mate <paramref name="ally"/>, or inside the own base.</summary>
    internal bool RetreatDone(in TankState me, int ally)
    {
        if (_standard && InCircle(me.Position, _bases[me.Team])) return true;
        if (ally == 0) return false;
        int slot = Sim.SlotOf(ally);
        return slot >= 0 && !Sim.Tanks[slot].IsDestroyed && Vector2.Distance(Sim.Tanks[slot].Position, me.Position) <= RetreatArrive;
    }

    /// <summary>"team 0 — 3 attack (north 1, middle 1, south 1), 1 defend; team 1 — …", or "4 wander" outside a standard battle.</summary>
    private string DescribeStandard()
    {
        var text = new StringBuilder();
        var perLane = new int[LaneCount];
        for (byte team = 0; team < 2; team++)
        {
            int attack = 0, defend = 0;
            Array.Clear(perLane);
            foreach (Bot bot in _bots)
            {
                if (TeamOf(bot) != team) continue;
                if (bot.Role == BotRole.Defend) defend++;
                else if (bot.Role == BotRole.Attack)
                {
                    attack++;
                    perLane[bot.Lane]++;
                }
            }
            if (attack + defend == 0) continue;
            if (text.Length > 0) text.Append("; ");
            text.Append($"team {team} — ");
            if (attack == 0) text.Append("no attackers");
            else
            {
                text.Append($"{attack} attack (");
                for (int k = 0; k < LaneCount; k++) text.Append(k == 0 ? "" : ", ").Append($"{_laneNames[k]} {perLane[k]}");
                text.Append(')');
            }
            text.Append($", {defend} defend");
        }
        return text.Length == 0 ? "none" : text.ToString();
    }

    /// <summary>"team 0 fell back 2 times, team 1 0 times; ", leaving out a team without bots (one of players only).</summary>
    private string FallBacksText()
    {
        bool bots0 = false, bots1 = false;
        foreach (Bot bot in _bots)
        {
            byte team = TeamOf(bot);
            bots0 |= team == 0;
            bots1 |= team == 1;
        }
        string first = bots0 ? $"team 0 fell back {_fallBacks[0]} {Times(_fallBacks[0])}" : "";
        string second = !bots1 ? "" : bots0 ? $", team 1 {_fallBacks[1]} {Times(_fallBacks[1])}" : $"team 1 fell back {_fallBacks[1]} {Times(_fallBacks[1])}";
        return bots0 || bots1 ? $"{first}{second}; " : "";
    }

    private static string Times(int n) => n == 1 ? "time" : "times";
}

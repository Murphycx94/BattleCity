using System.Diagnostics;
using System.Numerics;
using Tank.Net.Protocol;
using Tank.Sim;
using Tank.Sim.Ai;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Navigation;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Net.Battle;

public sealed partial class BattleServer
{
    /// <summary>
    /// Starts a match in the room's mode on the room's map with everyone seated, plus the bots asked for (named, given
    /// random tanks, put on the thinner team: <see cref="BotSeating"/>; in duos, in slot order, partnering whoever is alone
    /// on a team first, then pairing up on the empty teams: <see cref="DuoSeating.ForBot"/>), if the map is ready and may hold
    /// the mode (a royale or duos: the riverland only) and, for a standard battle, has bases, there are at least two tanks
    /// and, in a team mode, both teams have one (in duos, at least two teams). The match is built whole — battlefield, a
    /// royale's rings (drawn from the match seed on the map's navigation grid, which <see cref="ServerMaps"/> built before
    /// calling the map ready) and its crates (laid from the seed on the same grid: <see cref="PlaceCrates"/>), rules, tanks,
    /// the bots' director — before any of it becomes the server's: if the grid copy, the rings, the crates, the rules (which
    /// check the rings) or the bots fail (a bug), that is logged, the bot seats are taken away again and the room stays as it was.
    /// </summary>
    public bool TryStartMatch(out string? reason)
    {
        if (_sim is not null)
        {
            reason = "a match is running";
            return false;
        }
        switch (_maps.StateOf(_roomMap))
        {
            case MapState.Generating:
                reason = "the map is still being generated";
                return false;
            case MapState.Failed:
                reason = "the map failed to generate";
                return false;
        }
        if (!ModeRules.AllowsMap(_mode, _roomMap.Id))   // the room never gets there (SetMode, SetMap); kept as the client's mirror has it
        {
            reason = NotHereReason(_mode);
            return false;
        }
        if (ModeRules.HasBases(_mode) && _roomMap.Bases.Count == 0)   // before the bots are seated: nothing to undo
        {
            reason = "the map has no bases";
            return false;
        }
        var seating = new BotSeating(_options.Seed ?? (uint)Random.Shared.Next(1, int.MaxValue));
        int humans = _seats.Count(s => s is { IsBot: false });
        bool duos = ModeRules.IsDuos(_mode);
        Span<int> duoCounts = stackalloc int[ModeRules.DuoTeams];
        for (int i = 0; i < _bots && humans + i < ModeRules.MaxPlayersFor(_mode); i++)
        {
            byte slot = (byte)Array.FindIndex(_seats, s => s is null);
            var bot = new BattleSeat(slot, 0, isBot: true) { Name = $"机器人 {i + 1}", TankIndex = (byte)seating.NextTankIndex() };
            if (duos)
            {
                CountDuoTeams(duoCounts);   // before the bot takes its seat; 16 seats at most, so a team always has room
                bot.Team = DuoSeating.ForBot(duoCounts);
            }
            else bot.Team = ThinnerTeam(slot);
            _seats[slot] = bot;
        }
        int seated = _seats.Count(s => s is not null);
        if (duos) CountDuoTeams(duoCounts);
        bool teamsManned = duos ? DuoSeating.Manned(duoCounts) >= 2
            : !ModeRules.HasTeams(_mode) || (_seats.Any(s => s is { Team: 0 }) && _seats.Any(s => s is { Team: 1 }));
        if (seated < ModeRules.MinPlayers || !teamsManned)
        {
            RemoveBots();
            reason = seated < ModeRules.MinPlayers ? "fewer than two tanks" : duos ? "fewer than two teams" : "a team is empty";
            return false;
        }

        PreparedMap map = _maps.Get(_roomMap);
        // A Fresh() copy of the battlefield ServerMaps built while preparing the map (bit for bit map.Data.Build(), what
        // every client builds): about 0.1 ms even for the riverland, so the tick thread never builds a map.
        var sim = new Simulation(map.Definition.NewBattlefield());
        var vision = new VisionTracker(sim);
        bool zone = ModeRules.HasZone(_mode), crated = ModeRules.HasCrates(_mode), bots = _seats.Any(s => s is { IsBot: true });
        NavGrid? grid;
        MatchRules rules;
        Crate[]? crates = null;
        string step = "the navigation grid";
        try
        {
            // One copy of the grid ServerMaps built while preparing the map (ready means built): the rings and the crates read it, the bots keep it.
            grid = zone || crated || bots ? map.Definition.NewNavGrid() : null;
            step = "the zone";
            ZoneRing[]? rings = zone ? PickZone(sim.Field, grid!, map.Definition.Spawns.Solo, seating.MatchSeed ^ ZonePicker.SeedSalt) : null;   // before any tank
            if (crated)
            {
                step = "the crates";
                crates = PlaceCrates(sim.Field, grid!, map.Definition.Spawns.Solo, seating.MatchSeed ^ CratePlacer.SeedSalt);
            }
            step = zone ? "the zone" : "the rules";   // in a royale the rules check the rings: a refusal there is the zone's
            rules = new MatchRules(_mode, sim, map.Definition.Spawns, _options.MatchSeconds is { } seconds ? System.Math.Max(1, (int)(seconds * SimConstants.TickRate)) : null,
                bases: map.Definition.Bases, seed: seating.MatchSeed, zone: rings, crates: crates);
            step = "the seats";   // a room that put three on one duos team (or one past team 7) would throw here: the bots go again
            foreach (BattleSeat? seat in _seats)
                if (seat is not null) rules.Join(seat.Slot, TankCatalog.All[seat.TankIndex], seat.Team);
        }
        catch (Exception ex)
        {
            RemoveBots();
            _log($"match not started: {step} failed: {ex}");
            reason = $"{step} failed to start";
            return false;
        }
        BotDirector? director;
        try
        {
            director = SeatBots(sim, vision, rules, map.Definition, seating, grid);
        }
        catch (Exception ex)
        {
            RemoveBots();
            _log($"match not started: the bots failed: {ex}");
            reason = "the bots failed to start";
            return false;
        }

        (_sim, _vision, _rules, _director, _matchMap) = (sim, vision, rules, director, map);
        _zoneSent = rules.KnownRings;   // ring 0 and ring 1 go with the match start
        _matchNumber++;
        _endedAtFrame = -1;
        _hadTank.Clear();
        ForgetCrates();
        foreach (BattleSeat? seat in _seats)
        {
            if (seat is null) continue;
            MarkInMatch(seat);
            if (!seat.IsBot) seat.ResetForMatch();
        }
        _log($"match started: {ModeRules.Id(_mode)}, map {map.Definition.Id}, {humans} players, {seated - humans} bots, seed {seating.MatchSeed}");
        if (duos) _log($"teams: {DescribeDuoTeams()}");
        if (director is not null) _log($"bots: {director.Describe()}");
        if (rules.ZoneRings.Count > 0) _log($"zone: {DescribeRings(rules.ZoneRings)}");
        if (rules.Crates is { } set) LogCratesPlaced(set, sim.Field, map.Definition.Id);
        foreach (BattleSeat seat in _byPeer.Values) SendMatchStart(seat, withMap: true);
        _roomDirty = true;
        reason = null;
        return true;
    }

    /// <summary>
    /// A new <see cref="BotDirector"/> for the match being built with every bot seat's tank (each bot with its own seed
    /// from the match's: <see cref="BotSeating.BotSeed"/>) and the roles given out; none without bot seats. Bots join only
    /// here, at the start (a player may join later, a bot never): their number can't change mid-match.
    /// </summary>
    /// <param name="grid">
    /// The match's copy (about 180 KB on the riverland) of the grid <see cref="ServerMaps"/> built while preparing the map, so
    /// the tick thread never builds one; null only when no seat is a bot.
    /// </param>
    private BotDirector? SeatBots(Simulation sim, VisionTracker vision, MatchRules rules, MapDefinition map, BotSeating seating, NavGrid? grid)
    {
        if (!_seats.Any(s => s is { IsBot: true })) return null;
        var director = new BotDirector(sim, vision, rules, map, grid ?? throw new ArgumentNullException(nameof(grid), "Bots need the navigation grid."));
        DirectorMade?.Invoke(director);
        foreach (BattleSeat? seat in _seats)
            if (seat is { IsBot: true }) director.Add(rules.Of(seat.Slot).TankId, seating.BotSeed(seat.Slot));
        director.AssignRoles();
        return director;
    }

    /// <summary>
    /// "0+1 2+3 - 4 …": each duos team's seat slots joined by '+', teams 0–7 in order, "-" for a team nobody is on; for the
    /// server's log at the start (allocates: once a match).
    /// </summary>
    private string DescribeDuoTeams()
    {
        var teams = new string[ModeRules.DuoTeams];
        for (byte team = 0; team < teams.Length; team++)
        {
            IEnumerable<string> slots = _seats.Where(s => s is not null && s.Team == team).Select(s => s!.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
            teams[team] = string.Join('+', slots) is { Length: > 0 } joined ? joined : "-";
        }
        return string.Join(' ', teams);
    }

    /// <summary>"ring 1 (x, z) r 420, ring 2 …": rings 1–5 of a royale in whole metres, for the server's log.</summary>
    private static string DescribeRings(IReadOnlyList<ZoneRing> rings) =>
        string.Join(", ", Enumerable.Range(1, rings.Count - 1).Select(k => $"ring {k} {Describe(rings[k])}"));

    private static string Describe(ZoneRing ring) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({ring.Centre.X:F0}, {ring.Centre.Y:F0}) r {ring.Radius:F0}");

    /// <summary>Tests: called with each new director before its bots are added; throwing stands for bots that fail to start.</summary>
    internal Action<BotDirector>? DirectorMade { get; set; }

    /// <summary>
    /// Tests: draws a royale's rings in place of <see cref="ZonePicker.Pick(Battlefield, NavGrid, IReadOnlyList{SpawnPoint}, uint)"/>
    /// (same arguments): hand-built rings for a quick zone, or a throw (or rings the rules refuse) for a zone that fails to start.
    /// </summary>
    internal Func<Battlefield, NavGrid, IReadOnlyList<SpawnPoint>, uint, ZoneRing[]>? ZonePickerOverride { get; set; }

    private ZoneRing[] PickZone(Battlefield field, NavGrid grid, IReadOnlyList<SpawnPoint> spawns, uint seed) =>
        ZonePickerOverride is { } pick ? pick(field, grid, spawns, seed) : ZonePicker.Pick(field, grid, spawns, seed);

    /// <summary>
    /// A player who comes into the running match: a tank in it, at once — unless the rules have no spot for the seat on its team
    /// (<see cref="MatchRules.HasSpot"/>, which <see cref="MatchRules.Join"/> checks too: in duos a team with its two already in
    /// this match, those that have left included, or no team at all): then it waits in the room for the next, logged. The room
    /// seats a newcomer on a team it counts as having room (<see cref="DuoSeating.ForPerson"/>), and nothing frees a seat between
    /// the start and the start tick's step (the only tick duos takes joins), so that shouldn't happen; but Join would throw.
    /// </summary>
    private void PutIntoMatch(BattleSeat seat)
    {
        MatchRules rules = _rules!;
        if (!rules.HasSpot(seat.Slot, seat.Team))
        {
            _log(seat.Team >= ModeRules.DuoTeams
                ? $"slot {seat.Slot}: has no team in this match; waiting for the next"
                : $"slot {seat.Slot}: team {seat.Team} is full in this match; waiting for the next");
            return;
        }
        rules.Join(seat.Slot, TankCatalog.All[seat.TankIndex], seat.Team);
        MarkInMatch(seat);
    }

    private void MarkInMatch(BattleSeat seat)
    {
        seat.InMatch = true;
        if (!seat.IsBot && !ModeRules.Respawns(_mode)) _hadTank.Add(seat.Token);
    }

    /// <summary>
    /// Whether a player who isn't in the match may be put into it now: it takes joins, and — in a mode without respawns
    /// — the player's token hasn't had a tank in it yet. Leaving counts as going out there (<see cref="MatchRules.Leave"/>),
    /// so a player who comes back, eliminated or not, after the seat was freed (quit to the menu, or away past the
    /// reconnect grace) waits in the room for the next match instead of taking a fresh tank. One still within the grace
    /// period never gets here: the seat, still in the match, hands back the tank it has, wreck or not.
    /// </summary>
    private bool MayJoinMatch(BattleSeat seat) => _rules!.AcceptsJoins && !_hadTank.Contains(seat.Token);

    /// <summary>
    /// Back to the room: after the results, on the host's word, or with nobody left in the match. The bots' summary is
    /// logged here if the match didn't get to its end (it is logged with the end otherwise): once a match either way.
    /// </summary>
    public void EndMatch()
    {
        if (_sim is null) return;
        _log("match over, back to the room");
        if (_endedAtFrame < 0)
        {
            if (_director is not null) _log($"bots: {_director.Summary()}");
            if (_rules?.Crates is { } crates) LogCratesTaken(crates);
        }
        ForgetCrates();
        _sim = null;
        _vision = null;
        _rules = null;
        _director = null;
        _matchMap = null;
        _zoneSent = 0;
        _endedAtFrame = -1;
        _hadTank.Clear();
        RemoveBots();
        for (byte slot = 0; slot < _seats.Length; slot++)
        {
            BattleSeat? seat = _seats[slot];
            if (seat is null) continue;
            seat.InMatch = false;
            if (!seat.Connected) _seats[slot] = null;
            else seat.Ready = false;
        }
        _roomDirty = true;
    }

    private void RemoveBots()
    {
        for (int i = 0; i < _seats.Length; i++)
            if (_seats[i] is { IsBot: true }) _seats[i] = null;
    }

    private void TickMatch()
    {
        Simulation sim = _sim!;
        VisionTracker vision = _vision!;
        MatchRules rules = _rules!;
        int tick = sim.Tick + 1;

        // Inputs, one per tank in slot order: players from their buffers, bots from the director (brought up to date first).
        _director?.Update();
        ReadOnlySpan<TankState> tanks = sim.Tanks;
        for (int i = 0; i < tanks.Length; i++)
        {
            BattleSeat? seat = SeatOfTank(tanks[i].Id);
            _tickInputs[i] = seat is null ? BattleInputBuffer.Idle(tick, default)
                : seat.IsBot ? _director!.Command(tanks[i].Id, tick)
                : seat.Inputs.Take(tick);
        }
        sim.Step(_tickInputs.AsSpan(0, tanks.Length));
        vision.Update();
        CrateSet? crates = rules.Crates;
        if (crates is not null && sim.Tick != _crateSightTick && (_crateSightTick < 0 || sim.Tick % VisionTracker.UpdateInterval == 0)) RefreshCrateSight(sim, rules, crates);   // as VisionTracker: the first tick, then 5 Hz, once a tick
        rules.Update();
        AnnounceRings(rules);
        if (crates is not null) TellPickups(crates);

        CollectPropChanges(sim);
        foreach (BattleSeat seat in _byPeer.Values)
        {
            if (!seat.InMatch) continue;
            SendEvents(seat, sim, vision);
            SendSnapshot(seat, sim, vision, rules);
        }

        if (rules.Phase == MatchPhase.Ended)
        {
            if (_endedAtFrame < 0)
            {
                _endedAtFrame = _frame;
                _log($"match ended: {ModeRules.Id(_mode)}, {WinnerText(rules)}, {ReasonText(rules.EndReason)}");
                if (_director is not null) _log($"bots: {_director.Summary()}");
                if (crates is not null) LogCratesTaken(crates);
            }
            if (_frame - _endedAtFrame >= _options.ReturnToRoomSeconds * SimConstants.TickRate) EndMatch();
        }
        else if (!AnyHumanInMatch())   // in the match, connected or not: a dropped player has the grace period to come back
        {
            _log("nobody left in the match");
            EndMatch();
        }
    }

    /// <summary>
    /// Sends each ring whose waiting began this tick (stage 6 design §3.5), reliably, to every connected seat in the match, ahead
    /// of the tick's events and snapshot; a seat that comes back later has it in its match start. No allocation but the log line.
    /// </summary>
    private void AnnounceRings(MatchRules rules)
    {
        while (_zoneSent < rules.KnownRings)
        {
            byte ring = (byte)_zoneSent;
            ZoneRing circle = rules.ZoneRings[ring];
            _writer.Reset();
            BattleMessages.Write(_writer, new ZoneMessage(_matchNumber, ring, circle));
            foreach (BattleSeat seat in _byPeer.Values)
                if (seat.InMatch) Send(seat, _writer.Data, Channel.Reliable);
            _log($"zone ring {ring} announced: {Describe(circle)}");
            _zoneSent++;
        }
    }

    /// <summary>The winner for the log: "team N" (the two-team modes, duos), "slot N" (a free-for-all, a royale) or "draw".</summary>
    private static string WinnerText(MatchRules rules) =>
        rules.WinnerTeam != MatchRules.NoTeam && ModeRules.HasAllies(rules.Mode) ? $"team {rules.WinnerTeam}"
        : rules.WinnerSlot != MatchRules.NoSlot ? $"slot {rules.WinnerSlot}"
        : "draw";

    private static string ReasonText(MatchEndReason reason) => reason switch
    {
        MatchEndReason.TimeUp => "time up",
        MatchEndReason.Eliminated => "eliminated",
        MatchEndReason.Captured => "captured",
        _ => $"reason {(byte)reason}",
    };

    /// <summary>
    /// Whether a player is in the match: connected, or away within the reconnect grace period (the seat, still in the
    /// match, is held). One waiting in the room for the next match (turned away from this one) doesn't keep it going.
    /// </summary>
    private bool AnyHumanInMatch()
    {
        foreach (BattleSeat? seat in _seats)
            if (seat is { IsBot: false, InMatch: true }) return true;
        return false;
    }

    private BattleSeat? SeatOfTank(int tankId)
    {
        foreach (BattleSeat? seat in _seats)
            if (seat is { InMatch: true } && _rules!.Of(seat.Slot).TankId == tankId) return seat;
        return null;
    }

    /// <summary>
    /// What the seat may see (<see cref="Viewer"/>): through its team's eyes; once its tank is gone, everything in a mode
    /// without team-mates (a free-for-all, a royale), and in duos once nobody of its team is left alive (stage 6 design §5.2:
    /// till then the dead watch through a live team-mate's eyes, which the team's sight already is). No allocation.
    /// </summary>
    private Viewer ViewerOf(BattleSeat seat, Simulation sim)
    {
        MatchRules rules = _rules!;
        ref readonly Standing standing = ref rules.Of(seat.Slot);
        int slot = sim.SlotOf(standing.TankId);
        bool destroyed = slot < 0 || sim.Tanks[slot].IsDestroyed;
        bool seesAllWhenDead = !ModeRules.HasAllies(_mode) || (ModeRules.IsDuos(_mode) && rules.AliveCount(standing.Team) == 0);
        return new Viewer(standing.Team, standing.Active ? standing.TankId : 0, destroyed, seesAllWhenDead);
    }

    /// <summary>The hit points, quantised, of every prop this tick's events touched, each once.</summary>
    private void CollectPropChanges(Simulation sim)
    {
        _propChanges.Clear();
        foreach (ref readonly GameEvent e in sim.Events)
        {
            if (e.Type is not (GameEventType.PropDamaged or GameEventType.PropDestroyed)) continue;
            bool known = false;
            foreach (PropState p in _propChanges) known |= p.PropId == e.PropId;
            if (known) continue;
            Prop prop = sim.Field.PropById(e.PropId);
            _propChanges.Add(new PropState(e.PropId, BattleProtocol.QuantizeDurability(sim.Field.Durability(e.PropId), prop.MaxDurability)));
        }
    }

    private void SendEvents(BattleSeat seat, Simulation sim, VisionTracker vision)
    {
        if (sim.Events.Length == 0) return;
        Viewer viewer = ViewerOf(seat, sim);
        _sentEvents.Clear();
        foreach (ref readonly GameEvent e in sim.Events)
            if (viewer.Wants(e, sim, vision, out GameEvent sent)) _sentEvents.Add(sent);
        if (_sentEvents.Count == 0 && _propChanges.Count == 0) return;
        _writer.Reset();
        BattleMessages.Write(_writer, new EventBatch(sim.Tick, _sentEvents, _propChanges));
        Send(seat, _writer.Data, Channel.Reliable);
    }

    private void SendSnapshot(BattleSeat seat, Simulation sim, VisionTracker vision, MatchRules rules)
    {
        int tick = sim.Tick;
        BattleSnapshot snapshot = seat.History![tick % BattleSeat.HistorySize];
        Build(snapshot, seat, sim, vision, rules);
        int ack = seat.AckedSnapshotTick;
        BattleSnapshot? baseline = null;
        if (ack > 0 && ack < tick && tick - ack < BattleSeat.HistorySize && seat.History[ack % BattleSeat.HistorySize].Tick == ack)
            baseline = seat.History[ack % BattleSeat.HistorySize];
        var feedback = new ClientFeedback { LastProcessedInputTick = tick, InputSlack = seat.Inputs.SlackAt(tick) };
        _writer.Reset();
        _codec.Write(_writer, snapshot, baseline, feedback);
        Send(seat, _writer.Data, ChannelFor(_writer.ByteLength));
    }

    /// <summary>What <paramref name="seat"/> may know of tick <paramref name="sim"/>.Tick (design §5.5; the crates it sees, stage 6 design §4.4).</summary>
    private void Build(BattleSnapshot s, BattleSeat seat, Simulation sim, VisionTracker vision, MatchRules rules)
    {
        Viewer viewer = ViewerOf(seat, sim);
        s.Tick = sim.Tick;
        s.CrateCount = 0;
        if (rules.Crates is { } crates)
        {
            ulong seen = _crateVisible[seat.Slot];
            s.CrateCount = BuildCrateRows(s.Crates, seen, crates);
            _crateGone[seat.Slot] |= seen & ~crates.StandingMask;   // a row down tells the seat the crate is gone: a resync leaves it out from now on
        }
        int n = 0;
        s.HasOwn = false;
        foreach (ref readonly TankState t in sim.Tanks)
        {
            if (!viewer.Sees(t, vision)) continue;
            BattleSeat? owner = SeatOfTank(t.Id);
            s.Entities[n++] = ToEntity(t, owner?.Slot ?? MatchRules.NoSlot, owner?.TankIndex ?? 0, sim.Tick);
            if (t.Id == viewer.OwnTankId)
            {
                s.HasOwn = true;
                s.Own = ToOwn(t);
            }
        }
        s.EntityCount = n;
        s.Entities.AsSpan(0, n).Sort(ByIdComparison);   // a span sort with a cached delegate: Array.Sort with a comparer allocates one per call
        ref readonly Standing mine = ref rules.Of(seat.Slot);
        s.Match = new MatchBlock
        {
            Phase = rules.Phase,
            TicksLeft = rules.TicksLeft,
            Spotted = viewer.IsSpotted(sim, vision),
            WinnerTeam = rules.WinnerTeam,
            WinnerSlot = rules.WinnerSlot,
            RespawnIn = mine.RespawnAt >= 0 ? System.Math.Max(0, mine.RespawnAt - sim.Tick) : 0,
            TeamScore0 = rules.TeamScore(0),
            TeamScore1 = rules.TeamScore(1),
            // the bases' progress goes to everyone, seen or not (stage 5 design §3.3): standing in a base doesn't spot a tank
            BaseProgress0 = rules.Capture?.Progress(0) ?? 0,
            BaseProgress1 = rules.Capture?.Progress(1) ?? 0,
            EndReason = rules.EndReason,
            // the places only where they mean something ("共 N 名": a royale's seats, duos' teams); never past the seats, which the reader refuses
            Entrants = ModeRules.HasZone(rules.Mode) ? (byte)System.Math.Min(rules.Places, BattleProtocol.MaxPlayers) : (byte)0,
        };
        bool playing = rules.Phase == MatchPhase.Playing;
        for (byte slot = 0; slot < s.Players.Length; slot++)
        {
            ref readonly Standing p = ref rules.Of(slot);
            s.Players[slot] = p.Active ? new PlayerStats
            {
                Active = true, Team = p.Team, Spec = _seats[slot]?.TankIndex ?? 0, TankId = p.TankId,
                Kills = p.Kills, Deaths = p.Deaths, Alive = p.Alive, Rank = p.Rank, Capture = CaptureFor(viewer, p, slot, playing, rules),
                // an opponent's damage, like its capture, only once the match has ended: a climbing number would tell who is fighting;
                // the time lasted only for those out of a no-respawn match (everyone once it has ended), so a line doesn't change every
                // second: not a living player's, nor (a respawn mode, where it means nothing mid-match) one waiting to come back
                Damage = playing && p.Team != viewer.Team ? 0 : p.Damage,
                Lasted = !playing || (!p.Alive && !ModeRules.Respawns(rules.Mode)) ? rules.LastedTicks(slot) / SimConstants.TickRate : 0,
            } : default;   // an empty line, not the leaver's stale one (the wire drops inactive fields anyway)
        }
    }

    /// <summary>
    /// The capture the viewer may read on <paramref name="slot"/>'s line: its own and its team-mates' always, an enemy's
    /// only once the match has ended (0 till then). Standing in a base doesn't spot a tank (stage 5 design §3.3), and an
    /// enemy's capture ticking up would tell the defenders who is in it. The viewer's team is the seat's, as for the
    /// spotted lamp (<see cref="ViewerOf"/>), alive, a wreck or watching.
    /// </summary>
    private static byte CaptureFor(Viewer viewer, in Standing line, byte slot, bool playing, MatchRules rules) =>
        playing && line.Team != viewer.Team ? (byte)0 : rules.CaptureBest(slot);

    private static readonly Comparison<BattleEntity> ByIdComparison = static (a, b) => a.Id.CompareTo(b.Id);

    /// <param name="tick">The snapshot's tick: protection is a window, so the flag needs the time (<see cref="Simulation.IsProtected"/>).</param>
    public static BattleEntity ToEntity(in TankState t, byte slot, byte spec, int tick) => new()
    {
        Id = t.Id,
        Slot = slot,
        Spec = spec,
        Team = t.Team,
        X = BattleProtocol.ToCm(t.Position.X),
        Z = BattleProtocol.ToCm(t.Position.Y),
        Yaw = (ushort)BattleProtocol.QuantizeAngle(t.Yaw, BattleProtocol.YawBits),
        TurretYaw = (ushort)BattleProtocol.QuantizeAngle(t.TurretYaw, BattleProtocol.YawBits),
        Pitch = (ushort)BattleProtocol.QuantizePitch(t.GunPitch, BattleProtocol.PitchBits),
        Speed = (short)MathF.Round(t.Speed / BattleProtocol.SpeedUnit),
        Health = (ushort)System.Math.Clamp(t.Health, 0, ushort.MaxValue),
        Flags = (t.IsDestroyed ? TankFlags.Destroyed : TankFlags.None) | (tick < t.ProtectedUntil ? TankFlags.Protected : TankFlags.None),
    };

    public static OwnTank ToOwn(in TankState t) => new()
    {
        Id = t.Id, X = t.Position.X, Z = t.Position.Y, Yaw = t.Yaw, TurretYaw = t.TurretYaw, GunPitch = t.GunPitch, Speed = t.Speed,
        Reload = t.Reload, Bloom = t.Bloom, Ammo = t.Ammo, ApLeft = t.ApLeft, HeLeft = t.HeLeft, ShotCount = t.ShotCount, Health = t.Health,
        ProtectedUntil = t.ProtectedUntil, LastFired = t.LastFired, Destroyed = t.IsDestroyed,
        ArmorLayers = t.ArmorLayers, ReloadLayers = t.ReloadLayers, OpticsLayers = t.OpticsLayers,
    };

    /// <summary>
    /// The match as it stands (with the bases in a standard battle, the known rings and the crates the seat believes still stand in a
    /// royale), for a seat that joins, rejoins or resyncs: with the map's prepared bytes copied in (not encoded again), or without
    /// the map for a connection that has it (<paramref name="withMap"/> false). Allocates: a start or a resync, not a tick.
    /// </summary>
    private void SendMatchStart(BattleSeat seat, bool withMap)
    {
        Simulation sim = _sim!;
        PreparedMap map = _matchMap!;
        var props = new List<PropState>();
        foreach (Prop p in sim.Field.Props)
        {
            if (!p.IsDestructible) continue;
            int left = sim.Field.Durability(p.Id);
            if (left != p.MaxDurability) props.Add(new PropState(p.Id, BattleProtocol.QuantizeDurability(left, p.MaxDurability)));
        }
        // the bases only where they count: a client draws the circles of a standard battle alone
        IReadOnlyList<TeamBase>? bases = ModeRules.HasBases(_mode) ? map.Definition.Bases : null;
        // a royale's zone: the start tick and the rings known so far, ring 0 first (the later ones follow as ZoneMessages)
        ZoneSync? zone = _rules!.KnownRings > 0 ? new ZoneSync(_rules.StartTick, _rules.AnnouncedRings.ToArray()) : null;
        // a royale's crates: the ones this seat has seen and not been told are gone (a fresh seat has seen none; the other modes have none)
        List<Crate>? crates = _rules.Crates is { } set ? KnownStanding(seat.Slot, set) : null;
        var start = new MatchStartMessage(_mode, _matchNumber, sim.Tick, _rules.EndTick, withMap ? map.Data : null, props, TankOf(seat.Slot), bases, zone, crates);
        _mapWriter.Reset();
        if (withMap) BattleMessages.Write(_mapWriter, start, map.EncodedMap);
        else BattleMessages.Write(_mapWriter, start);
        Send(seat, _mapWriter.Data, Channel.Reliable);
        if (withMap && seat.Connected) seat.MapSentForMatch = _matchNumber;
    }
}

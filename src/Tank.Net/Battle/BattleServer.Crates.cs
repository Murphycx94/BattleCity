using System.Diagnostics;
using System.Numerics;
using Tank.Net.Protocol;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Navigation;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Net.Battle;

/// <summary>
/// The crates of a match (stage 6 design §4.4; 6B task 5): laid from the match seed when a crate mode starts (<see cref="PlaceCrates"/>),
/// each human seat's crates in sight worked out after the vision update on the ticks it works (<see cref="RefreshCrateSight"/>) and sent
/// as its snapshot's rows (<see cref="BuildCrateRows"/>), a pickup told to the one who made it (<see cref="TellPickups"/>), the crates a
/// seat knows and hasn't been told are gone handed back on a resync (<see cref="KnownStanding"/>), and the match's crate lines in the log. The table
/// itself never goes on the wire; the bots read the set directly. See BattleServer.Match.cs for where the tick calls these.
/// </summary>
public sealed partial class BattleServer
{
    /// <summary>
    /// In a crate mode (stage 6 design §4.4), per seat slot: bit <c>id</c> set for each crate the seat's tank (in duos, its team's live
    /// tanks) saw at the last sight pass (<see cref="RefreshCrateSight"/>; its snapshot's rows), for each it has seen at any pass this match, and for each it has
    /// been told is gone: a row of it down in a snapshot built for the seat (<see cref="Build"/>), or its own pickup (<see cref="TellPickups"/>).
    /// A resync hands back the known ones not told gone (<see cref="KnownStanding"/>): what the client believes still stands, so a
    /// crate eaten out of the seat's sight isn't given away by the resync either. Cleared when a match starts and ends, and with a
    /// seat freed — not on a reconnect within the grace period, which keeps what the seat knows. Bots' slots stay 0: they read the
    /// set itself.
    /// </summary>
    private readonly ulong[] _crateVisible = new ulong[BattleProtocol.MaxPlayers];
    private readonly ulong[] _crateKnown = new ulong[BattleProtocol.MaxPlayers];
    private readonly ulong[] _crateGone = new ulong[BattleProtocol.MaxPlayers];
    /// <summary>Duos, during a sight pass: each team's crates in sight, its live tanks' looks or'd together (valid where <see cref="_teamCrateSightDone"/> has the team's bit).</summary>
    private readonly ulong[] _teamCrateSight = new ulong[ModeRules.DuoTeams];
    private uint _teamCrateSightDone;   // duos, during a sight pass: bit per team whose sight is worked out already
    private int _crateSightTick = -1;   // the tick of the last sight pass (none again on that tick); -1 before the first of the match
    private int _crateSightPasses;      // the sight passes made since this server was made
    private long _crateSightTicks;      // Stopwatch ticks the passes took, in all

    /// <summary>
    /// Tests: lays a crate mode's crates in place of <see cref="CratePlacer.Place(Battlefield, NavGrid, IReadOnlyList{SpawnPoint}, uint)"/>
    /// (same arguments): a hand-laid few, or a throw (or a table the set refuses) for crates that fail to start.
    /// </summary>
    internal Func<Battlefield, NavGrid, IReadOnlyList<SpawnPoint>, uint, Crate[]>? CratePlacerOverride { get; set; }

    /// <summary>
    /// A crate mode's crates for the match being built (stage 6 design §4.1): <see cref="CratePlacer.Place(Battlefield, NavGrid, IReadOnlyList{SpawnPoint}, uint)"/>
    /// from the match seed, or the tests' <see cref="CratePlacerOverride"/>; checked as a <see cref="CrateSet"/> here, and each
    /// position against the snapshot's bounds (its rows carry unsigned centimetres up to <see cref="BattleProtocol.MaxCoordinateCm"/>;
    /// the placer's <see cref="CratePlacer.EdgeMargin"/> keeps it there, an override need not), so a table the rules or the codec
    /// would refuse fails as the crates', now, not as the zone's or in the first snapshot. Fewer than <see cref="CratePlacer.Count"/>
    /// is the placer running out of places, logged once the match has started (<see cref="LogCratesPlaced"/>).
    /// </summary>
    private Crate[] PlaceCrates(Battlefield field, NavGrid grid, IReadOnlyList<SpawnPoint> spawns, uint seed)
    {
        Crate[] crates = CratePlacerOverride is { } place ? place(field, grid, spawns, seed) : CratePlacer.Place(field, grid, spawns, seed);
        _ = new CrateSet(crates);
        foreach (ref readonly Crate c in crates.AsSpan())
        {
            int x = BattleProtocol.ToCm(c.Position.X), z = BattleProtocol.ToCm(c.Position.Y);
            if (x < 0 || x > BattleProtocol.MaxCoordinateCm || z < 0 || z > BattleProtocol.MaxCoordinateCm)
                throw new ArgumentException($"Crate {c.Id} at {c.Position} lies outside the snapshot's 0–{BattleProtocol.MaxCoordinateCm} cm on either axis.", nameof(crates));
        }
        return crates;
    }

    /// <summary>"crates: 48 placed (29 by houses)", and a warning when the placer found fewer places than <see cref="CratePlacer.Count"/>.</summary>
    private void LogCratesPlaced(CrateSet crates, Battlefield field, string mapId)
    {
        int byHouses = 0;
        foreach (Crate c in crates.All)
            if (CratePlacer.ByHouse(field, c.Position)) byHouses++;
        _log($"crates: {crates.Count} placed ({byHouses} by houses)");
        if (crates.Count < CratePlacer.Count) _log($"warning: only {crates.Count} of {CratePlacer.Count} crates found a place on {mapId}");
    }

    /// <summary>
    /// "crates: 20 of 48 taken, 3 by players, 17 by bots" at the match's end (and, if it didn't get there, on the way back to the
    /// room, beside the bots' summary): the whole match's pickups for the log.
    /// </summary>
    private void LogCratesTaken(CrateSet crates)
    {
        int taken = crates.Count - crates.StandingCount, byBots = 0;
        for (int id = 0; id < crates.Count; id++)
        {
            int tank = crates.TakenBy(id);
            if (tank != 0 && _director?.Find(tank) is not null) byBots++;
        }
        _log($"crates: {taken} of {crates.Count} taken, {taken - byBots} by players, {byBots} by bots");
    }

    /// <summary>Every seat's crates in sight, known and told gone, and the sight pass's clock, as before a match.</summary>
    private void ForgetCrates()
    {
        Array.Clear(_crateVisible);
        Array.Clear(_crateKnown);
        Array.Clear(_crateGone);
        _crateSightTick = -1;
    }

    /// <summary>
    /// What each human seat in the match sees of the crates right now (stage 6 design §4.4): with a live tank, <see cref="CrateSet.VisibleTo"/>
    /// from it (a wreck sees none) — in duos (§5.2) its team's, every live tank of the team looking, a bot team-mate's too, the
    /// seat's own tank dead or alive (worked out once a team a pass: <see cref="TeamCrateSight"/>) — and that is added to what the
    /// seat knows; without a tank, nothing in sight (what it knows stays). Called after the vision update on the ticks it works, the
    /// match's first and then every <see cref="VisionTracker.UpdateInterval"/>-th (<see cref="_crateSightTick"/> keeps it to once a
    /// tick, as the tracker does): a seat's snapshot rows between passes are the last pass's crates, standing as of each tick. Bots'
    /// slots are left alone. No allocation.
    /// </summary>
    private void RefreshCrateSight(Simulation sim, MatchRules rules, CrateSet crates)
    {
        long started = Stopwatch.GetTimestamp();
        bool duos = ModeRules.IsDuos(rules.Mode);
        _teamCrateSightDone = 0;
        foreach (BattleSeat? seat in _seats)
        {
            if (seat is not { IsBot: false, InMatch: true }) continue;
            ref readonly Standing standing = ref rules.Of(seat.Slot);
            ulong seen;
            if (!standing.Active) seen = 0;
            else if (duos) seen = TeamCrateSight(sim, crates, standing.Team);
            else
            {
                int slot = sim.SlotOf(standing.TankId);
                seen = slot < 0 ? 0 : crates.VisibleTo(sim.Tanks[slot], sim.Field);
            }
            _crateVisible[seat.Slot] = seen;
            _crateKnown[seat.Slot] |= seen;
        }
        _crateSightTick = sim.Tick;
        _crateSightPasses++;
        _crateSightTicks += Stopwatch.GetTimestamp() - started;
    }

    /// <summary>
    /// Duos: the crates <paramref name="team"/> sees in this sight pass, every live tank of it looking (<see cref="CrateSet.VisibleTo"/>,
    /// or'd); worked out on the first ask of the pass and kept in <see cref="_teamCrateSight"/> for the team-mate's. No allocation.
    /// </summary>
    private ulong TeamCrateSight(Simulation sim, CrateSet crates, byte team)
    {
        if (team >= _teamCrateSight.Length) return 0;
        uint bit = 1u << team;
        if ((_teamCrateSightDone & bit) != 0) return _teamCrateSight[team];
        ulong seen = 0;
        foreach (ref readonly TankState t in sim.Tanks)
            if (t.Team == team && !t.IsDestroyed) seen |= crates.VisibleTo(t, sim.Field);
        _teamCrateSight[team] = seen;
        _teamCrateSightDone |= bit;
        return seen;
    }

    /// <summary>
    /// This tick's pickups (<see cref="CrateSet.Pickups"/>, collected by the rules' update), each told to the one player who made it
    /// (stage 6 design §4.4) as a reliable <see cref="CrateMessage"/>: which crate, where it stood (to the centimetre, as its row had
    /// it: <see cref="OnTheWire"/>), what it did. Nobody else is told — the others see the empty spot through their own rows, and only
    /// when they see the spot — and a bot's pickup goes unsaid (the bots read the set). A player away within the reconnect grace isn't
    /// told either: the send to an unconnected seat is dropped, the effect stands all the same (the layers come back through
    /// <see cref="OwnTank"/>), and the resync it comes back to simply leaves the eaten crate out: the pickup counts as told gone,
    /// sent or not (<see cref="_crateGone"/>). No allocation: the message is a struct, written into the server's writer.
    /// </summary>
    private void TellPickups(CrateSet crates)
    {
        foreach (ref readonly CratePickup pickup in crates.Pickups)
        {
            BattleSeat? seat = SeatOfTank(pickup.TankId);
            if (seat is not { IsBot: false, InMatch: true }) continue;
            _crateGone[seat.Slot] |= 1ul << pickup.CrateId;
            Crate crate = crates[pickup.CrateId];
            _writer.Reset();
            BattleMessages.Write(_writer, new CrateMessage(_matchNumber, pickup.CrateId, pickup.Take.Kind, OnTheWire(crate.Position), pickup.Take.Amount, pickup.Take.Amount2, pickup.Take.Layers));
            Send(seat, _writer.Data, Channel.Reliable);
        }
    }

    /// <summary>
    /// The rows of a seat's snapshot: one per bit of <paramref name="seen"/> (its crates in sight as of the last sight pass), ids
    /// rising, each with the crate's kind and position and whether it stands <em>now</em> — so a seat that sees a spot sees the
    /// crate go the tick it is eaten. Returns the row count. No allocation.
    /// </summary>
    private static int BuildCrateRows(CrateView[] rows, ulong seen, CrateSet crates)
    {
        int n = 0;
        for (ulong rest = seen; rest != 0; rest &= rest - 1)
        {
            int id = BitOperations.TrailingZeroCount(rest);
            ref readonly Crate c = ref crates.All[id];
            rows[n++] = new CrateView { Id = (byte)id, Kind = c.Kind, Standing = crates.IsStanding(id), X = BattleProtocol.ToCm(c.Position.X), Z = BattleProtocol.ToCm(c.Position.Y) };
        }
        return n;
    }

    /// <summary>
    /// The crates seat <paramref name="slot"/> has seen this match and not been told are gone (<see cref="_crateGone"/>), ids rising,
    /// each where its rows put it: what a resync hands back (stage 6 design §4.4) — what the client believes still stands. Not "seen
    /// and still standing": that would tell a seat on every resync of a crate eaten where it couldn't see, and a client may ask once
    /// a second (plan deviation 45). Allocates: a start or a resync, not a tick.
    /// </summary>
    private List<Crate> KnownStanding(byte slot, CrateSet crates)
    {
        var known = new List<Crate>();
        for (ulong rest = _crateKnown[slot] & ~_crateGone[slot]; rest != 0; rest &= rest - 1)
        {
            Crate crate = crates[BitOperations.TrailingZeroCount(rest)];
            known.Add(crate with { Position = OnTheWire(crate.Position) });
        }
        return known;
    }

    /// <summary>
    /// A crate's position as the client has it from its snapshot rows (<see cref="BuildCrateRows"/>): to the centimetre. The match
    /// start and the <see cref="CrateMessage"/> carry floats and could say the table's exact spot, but then a resync's crates would
    /// move under half a centimetre with the next rows and the client's knowledge bump its version for nothing; so every path
    /// says the same.
    /// </summary>
    private static Vector2 OnTheWire(Vector2 position) =>
        new(BattleProtocol.FromCm(BattleProtocol.ToCm(position.X)), BattleProtocol.FromCm(BattleProtocol.ToCm(position.Y)));

    /// <summary>Tests: the crates seat <paramref name="slot"/>'s tank (in duos, its team's) saw at the last sight pass, bit per id (its snapshot's rows); 0 outside a crate match, for a bot, a wreck (in duos, a team with none alive).</summary>
    internal ulong CrateVisibleOf(byte slot) => _crateVisible[slot];

    /// <summary>Tests: every crate seat <paramref name="slot"/> has seen this match, bit per id.</summary>
    internal ulong CrateKnownOf(byte slot) => _crateKnown[slot];

    /// <summary>Tests: every crate seat <paramref name="slot"/> has been told is gone this match (a row of it down, or its own pickup), bit per id.</summary>
    internal ulong CrateGoneOf(byte slot) => _crateGone[slot];

    /// <summary>Tests and the load test: the crate sight passes made since this server was made (<see cref="RefreshCrateSight"/>), every match's.</summary>
    internal int CrateSightPasses => _crateSightPasses;

    /// <summary>Tests and the load test: the time those passes took in all, the human seats' looks summed (a pass with none alive costs next to nothing).</summary>
    internal TimeSpan CrateSightTime => System.Diagnostics.Stopwatch.GetElapsedTime(0, _crateSightTicks);
}

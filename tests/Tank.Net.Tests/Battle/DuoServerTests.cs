using System.Numerics;
using Tank.Net.Battle;
using Tank.Net.Protocol;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;
using Tank.Testing;
using Xunit.Abstractions;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// The server's duos match (stage 6 design §5.2–§5.3; 6C task 4): team-mates see each other always and share what they spot, a
/// dead player watches through a live team-mate's eyes and sees everything only once the whole team is out, a team-mate's
/// crates in sight (a bot's too) go into the player's rows while a pickup is still told to the eater alone, the match block
/// carries the teams as the places, the end names the winning team and gives both of a team one rank, the start logs the teams,
/// a tick allocates nothing, and a seat for a team already full in the match never reaches <see cref="MatchRules.Join"/>.
/// Mostly people on <see cref="TestMaps.DuoStandIn"/>, put where each test needs them (the team points are the seed's): A and
/// B (team 0) west of the wall, C (team 2) and D (team 3) east of it, which the wall hides from the west.
/// </summary>
public sealed class DuoServerTests
{
    private const double FrameDt = 1.0 / 120;
    private const byte Ahead = TestMaps.CrateAhead, Behind = TestMaps.CrateBehind, East = TestMaps.CrateEast;
    private const ulong EastBit = 1ul << East, WestBits = 1ul << Ahead | 1ul << Behind;

    private static readonly Vector2 WestA = new(40, 100), WestB = new(40, 110), EastC = new(170, 100), EastD = new(170, 140);

    private readonly ITestOutputHelper _output;

    public DuoServerTests(ITestOutputHelper output) => _output = output;

    /// <summary>A duos room on the stand-in, its crates <see cref="TestMaps.StandInCrates"/> and its zone wide enough to hurt no one.</summary>
    private static BattleHarness StandIn(List<string> log)
    {
        MapDefinition map = TestMaps.DuoStandIn();
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Mode = BattleMode.Duos, Map = map, Maps = new[] { map } }, log: log.Add);
        h.Server.CratePlacerOverride = TestMaps.StandInCratePlacer;
        h.Server.ZonePickerOverride = TestMaps.StandInWideZone;
        return h;
    }

    private static void SetTeam(BattleHarness h, int client, string team) => DuoRoomTests.SetTeam(h, client, team);

    /// <summary>Puts <paramref name="client"/>'s tank at <paramref name="at"/> facing <paramref name="facing"/> on the server.</summary>
    private static void Place(BattleHarness h, BattleClient client, Vector2 at, Vector2 facing) =>
        h.Server.Simulation!.Respawn(h.Server.TankOf(client.Slot), at, Angles.YawOf(facing));

    /// <summary>
    /// Four people in a duos match on the stand-in: A and B on team 0, C on team 2, D on team 3 (newcomers take the empty teams,
    /// then B moves to A's); put A and B west of the wall, C and D east of it, and run a second so everyone has looked.
    /// </summary>
    private static (BattleHarness H, BattleClient A, BattleClient B, BattleClient C, BattleClient D) Four(List<string> log)
    {
        BattleHarness h = StandIn(log);
        for (int i = 0; i < 4; i++)
        {
            h.AddClient($"P{i}");
            h.Run(0.1);   // one at a time: slots and teams in order
        }
        h.Run(0.3);
        SetTeam(h, 1, "0");
        h.StartMatch(BattleMode.Duos);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        var (a, b, c, d) = (h.Clients[0], h.Clients[1], h.Clients[2], h.Clients[3]);
        MatchRules rules = h.Server.Rules!;
        Assert.Equal(new byte[] { 0, 0, 2, 3 }, new[] { a, b, c, d }.Select(x => rules.Of(x.Slot).Team));
        Place(h, a, WestA, Vector2.UnitX);
        Place(h, b, WestB, Vector2.UnitX);
        Place(h, c, EastC, -Vector2.UnitX);
        Place(h, d, EastD, -Vector2.UnitX);
        h.Run(1.0);
        return (h, a, b, c, d);
    }

    private static HashSet<int> Ids(BattleClient c) => c.Latest!.Entities.Take(c.Latest.EntityCount).Select(e => e.Id).ToHashSet();

    private static int Tank(BattleHarness h, BattleClient c) => h.Server.TankOf(c.Slot);

    /// <summary>
    /// Has <paramref name="killer"/> fire from 25 m in front of <paramref name="victim"/> until the server has the victim destroyed
    /// (<see cref="RoyaleServerTests.Destroy"/>), then puts the killer back where it stood. (The zone's bite from a test would
    /// destroy the hull, but its event is gone before the rules' next update: the rules would never hear of it.)
    /// </summary>
    private static void Kill(BattleHarness h, BattleClient killer, BattleClient victim)
    {
        TankState k = h.ServerTankOf(killer), v = h.ServerTankOf(victim);
        Vector2 ahead = Angles.Forward(v.Yaw);
        Place(h, killer, v.Position + ahead * 25f, -ahead);
        RoyaleServerTests.Destroy(h, killer, victim);
        h.Server.Simulation!.Respawn(Tank(h, killer), k.Position, k.Yaw);
    }

    private static ulong RowsOf(BattleSnapshot s)
    {
        ulong rows = 0;
        foreach (ref readonly CrateView row in s.CrateSpan) rows |= 1ul << row.Id;
        return rows;
    }

    private static void AssertClean(BattleHarness h)
    {
        foreach (BattleClient c in h.Clients)
        {
            Assert.Null(c.LastPacketError);
            Assert.Equal(0, c.Resyncs);
        }
    }

    // ---- tanks in sight ----

    [Fact]
    public void Team_mates_always_see_each_other_and_share_what_either_spots()
    {
        var log = new List<string>();
        var (h, a, b, c, d) = Four(log);
        int ta = Tank(h, a), tb = Tank(h, b), tc = Tank(h, c);

        Assert.Equal(new HashSet<int> { ta, tb }, Ids(a));   // the wall hides C and D from both
        Assert.Equal(new HashSet<int> { ta, tb }, Ids(b));
        Assert.Contains(tc, Ids(c));   // nor do C and D see A or B
        Assert.DoesNotContain(ta, Ids(c));
        Assert.DoesNotContain(tb, Ids(d));

        Place(h, b, new Vector2(160, 100), Vector2.UnitX);   // B round the wall, 10 m from C
        h.Run(0.5);

        Simulation sim = h.Server.Simulation!;
        Assert.True(h.Server.Vision!.IsVisible(tc, 0), "B doesn't see C from 10 m");
        Assert.False(Spotting.Sees(sim.Tanks[sim.SlotOf(ta)], sim.Tanks[sim.SlotOf(tc)], sim.Field, sim.Tick, false), "A sees C through the wall");
        Assert.Contains(tc, Ids(b));
        Assert.Contains(tc, Ids(a));   // what B spots, A is told of
        Assert.Contains(tb, Ids(a));   // and a team-mate across the map stays in view
        Assert.Contains(ta, Ids(b));
        AssertClean(h);
    }

    [Fact]
    public void A_dead_player_watches_through_a_live_team_mate_and_sees_everything_once_the_team_is_out()
    {
        var log = new List<string>();
        var (h, a, b, c, d) = Four(log);
        int ta = Tank(h, a), tb = Tank(h, b), tc = Tank(h, c), td = Tank(h, d);

        Kill(h, c, a);
        h.Run(2.5);   // C, seen while it fired from the west, back east and out of team 0's sight once the linger is over
        Assert.False(h.Server.Rules!.Of(a.Slot).Alive);
        Assert.Equal(new HashSet<int> { ta, tb }, Ids(a));   // what team 0 sees: B's eyes, not the whole field

        Kill(h, d, b);
        h.Run(0.5);
        Assert.Equal(MatchPhase.Playing, h.Server.Rules.Phase);   // teams 2 and 3 are still in
        Assert.Equal(0, h.Server.Rules.AliveCount(0));
        foreach (BattleClient watcher in new[] { a, b })
            Assert.Equal(new HashSet<int> { ta, tb, tc, td }, Ids(watcher));   // the whole team out: every tank
        AssertClean(h);
    }

    [Fact]
    public void In_a_royale_the_dead_still_watch_every_tank_at_once()
    {
        var log = new List<string>();
        MapDefinition map = TestMaps.RoyaleStandIn();
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Mode = BattleMode.Royale, Map = map, Maps = new[] { map } }, log: log.Add);
        h.Server.ZonePickerOverride = TestMaps.StandInWideZone;
        for (int i = 0; i < 3; i++) h.AddClient($"P{i}");
        h.StartMatch(BattleMode.Royale);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        BattleClient west = h.Clients.MinBy(c => h.ServerTankOf(c).Position.X)!, east = h.Clients.MaxBy(c => h.ServerTankOf(c).Position.X)!;
        Assert.DoesNotContain(Tank(h, east), Ids(west));   // the wall hides it

        BattleClient middle = h.Clients.Single(c => c != west && c != east);
        Kill(h, middle, west);
        h.Run(0.5);

        Assert.Equal(MatchPhase.Playing, h.Server.Rules!.Phase);
        Assert.Equal(h.Clients.Select(c => Tank(h, c)).ToHashSet(), Ids(west));
    }

    /// <summary>
    /// <see cref="Four"/> with A destroyed by C and B, A's team-mate, gone from the network but still within its reconnect grace
    /// (the stand-in's default 30 s): its tank stays in the match, alive.
    /// </summary>
    private static (BattleHarness H, BattleClient A, BattleClient B) DeadWithMateInGrace(List<string> log)
    {
        var (h, a, b, c, _) = Four(log);
        Kill(h, c, a);
        h.Run(2.5);   // C back east and out of team 0's sight
        h.Links[h.Clients.IndexOf(b)].Dispose();
        h.Run(0.5);
        Assert.Contains(log, l => l.StartsWith($"slot {b.Slot} (", StringComparison.Ordinal) && l.Contains("disconnected", StringComparison.Ordinal));
        Assert.False(h.Server.Rules!.Of(a.Slot).Alive);
        Assert.True(h.Server.Rules.Of(b.Slot) is { Active: true, Alive: true });
        return (h, a, b);
    }

    [Fact]
    public void A_dead_player_sees_everything_once_its_team_mate_leaves_the_match()
    {
        var log = new List<string>();
        var (h, a, b) = DeadWithMateInGrace(log);
        int ta = Tank(h, a), tb = Tank(h, b);
        int[] all = h.Server.Simulation!.Tanks.ToArray().Select(t => t.Id).ToArray();

        Assert.Equal(1, h.Server.Rules!.AliveCount(0));
        Assert.Equal(new HashSet<int> { ta, tb }, Ids(a));   // still through B's eyes while B's seat waits

        h.Server.FreeSeatForTests(b.Slot);   // as the grace running out does: B leaves the match
        Assert.Equal(0, h.Server.Rules.AliveCount(0));
        h.Run(0.2);

        Assert.Equal(MatchPhase.Playing, h.Server.Rules.Phase);   // teams 2 and 3 are still in
        Assert.Equal(all.Where(id => id != tb).ToHashSet(), Ids(a));   // every tank left
        Assert.Equal(3, Ids(a).Count);
        Assert.Null(a.LastPacketError);
    }

    [Fact]
    public void A_dead_player_whose_team_mate_leaves_sees_no_crates_but_keeps_what_it_knew()
    {
        var log = new List<string>();
        var (h, a, b) = DeadWithMateInGrace(log);
        Assert.NotEqual(0ul, h.Server.CrateVisibleOf(a.Slot));   // B's look, while B's seat waits
        ulong known = h.Server.CrateKnownOf(a.Slot);
        Assert.NotEqual(0ul, known);

        h.Server.FreeSeatForTests(b.Slot);
        int passes = h.Server.CrateSightPasses;
        for (int f = 0; f < 120 && h.Server.CrateSightPasses == passes; f++) h.Run(FrameDt);
        Assert.NotEqual(passes, h.Server.CrateSightPasses);

        Assert.Equal(0ul, h.Server.CrateVisibleOf(a.Slot));
        Assert.Equal(known, h.Server.CrateKnownOf(a.Slot));
        Assert.Null(a.LastPacketError);
    }

    // ---- crates in sight ----

    [Fact]
    public void A_team_mates_crates_reach_the_whole_team_and_a_pickup_is_told_to_the_eater_alone()
    {
        var log = new List<string>();
        var (h, a, b, c, d) = Four(log);
        CrateSet crates = h.Server.Rules!.Crates!;

        // only the eastern team sees the eastern crate: nobody on team 0 knows it
        Assert.Equal(WestBits, h.Server.CrateVisibleOf(a.Slot));
        Assert.Equal(WestBits, RowsOf(a.Latest!));
        Assert.Equal(0ul, h.Server.CrateKnownOf(a.Slot) & EastBit);
        Assert.Equal(0ul, h.Server.CrateKnownOf(b.Slot) & EastBit);
        Assert.Equal(EastBit, h.Server.CrateVisibleOf(c.Slot));
        Assert.Equal(EastBit, RowsOf(d.Latest!));

        Place(h, b, new Vector2(160, 122), Vector2.UnitY);   // B round the wall, 8 m short of the eastern crate and facing it
        h.Run(0.5);

        Simulation sim = h.Server.Simulation!;
        Assert.Equal(0ul, crates.VisibleTo(h.ServerTankOf(a), sim.Field) & EastBit);   // A's own look still doesn't reach it
        Assert.Equal(WestBits | EastBit, h.Server.CrateVisibleOf(a.Slot));          // A's and B's looks together
        Assert.Equal(WestBits | EastBit, RowsOf(a.Latest!));
        Assert.Equal(EastBit, h.Server.CrateKnownOf(a.Slot) & EastBit);
        Assert.True(a.Crates.All[East] is { Known: true, Visible: true, Standing: true });

        int b0 = Tank(h, b);
        float yaw = h.ServerTankOf(b).Yaw;
        int index = h.Clients.IndexOf(b);
        for (int f = 0; f < 10 * 120 && crates.IsStanding(East); f++)
            h.Run(FrameDt, (i, _) => i == index ? new LocalCommand(1, 0, yaw, 0f, false) : LocalCommand.None, FrameDt);
        Assert.False(crates.IsStanding(East), "B never got to the crate");
        Assert.Equal(b0, crates.TakenBy(East));
        h.Run(0.3);

        Assert.True(b.TryDequeuePickup(out CrateMessage pickup), "the eater wasn't told");
        Assert.Equal(East, pickup.CrateId);
        foreach (BattleClient other in new[] { a, c, d })
            Assert.False(other.TryDequeuePickup(out CrateMessage stray), $"slot {other.Slot} was told of B's pickup: {stray}");
        CrateView row = a.Latest!.CrateSpan.ToArray().Single(r => r.Id == East);
        Assert.False(row.Standing);   // A sees the spot through B's eyes, empty now
        Assert.True(a.Crates.All[East] is { Known: true, Standing: false });
        Assert.Equal(EastBit, h.Server.CrateGoneOf(a.Slot) & EastBit);
        AssertClean(h);
    }

    [Fact]
    public void A_bot_team_mates_crates_reach_the_players_rows()
    {
        var log = new List<string>();
        BattleHarness h = StandIn(log);
        BattleClient player = h.AddClient("A");
        h.Run(0.1);
        BattleClient killer = h.AddClient("K");   // team 1
        h.StartMatch(BattleMode.Duos, bots: 3);   // the bots partner the player, then K, then take team 2
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        MatchRules rules = h.Server.Rules!;
        byte mateSlot = (byte)Enumerable.Range(0, BattleProtocol.MaxPlayers).Single(s => s != player.Slot && rules.Of((byte)s).Active && rules.Of((byte)s).Team == rules.Of(player.Slot).Team);
        int mate = rules.Of(mateSlot).TankId;
        Assert.NotNull(h.Server.Director!.Find(mate));

        Kill(h, killer, player);   // a wreck looks at nothing: every row from here on is the bot's
        h.Run(0.2);
        int passes = h.Server.CrateSightPasses, checkedPasses = 0, seeing = 0;
        h.AfterServerTick = server =>
        {
            if (server.CrateSightPasses == passes || server.Simulation is not { } sim) return;
            passes = server.CrateSightPasses;
            int slot = sim.SlotOf(mate);
            ulong expected = slot < 0 ? 0 : server.Rules!.Crates!.VisibleTo(sim.Tanks[slot], sim.Field);
            Assert.Equal(expected, server.CrateVisibleOf(player.Slot));
            checkedPasses++;
            if (expected != 0) seeing++;
        };
        h.Run(3.0);
        h.AfterServerTick = null;

        Assert.True(checkedPasses >= 3 * SimConstants.TickRate / VisionTracker.UpdateInterval * 0.9, $"only {checkedPasses} sight passes checked");
        Assert.True(seeing > 0, "the bot never saw a crate");
        Assert.NotEqual(0ul, RowsOf(player.Latest!) | h.Server.CrateKnownOf(player.Slot));
        Assert.Equal(h.Server.CrateKnownOf(player.Slot), h.Server.CrateKnownOf(player.Slot) | h.Server.CrateVisibleOf(player.Slot));
        _output.WriteLine($"{checkedPasses} sight passes, the bot seeing crates at {seeing}; the player knows {BitOperations.PopCount(h.Server.CrateKnownOf(player.Slot))}");
        AssertClean(h);
    }

    // ---- the match block, the end, the log ----

    [Fact]
    public void The_match_block_counts_the_teams_as_the_places()
    {
        var log = new List<string>();
        BattleHarness h = StandIn(log);
        BattleClient player = h.AddClient("A");
        h.StartMatch(BattleMode.Duos, bots: 6);   // 0 0 1 1 2 2 3: seven seats, four teams
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        Assert.Equal((7, 4), (h.Server.Rules!.Entrants, h.Server.Rules.Places));
        Assert.Equal(4, player.Match.Entrants);
    }

    [Fact]
    public void A_royales_match_block_still_counts_the_seats()
    {
        var log = new List<string>();
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Mode = BattleMode.Royale, Map = MapCatalog.Riverland, Maps = MapCatalog.All }, log: log.Add);
        BattleClient player = h.AddClient("A");
        h.StartMatch(BattleMode.Royale, bots: 15);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        Assert.Equal(16, h.Server.Rules!.Places);
        Assert.Equal(16, player.Match.Entrants);
    }

    [Fact]
    public void The_last_team_left_wins_and_both_of_a_team_share_its_rank()
    {
        var log = new List<string>();
        BattleHarness h = StandIn(log);
        for (int i = 0; i < 3; i++)
        {
            h.AddClient($"P{i}");
            h.Run(0.1);
        }
        h.Run(0.3);
        SetTeam(h, 2, "0");   // A and C on team 0, B alone on team 1
        h.StartMatch(BattleMode.Duos);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        var (a, b, c) = (h.Clients[0], h.Clients[1], h.Clients[2]);

        Kill(h, a, b);
        h.Run(0.5);

        MatchRules rules = h.Server.Rules!;
        Assert.Equal(MatchPhase.Ended, rules.Phase);
        Assert.Equal(((byte)0, MatchRules.NoSlot), (rules.WinnerTeam, rules.WinnerSlot));
        Assert.Contains("match ended: duos, team 0, eliminated", log);   // the line as every mode has it: the team, not "draw"
        foreach (BattleClient viewer in h.Clients)
        {
            Assert.Equal(((byte)0, MatchRules.NoSlot, 2), (viewer.Match.WinnerTeam, viewer.Match.WinnerSlot, (int)viewer.Match.Entrants));
            PlayerStats[] rows = viewer.Latest!.Players;
            Assert.Equal((1, 1, 2), ((int)rows[a.Slot].Rank, (int)rows[c.Slot].Rank, (int)rows[b.Slot].Rank));
        }
    }

    [Fact]
    public void The_start_logs_each_teams_seats()
    {
        var log = new List<string>();
        var (h, _, _, _, _) = Four(log);
        int started = log.FindIndex(l => l.StartsWith("match started: duos", StringComparison.Ordinal));
        Assert.True(started >= 0);
        Assert.Equal("teams: 0+1 - 2 3 - - - -", log[started + 1]);   // in team order, "-" for a team nobody is on
        Assert.Single(log, l => l.StartsWith("teams: ", StringComparison.Ordinal));
        foreach (string l in log) _output.WriteLine(l);
        Assert.True(h.Server.MatchRunning);
    }

    [Fact]
    public void Other_modes_log_no_teams_line()
    {
        var log = new List<string>();
        MapDefinition map = TestMaps.RoyaleStandIn();
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Mode = BattleMode.Royale, Map = map, Maps = new[] { map } }, log: log.Add);
        h.Server.ZonePickerOverride = TestMaps.StandInWideZone;
        for (int i = 0; i < 2; i++) h.AddClient($"P{i}");
        h.StartMatch(BattleMode.Royale);
        Assert.True(h.Server.MatchRunning);
        Assert.DoesNotContain(log, l => l.StartsWith("teams: ", StringComparison.Ordinal));
    }

    // ---- a full team ----

    private static byte[] Encode(Action<BitWriter> write)
    {
        var w = new BitWriter();
        write(w);
        return w.Data.ToArray();
    }

    private static byte[] Hello(string name) => Encode(w => BattleMessages.Write(w, new HelloMessage(BattleProtocol.Version, name, 0)));

    private static byte[] Command(RoomCommandKind kind, string arg = "") => Encode(w => BattleMessages.Write(w, new RoomCommandMessage(kind, arg)));

    private static byte[] Input(int tick, int ack, sbyte throttle, sbyte steer, float yaw, bool fire) => Encode(w =>
    {
        var inputs = new InputCmd[3];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = new InputCmd(tick - i, throttle, steer, yaw, 0f, fire && i == 0, AmmoType.AP);
        BattleInputPacket.Write(w, ack, inputs);
    });

    private static BattleServer DuoServer(FakeServerTransport net, List<string> log, int bots)
    {
        var server = new BattleServer(net, TestMaps.Options(TestMaps.DuoStandIn()) with { Mode = BattleMode.Duos, Bots = bots, ReconnectGraceSeconds = 0 }, log.Add);
        server.CratePlacerOverride = TestMaps.StandInCratePlacer;
        server.ZonePickerOverride = TestMaps.StandInWideZone;
        return server;
    }

    private static void Join(FakeServerTransport net, BattleServer server, int peer)
    {
        net.Connect(peer);
        net.Receive(peer, Hello($"p{peer}"));
        server.Tick();
    }

    /// <summary>
    /// On the start tick (the only one that takes joins in duos) a newcomer is seated on team 0, which the room counts as one seat
    /// short but the match counts as full: a seat that left it still holds its spot there. Nothing in the room gets there today —
    /// seats are freed only after the start tick's step — so the two seats are freed by hand here, as a run-out reconnect grace frees
    /// them; whatever the way in, the newcomer waits for the next match rather than reaching <see cref="MatchRules.Join"/>, which
    /// would throw "Team 0 is full.".
    /// </summary>
    [Fact]
    public void A_seat_for_a_team_already_full_in_the_match_waits_instead_of_joining()
    {
        var net = new FakeServerTransport { Capture = true };
        var log = new List<string>();
        BattleServer server = DuoServer(net, log, bots: 13);
        for (int peer = 1; peer <= 3; peer++) Join(net, server, peer);   // slots 0, 1, 2
        net.Receive(1, Command(RoomCommandKind.SetTeam, "3"));
        net.Receive(2, Command(RoomCommandKind.SetTeam, "0"));
        net.Receive(3, Command(RoomCommandKind.SetTeam, "0"));
        server.Tick();
        Assert.True(server.TryStartMatch(out string? why), why);   // the bots: one partners slot 0 on team 3, the rest pair up on 1, 2, 4–7
        MatchRules rules = server.Rules!;
        Assert.Equal(16, server.Simulation!.Tanks.Length);
        Assert.Equal(((byte)3, (byte)0, (byte)0), (rules.Of(0).Team, rules.Of(1).Team, rules.Of(2).Team));

        foreach (int peer in new[] { 1, 3 })
        {
            server.OnPeerDisconnected(peer, "test");
            server.FreeSeatForTests((byte)(peer - 1));
        }
        Assert.Equal(rules.StartTick, server.Simulation.Tick);
        Assert.True(rules.AcceptsJoins);

        int tanks = server.Simulation.TankCount;
        Join(net, server, 4);   // slot 0, and by the room's count team 0 (all teams manned, team 0 the lowest with one seat)

        Assert.True(server.MatchRunning);
        Assert.Equal(0, server.TankOf(0));
        Assert.Equal(tanks, server.Simulation.TankCount);
        Assert.Equal(0, net.CountTo(4, BattleMessage.MatchStart));
        Assert.Contains(log, l => l.Contains("team 0 is full", StringComparison.Ordinal));
        Assert.DoesNotContain(log, l => l.StartsWith("error handling packet", StringComparison.Ordinal));   // nothing thrown
        for (int i = 0; i < 30; i++) server.Tick();
        Assert.True(server.MatchRunning);
        foreach (string l in log) _output.WriteLine(l);
    }

    /// <summary>
    /// A seat within its reconnect grace keeps its team spot: X and Y on team 2, X drops mid-match; a newcomer W, waiting for
    /// the next match, can't move onto team 2 until X's seat is freed.
    /// </summary>
    [Fact]
    public void A_seat_in_its_reconnect_grace_holds_its_team_spot_in_the_room()
    {
        var log = new List<string>();
        BattleHarness h = StandIn(log);   // the default grace: 30 s
        for (int i = 0; i < 4; i++)
        {
            h.AddClient($"P{i}");
            h.Run(0.1);
        }
        h.Run(0.3);
        SetTeam(h, 3, "2");   // teams 0, 1, 2, 2
        h.StartMatch(BattleMode.Duos);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        BattleClient x = h.Clients[2], y = h.Clients[3];
        Assert.Equal(((byte)2, (byte)2), (h.Server.Rules!.Of(x.Slot).Team, h.Server.Rules.Of(y.Slot).Team));

        h.Links[2].Dispose();   // X drops: its seat waits out the grace
        h.Run(0.5);
        BattleClient w = h.AddClient("W");
        h.Run(0.5);
        byte TeamOfW() => w.Room!.Players.Single(p => p.Slot == w.Slot).Team;
        Assert.Equal(0, h.Server.TankOf(w.Slot));   // duos takes nobody mid-match: W waits
        Assert.NotEqual(x.Slot, w.Slot);
        Assert.Equal(3, TeamOfW());   // the lowest empty team: X still counts on team 2

        SetTeam(h, h.Clients.IndexOf(w), "2");
        Assert.Equal(3, TeamOfW());

        h.Server.FreeSeatForTests(x.Slot);   // as the grace running out does
        h.Run(0.2);
        SetTeam(h, h.Clients.IndexOf(w), "2");
        Assert.Equal(2, TeamOfW());
        Assert.True(h.Server.MatchRunning);
    }

    // ---- allocation ----

    /// <summary>
    /// Duos with two people on team 0 (their crate sight worked out as one), six bots on teams 1–3 and the crates in sight:
    /// after a warm-up the server's tick allocates nothing, measured through a <see cref="FakeServerTransport"/> as
    /// <see cref="BattleServerTests"/> does.
    /// </summary>
    [Fact]
    public void A_duos_match_allocates_nothing_per_tick()
    {
        Allocation.AssertNone(() =>
        {
            const int WarmUpTicks = 60, MeasuredTicks = 300, Humans = 2, Bots = 6;
            var net = new FakeServerTransport { Capture = true };
            var log = new List<string>();
            BattleServer server = DuoServer(net, log, Bots);
            for (int peer = 1; peer <= Humans; peer++) Join(net, server, peer);
            net.Receive(2, Command(RoomCommandKind.SetTeam, "0"));
            server.Tick();
            net.Receive(1, Command(RoomCommandKind.StartMatch));
            server.Tick();
            Assert.True(server.MatchRunning);
            Assert.Equal(Humans + Bots, server.Simulation!.Tanks.Length);
            Assert.Equal((4, (byte)0, (byte)0), (server.Rules!.Places, server.Rules.Of(0).Team, server.Rules.Of(1).Team));
            net.Capture = false;   // copying packets for inspection allocates; counting them doesn't

            int firstTick = server.Simulation.Tick + 1, total = WarmUpTicks + MeasuredTicks;
            var packets = new byte[Humans][][];
            for (int h = 0; h < Humans; h++)
            {
                packets[h] = new byte[total][];
                for (int i = 0; i < total; i++)
                    packets[h][i] = Input(firstTick + i, ack: System.Math.Max(0, firstTick + i - 2), throttle: 1, steer: (sbyte)((i / 40 % 3) - 1), yaw: (i % 100) / 20f, fire: i % 15 == 0);
            }
            for (int i = 0; i < WarmUpTicks; i++)
            {
                for (int h = 0; h < Humans; h++) net.Receive(h + 1, packets[h][i]);
                server.Tick();
            }

            int passes = server.CrateSightPasses;
            Allocation.Window window = Allocation.Start();
            for (int i = WarmUpTicks; i < total; i++)
            {
                for (int h = 0; h < Humans; h++) net.Receive(h + 1, packets[h][i]);
                server.Tick();
            }
            Allocation.Measured measured = window.Stop();

            _output.WriteLine($"duos, {Humans} humans + {Bots} bots: {measured.Bytes} bytes allocated over {MeasuredTicks} ticks; {server.CrateSightPasses - passes} sight passes");
            Assert.True(server.MatchRunning);
            Assert.True(server.CrateSightPasses - passes >= MeasuredTicks / VisionTracker.UpdateInterval - 1);
            for (int h = 1; h <= Humans; h++) Assert.True(net.CountTo(h, BattleMessage.Snapshot) >= total);
            return measured;
        });
    }
}

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
using Xunit.Abstractions;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// The server's crates (stage 6 design §4.4; 6B task 5): laid from the match seed when a royale starts, each human seat's crates in
/// sight worked out at every vision update and sent as its snapshot's rows, a pickup told to the eater alone (the others see the
/// empty spot through their own rows, and only while they see it), a resync carrying the crates the seat knows and hasn't seen go,
/// a wreck seeing no more, the other modes without any — and, as a probe, every snapshot's rows exactly the server's mask for
/// that seat that tick, that mask what a fresh look from the tank gives, and nothing on the wire that shouldn't be. Three players on <see cref="TestMaps.RoyaleStandIn"/> with <see cref="TestMaps.StandInCrates"/>: the
/// <c>Shooter</c> on the western spawn (40, 100) with a crate ahead and one behind, the <c>Target</c> 30 m east of it seeing the
/// same two, and <c>Hidden</c> east of the wall with the eastern crate alone.
/// </summary>
public sealed class CrateServerTests
{
    private const double FrameDt = 1.0 / 120;
    private const byte Ahead = TestMaps.CrateAhead, Behind = TestMaps.CrateBehind, East = TestMaps.CrateEast, Far = TestMaps.CrateFar;
    private const ulong West = 1ul << Ahead | 1ul << Behind;

    /// <summary>Where the <c>Target</c> is put to lose sight of the western crates: east of the wall, which then hides both.</summary>
    private static readonly Vector2 BehindTheWall = new(150, 60);

    private readonly ITestOutputHelper _output;

    public CrateServerTests(ITestOutputHelper output) => _output = output;

    /// <summary>A royale room on the stand-in, its crates <see cref="TestMaps.StandInCrates"/> and its zone wide enough to hurt no one.</summary>
    private static BattleHarness StandIn(List<string> log, double? matchSeconds = null, double graceSeconds = 30)
    {
        MapDefinition map = TestMaps.RoyaleStandIn();
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Mode = BattleMode.Royale, Map = map, Maps = new[] { map }, MatchSeconds = matchSeconds, ReconnectGraceSeconds = graceSeconds }, log: log.Add);
        h.Server.CratePlacerOverride = TestMaps.StandInCratePlacer;
        h.Server.ZonePickerOverride = TestMaps.StandInWideZone;
        return h;
    }

    /// <summary>Three players in a royale on the stand-in a second in (the spawn order is the seed's, so the roles go by where the tanks stand).</summary>
    private static (BattleHarness H, BattleClient Shooter, BattleClient Target, BattleClient Hidden) ThreeWithCrates(List<string> log, double? matchSeconds = null, double graceSeconds = 30,
        Func<IClientTransport, IClientTransport>? wrap = null)
    {
        BattleHarness h = StandIn(log, matchSeconds, graceSeconds);
        h.WrapTransport = wrap;
        for (int i = 0; i < 3; i++) h.AddClient($"P{i}");
        h.StartMatch(BattleMode.Royale);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        Assert.Equal(TestMaps.StandInCrates, h.Server.Rules!.Crates!.All.ToArray());
        var byX = h.Clients.OrderBy(c => h.ServerTankOf(c).Position.X).ToArray();
        Assert.True(h.ServerTankOf(byX[2]).Position.X > TestMaps.StandardWallX);
        return (h, byX[0], byX[1], byX[2]);
    }

    /// <summary>
    /// Drives <paramref name="driver"/> straight (<paramref name="throttle"/> 1 ahead, −1 back, the turret kept over the hull) until the
    /// server has crate <paramref name="id"/> eaten, then lets go of the throttle; returns the server tick it was eaten on.
    /// </summary>
    private static int DriveOver(BattleHarness h, BattleClient driver, byte id, sbyte throttle, double maxSeconds = 10)
    {
        CrateSet crates = h.Server.Rules!.Crates!;
        int index = h.Clients.IndexOf(driver);
        float yaw = h.ServerTankOf(driver).Yaw;
        int frames = (int)(maxSeconds / FrameDt);
        for (int f = 0; f < frames && crates.IsStanding(id); f++)
            h.Run(FrameDt, (c, _) => c == index ? new LocalCommand(throttle, 0, yaw, 0f, false) : LocalCommand.None, FrameDt);
        Assert.False(crates.IsStanding(id), $"crate {id} still stands after {maxSeconds} s of driving");
        Assert.Equal(h.Server.TankOf(driver.Slot), crates.TakenBy(id));
        return h.Server.Simulation!.Tick;
    }

    private static void AssertClean(BattleHarness h)
    {
        foreach (BattleClient c in h.Clients)
        {
            Assert.Null(c.LastPacketError);
            Assert.Equal(0, c.Resyncs);
        }
    }

    private static void AssertNoPickup(BattleClient c) => Assert.False(c.TryDequeuePickup(out CrateMessage stray), $"{c.Slot} was told of a pickup it didn't make: {stray}");

    /// <summary>A position as the wire carries it, to the centimetre: the rows, and so the match start and the Crate message too (the server quantises them once).</summary>
    private static Vector2 Cm(Vector2 p) => new(BattleProtocol.FromCm(BattleProtocol.ToCm(p.X)), BattleProtocol.FromCm(BattleProtocol.ToCm(p.Y)));

    /// <summary>Crate <paramref name="id"/> of <see cref="TestMaps.StandInCrates"/> as the client gets it.</summary>
    private static Crate OnTheWire(byte id) => TestMaps.StandInCrates[id] with { Position = Cm(TestMaps.StandInCrates[id].Position) };

    private static ulong RowsOf(BattleSnapshot s)
    {
        ulong rows = 0;
        int prev = -1;
        foreach (ref readonly CrateView row in s.CrateSpan)
        {
            Assert.True(row.Id > prev, $"crate rows out of order: {row.Id} after {prev}");
            prev = row.Id;
            rows |= 1ul << row.Id;
        }
        return rows;
    }

    // ---- the start ----

    [Fact]
    public void A_royale_lays_its_crates_from_the_match_seed_and_logs_them()
    {
        var log = new List<string>();
        var h = new BattleHarness(new BattleServerOptions { Seed = 3, Mode = BattleMode.Royale, Map = MapCatalog.Riverland, Maps = MapCatalog.All }, log: log.Add);
        BattleClient a = h.AddClient("A");
        h.StartMatch(BattleMode.Royale, bots: 1);
        Assert.True(h.Server.MatchRunning, "the match didn't start");

        Crate[] expected = CratePlacer.Place(MapCatalog.Riverland.NewBattlefield(), MapCatalog.Riverland.NewNavGrid(), RiverlandMap.Spawns.Solo, 3u ^ CratePlacer.SeedSalt);
        CrateSet crates = h.Server.Rules!.Crates!;
        Assert.NotNull(crates);
        Assert.Equal(expected, crates.All.ToArray());
        Assert.Equal(CratePlacer.Count, crates.Count);
        string line = Assert.Single(log, l => l.StartsWith("crates: ", StringComparison.Ordinal));
        foreach (string l in log) _output.WriteLine(l);
        Assert.StartsWith($"crates: {CratePlacer.Count} placed", line);
        Assert.Equal(log.FindIndex(l => l.StartsWith("zone: ", StringComparison.Ordinal)) + 1, log.IndexOf(line));   // right after the zone's line, which follows "match started" and the bots'
        Assert.True(log.FindIndex(l => l.StartsWith("match started", StringComparison.Ordinal)) < log.IndexOf(line));
        Assert.DoesNotContain(log, l => l.Contains("warning", StringComparison.OrdinalIgnoreCase));
        Assert.True(a.MatchInfo!.Crates is null or { Count: 0 });   // a fresh start: the seat had seen none
        Assert.Equal(BitOperations.PopCount(h.Server.CrateKnownOf(a.Slot)), a.Crates.KnownCount);   // a second in: what it has seen from its spawn
        _output.WriteLine($"from its spawn the player sees {a.Crates.KnownCount} of the {crates.Count} crates");
        AssertClean(h);
    }

    [Fact]
    public void Crates_that_fail_to_start_leave_the_room_as_it_was_and_the_next_start_runs()
    {
        var log = new List<string>();
        BattleHarness h = StandIn(log);
        BattleClient host = h.AddClient("A");
        h.AddClient("B");
        var failures = new Func<Battlefield, Tank.Sim.Navigation.NavGrid, IReadOnlyList<SpawnPoint>, uint, Crate[]>[]
        {
            (_, _, _, _) => throw new InvalidOperationException("No place."),   // the placer fails
            (_, _, _, _) => new[] { new Crate(1, new Vector2(48, 100), CrateKind.Repair) },   // or lays what the set refuses (ids from 1)
            (_, _, _, _) => new[] { new Crate(0, new Vector2(-1, 100), CrateKind.Repair) },   // or one the snapshot can't carry (its rows are unsigned centimetres)
        };
        foreach (var failure in failures)
        {
            log.Clear();
            h.Server.CratePlacerOverride = failure;
            h.StartMatch(BattleMode.Royale, bots: 1);

            Assert.False(h.Server.MatchRunning);
            Assert.Contains(log, l => l.StartsWith("match not started: the crates failed", StringComparison.Ordinal));
            Assert.Contains("can't start: the crates failed to start", log);
            Assert.Equal(BattlePhase.Room, host.Phase);
            Assert.False(host.Room!.MatchRunning);
            Assert.Equal(2, host.Room.Players.Count);   // no bot seated and left behind
            Assert.DoesNotContain(host.Room.Players, p => p.IsBot);
        }

        log.Clear();
        h.Server.CratePlacerOverride = null;   // the real placer on a map with no house, road or forest: nothing to lay, and a warning
        h.StartMatch(BattleMode.Royale, bots: 1);
        Assert.True(h.Server.MatchRunning);
        Assert.Equal(0, h.Server.Rules!.Crates!.Count);
        Assert.Contains(log, l => l.StartsWith("crates: 0 placed", StringComparison.Ordinal));
        Assert.Contains(log, l => l.Contains("warning", StringComparison.Ordinal) && l.Contains($"0 of {CratePlacer.Count}", StringComparison.Ordinal));
        h.Run(1.0);
        Assert.Equal(BattlePhase.InMatch, host.Phase);
        Assert.Equal(0, host.Latest!.CrateCount);
        AssertClean(h);
    }

    // ---- sight ----

    [Fact]
    public void Each_seat_knows_only_the_crates_it_has_seen()
    {
        var log = new List<string>();
        var (h, shooter, target, hidden) = ThreeWithCrates(log);

        foreach (BattleClient west in new[] { shooter, target })
        {
            Assert.Equal(West, h.Server.CrateVisibleOf(west.Slot));
            Assert.Equal(West, h.Server.CrateKnownOf(west.Slot));
            Assert.Equal(West, RowsOf(west.Latest!));
            Assert.Equal((2, 2), (west.Crates.KnownCount, west.Crates.StandingKnown));
            foreach (byte id in new[] { Ahead, Behind })
            {
                CrateKnowledge.Entry e = west.Crates.All[id];
                Assert.True(e.Known && e.Visible && e.Standing, $"crate {id} for slot {west.Slot}: {e.Known} {e.Visible} {e.Standing}");
                Assert.Equal(TestMaps.StandInCrates[id].Kind, e.Kind);
                Assert.Equal(Cm(TestMaps.StandInCrates[id].Position), e.Position);   // the row's centimetres
            }
            Assert.False(west.Crates.All[East].Known);
            Assert.False(west.Crates.All[Far].Known);
        }
        Assert.Equal(1ul << East, h.Server.CrateVisibleOf(hidden.Slot));
        Assert.Equal(1ul << East, RowsOf(hidden.Latest!));
        Assert.Equal((1, 1), (hidden.Crates.KnownCount, hidden.Crates.StandingKnown));
        Assert.True(hidden.Crates.All[East] is { Known: true, Visible: true, Standing: true });
        Assert.False(hidden.Crates.All[Ahead].Known);
        Assert.False(hidden.Crates.All[Behind].Known);
        Assert.False(hidden.Crates.All[Far].Known);
        Assert.True(h.Server.CrateSightPasses >= SimConstants.TickRate / VisionTracker.UpdateInterval, $"{h.Server.CrateSightPasses} sight passes in a second");
        AssertClean(h);
    }

    // ---- pickups ----

    [Fact]
    public void Driving_over_a_crate_eats_it_and_tells_the_eater_alone()
    {
        var log = new List<string>();
        var (h, shooter, target, hidden) = ThreeWithCrates(log);
        int shooterTank = h.Server.TankOf(shooter.Slot);
        Simulation sim = h.Server.Simulation!;
        const int Hurt = 100;
        sim.HurtByZone(shooterTank, Hurt);   // so the repair crate has something to mend
        int n = shooter.MatchInfo!.MatchNumber;

        int ate = DriveOver(h, shooter, Ahead, throttle: 1);
        h.Run(0.3);

        Assert.True(shooter.TryDequeuePickup(out CrateMessage repair), "the eater wasn't told");
        Assert.Equal(new CrateMessage(n, Ahead, CrateKind.Repair, TestMaps.StandInCrates[Ahead].Position, Hurt, 0, 0), repair);
        Assert.False(shooter.TryDequeuePickup(out _));
        Assert.Equal(TankCatalog.Medium.MaxHealth, h.ServerTankOf(shooter).Health);
        Assert.True(shooter.Crates.All[Ahead] is { Known: true, Standing: false }, "the eater's own table still has the crate standing");
        AssertNoPickup(target);
        AssertNoPickup(hidden);
        Assert.False(hidden.Crates.All[Ahead].Known, "the wall hides the crate from the east, eaten or not");
        _output.WriteLine($"crate {Ahead} eaten on tick {ate} by tank {shooterTank}");

        DriveOver(h, shooter, Behind, throttle: -1);
        h.Run(0.3);

        Assert.True(shooter.TryDequeuePickup(out CrateMessage armor));
        Assert.Equal(new CrateMessage(n, Behind, CrateKind.Armor, OnTheWire(Behind).Position, 1, 0, 1), armor);   // to the centimetre, as its row had it
        Assert.Equal(1, h.ServerTankOf(shooter).ArmorLayers);
        Assert.Equal((byte)1, shooter.Latest!.Own.ArmorLayers);
        Assert.Equal((1, 0, 0), ((int)shooter.Own.ArmorLayers, (int)shooter.Own.ReloadLayers, (int)shooter.Own.OpticsLayers));
        for (int frame = 0; frame < 120; frame++)   // and it stays: every snapshot of the next second says one layer
        {
            h.Run(FrameDt, null, FrameDt);
            Assert.Equal((byte)1, shooter.Latest!.Own.ArmorLayers);
            Assert.Equal((byte)0, target.Latest!.Own.ArmorLayers);
        }
        Assert.Equal((2, 0), (shooter.Crates.KnownCount, shooter.Crates.StandingKnown));
        AssertNoPickup(target);
        AssertNoPickup(hidden);
        Assert.False(hidden.Crates.All[Behind].Known);
        AssertClean(h);
    }

    [Fact]
    public void Anothers_pickup_is_not_broadcast_and_shows_only_to_a_seat_that_sees_the_spot()
    {
        var log = new List<string>();
        var (h, shooter, target, hidden) = ThreeWithCrates(log);
        Assert.True(target.Crates.All[Ahead].Standing);
        int targetTank = h.Server.TankOf(target.Slot);

        // The target sees the spot: it learns of the pickup through its own rows, as the server's next snapshot of it, not through a message.
        int ate = DriveOver(h, shooter, Ahead, throttle: 1);
        BattleSnapshot? told = null;
        for (int frame = 0; frame < 60 && told is null; frame++)
        {
            h.Run(FrameDt, null, FrameDt);
            if (target.Latest is { } latest && target.Crates.All[Ahead].Standing == false) told = latest;
        }
        Assert.NotNull(told);
        Assert.True(told.Tick >= ate, $"the target saw the crate down on tick {told.Tick}, before it was eaten on {ate}");
        Assert.True(told.Tick < ate + VisionTracker.UpdateInterval, $"the target saw the crate down only on tick {told.Tick}, eaten on {ate}");
        Assert.Contains(told.CrateSpan.ToArray(), row => row.Id == Ahead && !row.Standing);
        AssertNoPickup(target);
        AssertNoPickup(hidden);
        Assert.Equal((2, 1), (target.Crates.KnownCount, target.Crates.StandingKnown));

        // Out of sight of the other crate's spot (east of the wall), the target keeps it standing while the shooter eats it.
        h.Server.Simulation!.Respawn(targetTank, BehindTheWall, Angles.YawOf(-Vector2.UnitX));
        h.Run(0.5);
        Assert.Equal(1ul << East, h.Server.CrateVisibleOf(target.Slot));
        Assert.Equal(West | 1ul << East, h.Server.CrateKnownOf(target.Slot));
        Assert.True(target.Crates.All[Behind] is { Known: true, Standing: true, Visible: false });
        DriveOver(h, shooter, Behind, throttle: -1);
        h.Run(1.0);
        Assert.False(h.Server.Rules!.Crates!.IsStanding(Behind));
        Assert.True(target.Crates.All[Behind] is { Known: true, Standing: true, Visible: false }, "the target was told of a pickup at a spot it can't see");
        Assert.Equal((3, 2), (target.Crates.KnownCount, target.Crates.StandingKnown));
        AssertNoPickup(target);

        // Back where it sees the spot: the empty spot is in its rows, and the crate goes down in its table.
        h.Server.Simulation.Respawn(targetTank, new Vector2(70, 100), Angles.YawOf(Vector2.UnitX));
        h.Run(0.5);
        Assert.True(target.Crates.All[Behind] is { Known: true, Standing: false, Visible: true });
        Assert.Equal((3, 1), (target.Crates.KnownCount, target.Crates.StandingKnown));
        AssertNoPickup(target);
        AssertClean(h);
    }

    // ---- resync ----

    /// <summary>
    /// A resync by request (the target's: a snapshot against a baseline it never had makes it ask) and one by reconnect (the shooter's,
    /// within the grace): the start carries the crates the seat knows and hasn't seen go (in its rows, or by eating it), ids rising,
    /// to the centimetre as the rows have them — so the rows that follow move nothing and bump no version.
    /// </summary>
    [Fact]
    public void A_resync_carries_the_crates_the_seat_knows_that_still_stand()
    {
        var log = new List<string>();
        var (h, shooter, target, _) = ThreeWithCrates(log);

        int versionBefore = target.Crates.Version;
        target.OnData(0, BattleClientRobustnessTests.SnapshotAgainstAMissingBaseline(target));
        string? refused = target.LastPacketError;
        Assert.NotNull(refused);
        Assert.Equal(1, target.Resyncs);
        h.Run(0.5);

        Assert.Equal(1, target.Resyncs);
        Assert.Equal(refused, target.LastPacketError);   // nothing since
        Assert.Equal(new[] { OnTheWire(Ahead), OnTheWire(Behind) }, target.MatchInfo!.Crates!);   // both western crates: it has seen both, both stand
        Assert.Equal(versionBefore + 1 + 2, target.Crates.Version);   // Clear's one bump and one per crate the start carried; the rows since, the same to the centimetre, added none
        foreach (byte id in new[] { Ahead, Behind })
        {
            Assert.True(target.Crates.All[id] is { Known: true, Standing: true, Visible: true });
            Assert.Equal(Cm(TestMaps.StandInCrates[id].Position), target.Crates.All[id].Position);
        }

        DriveOver(h, shooter, Ahead, throttle: 1);
        h.Run(0.3);
        Assert.True(shooter.TryDequeuePickup(out _));
        Assert.Equal((2, 1), (shooter.Crates.KnownCount, shooter.Crates.StandingKnown));
        int shooterTank = h.Server.TankOf(shooter.Slot);

        h.Links[h.Clients.IndexOf(shooter)].Dispose();   // the shooter drops and comes back within the grace period
        h.Run(0.5);
        BattleClient back = h.AddClient("Shooter again", token: shooter.SessionToken);
        h.Run(1.0);

        Assert.Equal(BattlePhase.InMatch, back.Phase);
        Assert.Equal(shooter.Slot, back.Slot);
        Assert.Equal(shooterTank, back.OwnTankId);
        Crate known = Assert.Single(back.MatchInfo!.Crates!);   // the one it knew that still stands: not the one it ate
        Assert.Equal(OnTheWire(Behind), known);
        Assert.True(back.Crates.All[Behind] is { Known: true, Standing: true, Visible: true });
        Assert.True(back.Crates.All[Ahead] is { Known: true, Standing: false, Visible: true });   // seen again since: the empty spot
        Assert.Equal((2, 1), (back.Crates.KnownCount, back.Crates.StandingKnown));
        Assert.False(back.TryDequeuePickup(out _));   // the pickup was told once, to the connection that made it
        Assert.Equal(West, h.Server.CrateKnownOf(back.Slot));
        foreach (BattleClient c in h.Clients)
        {
            if (c == target) continue;
            Assert.Null(c.LastPacketError);
            Assert.Equal(0, c.Resyncs);
        }

        // The target asks again: it ate neither crate, but it saw the shooter eat the one ahead, so the start leaves that out.
        target.OnData(0, BattleClientRobustnessTests.SnapshotAgainstAMissingBaseline(target));
        h.Run(0.5);
        Assert.Equal(2, target.Resyncs);
        Assert.Equal(new[] { OnTheWire(Behind) }, target.MatchInfo!.Crates!);
        Assert.True(target.Crates.All[Ahead] is { Known: true, Standing: false, Visible: true });   // seen again since: the empty spot
        Assert.Equal((2, 1), (target.Crates.KnownCount, target.Crates.StandingKnown));
    }

    /// <summary>
    /// A resync tells a seat nothing its rows haven't (stage 6B batch E; plan deviation 45): a crate it saw eaten in sight is left
    /// out, and so is one it ate itself, but one eaten while it couldn't see the spot comes back standing, as its table had it —
    /// else a client asking once a second (the server allows that) would learn "a crate I saw was just eaten" to the second.
    /// </summary>
    [Fact]
    public void A_resync_keeps_a_crate_eaten_out_of_sight_and_leaves_out_the_ones_seen_or_made_gone()
    {
        var log = new List<string>();
        var (h, shooter, target, _) = ThreeWithCrates(log);
        int targetTank = h.Server.TankOf(target.Slot);
        Assert.Equal(0ul, h.Server.CrateGoneOf(target.Slot));

        // In sight: the target sees the shooter eat the crate ahead (its rows say so).
        DriveOver(h, shooter, Ahead, throttle: 1);
        h.Run(0.3);
        Assert.True(target.Crates.All[Ahead] is { Known: true, Standing: false, Visible: true });
        Assert.Equal(1ul << Ahead, h.Server.CrateGoneOf(target.Slot));
        Assert.Equal(1ul << Ahead, h.Server.CrateGoneOf(shooter.Slot));

        // Out of sight: east of the wall, the target can't see the shooter eat the crate behind.
        h.Server.Simulation!.Respawn(targetTank, BehindTheWall, Angles.YawOf(-Vector2.UnitX));
        h.Run(0.5);
        Assert.Equal(1ul << East, h.Server.CrateVisibleOf(target.Slot));
        DriveOver(h, shooter, Behind, throttle: -1);
        h.Run(1.0);
        Assert.False(h.Server.Rules!.Crates!.IsStanding(Behind));
        Assert.True(target.Crates.All[Behind] is { Known: true, Standing: true, Visible: false });
        Assert.Equal(1ul << Ahead, h.Server.CrateGoneOf(target.Slot));   // nothing it was told
        Assert.Equal(West, h.Server.CrateGoneOf(shooter.Slot));
        Assert.Equal((3, 2), (target.Crates.KnownCount, target.Crates.StandingKnown));

        target.OnData(0, BattleClientRobustnessTests.SnapshotAgainstAMissingBaseline(target));
        h.Run(0.5);

        Assert.Equal(1, target.Resyncs);
        Assert.Equal(new[] { OnTheWire(Behind), OnTheWire(East) }, target.MatchInfo!.Crates!);   // not the one it saw go
        Assert.True(target.Crates.All[Behind] is { Known: true, Standing: true, Visible: false }, "the resync told the target of a pickup at a spot it can't see");
        Assert.True(target.Crates.All[East] is { Known: true, Standing: true, Visible: true });
        Assert.Equal(2, target.Crates.StandingKnown);   // what it believed standing before the resync, no more, no less

        // The shooter asks too: it ate both, so none comes back.
        shooter.OnData(0, BattleClientRobustnessTests.SnapshotAgainstAMissingBaseline(shooter));
        h.Run(0.5);
        Assert.Equal(1, shooter.Resyncs);
        Assert.Empty(shooter.MatchInfo!.Crates!);
        Assert.Equal(0, shooter.Crates.StandingKnown);
    }

    [Fact]
    public void A_seat_freed_past_the_grace_period_forgets_its_crates()
    {
        var log = new List<string>();
        var (h, shooter, target, _) = ThreeWithCrates(log, graceSeconds: 0.5);
        Assert.Equal(West, h.Server.CrateKnownOf(target.Slot));

        h.Links[h.Clients.IndexOf(target)].Dispose();
        h.Run(1.5);   // past the grace: the seat is freed

        Assert.Equal(0ul, h.Server.CrateKnownOf(target.Slot));
        Assert.Equal(0ul, h.Server.CrateVisibleOf(target.Slot));
        Assert.Equal(West, h.Server.CrateKnownOf(shooter.Slot));
        Assert.Null(shooter.LastPacketError);
    }

    // ---- a wreck ----

    [Fact]
    public void A_destroyed_player_sees_no_more_crates_but_keeps_what_it_knew()
    {
        var log = new List<string>();
        var (h, shooter, target, _) = ThreeWithCrates(log);
        Assert.Equal((2, 2), (target.Crates.KnownCount, target.Crates.StandingKnown));

        RoyaleServerTests.Destroy(h, shooter, target);
        h.Run(0.5);

        Assert.True(h.ServerTankOf(target).IsDestroyed);
        Assert.Equal(0ul, h.Server.CrateVisibleOf(target.Slot));
        Assert.Equal(West, h.Server.CrateKnownOf(target.Slot));
        Assert.Equal(0, target.Latest!.CrateCount);
        Assert.Equal(3, target.Latest.EntityCount);   // the dead of a royale watch every tank, but no crate
        Assert.Equal((2, 2), (target.Crates.KnownCount, target.Crates.StandingKnown));
        Assert.True(target.Crates.All[Ahead] is { Known: true, Standing: true, Visible: false });
        Assert.Equal(West, h.Server.CrateVisibleOf(shooter.Slot));   // the living see on
        Assert.Equal(West, RowsOf(shooter.Latest!));
        AssertClean(h);
    }

    // ---- the other modes ----

    [Fact]
    public void A_team_battle_has_no_crates_and_never_asks_the_placer()
    {
        var log = new List<string>();
        var h = new BattleHarness(new BattleServerOptions { Seed = 1 }, log: log.Add);
        h.Server.CratePlacerOverride = (_, _, _, _) => throw new InvalidOperationException("the placer was asked in a team battle");
        BattleClient a = h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        Assert.True(h.Server.MatchRunning);

        Assert.Null(h.Server.Rules!.Crates);
        Assert.True(a.MatchInfo!.Crates is null or { Count: 0 });
        Assert.Equal(0, a.Latest!.CrateCount);
        Assert.Equal((byte)0, a.Latest.Own.ArmorLayers);
        Assert.Equal((byte)0, a.Latest.Own.ReloadLayers);
        Assert.Equal((byte)0, a.Latest.Own.OpticsLayers);
        Assert.Equal(0, a.Crates.KnownCount);
        Assert.Equal(0ul, h.Server.CrateVisibleOf(a.Slot));
        Assert.Equal(0, h.Server.CrateSightPasses);
        Assert.DoesNotContain(log, l => l.StartsWith("crates: ", StringComparison.Ordinal));
        AssertClean(h);
    }

    // ---- the probe: rows ⊆ the server's mask, the mask a fresh look, the wire clean ----

    /// <summary>
    /// Sits between a client and its link and counts what the server sent it: packets by their first byte, and the crate-picked
    /// events among the event batches (each batch read as the client reads it). Hands everything on untouched.
    /// </summary>
    private sealed class WireSniffer : IClientTransport, ITransportHandler
    {
        private readonly IClientTransport _link;
        private ITransportHandler? _client;

        public WireSniffer(IClientTransport link) => _link = link;

        public int Crates { get; private set; }
        public int EventBatches { get; private set; }
        public int CratePickedEvents { get; private set; }

        public bool IsConnected => _link.IsConnected;
        public TransportStats Stats => _link.Stats;
        public void Send(ReadOnlySpan<byte> data, Channel channel) => _link.Send(data, channel);
        public void Dispose() => _link.Dispose();

        public void Poll(ITransportHandler handler)
        {
            _client = handler;
            _link.Poll(this);
        }

        void ITransportHandler.OnPeerConnected(int peerId) => _client!.OnPeerConnected(peerId);
        void ITransportHandler.OnPeerDisconnected(int peerId, string reason) => _client!.OnPeerDisconnected(peerId, reason);

        void ITransportHandler.OnData(int peerId, ReadOnlySpan<byte> data)
        {
            switch ((BattleMessage)data[0])
            {
                case BattleMessage.Crate:
                    Crates++;
                    break;
                case BattleMessage.Events:
                    EventBatches++;
                    var r = new BitReader(data);
                    r.ReadByte();
                    foreach (GameEvent e in BattleMessages.ReadEvents(ref r).Events)
                        if (e.Type == GameEventType.CratePicked) CratePickedEvents++;
                    break;
            }
            _client!.OnData(peerId, data);
        }
    }

    /// <summary>
    /// Through a run of driving, eating, teleporting, a shot and watching: every snapshot a client takes has exactly the rows of the
    /// server's mask for that seat on that tick (ids rising), each row's Standing the set's that tick; on every tick the server
    /// works a sight pass, its mask for each seat is what a fresh <see cref="CrateSet.VisibleTo"/> from that seat's tank gives
    /// (a wreck's, none); what the server holds as known only grows; and on the wire itself no crate-picked event ever goes out
    /// and a <see cref="BattleMessage.Crate"/> reaches the eater alone, one per pickup.
    /// </summary>
    [Fact]
    public void Every_snapshots_crate_rows_are_the_seats_mask_on_the_server_that_tick()
    {
        var log = new List<string>();
        var sniffers = new List<WireSniffer>();
        var (h, shooter, target, hidden) = ThreeWithCrates(log, wrap: link =>
        {
            var sniffer = new WireSniffer(link);
            sniffers.Add(sniffer);
            return sniffer;
        });
        WireSniffer SnifferOf(BattleClient c) => sniffers[h.Clients.IndexOf(c)];
        int targetTank = h.Server.TankOf(target.Slot);
        var masks = new Dictionary<int, (ulong[] Seen, ulong Standing)>();
        var known = new ulong[h.Clients.Count];
        int passes = h.Server.CrateSightPasses, sightPasses = 0, sightChecks = 0, lateWrecks = 0;
        h.AfterServerTick = server =>
        {
            if (server.Simulation is not { } sim || server.Rules?.Crates is not { } set) return;
            var seen = new ulong[h.Clients.Count];
            for (int i = 0; i < seen.Length; i++)
            {
                byte slot = h.Clients[i].Slot;
                seen[i] = server.CrateVisibleOf(slot);
                Assert.Equal(known[i], known[i] & server.CrateKnownOf(slot));   // never forgets
                Assert.Equal(seen[i], seen[i] & server.CrateKnownOf(slot));     // seen is known
                known[i] = server.CrateKnownOf(slot);
            }
            masks[sim.Tick] = (seen, set.StandingMask);

            // The probe's own sight pass, on the ticks the server made one: each seat's mask from its tank as it stands now.
            bool passed = server.CrateSightPasses != passes;
            passes = server.CrateSightPasses;
            Assert.Equal(sim.Tick % VisionTracker.UpdateInterval == 0, passed);
            if (!passed) return;
            sightPasses++;
            foreach (BattleClient c in h.Clients)
            {
                ref readonly Standing standing = ref server.Rules.Of(c.Slot);
                int slot = standing.Active ? sim.SlotOf(standing.TankId) : -1;
                ulong fresh = slot < 0 || sim.Tanks[slot].IsDestroyed ? 0 : set.VisibleTo(sim.Tanks[slot], sim.Field);
                if (fresh == server.CrateVisibleOf(c.Slot)) sightChecks++;
                else if (slot >= 0 && sim.Tanks[slot].IsDestroyed) lateWrecks++;   // died in the rules' update, after the pass: its last live look stands till the next
                else Assert.Fail($"tick {sim.Tick}, slot {c.Slot}: the server's mask is {server.CrateVisibleOf(c.Slot):X}, a fresh look gives {fresh:X}");
            }
        };
        var checkedTick = new int[h.Clients.Count];   // the snapshots so far were taken before the masks were kept
        for (int i = 0; i < checkedTick.Length; i++) checkedTick[i] = h.Clients[i].Latest!.Tick;
        int snapshots = 0;
        void Check()
        {
            for (int i = 0; i < h.Clients.Count; i++)
            {
                if (h.Clients[i].Latest is not { } s || s.Tick == checkedTick[i]) continue;
                checkedTick[i] = s.Tick;
                snapshots++;
                Assert.True(masks.TryGetValue(s.Tick, out var mask), $"no server mask for tick {s.Tick}");
                Assert.Equal(mask.Seen[i], RowsOf(s));
                foreach (ref readonly CrateView row in s.CrateSpan)
                    Assert.Equal((mask.Standing >> row.Id & 1ul) != 0, row.Standing);
            }
        }
        void Run(double seconds, Func<int, double, LocalCommand>? input = null)
        {
            int frames = (int)System.Math.Round(seconds / FrameDt);
            for (int f = 0; f < frames; f++)
            {
                h.Run(FrameDt, input, FrameDt);
                Check();
            }
        }
        int shooterIndex = h.Clients.IndexOf(shooter), hiddenIndex = h.Clients.IndexOf(hidden);
        float yaw = h.ServerTankOf(shooter).Yaw, hiddenYaw = h.ServerTankOf(hidden).Yaw;
        LocalCommand Drive(sbyte throttle, int c) => c == shooterIndex ? new LocalCommand(throttle, 0, yaw, 0f, false) : LocalCommand.None;

        Run(2.0, (c, _) => Drive(1, c));    // over the crate ahead
        h.Server.Simulation!.Respawn(targetTank, BehindTheWall, Angles.YawOf(-Vector2.UnitX));
        Run(1.0, (c, _) => c == hiddenIndex ? new LocalCommand(0, 0, hiddenYaw, 0f, true) : LocalCommand.None);   // one shot at the wall: event batches on the wire for everyone
        Run(4.0, (c, _) => Drive(-1, c));   // back over the one behind
        h.Server.Simulation.Respawn(targetTank, new Vector2(70, 100), Angles.YawOf(Vector2.UnitX));
        Run(1.0);

        CrateSet crates = h.Server.Rules!.Crates!;
        Assert.False(crates.IsStanding(Ahead), "the crate ahead was never eaten");
        Assert.False(crates.IsStanding(Behind), "the crate behind was never eaten");
        Assert.True(snapshots >= 3 * 8 * SimConstants.TickRate * 0.9, $"only {snapshots} snapshots checked");
        Assert.True(sightPasses >= 8 * SimConstants.TickRate / VisionTracker.UpdateInterval * 0.9, $"only {sightPasses} sight passes recomputed");
        Assert.Equal(h.Clients.Count * sightPasses, sightChecks);   // nobody died: every seat's mask agreed at every pass
        Assert.Equal(0, lateWrecks);
        _output.WriteLine($"{snapshots} snapshots checked against the server's masks over {masks.Count} ticks; {sightPasses} sight passes recomputed, {sightChecks} seat masks agreed");
        Assert.True(shooter.TryDequeuePickup(out _) && shooter.TryDequeuePickup(out _) && !shooter.TryDequeuePickup(out _));
        AssertNoPickup(target);
        AssertNoPickup(hidden);
        Assert.Equal(1ul << East, h.Server.CrateKnownOf(hidden.Slot));
        foreach (BattleClient c in h.Clients)
        {
            WireSniffer sniffer = SnifferOf(c);
            Assert.True(sniffer.EventBatches > 0, $"{c.Slot} got no event batch: the shot never went out");
            Assert.Equal(0, sniffer.CratePickedEvents);   // no crate-picked event on the wire, ever, to anyone
            Assert.Equal(c == shooter ? 2 : 0, sniffer.Crates);   // a Crate message per pickup, to the eater alone
        }
        _output.WriteLine($"on the wire: {string.Join(", ", h.Clients.Select(c => $"slot {c.Slot} {SnifferOf(c).EventBatches} event batches, {SnifferOf(c).Crates} Crate messages"))}");
        AssertClean(h);
    }

    // ---- allocation ----

    private static byte[] Encode(Action<BitWriter> write)
    {
        var w = new BitWriter();
        write(w);
        return w.Data.ToArray();
    }

    private static byte[] Input(int tick, int ack, sbyte throttle, float yaw) => Encode(w =>
    {
        var inputs = new InputCmd[3];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = new InputCmd(tick - i, throttle, 0, yaw, 0f, false, AmmoType.AP);
        BattleInputPacket.Write(w, ack, inputs);
    });

    /// <summary>
    /// A royale with crates in every snapshot, two sight passes a second and two pickups within the window (the western seat drives
    /// over the crate ahead, then backs over the one behind): the server's tick allocates nothing (the sight pass, the rows, the
    /// crate messages), measured through a <see cref="FakeServerTransport"/> as <see cref="BattleServerTests"/> does.
    /// </summary>
    [Fact]
    public void A_royale_with_crates_in_sight_and_pickups_allocates_nothing_per_tick()
    {
        Allocation.AssertNone(() =>
        {
            const int WarmUpTicks = 30, MeasuredTicks = 300, ForwardTicks = 90;
            var net = new FakeServerTransport();
            var log = new List<string>();
            var server = new BattleServer(net, TestMaps.Options(TestMaps.RoyaleStandIn()) with { Mode = BattleMode.Royale }, log.Add);
            server.CratePlacerOverride = TestMaps.StandInCratePlacer;
            server.ZonePickerOverride = TestMaps.StandInWideZone;
            for (int peer = 1; peer <= 2; peer++)
            {
                net.Connect(peer);
                net.Receive(peer, Encode(w => BattleMessages.Write(w, new HelloMessage(BattleProtocol.Version, $"p{peer}", 0))));
                server.Tick();
            }
            net.Receive(1, Encode(w => BattleMessages.Write(w, new RoomCommandMessage(RoomCommandKind.StartMatch, string.Empty))));
            server.Tick();
            Assert.True(server.MatchRunning);
            Simulation sim = server.Simulation!;
            // the seat whose tank stands on the western spawn drives; the other sits (the spawn order is the seed's)
            byte driver = (byte)(sim.Tanks[sim.SlotOf(server.TankOf(0))].Position.X < sim.Tanks[sim.SlotOf(server.TankOf(1))].Position.X ? 0 : 1);
            Assert.Equal(40f, sim.Tanks[sim.SlotOf(server.TankOf(driver))].Position.X);
            float yaw = sim.Tanks[sim.SlotOf(server.TankOf(driver))].Yaw;

            int firstTick = sim.Tick + 1, total = WarmUpTicks + MeasuredTicks;
            var packets = new byte[2][][];
            for (int slot = 0; slot < 2; slot++)
            {
                packets[slot] = new byte[total][];
                for (int i = 0; i < total; i++)
                    packets[slot][i] = Input(firstTick + i, ack: System.Math.Max(0, firstTick + i - 2), throttle: slot != driver ? (sbyte)0 : i < WarmUpTicks + ForwardTicks ? (sbyte)1 : (sbyte)-1, yaw);
            }
            for (int i = 0; i < WarmUpTicks; i++)
            {
                for (int slot = 0; slot < 2; slot++) net.Receive(slot + 1, packets[slot][i]);
                server.Tick();
            }

            Allocation.Window window = Allocation.Start();
            for (int i = WarmUpTicks; i < total; i++)
            {
                for (int slot = 0; slot < 2; slot++) net.Receive(slot + 1, packets[slot][i]);
                server.Tick();
            }
            Allocation.Measured measured = window.Stop();

            _output.WriteLine($"royale with crates, 2 humans, 2 pickups: {measured.Bytes} bytes allocated over {MeasuredTicks} ticks; sight passes {server.CrateSightPasses} in {server.CrateSightTime.TotalMilliseconds:F3} ms");
            Assert.True(server.MatchRunning);
            CrateSet crates = server.Rules!.Crates!;
            Assert.Equal(server.TankOf(driver), crates.TakenBy(Ahead));
            Assert.Equal(server.TankOf(driver), crates.TakenBy(Behind));
            Assert.Equal(2, net.CountTo(driver + 1, BattleMessage.Crate));
            Assert.Equal(0, net.CountTo(2 - driver, BattleMessage.Crate));
            Assert.True(net.CountTo(1, BattleMessage.Snapshot) >= total);
            return measured;
        });
    }
}

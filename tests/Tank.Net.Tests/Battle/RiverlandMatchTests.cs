using System.Diagnostics;
using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Xunit.Abstractions;
using BitReader = Tank.Net.Protocol.BitReader;
using BitWriter = Tank.Net.Protocol.BitWriter;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// Matches on the riverland and on the small mixed map (riverland design §5, §7): the match start fits in a MiB, every
/// mode runs, clients see the server's ground, water and props, and the prediction agrees with the server driving over
/// water, sand and road and through trees. Ramming a house may cost a centimetre-level correction or two (plan deviation
/// 7): the client keeps it standing until the server's word arrives, so it doesn't slow on the ruin until then.
/// </summary>
public sealed class RiverlandMatchTests
{
    private const float MiB = 1 << 20;
    private const float SpawnReach = 20f;
    private const float SteerDeadband = 0.02f;   // rad, ~1°: a tick's turn is 1.4° for the medium
    private const float Lookahead = 12f;         // m ahead along the lane the steering aims at
    private const double ThroughTreesSeconds = 15.0;
    private const double ToHouseSeconds = 8.0;

    /// <summary>
    /// Stand still this long before comparing positions: the client predicts a few ticks ahead of the server, and a medium
    /// tank at 57.5 km/h on the road takes 1.8 s to brake to a stop.
    /// </summary>
    private const double SettleSeconds = 2.5;

    /// <summary>The house run: north along x = <see cref="HouseLaneX"/> into the house's south wall, and stop past z <see cref="HouseLaneEnd"/>.</summary>
    private const float HouseLaneX = 252f, HouseLaneEnd = 80f;

    private readonly ITestOutputHelper _output;

    public RiverlandMatchTests(ITestOutputHelper output) => _output = output;

    // ---- the riverland ----

    [Fact]
    public void A_riverland_match_start_is_under_one_MiB_and_round_trips()
    {
        MapData map = MapCatalog.Riverland.Data;
        var w = new BitWriter(1 << 20);
        var watch = Stopwatch.StartNew();
        BattleMessages.Write(w, new MatchStartMessage(BattleMode.TeamBattle, 1, 0, 12600, map, Array.Empty<PropState>(), 1));
        double encodeMs = watch.Elapsed.TotalMilliseconds;
        byte[] bytes = w.Data.ToArray();
        _output.WriteLine($"riverland match start: {bytes.Length} bytes ({bytes.Length / 1024.0:F0} KiB), {map.Props.Length} props, encoded in {encodeMs:F1} ms");
        Assert.True(bytes.Length < MiB, $"the riverland's match start is {bytes.Length} bytes");

        var r = new BitReader(bytes);
        Assert.Equal((byte)BattleMessage.MatchStart, r.ReadByte());
        watch.Restart();
        MapData back = BattleMessages.ReadMatchStart(ref r).Map!;
        _output.WriteLine($"decoded in {watch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(map.SamplesX, back.SamplesX);
        Assert.Equal(map.SamplesZ, back.SamplesZ);
        Assert.Equal(map.CellSize, back.CellSize);
        Assert.Equal(map.Border, back.Border);
        Assert.Equal(map.HeightsCm, back.HeightsCm);
        Assert.Equal(map.Props, back.Props);
        Assert.Equal(map.Surface, back.Surface);
        Assert.Equal(map.WaterLevelCm, back.WaterLevelCm);
    }

    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    [InlineData(BattleMode.FreeForAll)]
    public void Every_mode_starts_and_runs_on_riverland(BattleMode mode)
    {
        var log = new List<string>();
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Map = MapCatalog.Riverland }, log: log.Add);
        h.AddClient("A");
        h.AddClient("B");
        h.Run(0.3);
        BattleClient host = h.Clients.Single(c => c.IsHost);
        host.SetMode(mode);
        host.SetBots(3);
        h.Run(0.1);
        host.StartMatch();
        for (int frame = 0; frame < 240 && !h.Server.MatchRunning; frame++) h.Run(1.0 / 120);
        Assert.True(h.Server.MatchRunning, "the match didn't start");

        Simulation sim = h.Server.Simulation!;
        IReadOnlyList<SpawnPoint> spawns = ModeRules.HasTeams(mode) ? RiverlandMap.Spawns.Team : RiverlandMap.Spawns.Solo;
        Assert.Equal(5, sim.Tanks.Length);
        foreach (TankState t in sim.Tanks)
        {
            float nearest = spawns.Min(s => Vector2.Distance(s.Position, t.Position));
            Assert.True(nearest < SpawnReach, $"tank {t.Id} started at {t.Position}, {nearest:F1} m from the nearest spawn");
        }

        h.Run(10.0, (c, t) => Wander(c, t));
        foreach (BattleClient c in h.Clients)
        {
            Assert.Equal(BattlePhase.InMatch, c.Phase);
            Assert.True(c.HasTank, $"{c.Slot} has no tank");
            Assert.Null(c.LastPacketError);
        }
        Assert.DoesNotContain(log, line => line.Contains("error", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_late_joiner_gets_the_same_riverland()
    {
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Map = MapCatalog.Riverland });
        h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        h.Run(2.0);   // StartMatch ran the match's first second

        BattleClient late = h.AddClient("Late");
        h.Run(1.0);
        Assert.Equal(BattlePhase.InMatch, late.Phase);
        Assert.True(late.HasTank);
        MapData server = MapData.Of(h.Server.Simulation!.Field), seen = MapData.Of(late.Field!);
        Assert.Equal(server.HeightsCm, seen.HeightsCm);
        Assert.Equal(server.Props, seen.Props);
        Assert.Equal(server.Surface, seen.Surface);
        Assert.Equal(server.WaterLevelCm, seen.WaterLevelCm);
    }

    // ---- the mixed map ----

    [Fact]
    public void Clients_see_the_same_ground_and_water_as_the_server()
    {
        var h = new BattleHarness(TestMaps.Options(TestMaps.Mixed()));
        h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        Battlefield server = h.Server.Simulation!.Field;
        Assert.True(server.WaterLevel.HasValue, "the mixed map has water");

        var rng = new Random(14);
        var kinds = new HashSet<SurfaceKind>();
        int wet = 0;
        for (int n = 0; n < 200; n++)
        {
            var p = new Vector2((float)rng.NextDouble() * 300f, (float)rng.NextDouble() * 300f);
            kinds.Add(server.SurfaceAt(p));
            if (server.WaterDepthAt(p) > 0f) wet++;
            foreach (BattleClient c in h.Clients)
            {
                Battlefield client = c.Field!;
                Assert.Equal(server.SurfaceAt(p), client.SurfaceAt(p));
                Assert.Equal(server.WaterDepthAt(p), client.WaterDepthAt(p), 0.005f);
                Assert.Equal(server.SpeedFactorAt(p), client.SpeedFactorAt(p));
            }
        }
        Assert.True(wet > 0, "no point fell in the water");
        Assert.True(kinds.Count >= 3, $"only {string.Join(", ", kinds)} sampled");
        // And one point of each: the ditch, the ford, a bank, the road and the grass.
        foreach (Vector2 p in new Vector2[] { new(150, 120), new(150, TestMaps.RoadZ), new(137, 120), new(100, TestMaps.RoadZ), new(50, 50) })
            foreach (BattleClient c in h.Clients)
            {
                Assert.Equal(server.SurfaceAt(p), c.Field!.SurfaceAt(p));
                Assert.Equal(server.WaterDepthAt(p), c.Field.WaterDepthAt(p), 0.005f);
                Assert.Equal(server.SpeedFactorAt(p), c.Field.SpeedFactorAt(p));
            }
    }

    [Fact]
    public void Driving_through_water_sand_road_and_trees_is_never_corrected()
    {
        (BattleHarness h, BattleClient a, DriveLog drive) = DriveThroughTheTrees();
        h.Run(SettleSeconds, Still);   // the client predicts a few ticks ahead of the server: compare once both stand still

        _output.WriteLine($"A: {drive}; {FelledTrees(h.Server.Simulation!.Field)} trees down");
        Assert.True(drive.Wet, "A never drove in the water");
        Assert.True(drive.Sand, "A never drove on sand");
        Assert.True(drive.Road, "A never drove on the road");
        Assert.Equal(0, a.Predictor.Corrections);
        Assert.Equal(h.ServerTankOf(a).Position, a.Own.Position);
        Assert.True(FelledTrees(h.Server.Simulation!.Field) >= 2, $"only {FelledTrees(h.Server.Simulation!.Field)} trees down");
        Assert.All(h.Clients, c => Assert.Equal(0, c.Predictor.Corrections));
    }

    [Fact]
    public void After_ramming_trees_and_a_house_both_sides_agree_on_every_prop()
    {
        (BattleHarness h, BattleClient a, _) = DriveThroughTheTrees();
        Assert.Equal(TankCatalog.Medium, a.Own.Spec);
        Battlefield server = h.Server.Simulation!.Field;
        Prop house = server.Props.Single(p => p.Kind == PropKind.House);

        float rammedAt = 0f;
        h.Run(ToHouseSeconds, (c, t) =>
        {
            if (c != 0) return LocalCommand.None;
            TankState own = a.Own;
            if (server.IsStanding(house.Id)) rammedAt = MathF.Abs(h.ServerTankOf(a).Speed);   // the server's, the last tick before the house fell
            sbyte throttle = (sbyte)(own.Position.Y > HouseLaneEnd ? 1 : 0);
            return Steer(own, new Vector2(HouseLaneX, own.Position.Y - Lookahead), throttle);
        });
        h.Run(SettleSeconds, Still);

        _output.WriteLine($"rammed the house at {rammedAt * 3.6f:F1} km/h; A corrected {a.Predictor.Corrections} times, at most {a.Predictor.MaxCorrection:F3} m, " +
                          $"mean {a.Predictor.MeanCorrection:F3} m; {FelledTrees(server)} trees down");
        Assert.False(server.IsStanding(house.Id), "the house still stands");
        Assert.True(rammedAt > 20f / 3.6f, $"rammed at {rammedAt * 3.6f:F1} km/h");
        Assert.True(FelledTrees(server) >= 2, $"only {FelledTrees(server)} trees down");
        foreach (BattleClient c in h.Clients)
            foreach (Prop p in server.Props)
            {
                int max = p.MaxDurability;
                Assert.Equal(BattleProtocol.QuantizeDurability(server.Durability(p.Id), max), BattleProtocol.QuantizeDurability(c.Field!.Durability(p.Id), max));
                Assert.Equal(server.IsStanding(p.Id), c.Field.IsStanding(p.Id));
            }
        Assert.True(a.Predictor.Corrections <= 3, $"corrected {a.Predictor.Corrections} times");
        Assert.True(a.Predictor.MaxCorrection < 0.5f, $"corrected by up to {a.Predictor.MaxCorrection} m");
        Assert.True(Vector2.Distance(h.ServerTankOf(a).Position, a.Own.Position) < 0.01f, "settled on the server's position");
    }

    // ---- driving ----

    /// <summary>What ground A's hull centre crossed on the way.</summary>
    private sealed class DriveLog
    {
        public bool Wet, Sand, Road;
        public float MaxSpeed;

        public void Note(Battlefield field, in TankState t)
        {
            Wet |= field.WaterDepthAt(t.Position) > 0f;
            Sand |= field.SurfaceAt(t.Position) == SurfaceKind.Sand && field.WaterDepthAt(t.Position) == 0f;
            Road |= field.SurfaceAt(t.Position) == SurfaceKind.Road;
            MaxSpeed = MathF.Max(MaxSpeed, MathF.Abs(t.Speed));
        }

        public override string ToString() => $"wet {Wet}, sand {Sand}, road {Road}, top speed {MaxSpeed * 3.6f:F1} km/h";
    }

    /// <summary>
    /// A team battle on the mixed map over a perfect link: A (west) drives flat out east along the road, over the sand
    /// banks and through the ford, then through the row of trees, for <see cref="ThroughTreesSeconds"/>; B stands still.
    /// </summary>
    private static (BattleHarness H, BattleClient A, DriveLog Drive) DriveThroughTheTrees()
    {
        var h = new BattleHarness(TestMaps.Options(TestMaps.Mixed()));
        BattleClient a = h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        Assert.Equal(0, a.Own.Team);
        var drive = new DriveLog();
        h.Run(ThroughTreesSeconds, (c, t) =>
        {
            if (c != 0) return LocalCommand.None;
            TankState own = a.Own;
            drive.Note(a.Field!, own);
            return Steer(own, new Vector2(own.Position.X + Lookahead, TestMaps.RoadZ), throttle: 1);
        });
        return (h, a, drive);
    }

    /// <summary>Turns the hull towards <paramref name="target"/> (no turning inside the deadband), turret ahead, no fire.</summary>
    private static LocalCommand Steer(in TankState own, Vector2 target, sbyte throttle)
    {
        float error = Angles.Wrap(Angles.YawOf(target - own.Position) - own.Yaw);
        sbyte steer = error > SteerDeadband ? (sbyte)-1 : error < -SteerDeadband ? (sbyte)1 : (sbyte)0;   // +yaw is left; Steer +1 turns right
        return new LocalCommand(throttle, steer, own.Yaw, 0f, false);
    }

    private static int FelledTrees(Battlefield field) => field.Props.Count(p => p.Kind == PropKind.Tree && !field.IsStanding(p.Id));

    private static LocalCommand Still(int client, double t) => new(0, 0, 0f, 0f, false);

    /// <summary>Drives about without firing (the battle tests' busy human, gun quiet).</summary>
    private static LocalCommand Wander(int client, double t)
    {
        int phase = (int)(t / 0.7) + client * 3;
        sbyte throttle = (sbyte)((phase % 4) switch { 0 => 1, 1 => 1, 2 => -1, _ => 0 });
        sbyte steer = (sbyte)((phase % 3) switch { 0 => 1, 1 => -1, _ => 0 });
        return new LocalCommand(throttle, steer, (float)(t * 0.4 + client), 0.05f, false);
    }
}

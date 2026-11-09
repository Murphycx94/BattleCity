using System.Diagnostics;
using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Maps;
using Tank.Sim.Maps.Riverland;
using Tank.Sim.Navigation;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>The wall-clock tests: run on their own, after the parallel ones, so the other tests' threads don't time them.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}

/// <summary>
/// What generating the riverland and a tick on it cost (riverland design §3.12, §4.1). Debug builds only print and check
/// loose limits; the real ones hold in Release. Each takes the best of a few repeats (after a warm-up), so a moment of
/// load on the machine doesn't fail it, while a slower generator or tick still does.
/// </summary>
[Collection(TimingCollection.Name)]
[Trait("Category", "Benchmark")]   // wall-clock limits; CI skips them (coverage and shared runners, docs/04 §5)
public sealed class RiverlandPerformanceTests
{
    private const int Repeats = 3;

    private readonly ITestOutputHelper _output;

    public RiverlandPerformanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Terrain_alone_generates_quickly()
    {
        // The ground alone (heights, grids, villages); the whole map's budget is the next test's.
        double best = BestSeconds(() => RiverlandTerrain.Build(), "RiverlandTerrain.Build()");
#if DEBUG
        const double Limit = 4.0;
#else
        const double Limit = 0.8;
#endif
        Assert.True(best <= Limit, $"the terrain took {best:F2} s at best (limit {Limit} s)");
    }

    [Fact]
    public void The_whole_map_generates_within_budget()
    {
        double best = BestSeconds(() => RiverlandMap.Create(), "RiverlandMap.Create()");
#if DEBUG
        const double Limit = 6.0;
#else
        const double Limit = 1.5;
#endif
        Assert.True(best <= Limit, $"generation took {best:F2} s at best (limit {Limit} s)");
    }

    [Fact]
    public void The_navigation_grid_builds_within_budget()
    {
        Battlefield field = RiverlandFixture.Field;
        double best = BestSeconds(() => NavGrid.Build(field), "NavGrid.Build(riverland)");
#if DEBUG
        const double Limit = 4.0;
#else
        const double Limit = 0.5;
#endif
        Assert.True(best <= Limit, $"the grid took {best:F2} s at best (limit {Limit} s)");
    }

    [Fact]
    public void Searches_across_the_riverland_are_cheap()
    {
        IReadOnlyList<Vector2> waypoints = MapCatalog.Riverland.Waypoints;
        var finder = new PathFinder(RiverlandFixture.Grid, RiverlandFixture.Field);
        for (int k = 1; k < waypoints.Count; k++) finder.FindNow(waypoints[0], waypoints[k]);   // warm-up

        long ticks = 0, expansions = 0;
        int searches = 0, found = 0, most = 0;
        for (int a = 0; a < waypoints.Count; a++)
        for (int b = 0; b < waypoints.Count; b++)
        {
            if (a == b) continue;
            long start = Stopwatch.GetTimestamp();
            PathStatus status = finder.FindNow(waypoints[a], waypoints[b]);
            ticks += Stopwatch.GetTimestamp() - start;
            searches++;
            if (status == PathStatus.Found) found++;
            expansions += finder.Expansions;
            most = Math.Max(most, finder.Expansions);
        }
        double microseconds = Milliseconds(ticks) * 1000.0 / expansions;
        _output.WriteLine($"{searches} searches between the riverland's {waypoints.Count} waypoints: {found} found, {expansions / (double)searches:F0} expansions on average, " +
                          $"{most} at most, {Milliseconds(ticks):F0} ms in all, {microseconds:F3} µs an expansion");
#if DEBUG
        const double Limit = 2.0;
#else
        const double Limit = 0.3;
#endif
        Assert.Equal(waypoints.Count * (waypoints.Count - 1), searches);
        Assert.Equal(searches, found);
        Assert.True(microseconds <= Limit, $"{microseconds:F3} µs an expansion (limit {Limit} µs)");
    }

    [Fact]
    public void A_tick_with_ten_tanks_is_cheap()
    {
        BotArena arena = RiverlandFingerprintTests.TenBots();
        BotDirector director = arena.Director;
        Simulation sim = arena.Sim;
        var inputs = new InputCmd[sim.TankCount];
        const int Warmup = 300, Block = 300;
        var blocks = new double[Repeats];
        for (int i = 0; i < Warmup + Repeats * Block; i++)
        {
            long start = Stopwatch.GetTimestamp();
            director.Update();
            for (int k = 0; k < inputs.Length; k++) inputs[k] = director.Command(sim.Tanks[k].Id, sim.Tick + 1);
            sim.Step(inputs);
            if (i >= Warmup) blocks[(i - Warmup) / Block] += Milliseconds(Stopwatch.GetTimestamp() - start) / Block;
            arena.Vision.Update();   // timed by the spotting test
            arena.Rules.Update();
        }
        double ms = blocks.Min();
        _output.WriteLine($"ten tanks on the riverland: {ms:F3} ms a tick at best (the bot director with its searches, the bots' commands and Simulation.Step; blocks of {Block} ticks: {Join(blocks)})");
        // The bots' searches have a budget of their own, about 0.3 ms a tick (PathService.ExpansionsPerTick); the server's
        // gate is the whole tick's p99 under 2 ms with nine bots (stage 5C task 9).
#if DEBUG
        const double Limit = 5.0;
#else
        const double Limit = 1.0;
#endif
        Assert.True(ms <= Limit, $"a tick took {ms:F3} ms at best (limit {Limit} ms)");
    }

    [Fact]
    public void Spotting_sixteen_tanks_is_cheap()
    {
        var sim = new Simulation(MapCatalog.Riverland.NewBattlefield());
        TankSpec[] kinds = { TankCatalog.Medium, TankCatalog.Heavy, TankCatalog.Light, TankCatalog.Td };
        for (int k = 0; k < RiverlandMap.Spawns.Solo.Count; k++)
            sim.AddTank(kinds[k % kinds.Length], RiverlandMap.Spawns.Solo[k].Position, RiverlandMap.Spawns.Solo[k].Yaw, team: (byte)k);
        var vision = new VisionTracker(sim);
        var inputs = new InputCmd[sim.TankCount];
        for (int k = 0; k < inputs.Length; k++) inputs[k] = TestWorld.Cmd(throttle: 1, steer: (sbyte)(k % 3 - 1));
        const int Warmup = 60, Block = 300;
        var spent = new double[Repeats];
        var updates = new int[Repeats];
        for (int i = 0; i < Warmup + Repeats * Block; i++)
        {
            sim.Step(inputs);
            long start = Stopwatch.GetTimestamp();
            vision.Update();
            bool looked = sim.Tick % VisionTracker.UpdateInterval == 0;   // on the other ticks it returns at once
            if (i < Warmup || !looked) continue;
            int block = (i - Warmup) / Block;
            spent[block] += Milliseconds(Stopwatch.GetTimestamp() - start);
            updates[block]++;
        }
        double[] perUpdate = spent.Select((ms, b) => ms / updates[b]).ToArray();
        double best = perUpdate.Min();
        _output.WriteLine($"sixteen tanks on the riverland: {best:F3} ms an update that looks, at best ({updates.Sum()} of them; blocks: {Join(perUpdate)})");
#if DEBUG
        const double Limit = 8.0;
#else
        const double Limit = 1.5;
#endif
        Assert.True(best <= Limit, $"an update took {best:F3} ms at best (limit {Limit} ms)");
    }

    /// <summary>Runs <paramref name="work"/> once to warm up, then <see cref="Repeats"/> times; the fastest of those, in seconds.</summary>
    private double BestSeconds(Action work, string what)
    {
        var watch = Stopwatch.StartNew();
        work();
        double first = watch.Elapsed.TotalSeconds;
        var runs = new double[Repeats];
        for (int r = 0; r < Repeats; r++)
        {
            watch.Restart();
            work();
            runs[r] = watch.Elapsed.TotalSeconds;
        }
        _output.WriteLine($"{what}: {first * 1000:F0} ms, then {Join(runs.Select(s => s * 1000).ToArray(), "F0")} ms");
        return runs.Min();
    }

    private static string Join(double[] values, string format = "F3") =>
        string.Join(", ", values.Select(v => v.ToString(format, System.Globalization.CultureInfo.InvariantCulture)));

    private static double Milliseconds(long timestamps) => timestamps * 1000.0 / Stopwatch.Frequency;
}

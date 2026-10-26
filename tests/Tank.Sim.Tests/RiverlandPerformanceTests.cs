using System.Diagnostics;
using Tank.Sim.Maps;
using Tank.Sim.Maps.Riverland;
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
    public void A_tick_with_ten_tanks_is_cheap()
    {
        var (sim, vision, bots) = RiverlandFingerprintTests.TenBots();
        var inputs = new InputCmd[sim.TankCount];
        const int Warmup = 300, Block = 300;
        var blocks = new double[Repeats];
        for (int i = 0; i < Warmup + Repeats * Block; i++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int k = 0; k < bots.Length; k++) inputs[k] = bots[k].Command(sim, vision, sim.Tick + 1);
            sim.Step(inputs);
            if (i >= Warmup) blocks[(i - Warmup) / Block] += Milliseconds(Stopwatch.GetTimestamp() - start) / Block;
            vision.Update();   // timed by the spotting test
        }
        double ms = blocks.Min();
        _output.WriteLine($"ten tanks on the riverland: {ms:F3} ms a tick at best (bots and Simulation.Step; blocks of {Block} ticks: {Join(blocks)})");
#if DEBUG
        const double Limit = 3.0;
#else
        const double Limit = 0.5;
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

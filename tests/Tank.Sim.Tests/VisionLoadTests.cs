using Tank.Sim.Ai;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// What spotting costs with a royale's sixteen tanks on the riverland, next to ten (stage 6 design §3.5: "16 tanks: the
/// cost of spotting … measured"; stage 6A plan, task 6): the tanks start on the sixteen solo spawns (the first ten of the
/// seating for ten), bots, and play a minute of a royale to spread out; then every tick's <see cref="VisionTracker.Update"/>
/// is timed for another minute, the bots playing on. The bots have no rounds, so all the tanks stay in. The work grows with the pairs of tanks (16 × 15 against 10 × 9). Wall-clock:
/// in the <see cref="TimingCollection"/>; Debug builds only print and check a loose limit.
/// </summary>
[Collection(TimingCollection.Name)]
public sealed class VisionLoadTests
{
    private const int Tps = SimConstants.TickRate;

    private readonly ITestOutputHelper _output;

    public VisionLoadTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Spotting_sixteen_tanks_on_the_riverland_stays_cheap()
    {
        Measured ten = Measure(10), sixteen = Measure(RoyaleMatch.Seats);
        _output.WriteLine($"ten tanks: {ten}");
        _output.WriteLine($"sixteen tanks: {sixteen}");
        _output.WriteLine($"sixteen against ten: {sixteen.LookMean / ten.LookMean:F2} × an update that looks (pairs: {16 * 15 / (10.0 * 9):F2} ×)");
#if DEBUG
        const double Limit = 20.0;
        Assert.True(sixteen.Max < Limit, $"an update took {sixteen.Max:F3} ms (limit {Limit} ms)");
#else
        const double Limit = 1.5;   // as RiverlandPerformanceTests' sixteen tanks driving straight on: here the bots' own play
        Assert.True(sixteen.LookP99 < Limit, $"an update that looks took {sixteen.LookP99:F3} ms at p99 (limit {Limit} ms)");
#endif
    }

    /// <summary>Every tick's time (p50, p99, max), and the updates that look (every <see cref="VisionTracker.UpdateInterval"/> ticks): mean and p99, ms.</summary>
    private readonly record struct Measured(int Tanks, int Ticks, double P50, double P99, double Max, double LookMean, double LookP99)
    {
        public override string ToString() =>
            $"{Tanks} tanks over {Ticks} ticks: p50 {P50:F3} ms, p99 {P99:F3} ms, max {Max:F3} ms a tick; an update that looks {LookMean:F3} ms on average, p99 {LookP99:F3} ms";
    }

    private static Measured Measure(int tanks)
    {
        BotArena arena = BotArena.Royale(MapCatalog.Riverland, 3);
        arena.Recording = false;
        var seating = new BotSeating(3);
        for (int slot = 0; slot < tanks; slot++)
        {
            Bot bot = arena.Bot(TankCatalog.All[seating.NextTankIndex()], 0, seating.BotSeed(slot));
            ref TankState t = ref arena.Sim.TankRef(bot.TankId);
            (t.ApLeft, t.HeLeft) = (0, 0);   // no rounds: every tank stays in, the most there is to spot
        }
        arena.Director.AssignRoles();
        arena.Run(60 * Tps);   // spread out

        var times = new List<double>(60 * Tps);
        arena.VisionTimes = times;
        int start = arena.Sim.Tick;
        arena.Run(60 * Tps);
        arena.VisionTimes = null;

        double[] all = times.Order().ToArray();
        double[] looks = times.Where((_, k) => (start + 1 + k) % VisionTracker.UpdateInterval == 0).Order().ToArray();
        return new Measured(tanks, all.Length, all[all.Length / 2], all[(int)(all.Length * 0.99)], all[^1], looks.Average(), looks[(int)(looks.Length * 0.99)]);
    }
}

using Tank.Core.World;
using Tank.Net.Client;
using Tank.Net.Protocol;
using Tank.Net.Server;

namespace Tank.Net.Tests;

public sealed class ServerInputBufferTests
{
    [Fact]
    public void Take_ReturnsReceivedInput_AndDeduplicates()
    {
        var buf = new ServerInputBuffer();
        buf.Add(new InputCmd(5, 1, true), 4);
        buf.Add(new InputCmd(5, 1, true), 4);

        Assert.Equal(new InputCmd(5, 1, true), buf.Take(5));
        Assert.Equal(0, buf.MissingCount);
    }

    [Fact]
    public void MissingInput_RepeatsMovementWithoutFiring()
    {
        var buf = new ServerInputBuffer();
        buf.Add(new InputCmd(1, 3, true), 0);
        buf.Take(1);

        var repeated = buf.Take(2);

        Assert.Equal(3, repeated.Move);
        Assert.False(repeated.Fire);
        Assert.Equal(1, buf.MissingCount);
    }

    [Fact]
    public void LateInput_IsDropped_AndSlackIsReported()
    {
        var buf = new ServerInputBuffer();
        buf.Take(10);
        buf.Add(new InputCmd(9, 1, false), 10);
        buf.Add(new InputCmd(13, 1, false), 10);

        Assert.Equal(1, buf.LateCount);
        Assert.Equal(2, buf.SlackAt(11));
    }

    [Fact]
    public void ManyMissingInputs_StopTheTank()
    {
        var buf = new ServerInputBuffer();
        buf.Add(new InputCmd(1, 1, false), 0);
        buf.Take(1);

        for (int t = 2; t <= 1 + ServerInputBuffer.MaxRepeatedTicks; t++) Assert.Equal(1, buf.Take(t).Move);
        Assert.Equal(0, buf.Take(2 + ServerInputBuffer.MaxRepeatedTicks).Move);
    }

    [Fact]
    public void InputFarAheadOfServer_IsRejected_EvenRightAfterReset()
    {
        var buf = new ServerInputBuffer();
        buf.Reset();
        buf.Add(new InputCmd(100_000, 1, false), 0);
        Assert.Equal(-1, buf.NewestReceivedTick);
        Assert.Equal(0, buf.SlackAt(1));
    }
}

public sealed class ClockSyncTests
{
    [Fact]
    public void Initialize_PutsClientAheadByFullRttPlusTarget()
    {
        var clock = new ClockSync();
        clock.OnServerFeedback(0, 100, rttSeconds: 0.1);
        Assert.Equal(100 + 6 + 2, clock.Tick);
    }

    [Fact]
    public void LowSlack_SpeedsUp_HighSlack_SlowsDown()
    {
        var clock = new ClockSync();
        clock.Initialize(0, 0);
        for (int i = 0; i < 50; i++) clock.OnServerFeedback(-1, i, 0);
        Assert.True(clock.Rate > 1.0);

        for (int i = 0; i < 100; i++) clock.OnServerFeedback(8, i, 0);
        Assert.True(clock.Rate < 1.0);
        Assert.InRange(clock.Rate, 0.95, 1.05);
    }

    [Fact]
    public void HugeError_HardResyncs()
    {
        var clock = new ClockSync();
        clock.Initialize(0, 0);
        clock.OnServerFeedback(-100, 500, 0);
        Assert.Equal(1, clock.HardResyncs);
        Assert.Equal(502, clock.Tick);
    }

    [Fact]
    public void Advance_ProducesSixtyTicksPerSecond()
    {
        var clock = new ClockSync();
        clock.Initialize(0, 0);
        int total = 0;
        for (int i = 0; i < 144; i++) total += clock.Advance(1.0 / 144);
        Assert.InRange(total, 59, 61);
    }
}

public sealed class InterpolatorTests
{
    private static (SnapshotStore store, SnapshotInterpolator interp) Feed(int ticks, Func<int, double> arrival)
    {
        var store = new SnapshotStore();
        var interp = new SnapshotInterpolator(store);
        for (int t = 1; t <= ticks; t++)
        {
            var s = new Snapshot { Tick = t, EntityCount = 1 };
            s.Entities[0] = new NetEntity { Id = 1, Kind = EntityKind.Tank, X = t * 12 };
            store.Add(s);
            interp.OnSnapshot(t, arrival(t));
        }
        return (store, interp);
    }

    [Fact]
    public void Sample_InterpolatesBetweenSnapshots()
    {
        var (_, interp) = Feed(30, t => t / 60.0);
        double now = 30 / 60.0;
        interp.Advance(0, now);
        var output = new List<RenderEntity>();
        interp.Sample(output);

        double expectedTick = 30 - interp.DelayTicks;
        Assert.Single(output);
        Assert.Equal(expectedTick * 12, output[0].X, 3);
        Assert.False(output[0].Extrapolated);
    }

    [Fact]
    public void Jitter_RaisesTargetDelay()
    {
        var (_, steady) = Feed(120, t => t / 60.0);
        var (_, jittery) = Feed(120, t => t / 60.0 + (t % 3 == 0 ? 0.03 : 0));
        steady.Advance(0.016, 2.0);
        jittery.Advance(0.016, 2.0);
        Assert.True(jittery.TargetDelayTicks > steady.TargetDelayTicks + 1);
    }

    [Fact]
    public void Starvation_ExtrapolatesBoundedDistance()
    {
        var (_, interp) = Feed(30, t => t / 60.0);
        interp.Advance(0, 0.5);
        for (int i = 0; i < 60; i++) interp.Advance(1 / 60.0, 0.5 + i / 60.0); // a second with no packets
        var output = new List<RenderEntity>();
        interp.Sample(output);

        Assert.True(output[0].Extrapolated);
        Assert.InRange(output[0].X, 30 * 12, (30 + 6) * 12 + 0.001); // capped at 100ms ahead
    }
}

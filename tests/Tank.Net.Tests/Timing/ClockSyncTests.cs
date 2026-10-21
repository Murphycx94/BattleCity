using Tank.Net.Timing;

namespace Tank.Net.Tests.Timing;

/// <summary>The clock sync's arithmetic at 60 Hz; <see cref="ClockSync30Tests"/> covers the battle's 30 Hz.</summary>
public sealed class ClockSyncTests
{
    [Fact]
    public void Initialize_PutsClientAheadByFullRttPlusTarget()
    {
        var clock = new ClockSync(60);
        clock.OnServerFeedback(0, 100, rttSeconds: 0.1);
        Assert.Equal(100 + 6 + 2, clock.Tick);
    }

    [Fact]
    public void LowSlack_SpeedsUp_HighSlack_SlowsDown()
    {
        // The server's ticks stay well below the client's, as they do while the client leads: only the slack moves the
        // rate (a server tick at or past the client's would take the "fell behind" hard resync instead).
        var clock = new ClockSync(60);
        clock.Initialize(1000, 0);
        for (int i = 0; i < 50; i++) clock.OnServerFeedback(-1, i, 0);
        Assert.True(clock.Rate > 1.0);

        for (int i = 0; i < 100; i++) clock.OnServerFeedback(8, 50 + i, 0);
        Assert.True(clock.Rate < 1.0);
        Assert.InRange(clock.Rate, 0.95, 1.05);
        Assert.Equal(0, clock.HardResyncs);   // all of it on the dilation path
    }

    [Fact]
    public void HugeError_HardResyncs()
    {
        var clock = new ClockSync(60);
        clock.Initialize(500, 0);              // the client at 502
        clock.OnServerFeedback(2, 490, 0);     // the same server tick with the slack on target: the server isn't caught up…
        Assert.Equal(0, clock.HardResyncs);
        clock.OnServerFeedback(-100, 490, 0);  // … so this resync is the slack's alone
        Assert.Equal(1, clock.HardResyncs);
        Assert.Equal(492, clock.Tick);
    }

    [Fact]
    public void Advance_ProducesSixtyTicksPerSecond()
    {
        var clock = new ClockSync(60);
        clock.Initialize(0, 0);
        int total = 0;
        for (int i = 0; i < 144; i++) total += clock.Advance(1.0 / 144);
        Assert.InRange(total, 59, 61);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void A_tick_rate_that_is_not_positive_is_refused(int tickRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClockSync(tickRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServerRunner(new NoServer(), null, tickRate));
    }

    private sealed class NoServer : ITickServer
    {
        public void Tick() { }
    }
}

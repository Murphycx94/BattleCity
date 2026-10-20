using Tank.Client.Online;
using Tank.Net.Server;

namespace Tank.Art.Tests;

public class TickProbeTests
{
    private const int SlowMs = 40;   // a tick that sleeps this long takes at least as long

    /// <summary>A server whose next tick can be made slow, and whose match can be started inside a tick (as a StartMatch command does).</summary>
    private sealed class FakeServer : ITickServer
    {
        public bool MatchRunning;
        public bool SlowNext;
        public bool StartsMatchNext;

        public void Tick()
        {
            if (SlowNext) Thread.Sleep(SlowMs);
            if (StartsMatchNext) MatchRunning = true;
            SlowNext = StartsMatchNext = false;
        }
    }

    private static (TickProbe Probe, FakeServer Server) Make()
    {
        var server = new FakeServer();
        return (new TickProbe(server, () => server.MatchRunning), server);
    }

    [Fact]
    public void A_slow_tick_during_play_shows_as_the_worst()
    {
        var (probe, server) = Make();
        server.MatchRunning = true;
        for (int i = 0; i < 5; i++) probe.Tick();
        server.SlowNext = true;
        probe.Tick();
        Assert.True(probe.MaxMs >= SlowMs, $"max {probe.MaxMs} ms");
        Assert.True(probe.AverageMs > 0);
    }

    [Fact]
    public void The_tick_a_match_starts_in_is_left_out_and_starts_the_worst_over()
    {
        var (probe, server) = Make();
        server.SlowNext = true;   // something slow in the room, before any match
        probe.Tick();
        Assert.True(probe.MaxMs >= SlowMs);
        double average = probe.AverageMs;

        server.SlowNext = server.StartsMatchNext = true;   // the map build
        probe.Tick();
        Assert.Equal(0, probe.MaxMs);
        Assert.Equal(average, probe.AverageMs);   // not in the average either

        for (int i = 0; i < 5; i++) probe.Tick();   // ordinary match ticks: nowhere near the build's cost
        Assert.True(probe.MaxMs < SlowMs, $"max {probe.MaxMs} ms");
    }

    [Fact]
    public void A_second_match_starts_the_worst_over_again()
    {
        var (probe, server) = Make();
        server.MatchRunning = true;
        server.SlowNext = true;
        probe.Tick();   // a spike in the first match
        server.MatchRunning = false;   // back in the room
        probe.Tick();
        server.StartsMatchNext = true;
        probe.Tick();
        Assert.Equal(0, probe.MaxMs);
    }
}

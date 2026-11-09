using System.Numerics;
using Tank.Client.Match;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class ZoneTextTests
{
    private const int Hz = SimConstants.TickRate;

    private static readonly ZoneRing Ring1 = new(new Vector2(600, 600), 420), Ring2 = new(new Vector2(560, 640), 250);

    private static ZoneState Waiting(int ring, int ticksLeft, int percent = 1) => new(ring, ZonePhase.Waiting, Ring1, Ring2, true, ticksLeft, percent);
    private static ZoneState Shrinking(int ring, int ticksLeft, int percent = 2) => new(ring, ZonePhase.Shrinking, Ring1, Ring2, true, ticksLeft, percent);
    private static ZoneState Closed() => new(ZoneSchedule.Rings, ZonePhase.Closed, new ZoneRing(Ring2.Centre, 0), default, false, 0, 10);

    [Fact]
    public void Waiting_names_the_ring_of_five_and_the_time_to_the_shrink()
    {
        ZoneHud zone = ZoneHud.From(Waiting(2, 45 * Hz), outside: false);

        Assert.Equal(new ZoneHud(true, 2, ZonePhase.Waiting, 45, false, 1), zone);
        Assert.Equal("毒圈 2/5 · 0:45 后缩小", ZoneText.Line(zone));
    }

    [Fact]
    public void Shrinking_counts_down_the_shrink()
    {
        ZoneHud zone = ZoneHud.From(Shrinking(3, 30 * Hz, 4), outside: false);

        Assert.Equal(new ZoneHud(true, 3, ZonePhase.Shrinking, 30, false, 4), zone);
        Assert.Equal("毒圈缩小中 0:30", ZoneText.Line(zone));
    }

    [Fact]
    public void Closed_says_so_and_a_long_wait_shows_minutes()
    {
        Assert.Equal("毒圈已闭合", ZoneText.Line(ZoneHud.From(Closed(), outside: true)));
        Assert.Equal(ZoneText.ClosedLine, ZoneText.Line(ZoneHud.From(Closed(), outside: false)));
        Assert.Equal("毒圈 1/5 · 1:30 后缩小", ZoneText.Line(ZoneHud.From(Waiting(1, 90 * Hz, 0), outside: false)));
    }

    [Fact]
    public void Off_gives_an_empty_line()
    {
        ZoneHud off = ZoneHud.From(default, outside: true);

        Assert.False(off.On);
        Assert.Equal(default, off);
        Assert.Equal("", ZoneText.Line(off));
        Assert.Equal("", ZoneText.Line(new ZoneHud(false, 2, ZonePhase.Waiting, 45, false, 1)));
    }

    [Fact]
    public void Seconds_left_round_up_like_the_match_clock()
    {
        Assert.Equal(1, ZoneHud.From(Waiting(2, 1), false).SecondsLeft);
        Assert.Equal(1, ZoneHud.From(Waiting(2, Hz), false).SecondsLeft);
        Assert.Equal(2, ZoneHud.From(Waiting(2, Hz + 1), false).SecondsLeft);
        Assert.Equal(0, ZoneHud.From(Closed(), false).SecondsLeft);
    }

    [Fact]
    public void Outside_and_the_percent_are_carried_through()
    {
        ZoneHud zone = ZoneHud.From(Shrinking(3, 30 * Hz, 4), outside: true);

        Assert.True(zone.Outside);
        Assert.Equal(4, zone.Percent);
        Assert.False(ZoneHud.From(Shrinking(3, 30 * Hz, 4), outside: false).Outside);
    }

    [Fact]
    public void The_outside_warning_uses_the_minus_sign_not_a_hyphen()
    {
        string line = ZoneText.Outside(2);

        Assert.Equal("在安全区外 −2%/秒", line);
        Assert.Contains('−', line);
        Assert.DoesNotContain('-', line);
        Assert.Equal("在安全区外 −10%/秒", ZoneText.Outside(10));
    }
}

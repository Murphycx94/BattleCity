using Tank.Net.Client;

namespace Tank.Net.Tests.Battle;

/// <summary>The clock sync at the battle's 30 Hz: its tick-denominated constants mean twice the time they do at the old game's 60 Hz.</summary>
public sealed class ClockSync30Tests
{
    [Theory]
    [InlineData(0.0, 2)]      // nothing to cover but the two ticks of slack
    [InlineData(0.1005, 6)]   // 100.5 ms is 3.015 ticks: round up, then the 2 ticks of slack
    [InlineData(0.2005, 9)]   // 6.015 + 2
    public void The_client_starts_one_round_trip_plus_two_ticks_ahead_of_the_server(double rttSeconds, int leadTicks)
    {
        var clock = new ClockSync(30);
        clock.OnServerFeedback(inputSlack: 2, serverTick: 1000, rttSeconds);
        Assert.True(clock.Synced);
        Assert.Equal(1000 + leadTicks, clock.Tick);
    }

    [Fact]
    public void The_lead_at_30_Hz_covers_the_same_round_trip_in_half_the_ticks_it_takes_at_60_Hz()
    {
        var thirty = new ClockSync(30);
        var sixty = new ClockSync(60);
        thirty.Initialize(500, 0.2);
        sixty.Initialize(500, 0.2);
        Assert.Equal(500 + 6 + 2, thirty.Tick);
        Assert.Equal(500 + 12 + 2, sixty.Tick);
    }

    [Fact]
    public void Time_is_turned_into_ticks_at_the_battle_rate()
    {
        var clock = new ClockSync(30);
        clock.Initialize(0, 0);
        Assert.Equal(0, clock.Advance(0.02));   // 0.6 of a tick
        Assert.Equal(1, clock.Advance(0.02));   // 1.2
        Assert.Equal(0.2, clock.Alpha, 6);
        Assert.Equal(3, clock.Advance(0.1));    // 3.2 more ticks' worth with the 0.2 left over
    }

    [Fact]
    public void A_stall_longer_than_8_ticks_skips_ahead_instead_of_fast_forwarding()
    {
        var clock = new ClockSync(30);
        clock.Initialize(0, 0);
        int start = clock.Tick;
        Assert.Equal(8, clock.Advance(1.0));    // 30 ticks' worth: 8 simulated …
        Assert.Equal(start + 22, clock.Tick);   // … and the other 22 skipped
    }

    [Fact]
    public void A_client_the_server_has_caught_up_with_resyncs_ahead_even_when_the_slack_feedback_is_quiet()
    {
        // A long frame the engine's clamped delta never made up for: the client's tick is behind the server's, every
        // input it sends is late and rejected, and the server's slack reads 0 (nothing received), close to the target.
        var clock = new ClockSync(30);
        clock.OnServerFeedback(2, 100, 0.0);
        Assert.Equal(102, clock.Tick);
        clock.OnServerFeedback(0, 101, 0.0);   // still ahead by one: a normal frame
        Assert.Equal(0, clock.HardResyncs);
        clock.OnServerFeedback(0, 102, 0.0);   // the server reached the client's tick
        Assert.Equal(1, clock.HardResyncs);
        Assert.Equal(104, clock.Tick);         // ahead again by the two ticks of slack
    }

    [Fact]
    public void Slack_feedback_far_from_the_target_resyncs_at_20_ticks_which_is_two_thirds_of_a_second()
    {
        var clock = new ClockSync(30);
        clock.OnServerFeedback(2, 100, 0.05);
        RunSecond(clock);   // the client keeps ticking along with the server
        clock.OnServerFeedback(2 + 20, 130, 0.05);   // exactly 20 off the target: not yet
        Assert.Equal(0, clock.HardResyncs);
        RunSecond(clock);
        clock.OnServerFeedback(2 + 21, 160, 0.05);
        Assert.Equal(1, clock.HardResyncs);
        Assert.Equal(160 + 4, clock.Tick);   // ceil(0.05 * 30 + 2) = 4 ticks of lead
    }

    [Fact]
    public void Slack_above_the_target_slows_the_clock_a_little_and_below_it_speeds_it_up()
    {
        var late = new ClockSync(30);
        for (int i = 0; i < 40; i++) late.OnServerFeedback(8, 100 + i, 0.05);   // inputs arrive 8 ticks early: ahead of the server
        Assert.InRange(late.Rate, 0.94, 0.999);
        var early = new ClockSync(30);
        for (int i = 0; i < 40; i++) early.OnServerFeedback(-4, 100 + i, 0.05);
        Assert.InRange(early.Rate, 1.001, 1.06);
    }

    /// <summary>A second of frames as the client runs them: the ticks Advance asks for are each taken with NextTick.</summary>
    private static void RunSecond(ClockSync clock)
    {
        int ticks = clock.Advance(1.0);
        for (int i = 0; i < ticks; i++) clock.NextTick();
    }
}

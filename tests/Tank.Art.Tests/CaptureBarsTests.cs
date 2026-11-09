using System.Globalization;
using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class CaptureBarsTests
{
    private static MatchBlock Progress(byte base0, byte base1) => new() { Phase = MatchPhase.Playing, BaseProgress0 = base0, BaseProgress1 = base1 };

    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    [InlineData(BattleMode.FreeForAll)]
    public void No_bars_outside_a_standard_match(BattleMode mode)
    {
        CaptureBars bars = CaptureText.Bars(mode, 0, Progress(50, 50));

        Assert.Equal(0, bars.Count);
        Assert.Equal(CaptureBars.None, bars);
    }

    [Fact]
    public void The_own_base_comes_first_and_empty_bases_are_left_out()
    {
        CaptureBars fromZero = CaptureText.Bars(BattleMode.Standard, 0, Progress(37, 62));
        CaptureBars fromOne = CaptureText.Bars(BattleMode.Standard, 1, Progress(37, 62));
        CaptureBars ownOnly = CaptureText.Bars(BattleMode.Standard, 0, Progress(37, 0));
        CaptureBars theirsOnly = CaptureText.Bars(BattleMode.Standard, 0, Progress(0, 62));

        Assert.Equal((2, new CaptureBar(true, 37), new CaptureBar(false, 62)), (fromZero.Count, fromZero[0], fromZero[1]));
        Assert.Equal((2, new CaptureBar(true, 62), new CaptureBar(false, 37)), (fromOne.Count, fromOne[0], fromOne[1]));
        Assert.Equal((1, new CaptureBar(true, 37)), (ownOnly.Count, ownOnly[0]));
        Assert.Equal((1, new CaptureBar(false, 62)), (theirsOnly.Count, theirsOnly[0]));
        Assert.Equal(0, CaptureText.Bars(BattleMode.Standard, 0, Progress(0, 0)).Count);
    }

    [Fact]
    public void Labels_read_whose_base_and_the_points()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "de-DE" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Assert.Equal("我方基地 37", CaptureText.Label(new CaptureBar(true, 37)));
                Assert.Equal("敌方基地 100", CaptureText.Label(new CaptureBar(false, 100)));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void The_indexer_refuses_a_bar_that_is_not_there()
    {
        CaptureBars one = CaptureText.Bars(BattleMode.Standard, 0, Progress(37, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => one[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => one[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => CaptureBars.None[0]);
    }

    [Fact]
    public void Working_out_the_bars_allocates_nothing()
    {
        MatchBlock match = Progress(37, 62);
        CaptureText.Bars(BattleMode.Standard, 0, match);   // warm up

        long before = GC.GetAllocatedBytesForCurrentThread();
        int total = 0;
        for (int i = 0; i < 100; i++) total += CaptureText.Bars(BattleMode.Standard, (byte)(i & 1), match).Count;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(200, total);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void The_alarm_blinks_while_the_own_base_rises()
    {
        var alarm = new CaptureAlarm();

        Assert.False(alarm.Update(0, 0));
        Assert.True(alarm.Update(1, 1.0));
        Assert.True(alarm.Update(1, 2.0));    // a second without a rise: still within the hold
        Assert.False(alarm.Update(1, 2.6));   // 1.6 s since the rise
        Assert.True(alarm.Update(2, 3.0));
        Assert.False(alarm.Update(0, 3.1));   // the base is clear
        Assert.True(alarm.Update(3, 3.2));
        Assert.False(alarm.Update(2, 3.3));   // a drop stops it at once
        Assert.False(alarm.Update(2, 3.4));
    }

    [Fact]
    public void The_alarm_stays_on_through_a_steady_capture()
    {
        var alarm = new CaptureAlarm();
        byte points = 0;
        for (int i = 1; i <= 20; i++)
        {
            double now = i * 0.5;
            if (i % 2 == 0) points++;   // a point a second, as one attacker earns it
            Assert.Equal(points > 0, alarm.Update(points, now));
        }
    }

    [Fact]
    public void The_first_reading_is_only_the_baseline()
    {
        // A mid-match joiner or a reconnect: the own base already holds 37 and nobody is taking it.
        var alarm = new CaptureAlarm();

        Assert.False(alarm.Update(37, 10.0));
        Assert.False(alarm.Update(37, 10.5));
        Assert.False(alarm.Update(37, 30.0));
        Assert.True(alarm.Update(38, 31.0));   // a rise after the baseline does blink
    }

    [Fact]
    public void Enemy_only_bars_never_blink()
    {
        var alarm = new CaptureAlarm();
        for (byte points = 1; points <= 20; points++)
            Assert.False(alarm.Update(CaptureText.Bars(BattleMode.Standard, 0, Progress(0, points)), points * 0.5));
    }

    [Fact]
    public void An_own_bar_rising_blinks_whichever_team_the_player_is_on()
    {
        foreach (byte ownTeam in new byte[] { 0, 1 })
        {
            var alarm = new CaptureAlarm();
            Assert.False(alarm.Update(CaptureText.Bars(BattleMode.Standard, ownTeam, Progress(0, 0)), 0.0));
            MatchBlock rising = ownTeam == 0 ? Progress(1, 5) : Progress(5, 1);
            Assert.True(alarm.Update(CaptureText.Bars(BattleMode.Standard, ownTeam, rising), 0.5));
            // The enemy base rising while the own one holds still does not stretch the hold.
            for (byte enemy = 6; enemy <= 8; enemy++)
            {
                double now = 0.5 + (enemy - 5) * 0.55;   // 1.05, 1.6, 2.15: the last is 1.65 s after the own rise
                MatchBlock enemyRising = ownTeam == 0 ? Progress(1, enemy) : Progress(enemy, 1);
                Assert.Equal(enemy < 8, alarm.Update(CaptureText.Bars(BattleMode.Standard, ownTeam, enemyRising), now));
            }
        }
    }

    [Fact]
    public void The_blink_starts_bright_and_alternates_twice_a_second()
    {
        Assert.False(CaptureAlarm.IsDarkHalf(0));
        Assert.False(CaptureAlarm.IsDarkHalf(0.24));
        Assert.True(CaptureAlarm.IsDarkHalf(0.25));
        Assert.True(CaptureAlarm.IsDarkHalf(0.49));
        Assert.False(CaptureAlarm.IsDarkHalf(0.5));
    }
}

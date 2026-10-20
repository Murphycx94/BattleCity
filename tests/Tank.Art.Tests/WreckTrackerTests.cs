using Tank.Client.Match;

namespace Tank.Art.Tests;

public class WreckTrackerTests
{
    private const float Frame = 1f / 60;

    [Fact]
    public void The_destroyed_event_wrecks_at_once_and_only_once()
    {
        var tracker = new WreckTracker();
        Assert.Equal(WreckTracker.Change.Wreck, tracker.Destroyed());
        Assert.True(tracker.Wrecked);
        Assert.Equal(WreckTracker.Change.None, tracker.Destroyed());
    }

    [Fact]
    public void The_revived_event_restores_a_wreck_and_does_nothing_to_an_intact_tank()
    {
        var tracker = new WreckTracker();
        Assert.Equal(WreckTracker.Change.None, tracker.Revived());
        tracker.Destroyed();
        Assert.Equal(WreckTracker.Change.Restore, tracker.Revived());
        Assert.False(tracker.Wrecked);
    }

    [Fact]
    public void A_destroyed_flag_waits_for_the_event_then_settles_the_wreck_after_the_fallback_time()
    {
        var tracker = new WreckTracker();
        float waited = 0f;
        while (waited + Frame <= WreckTracker.FallbackSeconds)
        {
            Assert.Equal(WreckTracker.Change.None, tracker.Observe(destroyed: true, Frame));
            waited += Frame;
        }
        Assert.Equal(WreckTracker.Change.Settle, tracker.Observe(destroyed: true, Frame));
        Assert.True(tracker.Wrecked);
        Assert.Equal(WreckTracker.Change.None, tracker.Observe(destroyed: true, Frame));
    }

    [Fact]
    public void The_event_arriving_during_the_wait_wins_with_the_full_kill()
    {
        var tracker = new WreckTracker();
        tracker.Observe(destroyed: true, 0.1f);
        Assert.Equal(WreckTracker.Change.Wreck, tracker.Destroyed());
        Assert.Equal(WreckTracker.Change.None, tracker.Observe(destroyed: true, Frame));
    }

    [Fact]
    public void A_tank_first_seen_destroyed_settles_at_once()
    {
        var tracker = new WreckTracker();
        Assert.Equal(WreckTracker.Change.Settle, tracker.Observe(destroyed: true, Frame, firstSight: true));
    }

    [Fact]
    public void A_tank_first_seen_intact_changes_nothing()
    {
        var tracker = new WreckTracker();
        Assert.Equal(WreckTracker.Change.None, tracker.Observe(destroyed: false, Frame, firstSight: true));
        Assert.False(tracker.Wrecked);
    }

    [Fact]
    public void An_intact_flag_on_a_wreck_restores_after_the_fallback_time_a_respawn_out_of_sight()
    {
        var tracker = new WreckTracker();
        tracker.Destroyed();
        Assert.Equal(WreckTracker.Change.None, tracker.Observe(destroyed: false, 0.2f));
        Assert.Equal(WreckTracker.Change.Restore, tracker.Observe(destroyed: false, 0.2f));
        Assert.False(tracker.Wrecked);
    }

    [Fact]
    public void Agreement_resets_the_wait()
    {
        var tracker = new WreckTracker();
        tracker.Observe(destroyed: true, 0.2f);
        tracker.Observe(destroyed: false, Frame);   // a flicker: back in agreement
        Assert.Equal(WreckTracker.Change.None, tracker.Observe(destroyed: true, 0.2f));   // the clock started again
    }
}

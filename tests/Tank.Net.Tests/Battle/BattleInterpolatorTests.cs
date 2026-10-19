using Tank.Net.Battle;
using static Tank.Net.Tests.Battle.BattleTestPackets;

namespace Tank.Net.Tests.Battle;

public sealed class BattleInterpolatorTests
{
    private const int OwnId = 99;
    private const double Rate = 30.0;
    private const double InitialDelayTicks = 3.0;   // the interpolator's shortest delay: where it starts, and where zero jitter keeps it

    /// <summary>Stores the snapshots as arriving on time, one tick apart in time per tick of the clock, and samples at <paramref name="renderTick"/>.</summary>
    private static (List<RemoteTank> Tanks, BattleInterpolator Interpolator) SampleAt(double renderTick, params BattleSnapshot[] snapshots)
    {
        var store = new BattleSnapshotStore();
        var interpolator = new BattleInterpolator(store);
        foreach (BattleSnapshot s in snapshots.OrderBy(s => s.Tick))
        {
            store.Add(s);
            interpolator.OnSnapshot(s.Tick, s.Tick / Rate);
        }
        BattleSnapshot newest = snapshots.MaxBy(s => s.Tick)!;
        double now = newest.Tick / Rate + (renderTick + InitialDelayTicks - newest.Tick) / Rate;   // the clock reading at which the render clock shows renderTick
        interpolator.Advance(0, now);
        Assert.Equal(renderTick, interpolator.RenderTick, 6);
        var tanks = new List<RemoteTank>();
        interpolator.Sample(tanks, OwnId);
        return (tanks, interpolator);
    }

    [Fact]
    public void Between_two_snapshots_a_tank_is_drawn_at_the_interpolated_position()
    {
        var (tanks, interpolator) = SampleAt(101.5,
            Of(100, Entity(2, 1000, 0), Entity(OwnId, 5, 5)),
            Of(103, Entity(2, 2000, 400), Entity(OwnId, 6, 6)));
        RemoteTank t = Assert.Single(tanks);   // the own tank is left to the predictor
        Assert.Equal(2, t.Entity.Id);
        Assert.Equal(15.0f, t.Position.X, 4);   // halfway between 10 m and 20 m
        Assert.Equal(2.0f, t.Position.Y, 4);    // and between 0 and 4 m
        Assert.False(t.Extrapolated);
        Assert.Equal(0, interpolator.ExtrapolatedFrames);
    }

    [Fact]
    public void A_quarter_of_the_way_is_a_quarter_of_the_way()
    {
        var (tanks, _) = SampleAt(100.75, Of(100, Entity(2, 1000, 0)), Of(103, Entity(2, 2000, 0)));
        Assert.Equal(12.5f, Assert.Single(tanks).Position.X, 4);
    }

    [Fact]
    public void A_jump_of_more_than_20_metres_is_a_respawn_and_is_not_lerped_across_the_map()
    {
        BattleSnapshot before = Of(100, Entity(2, 1000, 0)), after = Of(103, Entity(2, 3500, 0));   // 25 m
        Assert.Equal(10.0f, Assert.Single(SampleAt(100.75, before, after).Tanks).Position.X, 4);   // still at the old spot …
        Assert.Equal(35.0f, Assert.Single(SampleAt(102.25, before, after).Tanks).Position.X, 4);   // … then at the new, never between
    }

    [Fact]
    public void A_hostile_coordinate_pair_cannot_overflow_the_distance_test()
    {
        // int.MinValue then 0: the distance doesn't fit an int. The codec refuses such coordinates, but the interpolator must not rely on that.
        var (tanks, _) = SampleAt(100.5, Of(100, Entity(2, int.MinValue, 0)), Of(101, Entity(2, 0, 0)));
        Assert.Single(tanks);
    }

    [Fact]
    public void With_no_newer_snapshot_a_tank_carries_on_at_its_velocity_for_three_ticks_at_most()
    {
        // 3 m a tick; the render clock is 9 ticks past the newest snapshot, so only 3 ticks' worth is extrapolated.
        var (tanks, interpolator) = SampleAt(110, Of(100, Entity(2, 1000, 0)), Of(101, Entity(2, 1300, 0)));
        RemoteTank t = Assert.Single(tanks);
        Assert.True(t.Extrapolated);
        Assert.Equal(13.0f + 3 * 3.0f, t.Position.X, 4);
        Assert.Equal(1, interpolator.ExtrapolatedFrames);

        // half a tick past the newest: a fraction of a tick is extrapolated too
        var (near, _) = SampleAt(101.5, Of(100, Entity(2, 1000, 0)), Of(101, Entity(2, 1300, 0)));
        Assert.Equal(13.0f + 0.5f * 3.0f, Assert.Single(near).Position.X, 4);   // half a tick past the newest
    }

    [Fact]
    public void Extrapolation_does_not_run_past_a_respawn_between_two_far_apart_snapshots()
    {
        // 250 m over 20 ticks is 12.5 m a tick: under the 20 m "teleport" a tick, so only the displacement shows it was a respawn.
        var (tanks, _) = SampleAt(125, Of(100, Entity(2, 1000, 0)), Of(120, Entity(2, 26000, 0)));
        RemoteTank t = Assert.Single(tanks);
        Assert.Equal(260.0f, t.Position.X, 4);   // stands where it respawned; no 12.5 m a tick onward
    }

    [Fact]
    public void Reset_forgets_the_render_clock_and_the_extrapolation_count()
    {
        var (_, interpolator) = SampleAt(110, Of(100, Entity(2, 1000, 0)), Of(101, Entity(2, 1300, 0)));
        Assert.True(interpolator.RenderTick > 0);
        Assert.True(interpolator.ExtrapolatedFrames > 0);
        interpolator.Reset();
        Assert.Equal(0, interpolator.RenderTick);
        Assert.Equal(0, interpolator.ExtrapolatedFrames);
        Assert.Equal(3.0, interpolator.DelayTicks);
    }
}

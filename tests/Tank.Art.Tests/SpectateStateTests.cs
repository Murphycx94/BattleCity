using System.Numerics;
using Tank.Client.Match;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;

namespace Tank.Art.Tests;

public class SpectateStateTests
{
    private const float Dt = 1f / 60;
    private static readonly int[] Three = { 4, 7, 9 };

    private static SpectateState Dead(bool autoFollow)
    {
        var s = new SpectateState();
        s.OnDeath(autoFollow);
        return s;
    }

    // 200 m square, flat at y = 0, 4 m border: playable X and Z are 4 … 196.
    private static Battlefield Flat()
    {
        const int samples = 101;
        return new Battlefield(new HeightField(samples, samples, 2f, new float[samples * samples]), Array.Empty<Prop>(), 4f);
    }

    [Fact]
    public void Death_starts_on_the_wreck_with_no_target()
    {
        SpectateState s = Dead(autoFollow: true);
        Assert.Equal(SpectateState.Mode.Wreck, s.Current);
        Assert.Equal(0, s.TargetId);
    }

    [Fact]
    public void The_close_up_holds_three_seconds_then_follows_the_first_candidate()
    {
        SpectateState s = Dead(autoFollow: true);
        s.Update(SpectateState.CloseUpSeconds - 0.1f, Three, freeWhenNone: false);
        Assert.Equal(SpectateState.Mode.Wreck, s.Current);
        s.Update(0.2f, Three, freeWhenNone: false);
        Assert.Equal(SpectateState.Mode.Follow, s.Current);
        Assert.Equal(4, s.TargetId);
    }

    [Fact]
    public void A_respawning_mode_waits_on_the_wreck_until_the_player_steps_away()
    {
        SpectateState s = Dead(autoFollow: false);
        s.Update(10f, Three, freeWhenNone: false);
        Assert.Equal(SpectateState.Mode.Wreck, s.Current);
        s.Step(1, Three);
        Assert.Equal((SpectateState.Mode.Follow, 4), (s.Current, s.TargetId));
    }

    [Fact]
    public void Stepping_wraps_at_both_ends()
    {
        SpectateState s = Dead(autoFollow: false);
        s.Step(1, Three);
        s.Step(1, Three);
        s.Step(1, Three);
        Assert.Equal(9, s.TargetId);
        s.Step(1, Three);
        Assert.Equal(4, s.TargetId);   // past the end, round to the front
        s.Step(-1, Three);
        Assert.Equal(9, s.TargetId);   // and back again
    }

    [Fact]
    public void Stepping_back_from_the_wreck_picks_the_last_candidate()
    {
        SpectateState s = Dead(autoFollow: false);
        s.Step(-1, Three);
        Assert.Equal((SpectateState.Mode.Follow, 9), (s.Current, s.TargetId));
    }

    [Fact]
    public void A_step_with_nobody_to_watch_changes_nothing()
    {
        SpectateState s = Dead(autoFollow: false);
        s.Step(1, ReadOnlySpan<int>.Empty);
        Assert.Equal((SpectateState.Mode.Wreck, 0), (s.Current, s.TargetId));
    }

    [Fact]
    public void A_dead_target_is_swapped_for_the_next()
    {
        SpectateState s = Dead(autoFollow: false);
        s.Step(1, Three);
        s.Step(1, Three);
        Assert.Equal(7, s.TargetId);
        s.Update(Dt, stackalloc int[] { 4, 9 }, freeWhenNone: false);   // 7 died
        Assert.Equal((SpectateState.Mode.Follow, 4), (s.Current, s.TargetId));
    }

    [Fact]
    public void Nobody_left_returns_a_team_mode_to_the_wreck()
    {
        SpectateState s = Dead(autoFollow: false);
        s.Step(1, Three);
        s.Update(Dt, ReadOnlySpan<int>.Empty, freeWhenNone: false);
        Assert.Equal((SpectateState.Mode.Wreck, 0), (s.Current, s.TargetId));
    }

    [Fact]
    public void Nobody_left_frees_the_camera_in_a_free_for_all()
    {
        SpectateState s = Dead(autoFollow: true);
        s.Step(1, Three);
        s.Update(Dt, ReadOnlySpan<int>.Empty, freeWhenNone: true);
        Assert.Equal((SpectateState.Mode.Free, 0), (s.Current, s.TargetId));
    }

    [Fact]
    public void The_close_up_with_nobody_to_watch_waits_in_a_team_mode_and_takes_a_late_arrival()
    {
        SpectateState s = Dead(autoFollow: true);
        s.Update(SpectateState.CloseUpSeconds + 0.5f, ReadOnlySpan<int>.Empty, freeWhenNone: false);
        Assert.Equal(SpectateState.Mode.Wreck, s.Current);
        s.Update(Dt, stackalloc int[] { 7 }, freeWhenNone: false);   // a teammate came back in sight
        Assert.Equal((SpectateState.Mode.Follow, 7), (s.Current, s.TargetId));
    }

    [Fact]
    public void The_close_up_with_nobody_to_watch_frees_the_camera_in_a_free_for_all()
    {
        SpectateState s = Dead(autoFollow: true);
        s.Update(SpectateState.CloseUpSeconds + 0.5f, ReadOnlySpan<int>.Empty, freeWhenNone: true);
        Assert.Equal(SpectateState.Mode.Free, s.Current);
    }

    [Fact]
    public void Stepping_from_the_free_camera_follows_again()
    {
        SpectateState s = Dead(autoFollow: true);
        s.GoFree();
        Assert.Equal(SpectateState.Mode.Free, s.Current);
        s.Step(1, Three);
        Assert.Equal((SpectateState.Mode.Follow, 4), (s.Current, s.TargetId));
    }

    [Fact]
    public void A_fresh_death_starts_the_close_up_over()
    {
        SpectateState s = Dead(autoFollow: true);
        s.Step(1, Three);
        s.OnDeath(autoFollow: true);
        Assert.Equal((SpectateState.Mode.Wreck, 0), (s.Current, s.TargetId));
        s.Update(1f, Three, freeWhenNone: false);
        Assert.Equal(SpectateState.Mode.Wreck, s.Current);   // the three seconds count anew
    }

    [Fact]
    public void The_focus_moves_forward_toward_minus_z_at_yaw_zero()
    {
        Vector2 moved = SpectateState.MoveFocus(new Vector2(100, 100), cameraYaw: 0f, forward: 1, right: 0, fast: false, dt: 0.5f, Flat());
        Assert.Equal(100f, moved.X, 3);
        Assert.Equal(100f - SpectateState.FocusSpeed * 0.5f, moved.Y, 3);
    }

    [Fact]
    public void The_focus_moves_right_toward_plus_x_at_yaw_zero()
    {
        Vector2 moved = SpectateState.MoveFocus(new Vector2(100, 100), cameraYaw: 0f, forward: 0, right: 1, fast: false, dt: 0.5f, Flat());
        Assert.Equal(100f + SpectateState.FocusSpeed * 0.5f, moved.X, 3);
        Assert.Equal(100f, moved.Y, 3);
    }

    [Fact]
    public void A_quarter_turn_of_the_camera_turns_forward_toward_minus_x()
    {
        Vector2 moved = SpectateState.MoveFocus(new Vector2(100, 100), cameraYaw: MathF.PI / 2, forward: 1, right: 0, fast: false, dt: 0.5f, Flat());
        Assert.Equal(100f - SpectateState.FocusSpeed * 0.5f, moved.X, 3);
        Assert.Equal(100f, moved.Y, 3);
    }

    [Fact]
    public void Shift_triples_the_speed_and_a_diagonal_is_no_faster()
    {
        Vector2 fast = SpectateState.MoveFocus(new Vector2(100, 100), 0f, forward: 1, right: 0, fast: true, dt: 0.5f, Flat());
        Assert.Equal(100f - SpectateState.FocusSpeed * SpectateState.FastMultiplier * 0.5f, fast.Y, 3);

        Vector2 diagonal = SpectateState.MoveFocus(new Vector2(100, 100), 0f, forward: 1, right: 1, fast: false, dt: 0.5f, Flat());
        Assert.Equal(SpectateState.FocusSpeed * 0.5f, Vector2.Distance(new Vector2(100, 100), diagonal), 3);
    }

    [Fact]
    public void The_focus_stops_at_the_playable_border()
    {
        Battlefield field = Flat();
        Vector2 forward = SpectateState.MoveFocus(new Vector2(10, 10), cameraYaw: 0f, forward: 1, right: 0, fast: true, dt: 10f, field);
        Assert.Equal(new Vector2(10, field.Border), forward);

        Vector2 right = SpectateState.MoveFocus(new Vector2(190, 50), cameraYaw: 0f, forward: 0, right: 1, fast: true, dt: 10f, field);
        Assert.Equal(new Vector2(field.Terrain.SizeX - field.Border, 50), right);
    }

    [Fact]
    public void Standing_still_only_clamps()
    {
        Vector2 still = SpectateState.MoveFocus(new Vector2(1, 100), 0.7f, forward: 0, right: 0, fast: false, dt: Dt, Flat());
        Assert.Equal(new Vector2(4, 100), still);
    }
}

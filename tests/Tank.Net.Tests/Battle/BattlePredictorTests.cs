using System.Diagnostics;
using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Net.Tests.Battle;

public sealed class BattlePredictorTests
{
    private const int ShotSlackTicks = 45;   // the predictor's orphan window

    private static readonly Vector2 Start = new(100, 100);

    private static InputCmd Drive(int tick) => new(tick, 1, 0, 0f, 0f, false);
    private static InputCmd Fire(int tick) => new(tick, 0, 0, 0f, 0f, true);
    private static InputCmd Idle(int tick) => new(tick, 0, 0, 0f, 0f, false);

    private static TankState Medium() => TestWorld.Tank(Start.X, Start.Y) with { ApLeft = 20, HeLeft = 5, Health = 100 };

    /// <summary>A predictor that has been told the server's state once, so it has a tank.</summary>
    private static BattlePredictor Established(Battlefield field)
    {
        var p = new BattlePredictor();
        p.Reconcile(Medium(), -1, false, field, ReadOnlySpan<Obb2>.Empty);
        Assert.True(p.HasTank);
        return p;
    }

    /// <summary>Drives for <paramref name="ticks"/> ticks from tick 0, remembering the state after tick <paramref name="mark"/>.</summary>
    private static TankState DriveFor(BattlePredictor p, Battlefield field, int ticks, int mark)
    {
        TankState atMark = default;
        for (int tick = 0; tick < ticks; tick++)
        {
            p.Step(Drive(tick), field, ReadOnlySpan<Obb2>.Empty);
            if (tick == mark) atMark = p.Current;
        }
        return atMark;
    }

    [Fact]
    public void A_server_state_shifted_a_little_is_replayed_over_the_known_inputs_to_an_exactly_corrected_tank()
    {
        Battlefield field = TestWorld.FlatField();
        BattlePredictor p = Established(field);
        TankState atFour = DriveFor(p, field, 10, mark: 4);
        Vector2 predicted = p.Current.Position;
        Assert.NotEqual(Start, predicted);

        var shift = new Vector2(0.1f, 0f);   // the server has the tank 10 cm over at tick 4
        p.Reconcile(atFour with { Position = atFour.Position + shift }, 4, false, field, ReadOnlySpan<Obb2>.Empty);

        Assert.Equal(predicted.X + shift.X, p.Current.Position.X, 3);   // inputs 5..9 replayed from the server's state: the same drive, 10 cm over
        Assert.Equal(predicted.Y, p.Current.Position.Y, 3);
        Assert.Equal(1, p.Corrections);
        Assert.Equal(0.1f, p.LastCorrection, 3);
        Assert.Equal(0.1f, p.MaxCorrection, 3);
        Assert.Equal(0.1, p.MeanCorrection, 3);
        Assert.Equal(-0.1f, p.VisualOffset.X, 3);   // drawn where it was: the correction is eased in
        Assert.Equal(0f, p.VisualOffset.Y, 3);
    }

    [Fact]
    public void A_shift_under_two_centimetres_is_noise_and_is_not_counted_as_a_correction()
    {
        Battlefield field = TestWorld.FlatField();
        BattlePredictor p = Established(field);
        TankState atFour = DriveFor(p, field, 10, mark: 4);
        p.Reconcile(atFour with { Position = atFour.Position + new Vector2(0.01f, 0f) }, 4, false, field, ReadOnlySpan<Obb2>.Empty);
        Assert.Equal(0, p.Corrections);
    }

    [Fact]
    public void A_correction_of_half_a_metre_or_more_snaps_and_a_smaller_one_is_smoothed_away_with_a_50_ms_time_constant()
    {
        Battlefield field = TestWorld.FlatField();

        BattlePredictor small = Established(field);
        TankState atFour = DriveFor(small, field, 10, mark: 4);
        small.Reconcile(atFour with { Position = atFour.Position + new Vector2(0.4f, 0f) }, 4, false, field, ReadOnlySpan<Obb2>.Empty);
        Assert.Equal(0.4f, small.VisualOffset.Length(), 3);
        small.DecayVisualOffset(0.05);   // one time constant: down to 1/e
        Assert.Equal(0.4f * MathF.Exp(-1f), small.VisualOffset.Length(), 3);
        small.DecayVisualOffset(0.10);   // two more
        Assert.Equal(0.4f * MathF.Exp(-3f), small.VisualOffset.Length(), 3);
        small.DecayVisualOffset(2.0);    // and it is gone, not an ever smaller number
        Assert.Equal(Vector2.Zero, small.VisualOffset);

        BattlePredictor large = Established(field);
        atFour = DriveFor(large, field, 10, mark: 4);
        large.Reconcile(atFour with { Position = atFour.Position + new Vector2(0.6f, 0f) }, 4, false, field, ReadOnlySpan<Obb2>.Empty);
        Assert.Equal(Vector2.Zero, large.VisualOffset);   // no smoothing across half a metre: a snap
        Assert.Equal(0.6f, large.MaxCorrection, 3);
    }

    [Fact]
    public void An_ack_just_inside_the_input_history_is_replayed_in_full()
    {
        Battlefield field = TestWorld.FlatField();
        BattlePredictor p = Established(field);
        for (int tick = 0; tick < 200; tick++) p.Step(Drive(tick), field, ReadOnlySpan<Obb2>.Empty);
        Vector2 predicted = p.Current.Position;
        p.Reconcile(Medium(), 199 - 128, false, field, ReadOnlySpan<Obb2>.Empty);   // the oldest input still held (72) is the first replayed
        BattlePredictor reference = Established(field);   // the same 128 inputs, driven from the server's state
        for (int tick = 72; tick < 200; tick++) reference.Step(Drive(tick), field, ReadOnlySpan<Obb2>.Empty);
        Assert.True(Vector2.Distance(predicted, Start) > 1f);
        Assert.Equal(reference.Current.Position.X, p.Current.Position.X, 3);
        Assert.Equal(reference.Current.Position.Y, p.Current.Position.Y, 3);
        p.Reconcile(Medium(), 199 - 129, false, field, ReadOnlySpan<Obb2>.Empty);   // one older: no input to start from, the server's state as it is
        Assert.Equal(Start, p.Current.Position);
    }

    [Fact]
    public void An_unconfirmed_shot_is_dropped_once_the_render_clock_is_past_it_and_a_confirmed_one_is_kept()
    {
        Battlefield field = TestWorld.FlatField();
        BattlePredictor p = Established(field);
        p.Step(Fire(10), field, ReadOnlySpan<Obb2>.Empty);
        for (int tick = 11; tick < 20; tick++) p.Step(Idle(tick), field, ReadOnlySpan<Obb2>.Empty);
        Assert.Single(p.Shells);
        Assert.Equal(10, p.Shells[0].SpawnTick);

        p.Prune(10 + ShotSlackTicks);   // the clock has only just got there: the server's word may be a moment away
        Assert.Single(p.Shells);
        p.Prune(10 + ShotSlackTicks + 0.5);
        Assert.Empty(p.Shells);   // the orphan: nobody ever said the server fired it

        for (int tick = 20; tick < 400; tick++) p.Step(Idle(tick), field, ReadOnlySpan<Obb2>.Empty);   // the gun reloads
        p.Step(Fire(400), field, ReadOnlySpan<Obb2>.Empty);
        Assert.True(p.Confirm(400, 777));
        p.Prune(1000);
        Assert.Single(p.Shells);
        Assert.Equal(777, p.Shells[0].Shell.Id);
        Assert.True(p.Shells[0].Confirmed);
    }

    [Fact]
    public void The_orphan_test_follows_the_render_clock_not_the_servers_acknowledgement()
    {
        // Pruning is driven by the clock the server's events are played on: an ack far ahead (the server has long since
        // processed this input) must not give up on a shot whose ShellFired is still waiting for the render clock.
        Battlefield field = TestWorld.FlatField();
        BattlePredictor p = Established(field);
        p.Step(Fire(10), field, ReadOnlySpan<Obb2>.Empty);
        p.Prune(renderTick: 12);   // the render clock lags: it is at tick 12, long before the event for tick 10 could be due
        Assert.Single(p.Shells);
        Assert.True(p.Confirm(10, 5));   // … and the confirmation, when it plays, finds its shell
        Assert.True(p.Shells[0].Confirmed);
    }

    [Fact]
    public void A_shot_the_server_reports_that_was_never_predicted_is_not_confirmed_so_it_flies_as_a_remote_shell()
    {
        Battlefield field = TestWorld.FlatField();
        BattlePredictor p = Established(field);
        for (int tick = 0; tick < 20; tick++) p.Step(Idle(tick), field, ReadOnlySpan<Obb2>.Empty);
        Assert.False(p.Confirm(5, 321), "no predicted shell on tick 5");
        Assert.Empty(p.Shells);

        p.Step(Fire(20), field, ReadOnlySpan<Obb2>.Empty);
        Assert.Single(p.Shells);
        Assert.False(p.Confirm(19, 322), "the shell is of tick 20, not 19");
        Assert.True(p.Confirm(20, 323));
        Assert.False(p.Confirm(20, 324), "a shell is confirmed once; a second report of the tick is another shell");
    }

    [Fact]
    public void A_shot_the_server_reports_before_it_is_predicted_is_confirmed_when_it_is()
    {
        Battlefield field = TestWorld.FlatField();
        BattlePredictor p = Established(field);
        p.Step(Idle(0), field, ReadOnlySpan<Obb2>.Empty);
        Assert.True(p.Confirm(3, 55));   // the client is behind: remembered
        for (int tick = 1; tick < 3; tick++) p.Step(Idle(tick), field, ReadOnlySpan<Obb2>.Empty);
        p.Step(Fire(3), field, ReadOnlySpan<Obb2>.Empty);
        PredictedShell shell = Assert.Single(p.Shells);
        Assert.True(shell.Confirmed);
        Assert.Equal(55, shell.Shell.Id);
    }

    [Fact]
    public void The_shots_waited_for_are_bounded()
    {
        Battlefield field = TestWorld.FlatField();
        BattlePredictor p = Established(field);
        p.Step(Idle(0), field, ReadOnlySpan<Obb2>.Empty);
        for (int i = 0; i < 1000; i++) Assert.True(p.Confirm(100 + i, i));   // a hostile flood of future shots
        // the oldest were forgotten: the newest are still waited for, the first is not
        for (int tick = 1; tick < 100; tick++) p.Step(Idle(tick), field, ReadOnlySpan<Obb2>.Empty);
        p.Step(Fire(100), field, ReadOnlySpan<Obb2>.Empty);
        Assert.False(Assert.Single(p.Shells).Confirmed);
    }

    [Fact]
    public void The_exposed_state_is_a_view_the_caller_cannot_change()
    {
        BattlePredictor p = Established(TestWorld.FlatField());
        Assert.Equal(Start, p.Current.Position);
        TankState copy = p.Current;
        copy.Position = Vector2.Zero;
        Assert.Equal(Start, p.Current.Position);
        Assert.IsAssignableFrom<IReadOnlyList<PredictedShell>>(p.Shells);
    }

    /// <summary>The wall-clock test, on its own in the <see cref="TimingCollection"/>.</summary>
    [Collection(TimingCollection.Name)]
    public sealed class Timed
    {
        [Fact]
        public void An_ack_far_older_than_the_input_history_is_taken_at_once_without_replaying_millions_of_ticks()
        {
            Battlefield field = TestWorld.FlatField();
            BattlePredictor p = Established(field);
            DriveFor(p, field, 10, mark: 4);
            p.Reconcile(Medium(), -10_000_000, false, field, ReadOnlySpan<Obb2>.Empty);   // warm up: the timing below is not JIT
            foreach (int ack in new[] { -10_000_000, int.MinValue, -1_000, 9 - 128 - 1 })   // the history holds 128 inputs: ticks -118 … 9 are replayable
            {
                var sw = Stopwatch.StartNew();
                p.Reconcile(Medium(), ack, false, field, ReadOnlySpan<Obb2>.Empty);
                Assert.True(sw.ElapsedMilliseconds < 50, $"ack {ack}: {sw.ElapsedMilliseconds} ms");
                Assert.Equal(Start, p.Current.Position);   // the server's state, nothing replayed
                Assert.Equal(Vector2.Zero, p.VisualOffset);
            }
        }
    }
}

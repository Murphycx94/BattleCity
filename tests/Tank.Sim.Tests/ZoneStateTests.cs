using System.Numerics;
using Tank.Sim.Modes;
using Tank.Testing;

namespace Tank.Sim.Tests;

/// <summary>The shared zone function: which ring, which phase, the circle now and next, the time left and the damage (stage 6 design §3.1).</summary>
public sealed class ZoneStateTests
{
    private static readonly ZoneRing R0 = new(new Vector2(600, 600), 806);
    private static readonly ZoneRing R1 = new(new Vector2(500, 600), 420);
    private static readonly ZoneRing R2 = new(new Vector2(450, 600), 250);
    private static readonly ZoneRing R3 = new(new Vector2(450, 640), 140);
    private static readonly ZoneRing R4 = new(new Vector2(470, 640), 60);
    private static readonly ZoneRing R5 = new(new Vector2(470, 650), 0);
    private static readonly ZoneRing[] All = { R0, R1, R2, R3, R4, R5 };

    private static ZoneState At(float elapsed) => ZoneState.At(All, elapsed);

    [Fact]
    public void At_the_start_ring_one_waits_inside_ring_zero_and_costs_nothing()
    {
        ZoneState s = At(0);
        Assert.Equal(1, s.Ring);
        Assert.Equal(ZonePhase.Waiting, s.Phase);
        Assert.Equal(R0, s.Current);
        Assert.True(s.HasNext);
        Assert.Equal(R1, s.Next);
        Assert.Equal(R1, s.Target);
        Assert.Equal(2700, s.TicksLeft);
        Assert.Equal(0, s.DamagePercent);
        Assert.Equal(5400, s.TicksToTarget);
        Assert.Equal(s, At(-5));
    }

    [Fact]
    public void Waiting_counts_down_whole_ticks_rounded_up()
    {
        Assert.Equal(2699, At(1).TicksLeft);
        Assert.Equal(2700, At(0.25f).TicksLeft);
        Assert.Equal(1, At(2699).TicksLeft);
    }

    [Fact]
    public void Ring_one_shrinks_from_ring_zero_to_ring_one_linearly()
    {
        ZoneState start = At(2700);
        Assert.Equal(ZonePhase.Shrinking, start.Phase);
        Assert.Equal(1, start.Ring);
        Assert.Equal(R0, start.Current);
        Assert.Equal(R1, start.Next);
        Assert.Equal(1, start.DamagePercent);
        Assert.Equal(2700, start.TicksLeft);
        Assert.Equal(2700, start.TicksToTarget);

        ZoneState half = At(4050);
        Assert.Equal(550f, half.Current.Centre.X, 0.001f);
        Assert.Equal(600f, half.Current.Centre.Y, 0.001f);
        Assert.Equal(613f, half.Current.Radius, 0.001f);
        Assert.Equal(1350, half.TicksLeft);
        Assert.True(At(4050.5f).Current.Radius < half.Current.Radius);

        Assert.Equal(R1.Radius, At(5399.999f).Current.Radius, 0.01f);
    }

    [Fact]
    public void Ring_two_waits_inside_ring_one_at_ring_ones_damage()
    {
        ZoneState s = At(5400);
        Assert.Equal(2, s.Ring);
        Assert.Equal(ZonePhase.Waiting, s.Phase);
        Assert.Equal(R1, s.Current);
        Assert.Equal(R2, s.Next);
        Assert.Equal(1, s.DamagePercent);
        Assert.Equal(1800, s.TicksLeft);
        Assert.Equal(1800 + 1800, s.TicksToTarget);
    }

    [Fact]
    public void Each_ring_costs_its_own_damage_while_it_shrinks()
    {
        ZoneState two = At(7200);
        Assert.Equal((2, ZonePhase.Shrinking, 2), (two.Ring, two.Phase, two.DamagePercent));
        Assert.Equal(R1, two.Current);
        ZoneState three = At(10500);
        Assert.Equal((3, ZonePhase.Shrinking, 4), (three.Ring, three.Phase, three.DamagePercent));
        ZoneState fourWaiting = At(11850);
        Assert.Equal((4, ZonePhase.Waiting, 4), (fourWaiting.Ring, fourWaiting.Phase, fourWaiting.DamagePercent));
        ZoneState five = At(15000);
        Assert.Equal((5, ZonePhase.Shrinking, 10), (five.Ring, five.Phase, five.DamagePercent));
        Assert.Equal(R4, five.Current);
        Assert.Equal(R5, five.Next);
    }

    [Theory]
    [InlineData(16200f)]
    [InlineData(20000f)]
    public void Once_closed_there_is_nowhere_safe(float elapsed)
    {
        ZoneState s = At(elapsed);
        Assert.Equal(5, s.Ring);
        Assert.Equal(ZonePhase.Closed, s.Phase);
        Assert.Equal(R5, s.Current);
        Assert.False(s.HasNext);
        Assert.Equal(R5, s.Target);
        Assert.Equal(0, s.TicksLeft);
        Assert.Equal(0, s.TicksToTarget);
        Assert.Equal(10, s.DamagePercent);
        Assert.True(s.IsOutside(R5.Centre));
    }

    [Fact]
    public void A_client_that_knows_rings_zero_and_one_waits_for_ring_two_without_a_next()
    {
        ZoneState s = ZoneState.At(new[] { R0, R1 }, 5400);
        Assert.Equal(2, s.Ring);
        Assert.Equal(ZonePhase.Waiting, s.Phase);
        Assert.Equal(R1, s.Current);
        Assert.False(s.HasNext);
        Assert.Equal(R1, s.Target);
        Assert.Equal(1, s.DamagePercent);
    }

    [Fact]
    public void A_ring_not_known_yet_holds_the_last_known_one()
    {
        ZoneState shrinking = ZoneState.At(new[] { R0 }, 3000);
        Assert.Equal(ZonePhase.Shrinking, shrinking.Phase);
        Assert.Equal(R0, shrinking.Current);
        Assert.False(shrinking.HasNext);
        Assert.Equal(1, shrinking.DamagePercent);

        ZoneState later = ZoneState.At(new[] { R0, R1 }, 12000);
        Assert.Equal(4, later.Ring);
        Assert.Equal(R1, later.Current);
        Assert.False(later.HasNext);

        ZoneState closed = ZoneState.At(new[] { R0, R1, R2 }, 17000);
        Assert.Equal(ZonePhase.Closed, closed.Phase);
        Assert.Equal(R2, closed.Current);
    }

    [Fact]
    public void Without_rings_there_is_no_zone()
    {
        ZoneState s = ZoneState.At(ReadOnlySpan<ZoneRing>.Empty, 3000);
        Assert.Equal(default, s);
        Assert.Equal(ZonePhase.None, s.Phase);
        Assert.False(s.IsOutside(new Vector2(-9999, -9999)));
        Assert.Equal(0, s.TicksToTarget);
    }

    [Fact]
    public void The_edge_itself_is_inside()
    {
        ZoneState s = At(0);
        Assert.False(s.IsOutside(new Vector2(600 + 806, 600)));
        Assert.True(s.IsOutside(new Vector2(600 + 806.01f, 600)));
        Assert.False(s.IsOutside(R0.Centre));
    }

    [Fact]
    public void Not_a_number_reads_as_the_start_and_infinity_as_closed()
    {
        Assert.Equal(At(0), At(float.NaN));
        Assert.Equal(At(0), At(float.NegativeInfinity));
        ZoneState end = At(float.PositiveInfinity);
        Assert.Equal((5, ZonePhase.Closed, R5, false, 0, 10), (end.Ring, end.Phase, end.Current, end.HasNext, end.TicksLeft, end.DamagePercent));
    }

    [Fact]
    public void Fractional_time_agrees_with_whole_ticks_between_them()
    {
        // The client draws at fractional render ticks, the rules read whole ones: between two whole ticks the circle moves no more
        // than a tick's worth, and the counts read as the earlier tick's (rounded up) until the next.
        for (int t = 0; t < ZoneSchedule.ClosedAt + 60; t += 7)
        {
            ZoneState whole = At(t), half = At(t + 0.5f), next = At(t + 1);
            Assert.True(Vector2.Distance(whole.Current.Centre, half.Current.Centre) <= Vector2.Distance(whole.Current.Centre, next.Current.Centre) + 1e-3f, $"tick {t}");
            Assert.InRange(half.Current.Radius, MathF.Min(whole.Current.Radius, next.Current.Radius) - 1e-3f, MathF.Max(whole.Current.Radius, next.Current.Radius) + 1e-3f);
            if (whole.Phase == next.Phase && whole.Ring == next.Ring)
                Assert.Equal((whole.Ring, whole.Phase, whole.TicksLeft, whole.DamagePercent), (half.Ring, half.Phase, half.TicksLeft, half.DamagePercent));
        }
    }

    [Fact]
    public void The_circle_is_continuous_across_every_phase_boundary()
    {
        const float Eps = 0.01f;   // ticks: at most 1.4 mm of shrinking on ring 1, the fastest
        var boundaries = new List<int>();
        for (int k = 1; k <= ZoneSchedule.Rings; k++) boundaries.AddRange(new[] { ZoneSchedule.ShrinkStart(k), ZoneSchedule.ShrinkEnd(k) });
        foreach (int b in boundaries)
        {
            ZoneState before = At(b - Eps), at = At(b), after = At(b + Eps);
            Assert.True(Vector2.Distance(before.Current.Centre, at.Current.Centre) < 0.01f, $"centre jumps at {b}: {before.Current} → {at.Current}");
            Assert.True(MathF.Abs(before.Current.Radius - at.Current.Radius) < 0.01f, $"radius jumps at {b}: {before.Current} → {at.Current}");
            Assert.True(Vector2.Distance(after.Current.Centre, at.Current.Centre) < 0.01f, $"centre jumps after {b}");
            Assert.True(MathF.Abs(after.Current.Radius - at.Current.Radius) < 0.01f, $"radius jumps after {b}");
            Assert.NotEqual((before.Ring, before.Phase), (at.Ring, at.Phase));   // the boundary itself belongs to what comes after
            Assert.Equal((at.Ring, at.Phase), (after.Ring, after.Phase));
        }
        // Each waiting ends where its shrink starts (ring k − 1, unmoved), each shrink where the next waiting starts (ring k).
        for (int k = 1; k <= ZoneSchedule.Rings; k++)
        {
            Assert.Equal(All[k - 1], At(ZoneSchedule.ShrinkStart(k)).Current);
            Assert.Equal(All[k], At(ZoneSchedule.ShrinkEnd(k)).Current);
        }
        Assert.Equal(ZonePhase.Closed, At(ZoneSchedule.ClosedAt).Phase);
        Assert.Equal(ZonePhase.Shrinking, At(ZoneSchedule.ClosedAt - Eps).Phase);
    }

    [Fact]
    public void Reading_the_zone_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            float sum = 0f;
            for (int i = 0; i < 100; i++) sum += At(i * 170.3f).Current.Radius;   // warm-up
            Allocation.Window window = Allocation.Start();
            for (int i = 0; i < 1000; i++)
            {
                ZoneState s = At(i * 17.3f);
                sum += s.Current.Radius + s.TicksToTarget + (s.IsOutside(Vector2.Zero) ? 1 : 0);
            }
            Allocation.Measured measured = window.Stop();
            Assert.True(sum > 0f);
            return measured;
        });
    }
}

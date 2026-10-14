using Tank.Client.Vehicles;

namespace Tank.Art.Tests;

public class TankAnimatorTests
{
    private const float Dt = 1f / 60f;

    private static void Run(TankAnimator animator, float seconds, Func<float, float> speedAt, float yawRate = 0)
    {
        int steps = (int)MathF.Round(seconds / Dt);
        for (int i = 0; i < steps; i++) animator.Step(speedAt(i * Dt), yawRate, Dt);
    }

    [Fact]
    public void Recoil_kicks_back_then_returns_in_time()
    {
        var animator = new TankAnimator(1.3f);
        Assert.Equal(0f, animator.Recoil);
        animator.Fire();
        Assert.Equal(TankAnimator.RecoilDistance, animator.Recoil, 5);
        animator.Step(0, 0, 0.1f);
        Assert.InRange(animator.Recoil, 0.01f, TankAnimator.RecoilDistance * 0.9f);
        animator.Step(0, 0, TankAnimator.RecoilReturnTime);
        Assert.Equal(0f, animator.Recoil);
    }

    [Fact]
    public void Driving_straight_rolls_both_tracks_the_same()
    {
        var animator = new TankAnimator(1.3f);
        Run(animator, 2f, _ => 5f);
        Assert.Equal(10f, animator.LeftDistance, 1);
        Assert.Equal(animator.LeftDistance, animator.RightDistance, 4);
    }

    [Fact]
    public void Turning_left_on_the_spot_runs_the_tracks_in_opposite_directions()
    {
        var animator = new TankAnimator(1.3f);
        Run(animator, 1f, _ => 0f, yawRate: 0.5f);
        Assert.True(animator.RightDistance > 0.6f, "right track runs forward");
        Assert.Equal(-animator.RightDistance, animator.LeftDistance, 4);
    }

    [Fact]
    public void Accelerating_lifts_the_nose_and_braking_dips_it_within_two_degrees()
    {
        var animator = new TankAnimator(1.3f);
        float maxNod = 0, minNod = 0;
        for (float t = 0; t < 4f; t += Dt)
        {
            float speed = t < 2f ? MathF.Min(t * 30f, 14f) : MathF.Max(14f - (t - 2f) * 60f, 0f); // violent on purpose
            animator.Step(speed, 0, Dt);
            maxNod = MathF.Max(maxNod, animator.Nod);
            minNod = MathF.Min(minNod, animator.Nod);
        }
        Assert.True(maxNod > 0.5f * TankAnimator.MaxNod, "nose comes up under acceleration");
        Assert.True(minNod < -0.5f * TankAnimator.MaxNod, "nose dips under braking");
        Assert.InRange(maxNod, 0f, TankAnimator.MaxNod);
        Assert.InRange(minNod, -TankAnimator.MaxNod, 0f);
    }

    [Fact]
    public void Nod_settles_at_steady_speed()
    {
        var animator = new TankAnimator(1.3f);
        Run(animator, 1f, t => t * 4f);
        Run(animator, 3f, _ => 4f);
        Assert.True(MathF.Abs(animator.Nod) < 0.001f);
    }

    [Fact]
    public void One_long_frame_does_not_blow_up_the_nod_spring()
    {
        var animator = new TankAnimator(1.3f);
        Run(animator, 2f, t => MathF.Min(t * 6f, 10f));
        animator.Step(0f, 0, 0.25f); // 10 m/s → 0 in one 250 ms hitch
        Assert.True(MathF.Abs(animator.NodVelocity) < 1f, $"nod velocity {animator.NodVelocity}");
        for (int i = 0; i < 6; i++) animator.Step(0f, 0, 0.25f); // a run of hitches while braked
        Assert.True(MathF.Abs(animator.NodVelocity) < 1f, $"nod velocity after repeated hitches {animator.NodVelocity}");
        Assert.InRange(animator.Nod, -TankAnimator.MaxNod, TankAnimator.MaxNod);
        Run(animator, 1f, _ => 0f);
        Assert.True(MathF.Abs(animator.Nod) < 0.002f, $"nod after settling {animator.Nod}");
    }

    [Fact]
    public void Driving_and_turning_gives_each_track_speed_plus_or_minus_yaw_rate_times_half_track()
    {
        var animator = new TankAnimator(1.3f);
        animator.Step(6f, 0.4f, 0.5f);
        Assert.Equal((6f - 0.4f * 1.3f) * 0.5f, animator.LeftDistance, 5);
        Assert.Equal((6f + 0.4f * 1.3f) * 0.5f, animator.RightDistance, 5);
    }

    [Fact]
    public void Reversing_rolls_both_tracks_backwards()
    {
        var animator = new TankAnimator(1.3f);
        Run(animator, 1f, _ => -3f);
        Assert.Equal(-3f, animator.LeftDistance, 1);
        Assert.Equal(-3f, animator.RightDistance, 1);
    }

    [Fact]
    public void Zero_or_negative_dt_changes_nothing()
    {
        var animator = new TankAnimator(1.3f);
        Run(animator, 0.5f, _ => 4f);
        animator.Fire();
        (float l, float r, float n, float rc) = (animator.LeftDistance, animator.RightDistance, animator.Nod, animator.Recoil);
        animator.Step(9f, 1f, 0f);
        animator.Step(9f, 1f, -0.1f);
        Assert.Equal((l, r, n, rc), (animator.LeftDistance, animator.RightDistance, animator.Nod, animator.Recoil));
    }

    [Fact]
    public void Firing_during_recoil_restarts_it()
    {
        var animator = new TankAnimator(1.3f);
        animator.Fire();
        animator.Step(0, 0, 0.3f);
        Assert.True(animator.Recoil < 0.1f);
        animator.Fire();
        Assert.Equal(TankAnimator.RecoilDistance, animator.Recoil, 5);
    }

    [Fact]
    public void Firing_forward_rocks_the_hull_but_firing_sideways_hardly_does()
    {
        float PeakNod(float turretYaw)
        {
            var animator = new TankAnimator(1.3f);
            animator.Fire(turretYaw);
            float peak = 0;
            for (int i = 0; i < 30; i++) { animator.Step(0, 0, Dt); peak = MathF.Max(peak, MathF.Abs(animator.Nod)); }
            return peak;
        }
        Assert.True(PeakNod(0f) > 0.005f, "forward shot rocks the hull");
        Assert.True(PeakNod(0f) <= TankAnimator.MaxNod);
        Assert.True(PeakNod(MathF.PI / 2) < 0.1f * PeakNod(0f), "a broadside shot pitches the hull far less");
    }
}

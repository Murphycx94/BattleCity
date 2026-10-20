using System.Numerics;
using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Art.Tests;

public class RenderMathTests
{
    private static readonly TankState Prev = new()
    {
        Id = 7, Spec = TankCatalog.Medium, Position = new Vector2(10f, 20f), Yaw = 0.2f, TurretYaw = -0.5f, GunPitch = 0.05f, Speed = 2f, Health = 500, Bloom = 0.1f,
    };

    private static readonly TankState Cur = Prev with
    {
        Position = new Vector2(12f, 24f), Yaw = 0.4f, TurretYaw = -0.3f, GunPitch = 0.15f, Speed = 4f, Health = 480, Bloom = 0.3f,
    };

    [Fact]
    public void OwnDisplay_at_alpha_0_is_the_previous_tick_apart_from_the_position()
    {
        var at = new Vector2(11f, 22.5f);
        TankState shown = RenderMath.OwnDisplay(Prev, Cur, 0f, at);
        Assert.Equal(at, shown.Position);
        Assert.Equal(Prev.Yaw, shown.Yaw, 5);
        Assert.Equal(Prev.TurretYaw, shown.TurretYaw, 5);
        Assert.Equal(Prev.GunPitch, shown.GunPitch, 5);
        Assert.Equal(Prev.Speed, shown.Speed, 5);
        Assert.Equal(Cur.Health, shown.Health);   // discrete fields are the latest tick's
    }

    [Fact]
    public void OwnDisplay_at_alpha_1_is_the_current_tick_apart_from_the_position()
    {
        var at = new Vector2(0f, 0f);
        TankState shown = RenderMath.OwnDisplay(Prev, Cur, 1f, at);
        Assert.Equal(at, shown.Position);
        Assert.Equal(Cur.Yaw, shown.Yaw, 5);
        Assert.Equal(Cur.TurretYaw, shown.TurretYaw, 5);
        Assert.Equal(Cur.GunPitch, shown.GunPitch, 5);
        Assert.Equal(Cur.Speed, shown.Speed, 5);
    }

    [Fact]
    public void YawRate_is_the_turn_over_the_time()
    {
        Assert.Equal(1.5f, RenderMath.YawRate(0.1f, 0.4f, 0.2f), 4);
        Assert.Equal(-1.5f, RenderMath.YawRate(0.4f, 0.1f, 0.2f), 4);
    }

    [Fact]
    public void YawRate_takes_the_shorter_arc_across_plus_minus_pi()
    {
        float rate = RenderMath.YawRate(Angles.Pi - 0.1f, -Angles.Pi + 0.1f, 1f);
        Assert.Equal(0.2f, rate, 4);   // a 0.2 rad turn left, not a 6.08 rad turn right
    }

    [Fact]
    public void YawRate_is_clamped()
    {
        Assert.Equal(RenderMath.MaxYawRate, RenderMath.YawRate(0f, 2f, 0.01f));
        Assert.Equal(-RenderMath.MaxYawRate, RenderMath.YawRate(2f, 0f, 0.01f));
    }

    [Fact]
    public void YawRate_is_zero_when_no_time_passed()
    {
        Assert.Equal(0f, RenderMath.YawRate(0f, 1f, 0f));
        Assert.Equal(0f, RenderMath.YawRate(0f, 1f, -0.1f));
    }

    [Fact]
    public void FromRemote_maps_the_entity_and_the_interpolated_pose()
    {
        var entity = new BattleEntity { Id = 42, Slot = 3, Spec = 2, Team = 1, Health = 321, Flags = TankFlags.Destroyed | TankFlags.Protected };
        var remote = new RemoteTank(entity, new Vector2(5f, -6f), Yaw: 1.1f, TurretYaw: -0.7f, GunPitch: 0.12f, Speed: 3.5f, Extrapolated: false);

        TankState state = RenderMath.FromRemote(remote);

        Assert.Equal(42, state.Id);
        Assert.Same(TankCatalog.All[2], state.Spec);
        Assert.Equal(1, state.Team);
        Assert.Equal(new Vector2(5f, -6f), state.Position);
        Assert.Equal(1.1f, state.Yaw);
        Assert.Equal(-0.7f, state.TurretYaw);
        Assert.Equal(0.12f, state.GunPitch);
        Assert.Equal(3.5f, state.Speed);
        Assert.Equal(321, state.Health);
        Assert.True(state.IsDestroyed);
        Assert.True(state.ProtectedUntil > 0);
    }

    [Fact]
    public void FromRemote_leaves_an_intact_unprotected_tank_so()
    {
        var entity = new BattleEntity { Id = 1, Spec = 0, Health = 600, Flags = TankFlags.None };
        TankState state = RenderMath.FromRemote(new RemoteTank(entity, Vector2.Zero, 0f, 0f, 0f, 0f, false));
        Assert.False(state.IsDestroyed);
        Assert.Equal(0, state.ProtectedUntil);
    }

    [Fact]
    public void ShotDetector_fires_once_per_increase_and_not_at_first_sight()
    {
        var shots = new ShotDetector();
        Assert.False(shots.Update(3, tankId: 1));   // the first reading only sets the baseline
        Assert.False(shots.Update(3, tankId: 1));
        Assert.True(shots.Update(4, tankId: 1));
        Assert.False(shots.Update(4, tankId: 1));   // the same count again: no new shot
        Assert.True(shots.Update(6, tankId: 1));    // two shots in one frame: one flash
    }

    [Fact]
    public void ShotDetector_ignores_a_decrease_then_tracks_the_new_count()
    {
        var shots = new ShotDetector();
        shots.Update(5, tankId: 1);
        Assert.False(shots.Update(0, tankId: 1));   // a respawn reset the count
        Assert.True(shots.Update(1, tankId: 1));
    }

    [Fact]
    public void ShotDetector_starts_afresh_on_another_tank_id()
    {
        var shots = new ShotDetector();
        shots.Update(0, tankId: 1);
        Assert.False(shots.Update(9, tankId: 2));   // a new tank with nine shots behind it is not nine flashes
        Assert.True(shots.Update(10, tankId: 2));
    }
}

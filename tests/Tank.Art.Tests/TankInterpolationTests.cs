using Tank.Client.Sandbox;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Art.Tests;

public class TankInterpolationTests
{
    private static TankState State(float x, float yaw, float turret, float pitch, float speed, int health) => new()
    {
        Id = 3, Spec = TankCatalog.Light, Position = new SN.Vector2(x, 50), Yaw = yaw, TurretYaw = turret,
        GunPitch = pitch, Speed = speed, Bloom = speed / 10, Health = health,
    };

    [Fact]
    public void Blends_place_headings_gun_speed_and_bloom_and_takes_the_rest_from_the_latest_tick()
    {
        TankState a = State(10, 0.2f, 0.1f, 0f, 4f, 800), b = State(12, 0.4f, -0.1f, 0.1f, 6f, 600);
        TankState mid = TankInterpolation.Between(a, b, 0.25f);
        Assert.Equal(10.5f, mid.Position.X, 4);
        Assert.Equal(0.25f, mid.Yaw, 4);
        Assert.Equal(0.05f, mid.TurretYaw, 4);
        Assert.Equal(0.025f, mid.GunPitch, 4);
        Assert.Equal(4.5f, mid.Speed, 4);
        Assert.Equal(0.45f, mid.Bloom, 4);
        Assert.Equal(600, mid.Health);
    }

    [Fact]
    public void Headings_take_the_short_way_across_a_half_turn()
    {
        TankState a = State(0, 3.1f, 3.1f, 0, 0, 1), b = State(0, -3.1f, -3.1f, 0, 0, 1);
        TankState mid = TankInterpolation.Between(a, b, 0.5f);
        Assert.True(MathF.Abs(mid.Yaw) > 3.1f, $"yaw {mid.Yaw} went the long way round");
        Assert.True(MathF.Abs(mid.TurretYaw) > 3.1f);
    }

    [Fact]
    public void The_ends_are_the_two_ticks()
    {
        TankState a = State(10, 0.2f, 0.1f, 0f, 4f, 800), b = State(12, 0.4f, -0.1f, 0.1f, 6f, 600);
        Assert.Equal(a.Position, TankInterpolation.Between(a, b, 0f).Position);
        Assert.Equal(b with { }, TankInterpolation.Between(a, b, 1f));
    }
}

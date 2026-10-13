using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class TankAimTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;

    private static void Aim(ref TankState t, InputCmd cmd, int ticks)
    {
        var terrain = TestWorld.Flat();
        for (int i = 0; i < ticks; i++) TankMotion.AimGun(ref t, cmd, terrain);
    }

    [Fact]
    public void Turret_TurnsTowardAim_AtTraverseSpeed()
    {
        var t = TestWorld.Tank(100, 100);
        var cmd = TestWorld.Cmd(aimYaw: MathF.PI / 2);
        Aim(ref t, cmd, 1);
        Assert.Equal(Spec.TurretTraverse * SimConstants.Dt, t.TurretYaw, 1e-4f);
        Aim(ref t, cmd, 300);
        Assert.Equal(MathF.PI / 2, t.TurretYaw, 1e-4f);
    }

    [Fact]
    public void Turret_HoldsWorldAim_WhenHullIsTurned()
    {
        var t = TestWorld.Tank(100, 100, yaw: 0.5f);
        Aim(ref t, TestWorld.Cmd(aimYaw: 0f), 300);
        Assert.Equal(-0.5f, t.TurretYaw, 1e-4f);
    }

    [Fact]
    public void Gun_IsClampedToElevationAndDepression()
    {
        var t = TestWorld.Tank(100, 100);
        Aim(ref t, TestWorld.Cmd(aimPitch: 1.2f), 300);
        Assert.Equal(Spec.GunElevation, t.GunPitch, 1e-4f);
        Aim(ref t, TestWorld.Cmd(aimPitch: -1.2f), 300);
        Assert.Equal(-Spec.GunDepression, t.GunPitch, 1e-4f);
    }

    [Fact]
    public void Aim_OnATiltedHull_PointsGunAlongWorldAim()
    {
        var terrain = TestWorld.RampNorth(0.3f);
        var t = TestWorld.Tank(100, 100, yaw: 0.4f);
        var cmd = TestWorld.Cmd(aimYaw: 1.0f, aimPitch: 0.05f);
        for (int i = 0; i < 300; i++) TankMotion.AimGun(ref t, cmd, terrain);
        Vector3 gun = TankPose.Compute(t, terrain).GunDirection(t.TurretYaw, t.GunPitch);
        Assert.True(Vector3.Dot(gun, Angles.Direction(1.0f, 0.05f)) > 0.9999f);
    }
}

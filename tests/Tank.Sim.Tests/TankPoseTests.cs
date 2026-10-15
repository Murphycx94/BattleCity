using System.Numerics;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class TankPoseTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;

    private static TankPose PoseOn(HeightField terrain, float yaw) =>
        TankPose.Compute(new TankState { Id = 1, Spec = Spec, Position = new Vector2(100, 100), Yaw = yaw }, terrain);

    [Fact]
    public void Catalog_AllSpecsHavePositiveValues()
    {
        foreach (var s in TankCatalog.All)
        {
            Assert.All(new[]
            {
                s.HullLength, s.HullWidth, s.MaxForwardSpeed, s.MaxReverseSpeed, s.Acceleration, s.Braking, s.HullTraverse,
                s.TurretTraverse, s.GunPitchSpeed, s.GunDepression, s.GunElevation, s.MaxClimb, s.TrunnionHeight, s.GunLength,
                s.ReloadTime, s.Caliber, s.Dispersion, s.AimTime,
            }, v => Assert.True(v > 0, $"{s.Id} has a non-positive parameter"));
        }
    }

    [Fact]
    public void FlatGround_SitsOnSurface_FacingYaw()
    {
        TankPose pose = PoseOn(TestWorld.Flat(5), 0.7f);
        Assert.Equal(new Vector3(100, 5, 100), pose.Position);
        Assert.Equal(-MathF.Sin(0.7f), pose.Forward.X, 1e-5f);
        Assert.Equal(0f, pose.Forward.Y, 1e-5f);
        Assert.Equal(-MathF.Cos(0.7f), pose.Forward.Z, 1e-5f);
    }

    [Fact]
    public void FacingUpARamp_PitchesNoseUp() =>
        Assert.Equal(MathF.Sin(MathF.Atan(0.3f)), PoseOn(TestWorld.RampNorth(0.3f), 0).Forward.Y, 1e-3f);

    [Fact]
    public void SideOnToARamp_RollsTowardTheLowSide()
    {
        TankPose pose = PoseOn(TestWorld.RampNorth(0.3f), -MathF.PI / 2); // facing +X, high ground on the left
        Assert.Equal(0f, pose.Forward.Y, 1e-3f);
        Assert.True(pose.Up.Z > 0.2f, $"Up.Z was {pose.Up.Z}");
    }

    [Fact]
    public void Trunnion_SitsAtSpecHeight_AheadOfCentreByTurretOffset()
    {
        Vector3 trunnion = PoseOn(TestWorld.Flat(), 0).Trunnion(Spec);
        Assert.Equal(100f, trunnion.X, 1e-5f);
        Assert.Equal(Spec.TrunnionHeight, trunnion.Y, 1e-5f);
        Assert.Equal(100f - Spec.TurretOffset, trunnion.Z, 1e-5f);
    }

    [Fact]
    public void Muzzle_OfTheToyMediumTank_Is3_2mAheadOfTheTrunnion()
    {
        TankPose pose = PoseOn(TestWorld.Flat(), 0);
        Vector3 muzzle = pose.Muzzle(Spec, 0f, 0f);
        Vector3 expected = pose.Trunnion(Spec) + new Vector3(0, 0, -3.2f);
        Assert.True(Vector3.Distance(expected, muzzle) < 1e-4f, $"muzzle at {muzzle}, expected {expected}");
    }

    [Fact]
    public void ToHullAngles_InvertsGunDirection()
    {
        TankPose pose = PoseOn(TestWorld.RampNorth(0.3f), 0.3f);
        var (turretYaw, gunPitch) = pose.ToHullAngles(pose.GunDirection(0.5f, 0.1f));
        Assert.Equal(0.5f, turretYaw, 1e-4f);
        Assert.Equal(0.1f, gunPitch, 1e-4f);
    }
}

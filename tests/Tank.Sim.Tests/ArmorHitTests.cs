using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class ArmorHitTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;

    /// <summary>A tank at (100, 100) on flat ground; yaw 0 faces north (−Z).</summary>
    private static (OrientedBox Hull, OrientedBox Turret) Boxes(float yaw = 0, float turretYaw = 0, Tank.Sim.Terrain.HeightField? ground = null)
    {
        var tank = TestWorld.Tank(100, 100, yaw);
        TankPose pose = TankPose.Compute(tank, ground ?? TestWorld.Flat());
        return (pose.HullBox(Spec), pose.TurretBox(Spec, turretYaw));
    }

    private static ArmorHit Shoot(Vector3 from, Vector3 to, float yaw = 0, float turretYaw = 0, Tank.Sim.Terrain.HeightField? ground = null)
    {
        var (hull, turret) = Boxes(yaw, turretYaw, ground);
        Assert.True(ArmorHit.Find(Spec.HullArmor, Spec.TurretArmor, hull, turret, from, to, 1f, out ArmorHit hit), "missed");
        return hit;
    }

    [Theory]
    [InlineData(100, 1.0f, 50, 100, 1.0f, 150, ArmorFace.Front, 90f)]    // from the north, low
    [InlineData(150, 1.0f, 100, 50, 1.0f, 100, ArmorFace.Side, 60f)]     // from the east
    [InlineData(100, 1.0f, 150, 100, 1.0f, 50, ArmorFace.Rear, 40f)]     // from the south
    [InlineData(100, 30f, 101.5f, 100, -5f, 101.5f, ArmorFace.Top, 20f)] // straight down on the rear deck
    [InlineData(100, -5f, 101.5f, 100, 30f, 101.5f, ArmorFace.Bottom, 20f)]
    public void HullFaces(float fx, float fy, float fz, float tx, float ty, float tz, ArmorFace face, float thickness)
    {
        ArmorHit hit = Shoot(new Vector3(fx, fy, fz), new Vector3(tx, ty, tz));
        Assert.Equal(HitPart.Hull, hit.Part);
        Assert.Equal(face, hit.Face);
        Assert.Equal(thickness, hit.Thickness);
    }

    [Fact]
    public void Turret_IsHitAboveTheHull_WithItsOwnArmour()
    {
        ArmorHit hit = Shoot(new Vector3(100, 2.3f, 50), new Vector3(100, 2.3f, 150));
        Assert.Equal(HitPart.Turret, hit.Part);
        Assert.Equal(ArmorFace.Front, hit.Face);
        Assert.Equal(120f, hit.Thickness);
        Assert.Equal(100 - Spec.TurretOffset - Spec.TurretArmor.Length / 2, hit.Point.Z, 1e-3f);
    }

    [Fact]
    public void TurnedTurret_ShowsItsSideToAShotFromAhead()
    {
        ArmorHit hit = Shoot(new Vector3(100, 2.3f, 50), new Vector3(100, 2.3f, 150), turretYaw: MathF.PI / 2);
        Assert.Equal(HitPart.Turret, hit.Part);
        Assert.Equal(ArmorFace.Side, hit.Face);
        Assert.Equal(80f, hit.Thickness);
        Assert.Equal(1f, hit.Normal.Z * -1f, 1e-4f);   // the side plate now faces north
    }

    [Fact]
    public void TiltedHull_TiltsItsFaces()
    {
        // Nose up a 0.3 grade: the ground is 30 m up at z = 100 and the tilted front plate's centre about 31.9 m. A level
        // shot from the north meets that plate, whose normal now leans upward by the slope.
        ArmorHit hit = Shoot(new Vector3(100, 31.9f, 50), new Vector3(100, 31.9f, 150), ground: TestWorld.RampNorth(0.3f));
        Assert.Equal(ArmorFace.Front, hit.Face);
        Assert.True(hit.Normal.Y > 0.25f, $"normal {hit.Normal}");
        float angle = Penetration.ImpactAngle(new Vector3(0, 0, 1), hit.Normal);
        Assert.Equal(MathF.Atan(0.3f), angle, 0.02f);
    }

    [Fact]
    public void YawedHull_ShowsItsSide()
    {
        ArmorHit hit = Shoot(new Vector3(100, 1.0f, 50), new Vector3(100, 1.0f, 150), yaw: MathF.PI / 2);
        Assert.Equal(ArmorFace.Side, hit.Face);
    }

    [Fact]
    public void AShellStartingInsideTheHull_StrikesThePlateFacingIt()
    {
        // A muzzle poked into the hull from behind, firing forward: treat it as entering the rear plate head-on, not a graze.
        ArmorHit hit = Shoot(new Vector3(100, 1.0f, 102.9f), new Vector3(100, 1.0f, 90f));
        Assert.Equal(0f, hit.T);
        Assert.Equal(ArmorFace.Rear, hit.Face);
        Assert.True(Penetration.ImpactAngle(new Vector3(0, 0, -1), hit.Normal) < 0.01f);
    }

    [Fact]
    public void Find_TakesTheNearerBox_AndHonoursMaxT()
    {
        var (hull, turret) = Boxes();
        // Level from the front, just under the turret's bottom (1.68 m): only the hull is in the way.
        Vector3 from = new(100, 1.6f, 80), to = new(100, 1.6f, 120);
        Assert.True(ArmorHit.Find(Spec.HullArmor, Spec.TurretArmor, hull, turret, from, to, 1f, out ArmorHit hit));
        Assert.Equal(HitPart.Hull, hit.Part);
        Assert.False(ArmorHit.Find(Spec.HullArmor, Spec.TurretArmor, hull, turret, from, to, hit.T * 0.9f, out _));
    }

    [Fact]
    public void Find_TakesTheTurret_WhenThePathMeetsItBeforeTheHull()
    {
        var (hull, turret) = Boxes();
        // Diving steeply from high in front. The turret front (z = 99.7 − 1.45 = 98.25) is met at t = 8.25 / 16.5 = 0.5,
        // 2.2 m up (inside its 1.68–2.8). The path would go on into the hull through its deck (y = 1.74) at
        // t = 8.26 / 15.6 ≈ 0.5295, z ≈ 98.74; it passes over the hull's front plate (z = 96.9) at 3.48 m, too high.
        Vector3 from = new(100, 10f, 90), to = new(100, -5.6f, 106.5f);
        Assert.True(hull.Raycast(from, to, out float hullT, out BoxFace hullFace));
        Assert.Equal(BoxFace.PosY, hullFace);
        Assert.Equal(0.5295f, hullT, 1e-3f);

        Assert.True(ArmorHit.Find(Spec.HullArmor, Spec.TurretArmor, hull, turret, from, to, 1f, out ArmorHit hit));
        Assert.Equal(HitPart.Turret, hit.Part);
        Assert.Equal(ArmorFace.Front, hit.Face);
        Assert.Equal(120f, hit.Thickness);
        Assert.Equal(0.5f, hit.T, 1e-4f);
        Assert.Equal(2.2f, hit.Point.Y, 1e-3f);
    }
}

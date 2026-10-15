using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class DispersionTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;

    [Fact]
    public void Target_AddsEachMotionInProportion_CappedAtFullRate()
    {
        Assert.Equal(0f, Dispersion.Target(Spec, 0, 0, 0));
        Assert.Equal(2.0f, Dispersion.Target(Spec, Spec.MaxForwardSpeed, 0, 0), 1e-5f);
        Assert.Equal(1.0f, Dispersion.Target(Spec, -Spec.MaxForwardSpeed / 2, 0, 0), 1e-5f);
        Assert.Equal(1.5f, Dispersion.Target(Spec, 0, -Spec.HullTraverse, 0), 1e-5f);
        Assert.Equal(1.0f, Dispersion.Target(Spec, 0, 0, Spec.TurretTraverse * 3), 1e-5f);
        Assert.Equal(4.5f, Dispersion.Target(Spec, Spec.MaxForwardSpeed, Spec.HullTraverse, Spec.TurretTraverse), 1e-5f);
    }

    [Fact]
    public void Step_JumpsUpAtOnce()
    {
        Assert.Equal(2f, Dispersion.Step(0.5f, 2f, Spec.AimTime));
    }

    [Fact]
    public void Step_ClosesWithinAboutOneAimTime()
    {
        float bloom = 2f;
        int ticks = (int)MathF.Round(Spec.AimTime * SimConstants.TickRate);
        for (int i = 0; i < ticks; i++) bloom = Dispersion.Step(bloom, 0f, Spec.AimTime);
        Assert.InRange(bloom, 0.05f * 2f * 0.9f, 0.05f * 2f * 1.1f);   // three time constants: e⁻³ ≈ 5% left
    }

    [Fact]
    public void Radius_GrowsWithBloom()
    {
        Assert.Equal(0.36f, Dispersion.Radius(Spec, 0), 1e-6f);
        Assert.Equal(1.44f, Dispersion.Radius(Spec, 3), 1e-5f);
    }

    [Fact]
    public void Deflect_StaysInsideTheCircle_SameSeedSameOffset()
    {
        Vector3 aim = Angles.Direction(0.4f, -0.05f);
        float radius = 0.36f;
        int inner = 0;
        var quadrants = new int[4];
        Vector3 side = Vector3.Normalize(Vector3.Cross(aim, Vector3.UnitY));
        Vector3 up = Vector3.Cross(side, aim);
        for (int shot = 0; shot < 1000; shot++)
        {
            uint seed = ShotRandom.Seed(10, 1, shot);
            Vector3 dir = Dispersion.Deflect(aim * 780f, radius, seed);
            Assert.Equal(1f, dir.Length(), 1e-5f);
            Assert.Equal(dir, Dispersion.Deflect(aim * 780f, radius, seed));
            float offsetAt100 = MathF.Tan(MathF.Acos(MathF.Min(1f, Vector3.Dot(dir, aim)))) * Dispersion.ReferenceRange;
            Assert.True(offsetAt100 <= radius * 1.001f, $"shot {shot} landed {offsetAt100} m off");
            if (offsetAt100 < radius / 2) inner++;
            float angle = MathF.Atan2(Vector3.Dot(dir, up), Vector3.Dot(dir, side));   // around the aim axis
            quadrants[Math.Min(3, (int)((angle + MathF.PI) / (MathF.PI / 2)))]++;
        }
        // Uniform radius share: half the shots fall in the inner half of the radius (a quarter of the area).
        Assert.InRange(inner, 430, 570);
        // ...and the offsets go all the way round the circle, not just one side of it.
        Assert.All(quadrants, count => Assert.InRange(count, 200, 300));
    }

    [Fact]
    public void Deflect_WorksStraightUp()
    {
        Vector3 dir = Dispersion.Deflect(Vector3.UnitY, 0.36f, 5);
        Assert.True(dir.Y > 0.99f);
    }
}

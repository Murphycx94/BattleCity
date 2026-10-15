using System.Numerics;
using Tank.Client.Vehicles;
using Tank.Sim.Vehicles;

namespace Tank.Art.Tests;

public class TurretTossTests
{
    private static readonly TankLook Look = TankLooks.Medium;
    private static readonly TurretToss Toss = new(Look, TankCatalog.Medium.TurretOffset);

    [Fact]
    public void Starts_where_the_intact_turret_is()
    {
        var (position, rotation) = Toss.At(0f, startYaw: 0.7f);
        Assert.Equal(Toss.Pivot, position);
        Assert.Equal(new Vector3(0, Look.TurretBase + TurretToss.PivotAboveRing, -TankCatalog.Medium.TurretOffset), position);
        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
        Assert.True(MathF.Abs(Quaternion.Dot(expected, rotation)) > 0.9999f, $"rotation {rotation}");
    }

    [Fact]
    public void Flies_about_four_metres_up()
    {
        float highest = 0f;
        for (int i = 0; i <= 110; i++) highest = MathF.Max(highest, Toss.At(i / 100f, 0f).Position.Y - Toss.Pivot.Y);
        Assert.InRange(highest, TurretToss.Apex - 0.4f, TurretToss.Apex + 0.4f);
    }

    [Fact]
    public void Lands_beside_the_hull_and_stays()
    {
        var landed = Toss.At(TurretToss.FlightTime, 0.3f);
        Assert.Equal(Toss.Landing, landed.Position);
        Assert.Equal(landed, Toss.At(TurretToss.FlightTime + 5f, 0.3f));
        Assert.True(Toss.Landing.X - Toss.DomeRadius >= Look.UpperHullWidth / 2, "the landed dome overlaps the hull");
    }

    [Fact]
    public void Landed_dome_rests_on_the_ground()
    {
        var (position, rotation) = Toss.At(TurretToss.FlightTime, 1.1f);
        float lowest = position.Y + TurretToss.LowestRimPoint(rotation, Toss.DomeRadius);
        Assert.Equal(0f, lowest, 0.02f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(MathF.PI / 2)]
    [InlineData(-MathF.PI / 2)]
    [InlineData(MathF.PI)]
    [InlineData(-MathF.PI)]
    [InlineData(2.5f)]
    public void Spins_more_than_a_full_turn_without_jumps(float startYaw)
    {
        // Sample the forward direction's heading; it must sweep past a full circle in small steps.
        float swept = 0f, last = Heading(Toss.At(0f, startYaw).Rotation);
        for (int i = 1; i <= 100; i++)
        {
            float heading = Heading(Toss.At(TurretToss.FlightTime * i / 100f, startYaw).Rotation);
            float step = heading - last;
            if (step > MathF.PI) step -= MathF.Tau;
            if (step < -MathF.PI) step += MathF.Tau;
            Assert.True(MathF.Abs(step) < 0.5f, $"heading jumps by {step} rad at sample {i}");
            swept += step;
            last = heading;
        }
        Assert.True(MathF.Abs(swept) > MathF.Tau, $"swept only {swept} rad");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(MathF.PI / 2)]
    [InlineData(-MathF.PI / 2)]
    [InlineData(MathF.PI)]
    [InlineData(-MathF.PI)]
    [InlineData(2.5f)]
    public void Landed_gun_points_away_from_the_hull(float startYaw)
    {
        var (position, rotation) = Toss.At(TurretToss.FlightTime, startYaw);
        Vector3 muzzle = LandedMuzzle(startYaw, gunPitch: 0f);
        Assert.True(MathF.Abs(muzzle.X) > Look.UpperHullWidth / 2, $"muzzle {muzzle} is over the hull");
        Assert.True(muzzle.X > position.X, $"muzzle {muzzle} points back towards the hull");
        Assert.True(muzzle.Y > Look.MuzzleRadius, $"muzzle {muzzle} dips into the ground");
        Quaternion landed = Toss.At(TurretToss.FlightTime, 0f).Rotation;
        Assert.True(MathF.Abs(Quaternion.Dot(landed, rotation)) > 0.9999f, "the landed pose depends on the start yaw");
    }

    [Fact]
    public void Landed_gun_at_full_depression_stays_above_the_ground()
    {
        // A wreck keeps the gun pitch it had at the kill (TankModel.Apply still sets it); the landed pose ignores the start yaw.
        Vector3 muzzle = LandedMuzzle(0f, -TankCatalog.Medium.GunDepression);
        Assert.True(muzzle.Y > 0f, $"muzzle {muzzle} is buried in the ground");
    }

    /// <summary>
    /// The landed muzzle in hull space, nested as TankModel nests it: Toss (at the pivot) → TurretBody (the pivot height
    /// undone) → Gun (trunnion, GunPivotZ, pitched) → muzzle GunLength + GunPivotZ further along the gun's −Z.
    /// </summary>
    private static Vector3 LandedMuzzle(float startYaw, float gunPitch)
    {
        TankSpec spec = TankCatalog.Medium;
        var (position, rotation) = Toss.At(TurretToss.FlightTime, startYaw);
        var gun = new Vector3(0, spec.TrunnionHeight, Look.GunPivotZ);
        Vector3 barrel = Vector3.Transform(new Vector3(0, 0, -(spec.GunLength + Look.GunPivotZ)), Quaternion.CreateFromAxisAngle(Vector3.UnitX, gunPitch));
        return position + Vector3.Transform(gun + barrel - new Vector3(0, Toss.Pivot.Y, 0), rotation);
    }

    private static float Heading(Quaternion rotation)
    {
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, rotation);
        return MathF.Atan2(-forward.X, -forward.Z);
    }
}

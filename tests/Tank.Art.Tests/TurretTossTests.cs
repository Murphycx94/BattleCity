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

    [Fact]
    public void Spins_more_than_a_full_turn()
    {
        // Sample the forward direction's heading; it must sweep past a full circle.
        float swept = 0f, last = 0f;
        for (int i = 1; i <= 100; i++)
        {
            Vector3 forward = Vector3.Transform(-Vector3.UnitZ, Toss.At(TurretToss.FlightTime * i / 100f, 0f).Rotation);
            float heading = MathF.Atan2(-forward.X, -forward.Z);
            float step = heading - last;
            if (step > MathF.PI) step -= MathF.Tau;
            if (step < -MathF.PI) step += MathF.Tau;
            swept += step;
            last = heading;
        }
        Assert.True(MathF.Abs(swept) > MathF.Tau, $"swept only {swept} rad");
    }
}

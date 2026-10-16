using System.Numerics;
using Tank.Client.Vehicles;
using Tank.Sim.Vehicles;

namespace Tank.Art.Tests;

public class TurretTossTests
{
    public static TheoryData<string> TurretIds => new(TankCatalog.All.Where(s => s.HasTurret).Select(s => s.Id));

    /// <summary>Every turret tank, crossed with kill-time turret yaws all round the clock.</summary>
    public static TheoryData<string, float> TurretIdsAndYaws()
    {
        var data = new TheoryData<string, float>();
        foreach (TankSpec spec in TankCatalog.All.Where(s => s.HasTurret))
        foreach (float yaw in new[] { 0f, MathF.PI / 2, -MathF.PI / 2, MathF.PI, -MathF.PI, 2.5f })
            data.Add(spec.Id, yaw);
        return data;
    }

    private static TurretToss TossOf(string specId) => new(TankLooks.For(specId), TankCatalog.ById(specId).TurretOffset);

    [Theory]
    [MemberData(nameof(TurretIds))]
    public void Starts_where_the_intact_turret_is(string specId)
    {
        TurretToss toss = TossOf(specId);
        TankLook look = TankLooks.For(specId);
        var (position, rotation) = toss.At(0f, startYaw: 0.7f);
        Assert.Equal(toss.Pivot, position);
        Assert.Equal(new Vector3(0, look.TurretBase + TurretToss.PivotAboveRing, -TankCatalog.ById(specId).TurretOffset), position);
        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
        Assert.True(MathF.Abs(Quaternion.Dot(expected, rotation)) > 0.9999f, $"rotation {rotation}");
    }

    [Theory]
    [MemberData(nameof(TurretIds))]
    public void Flies_about_four_metres_up(string specId)
    {
        TurretToss toss = TossOf(specId);
        float highest = 0f;
        for (int i = 0; i <= 110; i++) highest = MathF.Max(highest, toss.At(i / 100f, 0f).Position.Y - toss.Pivot.Y);
        Assert.InRange(highest, TurretToss.Apex - 0.4f, TurretToss.Apex + 0.4f);
    }

    /// <summary>
    /// Lands where it says and stays put, and no vertex of the landed turret mesh (hatch included) reaches over the hull
    /// side: checked on the mesh, since <see cref="TurretToss.Landing"/> is itself placed by <see cref="TurretToss.DomeRadius"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(TurretIds))]
    public void Lands_beside_the_hull_and_stays(string specId)
    {
        TurretToss toss = TossOf(specId);
        TankLook look = TankLooks.For(specId);
        var landed = toss.At(TurretToss.FlightTime, 0.3f);
        Assert.Equal(toss.Landing, landed.Position);
        Assert.Equal(landed, toss.At(TurretToss.FlightTime + 5f, 0.3f));
        float nearest = LandedTurretMesh(specId, 0.3f).Min(p => p.X);
        Assert.True(nearest >= look.UpperHullWidth / 2, $"the landed turret reaches to x={nearest:F3}, over the hull side at {look.UpperHullWidth / 2:F3}");
    }

    [Theory]
    [MemberData(nameof(TurretIds))]
    public void Landed_dome_rests_on_the_ground(string specId)
    {
        TurretToss toss = TossOf(specId);
        var (position, rotation) = toss.At(TurretToss.FlightTime, 1.1f);
        float lowest = position.Y + toss.LowestPoint(rotation);
        Assert.Equal(0f, lowest, 1e-3f);
    }

    /// <summary>
    /// The same landing checked against the turret mesh itself rather than the profile sampling <see cref="TurretToss"/>
    /// places it with: its lowest vertex sits on the ground, at most a hair off (the mesh has fewer sides than the sampling).
    /// </summary>
    [Theory]
    [MemberData(nameof(TurretIds))]
    public void Landed_turret_mesh_touches_the_ground(string specId)
    {
        float lowest = LandedTurretMesh(specId, 0.4f).Min(p => p.Y);
        Assert.InRange(lowest, -0.005f, 0.02f);
    }

    [Theory]
    [MemberData(nameof(TurretIdsAndYaws))]
    public void Spins_more_than_a_full_turn_without_jumps(string specId, float startYaw)
    {
        TurretToss toss = TossOf(specId);
        // Sample the forward direction's heading; it must sweep past a full circle in small steps.
        float swept = 0f, last = Heading(toss.At(0f, startYaw).Rotation);
        for (int i = 1; i <= 100; i++)
        {
            float heading = Heading(toss.At(TurretToss.FlightTime * i / 100f, startYaw).Rotation);
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
    [MemberData(nameof(TurretIdsAndYaws))]
    public void Landed_gun_points_away_from_the_hull(string specId, float startYaw)
    {
        TurretToss toss = TossOf(specId);
        TankLook look = TankLooks.For(specId);
        var (position, rotation) = toss.At(TurretToss.FlightTime, startYaw);
        Vector3 muzzle = LandedMuzzle(specId, startYaw, gunPitch: 0f);
        Assert.True(MathF.Abs(muzzle.X) > look.UpperHullWidth / 2, $"muzzle {muzzle} is over the hull");
        Assert.True(muzzle.X > position.X, $"muzzle {muzzle} points back towards the hull");
        Assert.True(muzzle.Y > look.MuzzleRadius, $"muzzle {muzzle} dips into the ground");
        Quaternion landed = toss.At(TurretToss.FlightTime, 0f).Rotation;
        Assert.True(MathF.Abs(Quaternion.Dot(landed, rotation)) > 0.9999f, "the landed pose depends on the start yaw");
    }

    [Theory]
    [MemberData(nameof(TurretIds))]
    public void Landed_gun_at_full_depression_stays_above_the_ground(string specId)
    {
        TurretToss toss = TossOf(specId);
        // A wreck keeps the gun pitch it had at the kill (TankModel.Apply still sets it); the landed pose ignores the start yaw.
        Vector3 muzzle = LandedMuzzle(specId, 0f, -TankCatalog.ById(specId).GunDepression);
        Assert.True(muzzle.Y > 0f, $"muzzle {muzzle} is buried in the ground");
    }

    /// <summary>
    /// The turret mesh's vertices in hull space once landed, nested as TankModel nests it: Toss (at the pivot) →
    /// TurretBody (the pivot height undone) → the shell.
    /// </summary>
    private static IEnumerable<Vector3> LandedTurretMesh(string specId, float startYaw)
    {
        TurretToss toss = TossOf(specId);
        var (position, rotation) = toss.At(TurretToss.FlightTime, startYaw);
        var undoPivot = new Vector3(0, toss.Pivot.Y, 0);
        return TankParts.Turret(TankLooks.For(specId)).Positions.Select(p => position + Vector3.Transform(p - undoPivot, rotation));
    }

    /// <summary>
    /// The landed muzzle in hull space, nested as TankModel nests it: Toss (at the pivot) → TurretBody (the pivot height
    /// undone) → Gun (trunnion, GunPivotZ, pitched) → muzzle GunLength + GunPivotZ further along the gun's −Z.
    /// </summary>
    private static Vector3 LandedMuzzle(string specId, float startYaw, float gunPitch)
    {
        TankSpec spec = TankCatalog.ById(specId);
        TankLook look = TankLooks.For(specId);
        TurretToss toss = TossOf(specId);
        var (position, rotation) = toss.At(TurretToss.FlightTime, startYaw);
        var gun = new Vector3(0, spec.TrunnionHeight, look.GunPivotZ);
        Vector3 barrel = Vector3.Transform(new Vector3(0, 0, -(spec.GunLength + look.GunPivotZ)), Quaternion.CreateFromAxisAngle(Vector3.UnitX, gunPitch));
        return position + Vector3.Transform(gun + barrel - new Vector3(0, toss.Pivot.Y, 0), rotation);
    }

    private static float Heading(Quaternion rotation)
    {
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, rotation);
        return MathF.Atan2(-forward.X, -forward.Z);
    }
}

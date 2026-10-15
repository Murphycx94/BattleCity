using SN = System.Numerics;

namespace Tank.Client.Vehicles;

/// <summary>
/// A knocked-out tank's turret flight, presentation only (no collision): the dome and gun pop up about
/// <see cref="Apex"/> m, spinning, and land tilted on the ground beside the hull, gun pointing away from it, where they
/// stay until the tank is revived. Everything is in hull space and describes the turret's pivot: the middle of the dome,
/// <see cref="PivotAboveRing"/> above the turret ring. Engine-neutral so Tank.Art.Tests can check it.
/// </summary>
public sealed class TurretToss
{
    public const float FlightTime = 1.1f;
    public const float Apex = 4f;                   // the flight peaks about this far above the turret's resting place
    public const float PivotAboveRing = 0.4f;
    private const float Lift = 4.5f;                 // parabola height that, with the drop to the ground, peaks near Apex
    private const float MinSpinTurns = 1.25f;        // spins at least this much, then on to the landing yaw
    private const float Clearance = 0.3f;            // gap between the landed dome and the hull side
    private const float LandBehind = 0.8f;           // lands a little behind the turret ring
    private static readonly float LandingYaw = -110f * MathF.PI / 180f;  // gun out to the right (+X, yaw −90°), swung 20° back
    private static readonly float LandingRoll = 18f * MathF.PI / 180f;
    private static readonly float LandingPitch = 8f * MathF.PI / 180f;   // gun drooping towards the ground
    private static readonly float WobbleRoll = 25f * MathF.PI / 180f;

    /// <param name="turretOffset">Turret ring centre ahead of the hull origin (<c>TankSpec.TurretOffset</c>).</param>
    public TurretToss(TankLook look, float turretOffset)
    {
        DomeRadius = look.TurretProfile.Max(p => p.X);
        Pivot = new SN.Vector3(0, look.TurretBase + PivotAboveRing, -turretOffset);
        float rest = -LowestRimPoint(Landed(0f), DomeRadius);
        Landing = new SN.Vector3(look.UpperHullWidth / 2 + DomeRadius + Clearance, rest, -turretOffset + LandBehind);
    }

    public float DomeRadius { get; }

    /// <summary>The pivot's place on an intact tank.</summary>
    public SN.Vector3 Pivot { get; }

    /// <summary>The pivot's place once landed: beside the right side of the hull, the tilted dome's rim on the ground.</summary>
    public SN.Vector3 Landing { get; }

    /// <summary>Pivot position and turret orientation <paramref name="time"/> s after the kill.</summary>
    /// <param name="startYaw">Turret yaw (hull-relative) at the moment of the kill; the landed pose doesn't depend on it.</param>
    public (SN.Vector3 Position, SN.Quaternion Rotation) At(float time, float startYaw)
    {
        float s = Math.Clamp(time / FlightTime, 0f, 1f);
        SN.Vector3 along = SN.Vector3.Lerp(Pivot, Landing, s);
        float height = Pivot.Y + (Landing.Y - Pivot.Y) * s + 4f * Lift * s * (1f - s);
        float spin = Spin(startYaw) * (1f - (1f - s) * (1f - s));
        float roll = LandingRoll * s + WobbleRoll * MathF.Sin(MathF.PI * s);
        var rotation = SN.Quaternion.CreateFromYawPitchRoll(startYaw + spin, -LandingPitch * s, roll);
        return (new SN.Vector3(along.X, height, along.Z), rotation);
    }

    /// <summary>The smallest turn of at least <see cref="MinSpinTurns"/> that takes <paramref name="startYaw"/> to <see cref="LandingYaw"/>.</summary>
    private static float Spin(float startYaw)
    {
        float least = MinSpinTurns * MathF.Tau;
        float rest = LandingYaw - startYaw - least;
        return least + rest - MathF.Tau * MathF.Floor(rest / MathF.Tau);
    }

    /// <summary>The landed tilt without the final spin (yaw doesn't change heights).</summary>
    private static SN.Quaternion Landed(float yaw) => SN.Quaternion.CreateFromYawPitchRoll(yaw, -LandingPitch, LandingRoll);

    /// <summary>Lowest point of the dome's base rim, relative to the pivot, for a turret turned by <paramref name="rotation"/>.</summary>
    public static float LowestRimPoint(SN.Quaternion rotation, float radius)
    {
        float lowest = float.MaxValue;
        for (int i = 0; i < 72; i++)
        {
            float a = MathF.Tau * i / 72;
            var rim = new SN.Vector3(radius * MathF.Cos(a), -PivotAboveRing, radius * MathF.Sin(a));
            lowest = MathF.Min(lowest, SN.Vector3.Transform(rim, rotation).Y);
        }
        return lowest;
    }
}

namespace Tank.Sim.Vehicles;

/// <summary>Per-vehicle constants. Metres, seconds, radians, m/s. Hull origin is the ground contact centre.</summary>
public sealed record TankSpec
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required float HullLength { get; init; }
    public required float HullWidth { get; init; }
    public required float MaxForwardSpeed { get; init; }
    public required float MaxReverseSpeed { get; init; }
    public required float Acceleration { get; init; }
    public required float Braking { get; init; }
    public required float HullTraverse { get; init; }
    public required float TurretTraverse { get; init; }
    public required float GunPitchSpeed { get; init; }

    /// <summary>How far the gun can point below the hull plane (positive number).</summary>
    public required float GunDepression { get; init; }

    public required float GunElevation { get; init; }
    public required float MaxClimb { get; init; }

    /// <summary>Gun pivot height above the hull origin.</summary>
    public required float TrunnionHeight { get; init; }

    /// <summary>Turret ring centre ahead (+) of the hull centre.</summary>
    public required float TurretOffset { get; init; }

    public required float GunLength { get; init; }
    public required float ShellSpeed { get; init; }
    public required float ReloadTime { get; init; }
}

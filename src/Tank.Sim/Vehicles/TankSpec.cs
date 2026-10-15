using Tank.Sim.Combat;

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
    public required float ReloadTime { get; init; }

    public required int MaxHealth { get; init; }

    /// <summary>Gun calibre, mm (for the three-calibre overmatch rule).</summary>
    public required float Caliber { get; init; }

    /// <summary>Hull hit box, anchored at the hull origin.</summary>
    public required ArmorBox HullArmor { get; init; }

    /// <summary>Turret hit box, anchored at the turret ring (<see cref="TurretOffset"/> ahead of the hull origin); turns with the turret.</summary>
    public required ArmorBox TurretArmor { get; init; }

    public required ShellSpec Ap { get; init; }
    public required ShellSpec He { get; init; }

    /// <summary>Aim-circle radius at 100 m when fully aimed, m.</summary>
    public required float Dispersion { get; init; }

    /// <summary>Seconds for the aim circle to close after moving (three time constants).</summary>
    public required float AimTime { get; init; }

    public required BloomFactors Bloom { get; init; }

    public ShellSpec Shell(AmmoType ammo) => ammo == AmmoType.HE ? He : Ap;
}

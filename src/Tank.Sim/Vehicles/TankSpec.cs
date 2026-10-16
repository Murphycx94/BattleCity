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

    /// <summary>A <see cref="GunArc"/> this wide is a full turret: the gun turns all the way round.</summary>
    public const float FullTraverse = MathF.PI;

    /// <summary>
    /// How far the gun turns left or right of the hull's heading. <see cref="FullTraverse"/> for a turret; a casemate gun
    /// (the tank destroyer) has a narrow arc and the hull turns for the rest (see <c>TankMotion.TraverseHull</c>).
    /// </summary>
    public required float GunArc { get; init; }

    /// <summary>The gun sits in a turret (else in a casemate built into the hull).</summary>
    public bool HasTurret => GunArc >= FullTraverse;

    public required float GunPitchSpeed { get; init; }

    /// <summary>How far the gun can point below the hull plane (positive number).</summary>
    public required float GunDepression { get; init; }

    public required float GunElevation { get; init; }
    public required float MaxClimb { get; init; }

    /// <summary>Gun pivot height above the hull origin.</summary>
    public required float TrunnionHeight { get; init; }

    /// <summary>Turret ring centre ahead (+) of the hull centre; for a casemate gun, its pivot.</summary>
    public required float TurretOffset { get; init; }

    public required float GunLength { get; init; }
    public required float ReloadTime { get; init; }

    public required int MaxHealth { get; init; }

    /// <summary>Gun calibre, mm (for the three-calibre overmatch rule).</summary>
    public required float Caliber { get; init; }

    /// <summary>Hull hit box, anchored at the hull origin.</summary>
    public required ArmorBox HullArmor { get; init; }

    /// <summary>
    /// Turret hit box, anchored at the turret ring (<see cref="TurretOffset"/> ahead of the hull origin); turns with the
    /// turret. Without a turret it is the casemate, part of the hull: anchored at the hull origin like the hull box, and
    /// <see cref="TurretOffset"/> places only the gun's pivot.
    /// </summary>
    public required ArmorBox TurretArmor { get; init; }

    public required ShellSpec Ap { get; init; }
    public required ShellSpec He { get; init; }

    /// <summary>Aim-circle radius at 100 m when fully aimed, m.</summary>
    public required float Dispersion { get; init; }

    /// <summary>Seconds for the aim circle to close after moving (three time constants).</summary>
    public required float AimTime { get; init; }

    public required BloomFactors Bloom { get; init; }

    /// <summary>The stats of this gun's round of type <paramref name="ammo"/>.</summary>
    public ShellSpec Round(AmmoType ammo) => ammo == AmmoType.HE ? He : Ap;
}

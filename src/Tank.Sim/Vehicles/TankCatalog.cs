using Tank.Sim.Combat;
using Tank.Sim.Geometry;

namespace Tank.Sim.Vehicles;

/// <summary>Vehicle table (design §4.4). Only the medium tank so far.</summary>
public static class TankCatalog
{
    private const float Kmh = 1f / 3.6f;

    public static readonly TankSpec Medium = new()
    {
        Id = "medium",
        Name = "中型坦克",
        HullLength = 6.5f,
        HullWidth = 3.2f,
        MaxForwardSpeed = 50 * Kmh,
        MaxReverseSpeed = 18 * Kmh,
        Acceleration = 4f,
        Braking = 9f,
        HullTraverse = Angles.Deg(42),
        TurretTraverse = Angles.Deg(40),
        GunPitchSpeed = Angles.Deg(30),
        GunDepression = Angles.Deg(7),
        GunElevation = Angles.Deg(20),
        MaxClimb = Angles.Deg(32),
        TrunnionHeight = 2.1f,
        TurretOffset = 0.3f,
        GunLength = 3.2f,
        ReloadTime = 4f,
        MaxHealth = 900,
        Caliber = 85,
        HullArmor = new ArmorBox(Width: 3.2f, Length: 6.2f, Bottom: 0.3f, Top: 1.74f, new Armor(Front: 90, Side: 60, Rear: 40, Top: 20, Bottom: 20)),
        TurretArmor = new ArmorBox(Width: 2.9f, Length: 2.9f, Bottom: 1.68f, Top: 2.8f, new Armor(Front: 120, Side: 80, Rear: 50, Top: 25, Bottom: 25)),
        Ap = new ShellSpec(Penetration: 175, Damage: 220, Speed: 780, SplashRadius: 0, Capacity: 40),
        He = new ShellSpec(Penetration: 45, Damage: 280, Speed: 780 * 0.8f, SplashRadius: 3, Capacity: 15),
        Dispersion = 0.36f,
        AimTime = 2.1f,
        Bloom = new BloomFactors(Moving: 2.0f, HullTurn: 1.5f, TurretTurn: 1.0f, Fire: 3.0f),
    };

    public static IReadOnlyList<TankSpec> All { get; } = new[] { Medium };
}

using Tank.Sim.Geometry;

namespace Tank.Sim.Vehicles;

/// <summary>Vehicle table (design §4.4). Stage 1 ships the medium tank only.</summary>
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
        ShellSpeed = 780f,
        ReloadTime = 6.5f,
    };

    public static IReadOnlyList<TankSpec> All { get; } = new[] { Medium };
}

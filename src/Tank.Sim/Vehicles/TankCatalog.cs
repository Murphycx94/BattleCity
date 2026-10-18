using Tank.Sim.Combat;
using Tank.Sim.Geometry;

namespace Tank.Sim.Vehicles;

/// <summary>
/// Vehicle table (design §4.4, rebalanced in the stage 2B design §2: every tank deals 52–58 damage a second, the
/// difference being in the size of each hit).
/// </summary>
public static class TankCatalog
{
    private const float Kmh = 1f / 3.6f;

    public static readonly TankSpec Light = new()
    {
        Id = "light",
        Name = "轻型坦克",
        HullLength = 5.5f,
        HullWidth = 2.8f,
        MaxForwardSpeed = 60 * Kmh,
        MaxReverseSpeed = 20 * Kmh,
        Acceleration = 5f,
        Braking = 10f,
        HullTraverse = Angles.Deg(50),
        TurretTraverse = Angles.Deg(48),
        GunArc = TankSpec.FullTraverse,
        GunPitchSpeed = Angles.Deg(35),
        GunDepression = Angles.Deg(8),
        GunElevation = Angles.Deg(20),
        MaxClimb = Angles.Deg(32),
        BrickWallRamSpeed = float.PositiveInfinity,
        TrunnionHeight = 1.85f,
        TurretOffset = 0f,
        GunLength = 3.0f,
        ReloadTime = 2.5f,
        MaxHealth = 600,
        Caliber = 75,
        HullArmor = new ArmorBox(Width: 2.8f, Length: 5.2f, Bottom: 0.3f, Top: 1.55f, new Armor(Front: 30, Side: 25, Rear: 20, Top: 15, Bottom: 15)),
        TurretArmor = new ArmorBox(Width: 2.2f, Length: 2.2f, Bottom: 1.49f, Top: 2.3f, new Armor(Front: 40, Side: 30, Rear: 25, Top: 15, Bottom: 15)),
        Ap = new ShellSpec(Penetration: 140, Damage: 130, Speed: 800, SplashRadius: 0, Capacity: 40),
        He = new ShellSpec(Penetration: 40, Damage: 170, Speed: 800 * 0.8f, SplashRadius: 2.5f, Capacity: 15),
        Dispersion = 0.40f,
        AimTime = 1.8f,
        Bloom = new BloomFactors(Moving: 1.5f, HullTurn: 1.2f, TurretTurn: 0.8f, Fire: 3.0f),
        ViewRange = 300,
        Camo = new Concealment(Moving: 0.30f, Still: 0.40f),
    };

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
        GunArc = TankSpec.FullTraverse,
        GunPitchSpeed = Angles.Deg(30),
        GunDepression = Angles.Deg(7),
        GunElevation = Angles.Deg(20),
        MaxClimb = Angles.Deg(32),
        BrickWallRamSpeed = float.PositiveInfinity,
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
        ViewRange = 270,
        Camo = new Concealment(Moving: 0.18f, Still: 0.25f),
    };

    public static readonly TankSpec Heavy = new()
    {
        Id = "heavy",
        Name = "重型坦克",
        HullLength = 7.5f,
        HullWidth = 3.6f,
        MaxForwardSpeed = 35 * Kmh,
        MaxReverseSpeed = 12 * Kmh,
        Acceleration = 2.8f,
        Braking = 7f,
        HullTraverse = Angles.Deg(26),
        TurretTraverse = Angles.Deg(24),
        GunArc = TankSpec.FullTraverse,
        GunPitchSpeed = Angles.Deg(22),
        GunDepression = Angles.Deg(6),
        GunElevation = Angles.Deg(18),
        MaxClimb = Angles.Deg(32),
        BrickWallRamSpeed = 15 * Kmh,
        TrunnionHeight = 2.55f,
        TurretOffset = 0.2f,
        GunLength = 3.8f,
        ReloadTime = 6f,
        MaxHealth = 1400,
        Caliber = 122,
        HullArmor = new ArmorBox(Width: 3.6f, Length: 7.2f, Bottom: 0.35f, Top: 2.0f, new Armor(Front: 150, Side: 90, Rear: 60, Top: 30, Bottom: 25)),
        TurretArmor = new ArmorBox(Width: 3.1f, Length: 3.1f, Bottom: 1.94f, Top: 3.3f, new Armor(Front: 200, Side: 120, Rear: 80, Top: 40, Bottom: 40)),
        Ap = new ShellSpec(Penetration: 225, Damage: 330, Speed: 720, SplashRadius: 0, Capacity: 25),
        He = new ShellSpec(Penetration: 65, Damage: 420, Speed: 720 * 0.8f, SplashRadius: 4, Capacity: 15),
        Dispersion = 0.44f,
        AimTime = 2.9f,
        Bloom = new BloomFactors(Moving: 2.5f, HullTurn: 2.0f, TurretTurn: 1.2f, Fire: 3.5f),
        ViewRange = 250,
        Camo = new Concealment(Moving: 0.08f, Still: 0.12f),
    };

    /// <summary>
    /// The tank destroyer: no turret. Its gun sits in a ball mount on the casemate's front plate (<c>TurretOffset</c> is
    /// that pivot) and turns 12° each way; the casemate box spans most of the hull (see <c>TankSpec.TurretArmor</c>).
    /// </summary>
    public static readonly TankSpec Td = new()
    {
        Id = "td",
        Name = "坦克歼击车",
        HullLength = 7.0f,
        HullWidth = 3.3f,
        MaxForwardSpeed = 40 * Kmh,
        MaxReverseSpeed = 15 * Kmh,
        Acceleration = 3.5f,
        Braking = 8f,
        HullTraverse = Angles.Deg(32),
        TurretTraverse = Angles.Deg(30),
        GunArc = Angles.Deg(12),
        GunPitchSpeed = Angles.Deg(25),
        GunDepression = Angles.Deg(6),
        GunElevation = Angles.Deg(15),
        MaxClimb = Angles.Deg(32),
        BrickWallRamSpeed = float.PositiveInfinity,
        TrunnionHeight = 1.85f,
        TurretOffset = 2.8f,
        GunLength = 3.5f,   // the muzzle 2.8 m past the nose (playtest)
        ReloadTime = 6.5f,
        MaxHealth = 950,
        Caliber = 105,
        HullArmor = new ArmorBox(Width: 3.3f, Length: 6.8f, Bottom: 0.3f, Top: 1.3f, new Armor(Front: 140, Side: 50, Rear: 35, Top: 20, Bottom: 20)),
        TurretArmor = new ArmorBox(Width: 3.0f, Length: 5.8f, Bottom: 1.25f, Top: 2.2f, new Armor(Front: 140, Side: 50, Rear: 35, Top: 20, Bottom: 20)),
        Ap = new ShellSpec(Penetration: 250, Damage: 380, Speed: 850, SplashRadius: 0, Capacity: 30),
        He = new ShellSpec(Penetration: 55, Damage: 480, Speed: 850 * 0.8f, SplashRadius: 3.5f, Capacity: 12),
        Dispersion = 0.32f,
        AimTime = 1.9f,
        Bloom = new BloomFactors(Moving: 2.5f, HullTurn: 2.0f, TurretTurn: 0.8f, Fire: 3.0f),
        ViewRange = 260,
        Camo = new Concealment(Moving: 0.25f, Still: 0.45f),
    };

    /// <summary>Light to heavy, then the tank destroyer; F1–F4 in the sandbox pick them in this order.</summary>
    public static IReadOnlyList<TankSpec> All { get; } = new[] { Light, Medium, Heavy, Td };

    /// <summary>The spec named <paramref name="id"/>.</summary>
    /// <exception cref="KeyNotFoundException">No such tank; the message lists the known names.</exception>
    public static TankSpec ById(string id)
    {
        foreach (TankSpec spec in All)
            if (spec.Id == id) return spec;
        throw new KeyNotFoundException($"No tank '{id}'. Known: {string.Join(", ", All.Select(s => s.Id))}.");
    }
}

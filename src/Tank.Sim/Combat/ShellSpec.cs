namespace Tank.Sim.Combat;

/// <summary>
/// One kind of round for a gun: penetration (mm), damage (hit points), muzzle speed (m/s), splash radius (m, 0 = none)
/// and how many the tank carries.
/// </summary>
public readonly record struct ShellSpec(float Penetration, int Damage, float Speed, float SplashRadius, int Capacity);

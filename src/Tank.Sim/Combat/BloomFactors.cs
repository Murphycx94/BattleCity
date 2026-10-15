namespace Tank.Sim.Combat;

/// <summary>
/// How much each action widens the aim circle. The circle's radius is the base dispersion × (1 + Bloom); driving, hull
/// turning and turret turning add their factor at full rate (proportionally below it), and each shot adds <see cref="Fire"/>.
/// </summary>
public readonly record struct BloomFactors(float Moving, float HullTurn, float TurretTurn, float Fire);

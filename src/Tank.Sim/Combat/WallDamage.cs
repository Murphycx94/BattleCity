namespace Tank.Sim.Combat;

/// <summary>
/// What shells do to walls (design §5.5). Walls have no armour: a shell that strikes one always does damage, AP half its
/// rolled damage (the shell stops), HE all of it (then bursts). An HE burst splashes every wall in its radius with half
/// its damage, fading to nothing at the edge, unrolled. The damage roll is the same ±25% as against tanks.
/// </summary>
public static class WallDamage
{
    public const float ApShare = 0.5f;
    public const float SplashShare = 0.5f;

    public static int Direct(ShellSpec round, AmmoType ammo, uint seed) =>
        Penetration.RoundHalfUp(round.Damage * Penetration.DamageRoll(seed) * (ammo == AmmoType.AP ? ApShare : 1f));

    /// <summary>Splash on a wall <paramref name="distance"/> m from the burst (to its nearest point); 0 at or past the radius.</summary>
    public static int Splash(ShellSpec round, float distance)
    {
        if (round.SplashRadius <= 0 || distance >= round.SplashRadius) return 0;
        return Penetration.RoundHalfUp(round.Damage * SplashShare * (1f - distance / round.SplashRadius));
    }
}

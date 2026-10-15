using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Combat;

/// <summary>What a shell did to the plate it struck, and the damage it dealt.</summary>
public readonly record struct Impact(HitResult Result, int Damage);

/// <summary>
/// Armour rules (design §3.6–3.7). Impact angles are radians between the reversed flight direction and the struck
/// face's outward normal: 0 is head-on. Rolls are ±25%, drawn from the shot's seed.
/// </summary>
public static class Penetration
{
    public static readonly float RicochetAngle = Angles.Deg(70);
    public const float OvermatchRatio = 3f;
    public const float MinRoll = 0.75f;
    public const float MaxRoll = 1.25f;
    private const float HeShare = 0.5f;          // an HE round that doesn't pierce, or a near miss, does up to half its damage
    private const float HeArmorFactor = 1.1f;     // … less this much per mm of armour
    private const float MinCos = 1e-3f;           // keeps effective armour finite at a grazing 90°

    public static float ImpactAngle(Vector3 velocity, Vector3 normal) =>
        MathF.Acos(System.Math.Clamp(Vector3.Dot(-Vector3.Normalize(velocity), normal), -1f, 1f));

    /// <summary>Steeper than 70° bounces off, unless the calibre is more than three times the plate (overmatch).</summary>
    public static bool Ricochets(float angle, float caliber, float armor) => angle > RicochetAngle && caliber <= OvermatchRatio * armor;

    public static float EffectiveArmor(float armor, float angle) => armor / MathF.Max(MathF.Cos(angle), MinCos);

    public static Impact Ap(ShellSpec round, float caliber, float armor, float angle, uint seed)
    {
        if (Ricochets(angle, caliber, armor)) return new Impact(HitResult.Ricochet, 0);
        return Pierces(round, armor, angle, seed)
            ? new Impact(HitResult.Penetrated, RolledDamage(round, seed))
            : new Impact(HitResult.NotPenetrated, 0);
    }

    /// <summary>HE never ricochets. If it doesn't pierce it still hurts: half damage less 1.1 × the effective armour.</summary>
    public static Impact HeDirect(ShellSpec round, float armor, float angle, uint seed)
    {
        if (Pierces(round, armor, angle, seed)) return new Impact(HitResult.Penetrated, RolledDamage(round, seed));
        float damage = round.Damage * HeShare - EffectiveArmor(armor, angle) * HeArmorFactor;
        return new Impact(HitResult.NotPenetrated, RoundHalfUp(MathF.Max(0f, damage)));
    }

    /// <summary>
    /// HE splash on a tank <paramref name="distance"/> m from the burst (to its nearest point): half damage fading to
    /// nothing at the splash radius, less 1.1 × its thinnest armour; never negative.
    /// </summary>
    public static int Splash(ShellSpec round, float distance, float thinnestArmor)
    {
        if (round.SplashRadius <= 0 || distance >= round.SplashRadius) return 0;
        float damage = round.Damage * HeShare * (1f - distance / round.SplashRadius) - thinnestArmor * HeArmorFactor;
        return RoundHalfUp(MathF.Max(0f, damage));
    }

    private static bool Pierces(ShellSpec round, float armor, float angle, uint seed) =>
        round.Penetration * ShotRandom.Range(seed, ShotRandom.PenetrationRoll, MinRoll, MaxRoll) >= EffectiveArmor(armor, angle);

    private static int RolledDamage(ShellSpec round, uint seed) =>
        RoundHalfUp(round.Damage * ShotRandom.Range(seed, ShotRandom.DamageRoll, MinRoll, MaxRoll));

    private static int RoundHalfUp(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);
}

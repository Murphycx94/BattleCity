using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class PenetrationTests
{
    private static readonly ShellSpec Ap = TankCatalog.Medium.Ap;   // 175 mm, 220 damage
    private static readonly ShellSpec He = TankCatalog.Medium.He;   // 45 mm, 280 damage, 3 m
    private const float Caliber = 85f;

    private static IEnumerable<uint> Seeds(int count) => Enumerable.Range(0, count).Select(i => ShotRandom.Seed(i, 1, i));

    [Fact]
    public void ImpactAngle_IsZeroHeadOn_AndGrowsWithObliquity()
    {
        Vector3 normal = Vector3.UnitZ;   // plate facing +Z, shell flying −Z into it
        Assert.Equal(0f, Penetration.ImpactAngle(new Vector3(0, 0, -780), normal), 1e-5f);
        Vector3 oblique = new(MathF.Sin(Angles.Deg(60)), 0, -MathF.Cos(Angles.Deg(60)));
        Assert.Equal(Angles.Deg(60), Penetration.ImpactAngle(oblique * 500, normal), 1e-4f);
    }

    [Theory]
    [InlineData(69f, false)]
    [InlineData(71f, true)]
    public void Ricochet_StartsPast70Degrees(float degrees, bool bounces) =>
        Assert.Equal(bounces, Penetration.Ricochets(Angles.Deg(degrees), Caliber, armor: 90));

    [Fact]
    public void Ricochet_NeverHappensWhenTheCalibreOvermatchesThePlate()
    {
        Assert.False(Penetration.Ricochets(Angles.Deg(85), Caliber, armor: 25));   // 85 > 3 × 25
        Assert.True(Penetration.Ricochets(Angles.Deg(85), Caliber, armor: 30));    // 85 ≤ 3 × 30
    }

    [Fact]
    public void EffectiveArmor_IsThicknessOverCosine()
    {
        Assert.Equal(90f, Penetration.EffectiveArmor(90, 0), 1e-4f);
        Assert.Equal(180f, Penetration.EffectiveArmor(90, Angles.Deg(60)), 1e-3f);
        Assert.True(float.IsFinite(Penetration.EffectiveArmor(90, Angles.Deg(90))));
    }

    [Fact]
    public void Ap_RolledDamage_StaysWithin25Percent_AndRepeatsForTheSameSeed()
    {
        foreach (uint seed in Seeds(500))
        {
            Impact hit = Penetration.Ap(Ap, Caliber, armor: 40, angle: 0, seed);
            Assert.Equal(HitResult.Penetrated, hit.Result);   // 175 × 0.75 > 40
            Assert.InRange(hit.Damage, 165, 275);             // 220 × [0.75, 1.25]
            Assert.Equal(hit, Penetration.Ap(Ap, Caliber, 40, 0, seed));
        }
    }

    [Fact]
    public void Ap_PenetrationRoll_DecidesBorderlineShots()
    {
        // 175 × [0.75, 1.25] = 131…219 against 175 mm: about half go through.
        var results = Seeds(400).Select(s => Penetration.Ap(Ap, Caliber, armor: 175, angle: 0, s)).ToList();
        int pierced = results.Count(r => r.Result == HitResult.Penetrated);
        Assert.InRange(pierced, 120, 280);
        Assert.All(results.Where(r => r.Result == HitResult.NotPenetrated), r => Assert.Equal(0, r.Damage));
    }

    [Fact]
    public void Ap_NeverPiercesBeyondItsBestRoll()
    {
        // 120 mm at 60° is 240 mm effective, more than 175 × 1.25.
        Assert.All(Seeds(200), s => Assert.Equal(HitResult.NotPenetrated, Penetration.Ap(Ap, Caliber, 120, Angles.Deg(60), s).Result));
    }

    [Fact]
    public void Ap_Ricochet_DealsNoDamage()
    {
        Impact hit = Penetration.Ap(Ap, Caliber, armor: 60, angle: Angles.Deg(80), seed: 1);
        Assert.Equal(new Impact(HitResult.Ricochet, 0), hit);
    }

    [Fact]
    public void HeDirect_Pierced_DealsRolledFullDamage()
    {
        Impact hit = Penetration.HeDirect(He, armor: 20, angle: 0, seed: 7);   // 45 × 0.75 > 20
        Assert.Equal(HitResult.Penetrated, hit.Result);
        Assert.InRange(hit.Damage, 210, 350);
    }

    [Fact]
    public void HeDirect_NotPierced_DealsHalfDamageLessArmour()
    {
        // 90 mm head-on: 280 × 0.5 − 90 × 1.1 = 41.
        Assert.Equal(new Impact(HitResult.NotPenetrated, 41), Penetration.HeDirect(He, armor: 90, angle: 0, seed: 3));
        // 120 mm at 30°: 138.6 effective → 140 − 152.4 < 0 → 0. HE never ricochets, even at 80°.
        Assert.Equal(new Impact(HitResult.NotPenetrated, 0), Penetration.HeDirect(He, 120, Angles.Deg(30), 3));
        Assert.Equal(HitResult.NotPenetrated, Penetration.HeDirect(He, 90, Angles.Deg(80), 3).Result);
    }

    [Fact]
    public void Splash_FadesWithDistance_LessTheThinnestArmour()
    {
        Assert.Equal(118, Penetration.Splash(He, distance: 0, thinnestArmor: 20));     // 140 − 22
        Assert.Equal(48, Penetration.Splash(He, distance: 1.5f, thinnestArmor: 20));   // 70 − 22
        Assert.Equal(1, Penetration.Splash(He, distance: 2.5f, thinnestArmor: 20));    // 23.3 − 22
        Assert.Equal(0, Penetration.Splash(He, distance: 2.8f, thinnestArmor: 20));    // armour soaks it all
        Assert.Equal(0, Penetration.Splash(He, distance: 3f, thinnestArmor: 0));       // edge of the radius
        Assert.Equal(0, Penetration.Splash(Ap, distance: 0, thinnestArmor: 0));        // AP has no splash
    }
}

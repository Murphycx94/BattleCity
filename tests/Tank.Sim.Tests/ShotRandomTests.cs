using Tank.Sim.Combat;

namespace Tank.Sim.Tests;

public sealed class ShotRandomTests
{
    [Fact]
    public void SameShot_SameNumbers()
    {
        uint a = ShotRandom.Seed(120, 3, 7), b = ShotRandom.Seed(120, 3, 7);
        Assert.Equal(a, b);
        Assert.Equal(ShotRandom.Unit(a, ShotRandom.DamageRoll), ShotRandom.Unit(b, ShotRandom.DamageRoll));
    }

    [Fact]
    public void Tick_Tank_AndShotIndex_AllChangeTheSeed()
    {
        uint seed = ShotRandom.Seed(120, 3, 7);
        Assert.NotEqual(seed, ShotRandom.Seed(121, 3, 7));
        Assert.NotEqual(seed, ShotRandom.Seed(120, 4, 7));
        Assert.NotEqual(seed, ShotRandom.Seed(120, 3, 8));
    }

    [Fact]
    public void Range_StaysInBounds_AndCoversThem()
    {
        float low = 1, high = 0;
        for (int shot = 0; shot < 2000; shot++)
        {
            float x = ShotRandom.Range(ShotRandom.Seed(1, 1, shot), ShotRandom.PenetrationRoll, 0.75f, 1.25f);
            Assert.InRange(x, 0.75f, 1.25f);
            low = MathF.Min(low, x);
            high = MathF.Max(high, x);
        }
        Assert.True(low < 0.76f && high > 1.24f, $"rolls only spanned {low}..{high}");
    }

    [Fact]
    public void Salts_AreIndependent()
    {
        uint seed = ShotRandom.Seed(5, 1, 0);
        Assert.NotEqual(ShotRandom.Unit(seed, ShotRandom.PenetrationRoll), ShotRandom.Unit(seed, ShotRandom.DamageRoll));
    }
}

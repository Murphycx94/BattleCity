using Tank.Sim.Geometry;

namespace Tank.Sim.Tests;

public sealed class XorShiftTests
{
    [Fact]
    public void SameSeed_SameSequence_InUnitRange()
    {
        var a = new XorShift(42);
        var b = new XorShift(42);
        for (int k = 0; k < 100; k++)
        {
            float x = a.NextFloat();
            Assert.Equal(x, b.NextFloat());
            Assert.InRange(x, 0f, 0.99999994f);
        }
    }

    [Fact]
    public void Seed42_MatchesGoldenValues()
    {
        var rng = new XorShift(42);
        Assert.Equal(11355432u, rng.NextUInt());
        Assert.Equal(2836018348u, rng.NextUInt());
        Assert.Equal(476557059u, rng.NextUInt());
    }
}

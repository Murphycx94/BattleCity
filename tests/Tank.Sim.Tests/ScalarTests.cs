using Tank.Sim.Geometry;

namespace Tank.Sim.Tests;

public sealed class ScalarTests
{
    [Theory]
    [InlineData(0f, 10f, 3f, 3f)]
    [InlineData(10f, 0f, 3f, 7f)]
    [InlineData(0f, 2f, 3f, 2f)]
    [InlineData(5f, 4f, 3f, 4f)]
    public void MoveTowards_StepsOrSnaps(float current, float target, float step, float expected) =>
        Assert.Equal(expected, Scalar.MoveTowards(current, target, step), 1e-6f);
}

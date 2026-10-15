using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Tests;

public sealed class AnglesTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(2.5f * MathF.PI, 0.5f * MathF.PI)]
    [InlineData(-MathF.PI, MathF.PI)]
    [InlineData(-3.5f * MathF.PI, 0.5f * MathF.PI)]
    public void Wrap_MapsIntoMinusPiToPi(float input, float expected) =>
        Assert.Equal(expected, Angles.Wrap(input), 1e-4f);

    [Fact]
    public void MoveTowards_TakesTheShortArcAcrossPi()
    {
        float result = Angles.MoveTowards(Angles.Deg(170), Angles.Deg(-170), Angles.Deg(5));
        Assert.Equal(Angles.Deg(175), result, 1e-4f);
    }

    [Fact]
    public void MoveTowards_SnapsWhenWithinStep() =>
        Assert.Equal(0.3f, Angles.MoveTowards(0.25f, 0.3f, 0.1f), 1e-6f);

    [Fact]
    public void Lerp_InterpolatesAcrossPi() =>
        Assert.Equal(MathF.PI, MathF.Abs(Angles.Lerp(Angles.Deg(170), Angles.Deg(-170), 0.5f)), 1e-4f);

    [Fact]
    public void Forward_AtZeroYaw_IsMinusZ_AndPositiveYawTurnsLeft()
    {
        Assert.Equal(new Vector2(0, -1), Angles.Forward(0));
        Vector2 left = Angles.Forward(MathF.PI / 2);
        Assert.Equal(-1f, left.X, 1e-6f);
        Assert.Equal(0f, left.Y, 1e-6f);
    }

    [Fact]
    public void Right_IsPerpendicularToForward_OnTheRightHandSide()
    {
        Assert.Equal(new Vector2(1, 0), Angles.Right(0));
        Assert.Equal(0f, Vector2.Dot(Angles.Forward(0.7f), Angles.Right(0.7f)), 1e-6f);
    }

    [Fact]
    public void AimAngles_InvertsDirection()
    {
        Vector3 dir = Angles.Direction(1.1f, -0.2f);
        var (yaw, pitch) = Angles.AimAngles(dir * 37f);
        Assert.Equal(1.1f, yaw, 1e-5f);
        Assert.Equal(-0.2f, pitch, 1e-5f);
    }

    [Fact]
    public void AimAngles_ZeroVector_ReturnsFallbackYaw()
    {
        var (yaw, pitch) = Angles.AimAngles(Vector3.Zero, 0.7f);
        Assert.Equal(0.7f, yaw);
        Assert.Equal(0f, pitch);
    }

    [Fact]
    public void AimAngles_StraightUp_UsesFallbackYaw()
    {
        var (yaw, pitch) = Angles.AimAngles(new Vector3(0, 1, 0), 0.4f);
        Assert.Equal(0.4f, yaw);
        Assert.Equal(MathF.PI / 2, pitch, 1e-5f);
    }

    [Fact]
    public void AimAngles_StraightBack_YawIsPlusPi()
    {
        var (yaw, _) = Angles.AimAngles(new Vector3(0, 0, 1));
        Assert.True(yaw > 0);
        Assert.Equal(MathF.PI, yaw, 1e-5f);
    }

    [Fact]
    public void MoveTowards_CrossingMinusPi_WrapsToTheOtherSide() =>
        Assert.Equal(Angles.Deg(-175), Angles.MoveTowards(Angles.Deg(175), Angles.Deg(-170), Angles.Deg(10)), 1e-4f);

    [Theory]
    [InlineData(0f)]
    [InlineData(0.7f)]
    [InlineData(-2.0f)]
    [InlineData(3.0f)]
    [InlineData(-MathF.PI / 2)]
    public void YawOf_InvertsForward(float yaw)
    {
        Assert.Equal(yaw, Angles.YawOf(Angles.Forward(yaw)), 1e-5f);
        Assert.Equal(yaw, Angles.YawOf(Angles.Forward(yaw) * 37f), 1e-5f);   // any length
    }

    [Fact]
    public void YawOf_StraightBack_IsPlusPi()
    {
        float yaw = Angles.YawOf(new Vector2(0, 1));
        Assert.True(yaw > 0);
        Assert.Equal(MathF.PI, yaw, 1e-5f);
    }

    [Fact]
    public void Wrap_Pi_StaysPi() => Assert.Equal(MathF.PI, Angles.Wrap(MathF.PI));

    [Fact]
    public void Right_HasTheSameHandednessAtAnyYaw()
    {
        static float Cross(float yaw)
        {
            Vector2 f = Angles.Forward(yaw), r = Angles.Right(yaw);
            return f.X * r.Y - f.Y * r.X;
        }
        Assert.Equal(MathF.Sign(Cross(0)), MathF.Sign(Cross(0.7f)));
    }
}

using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

public sealed class BallisticsTests
{
    private static readonly Vector3 Origin = new(20, 5, 100);
    private static readonly Vector3 Velocity = new(100, 0, 0);

    private static RayHit Fly(Battlefield field)
    {
        var shell = new Shell { Position = Origin, Velocity = Velocity };
        while (shell.Age < Ballistics.MaxLifetime)
            if (Ballistics.Step(ref shell, field, out var hit) == ShellStep.Hit) return hit;
        throw new Xunit.Sdk.XunitException("Shell never landed.");
    }

    [Fact]
    public void HorizontalShot_LandsWhereKinematicsPredict()
    {
        float expectedX = Origin.X + Velocity.X * MathF.Sqrt(2 * Origin.Y / SimConstants.Gravity);
        RayHit hit = Fly(TestWorld.FlatField());
        Assert.Equal(expectedX, hit.Point.X, 0.3f);
        Assert.Equal(0f, hit.Point.Y, 0.01f);
    }

    [Fact]
    public void PredictImpact_MatchesSteppedFlightExactly()
    {
        var field = TestWorld.FlatField();
        Assert.True(Ballistics.PredictImpact(Origin, Velocity, field, out Vector3 predicted));
        Assert.Equal(Fly(field).Point, predicted);
    }

    [Fact]
    public void Shell_HitsRockBeforeGround()
    {
        var rock = new Prop(7, PropKind.Rock, new Obb2(new Vector2(60, 100), new Vector2(1, 5), 0), -1, 10);
        RayHit hit = Fly(TestWorld.FlatField(rock));
        Assert.Equal(7, hit.PropId);
        Assert.Equal(59f, hit.Point.X, 0.05f);
    }

    [Fact]
    public void PredictImpact_ShotStraightUp_ExpiresWithoutHit() =>
        Assert.False(Ballistics.PredictImpact(new Vector3(100, 1, 100), new Vector3(0, 30, 0), TestWorld.FlatField(), out _));

    [Fact]
    public void LevelShotTowardMapEdge_IsGoneWithinAFewTicks_WithoutHit()
    {
        var field = TestWorld.FlatField();
        var shell = new Shell { Position = new Vector3(100, 5, 190), Velocity = new Vector3(0, 0, 800) };
        ShellStep result = ShellStep.Flying;
        int ticks = 0;
        while (result == ShellStep.Flying && ticks < 10) { result = Ballistics.Step(ref shell, field, out _); ticks++; }
        Assert.Equal(ShellStep.Gone, result);
        Assert.True(ticks <= 3, $"took {ticks} ticks");
    }

    [Fact]
    public void PredictImpact_ShotOffTheMap_ReturnsFalse() =>
        Assert.False(Ballistics.PredictImpact(new Vector3(100, 5, 190), new Vector3(0, 0, 800), TestWorld.FlatField(), out _));
}

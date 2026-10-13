using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class TankMotionTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;

    [Fact]
    public void Throttle_OnFlatGround_AcceleratesAlongMinusZ()
    {
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, TestWorld.FlatField(), TestWorld.Cmd(throttle: 1), SimConstants.TickRate);
        Assert.Equal(Spec.Acceleration, t.Speed, 0.01f);
        Assert.True(t.Position.Y < 100);
        Assert.Equal(100f, t.Position.X, 1e-3f);
    }

    [Fact]
    public void Throttle_HeldLong_CapsAtMaxForwardSpeed()
    {
        var t = TestWorld.Tank(100, 180);
        TestWorld.Drive(ref t, TestWorld.FlatField(), TestWorld.Cmd(throttle: 1), 300);
        Assert.Equal(Spec.MaxForwardSpeed, t.Speed, 1e-3f);
    }

    [Fact]
    public void Reverse_CapsAtMaxReverseSpeed_AndMovesBackward()
    {
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, TestWorld.FlatField(), TestWorld.Cmd(throttle: -1), 300);
        Assert.Equal(-Spec.MaxReverseSpeed, t.Speed, 1e-3f);
        Assert.True(t.Position.Y > 100);
    }

    [Fact]
    public void ReleasingThrottle_BrakesToAStop()
    {
        var field = TestWorld.FlatField();
        var t = TestWorld.Tank(100, 180);
        TestWorld.Drive(ref t, field, TestWorld.Cmd(throttle: 1), 300);
        TestWorld.Drive(ref t, field, TestWorld.Cmd(), 60);
        Assert.Equal(0f, t.Speed);
    }

    [Fact]
    public void SteerRight_AtRest_TurnsClockwiseAtFullTraverse()
    {
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, TestWorld.FlatField(), TestWorld.Cmd(steer: 1), SimConstants.TickRate);
        Assert.Equal(-Spec.HullTraverse, t.Yaw, 1e-3f);
    }

    [Fact]
    public void SteerWhileMoving_TurnsSlower()
    {
        var field = TestWorld.FlatField();
        var t = TestWorld.Tank(100, 180);
        TestWorld.Drive(ref t, field, TestWorld.Cmd(throttle: 1), 90);
        float before = t.Yaw;
        TestWorld.Drive(ref t, field, TestWorld.Cmd(throttle: 1, steer: 1), SimConstants.TickRate);
        Assert.Equal(-Spec.HullTraverse * 0.75f, t.Yaw - before, 1e-3f);
    }

    [Fact]
    public void GentleSlope_ReducesTopSpeed()
    {
        var t = TestWorld.Tank(100, 180);
        TestWorld.Drive(ref t, TestWorld.Field(TestWorld.RampNorth(0.2f)), TestWorld.Cmd(throttle: 1), 300);
        float c = MathF.Cos(MathF.Atan(0.2f));
        Assert.Equal(Spec.MaxForwardSpeed * c * c, t.Speed, 0.01f);
    }

    [Fact]
    public void SlopeSteeperThanMaxClimb_CannotBeClimbed()
    {
        var t = TestWorld.Tank(100, 150);
        TestWorld.Drive(ref t, TestWorld.Field(TestWorld.RampNorth(0.84f)), TestWorld.Cmd(throttle: 1), 120); // ~40°
        Assert.Equal(150f, t.Position.Y);
        Assert.Equal(0f, t.Speed);
    }

    [Fact]
    public void SteepSlope_CanBeDescended()
    {
        var t = TestWorld.Tank(100, 100, yaw: MathF.PI); // facing +Z, downhill
        TestWorld.Drive(ref t, TestWorld.Field(TestWorld.RampNorth(0.84f)), TestWorld.Cmd(throttle: 1), 60);
        Assert.True(t.Position.Y > 105, $"z was {t.Position.Y}");
    }

    [Fact]
    public void Rock_BlocksTheHull_AndStallsSpeed()
    {
        // 10m-wide, 2m-thick wall whose near face is at z = 81.
        var wall = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 80), new Vector2(5, 1), 0), -1, 4);
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, TestWorld.FlatField(wall), TestWorld.Cmd(throttle: 1), 300);
        float stopZ = 81 + Spec.HullLength / 2;
        Assert.InRange(t.Position.Y, stopZ - 0.01f, stopZ + 0.5f);
        Assert.True(t.Speed < 0.5f, $"speed was {t.Speed}");
    }

    [Fact]
    public void Rock_HitAtAnAngle_LetsTheHullSlideAlong()
    {
        // 160m-long wall so the tank is still alongside it after 10 s of sliding west.
        var wall = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 80), new Vector2(80, 1), 0), -1, 4);
        var t = TestWorld.Tank(100, 100, yaw: Angles.Deg(30)); // heading north-west into the wall
        TestWorld.Drive(ref t, TestWorld.FlatField(wall), TestWorld.Cmd(throttle: 1), 300);
        Assert.True(t.Position.X < 90, $"x was {t.Position.X}");
        Assert.True(t.Position.Y > 81, $"z was {t.Position.Y}");
    }

    [Fact]
    public void OtherTank_BlocksLikeAnObstacle()
    {
        Obb2 parked = TankMotion.Footprint(Spec, new Vector2(100, 90), 0);
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, TestWorld.FlatField(), TestWorld.Cmd(throttle: 1), 120, parked);
        Assert.True(t.Position.Y >= 90 + Spec.HullLength - 0.01f, $"z was {t.Position.Y}");
    }

    [Fact]
    public void Tank_CannotLeaveThePlayableArea()
    {
        var t = TestWorld.Tank(100, 10);
        TestWorld.Drive(ref t, TestWorld.FlatField(), TestWorld.Cmd(throttle: 1), 300);
        Assert.Equal(TestWorld.Border, t.Position.Y, 1e-3f);
    }

    [Fact]
    public void CliffApproachedAtFullSpeed_IsNotClimbed()
    {
        HeightField terrain = TestWorld.CliffNorth(100, 1f);
        Battlefield field = TestWorld.Field(terrain);
        var t = TestWorld.Tank(100, 190);
        InputCmd cmd = TestWorld.Cmd(throttle: 1);
        for (int i = 0; i < 300; i++)
        {
            TankMotion.Drive(ref t, cmd, field, ReadOnlySpan<Obb2>.Empty);
            Assert.True(terrain.HeightAt(t.Position.X, t.Position.Y) <= 1.0f, $"tick {i}: height {terrain.HeightAt(t.Position.X, t.Position.Y)}");
        }
    }

    [Fact]
    public void SteepSlope_CannotBeClimbedDiagonally()
    {
        HeightField terrain = TestWorld.RampNorth(0.84f);
        var t = TestWorld.Tank(150, 150, yaw: Angles.Deg(50));
        float h0 = terrain.HeightAt(t.Position.X, t.Position.Y);
        TestWorld.Drive(ref t, TestWorld.Field(terrain), TestWorld.Cmd(throttle: 1), 300);
        Assert.True(terrain.HeightAt(t.Position.X, t.Position.Y) - h0 < 1f);
    }

    [Fact]
    public void SteepSlope_CannotBeTraversedSideways()
    {
        var t = TestWorld.Tank(60, 100, yaw: -MathF.PI / 2);
        TestWorld.Drive(ref t, TestWorld.Field(TestWorld.RampNorth(0.84f)), TestWorld.Cmd(throttle: 1), 120);
        Assert.True(MathF.Abs(t.Position.X - 60) < 1f, $"x was {t.Position.X}");
    }

    [Fact]
    public void SteepSlope_CanBeDescendedDiagonally()
    {
        var t = TestWorld.Tank(100, 100, yaw: MathF.PI - Angles.Deg(40));
        TestWorld.Drive(ref t, TestWorld.Field(TestWorld.RampNorth(0.84f)), TestWorld.Cmd(throttle: 1), 60);
        Assert.True(t.Position.Y > 103, $"z was {t.Position.Y}");
    }

    [Fact]
    public void ReversingUpATooSteepSlope_IsBlocked()
    {
        var t = TestWorld.Tank(100, 150, yaw: MathF.PI);
        TestWorld.Drive(ref t, TestWorld.Field(TestWorld.RampNorth(0.84f)), TestWorld.Cmd(throttle: -1), 120);
        Assert.True(MathF.Abs(t.Position.Y - 150) < 1f, $"z was {t.Position.Y}");
    }

    [Fact]
    public void Pivot_BetweenCloseRocks_NeverOverlaps()
    {
        var left = new Prop(1, PropKind.Rock, new Obb2(new Vector2(97.1f, 100), new Vector2(1, 4), 0), -1, 4);
        var right = new Prop(2, PropKind.Rock, new Obb2(new Vector2(102.9f, 100), new Vector2(1, 4), 0), -1, 4);
        Battlefield field = TestWorld.FlatField(left, right);
        var t = TestWorld.Tank(100, 100);
        InputCmd cmd = TestWorld.Cmd(steer: 1);
        for (int i = 0; i < 90; i++)
        {
            TankMotion.Drive(ref t, cmd, field, ReadOnlySpan<Obb2>.Empty);
            Obb2 box = TankMotion.Footprint(Spec, t.Position, t.Yaw);
            Assert.False(Obb2.TryGetPenetration(box, left.Footprint, out _), $"tick {i} overlaps left rock");
            Assert.False(Obb2.TryGetPenetration(box, right.Footprint, out _), $"tick {i} overlaps right rock");
        }
    }

    [Theory]
    [InlineData(50f)]
    [InlineData(65f)]
    public void GlancingIntoARotatedWall_KeepsSliding(float headingDegrees)
    {
        var wall = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 80), new Vector2(60, 1), Angles.Deg(17)), -1, 4);
        Battlefield field = TestWorld.FlatField(wall);
        var t = TestWorld.Tank(100, 100, yaw: Angles.Deg(17) + Angles.Deg(headingDegrees));
        InputCmd cmd = TestWorld.Cmd(throttle: 1);
        float xAt150 = 0;
        for (int i = 1; i <= 300; i++)
        {
            TankMotion.Drive(ref t, cmd, field, ReadOnlySpan<Obb2>.Empty);
            if (i == 150) xAt150 = t.Position.X;
            if (Obb2.TryGetPenetration(TankMotion.Footprint(Spec, t.Position, t.Yaw), wall.Footprint, out Vector2 push))
                Assert.True(push.Length() <= 0.01f, $"tick {i}: penetration {push.Length()}");
        }
        Assert.True(xAt150 - t.Position.X > 10f, $"x went from {xAt150} to {t.Position.X}");
    }

    [Fact]
    public void WedgedHull_CanDriveOut()
    {
        var rock = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100 - 1.6f - 1f, 100), new Vector2(1, 5), 0), -1, 4);
        Obb2 parked = TankMotion.Footprint(Spec, new Vector2(100 + 3.2f - 0.6f, 100), 0);
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, TestWorld.FlatField(rock), TestWorld.Cmd(throttle: 1), 60, parked);
        Assert.True(t.Position.Y < 97, $"z was {t.Position.Y}");
    }

    [Fact]
    public void SteepSlope_CannotBeSkimmedJustBelowTheContour()
    {
        var t = TestWorld.Tank(60, 100, yaw: -MathF.PI / 2 - Angles.Deg(3));
        Assert.True(Angles.Forward(t.Yaw).Y > 0, "heading must point slightly downhill");
        TestWorld.Drive(ref t, TestWorld.Field(TestWorld.RampNorth(2.75f)), TestWorld.Cmd(throttle: 1), 120);
        Assert.True(MathF.Abs(t.Position.X - 60) < 2f, $"x was {t.Position.X}");
    }
}

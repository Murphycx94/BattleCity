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
}

using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>A casemate gun (the tank destroyer's) turns only within its arc; outside it the hull turns for it.</summary>
public sealed class CasemateTests
{
    /// <summary>The medium tank's gun in a casemate, 12° each way like the tank destroyer's.</summary>
    private static readonly TankSpec Casemate = TankCatalog.Medium with { Id = "casemate", GunArc = Angles.Deg(12) };

    private static readonly float TurnPerTick = Casemate.HullTraverse * SimConstants.Dt;

    private static Simulation NewSim(TankSpec spec, out int id)
    {
        var sim = new Simulation(TestWorld.FlatField());
        id = sim.AddTank(spec, new Vector2(100, 100), 0f);
        return sim;
    }

    private static void Run(Simulation sim, InputCmd cmd, int ticks)
    {
        var inputs = new[] { cmd };
        for (int i = 0; i < ticks; i++) sim.Step(inputs);
    }

    [Theory]
    [InlineData(1.0f, -1)]   // aim to the left (+yaw): steer left
    [InlineData(-1.0f, 1)]
    [InlineData(3.0f, -1)]
    public void TraverseHull_SteersTowardsAnAimOutsideTheArc(float aimYaw, int steer)
    {
        TankState t = TestWorld.Tank(100, 100) with { Spec = Casemate };
        InputCmd cmd = TankMotion.TraverseHull(t, TestWorld.Cmd(aimYaw: aimYaw), TestWorld.Flat());
        Assert.Equal(steer, cmd.Steer);
    }

    [Theory]
    [InlineData(3.0f, -3.0f, -1)]   // aim is 0.28 rad to the left across the ±π seam: steer left
    [InlineData(-3.0f, 3.0f, 1)]    // and the mirror image: 0.28 rad to the right
    public void TraverseHull_TakesTheShortWayAcrossTheYawSeam(float hullYaw, float aimYaw, int steer)
    {
        TankState t = TestWorld.Tank(100, 100, hullYaw) with { Spec = Casemate };
        InputCmd cmd = TankMotion.TraverseHull(t, TestWorld.Cmd(aimYaw: aimYaw), TestWorld.Flat());
        Assert.Equal(steer, cmd.Steer);
    }

    [Fact]
    public void TraverseHull_AimExactlyBehind_StillSteers_AndAlwaysTheSameWay()
    {
        // Which way is down to float rounding at exactly π, so only that it turns, and consistently, is promised.
        TankState t = TestWorld.Tank(100, 100) with { Spec = Casemate };
        InputCmd aim = TestWorld.Cmd(aimYaw: MathF.PI);
        InputCmd first = TankMotion.TraverseHull(t, aim, TestWorld.Flat());
        Assert.NotEqual(0, first.Steer);
        Assert.Equal(first, TankMotion.TraverseHull(t, aim, TestWorld.Flat()));
    }

    [Fact]
    public void TraverseHull_LeavesAnAimInsideTheArcAlone()
    {
        TankState t = TestWorld.Tank(100, 100) with { Spec = Casemate };
        InputCmd cmd = TestWorld.Cmd(aimYaw: 0.15f);
        Assert.Equal(cmd, TankMotion.TraverseHull(t, cmd, TestWorld.Flat()));
    }

    [Fact]
    public void Hull_TurnsTowardsTheAim_AtItsTraverseRate()
    {
        var sim = NewSim(Casemate, out _);
        Run(sim, TestWorld.Cmd(aimYaw: 1.0f), 1);
        Assert.Equal(TurnPerTick, sim.Tanks[0].Yaw, 1e-4f);
    }

    [Fact]
    public void Hull_StopsOnceTheGunCanReach_AndTheGunIsOnTarget()
    {
        var sim = NewSim(Casemate, out _);
        Run(sim, TestWorld.Cmd(aimYaw: 1.0f), 90);
        float yaw = sim.Tanks[0].Yaw;
        Assert.InRange(1.0f - yaw, Casemate.GunArc - TurnPerTick, Casemate.GunArc);   // stopped at the arc's edge
        Run(sim, TestWorld.Cmd(aimYaw: 1.0f), 30);
        Assert.Equal(yaw, sim.Tanks[0].Yaw);
        Assert.Equal(1.0f - yaw, sim.Tanks[0].TurretYaw, 1e-4f);
    }

    [Fact]
    public void HandSteering_Wins()
    {
        var sim = NewSim(Casemate, out _);
        Run(sim, TestWorld.Cmd(steer: 1, aimYaw: 1.0f), 1);   // steering right, aiming left
        Assert.Equal(-TurnPerTick, sim.Tanks[0].Yaw, 1e-4f);
    }

    [Fact]
    public void TurretTank_HullNeverTurnsByItself()
    {
        var sim = NewSim(TankCatalog.Medium, out _);
        Run(sim, TestWorld.Cmd(aimYaw: 2.0f), 30);
        Assert.Equal(0f, sim.Tanks[0].Yaw);
    }

    [Fact]
    public void Wreck_DoesNotTurn()
    {
        var sim = NewSim(Casemate, out int id);
        ref TankState tank = ref sim.TankRef(id);
        tank.Health = 0;
        tank.IsDestroyed = true;
        Run(sim, TestWorld.Cmd(aimYaw: 1.0f), 30);
        Assert.Equal(0f, sim.Tanks[0].Yaw);
    }

    [Fact]
    public void AutoTurn_OpensTheAimCircleLikeSteering()
    {
        var auto = NewSim(Casemate, out _);
        var hand = NewSim(Casemate, out _);
        Run(auto, TestWorld.Cmd(aimYaw: 1.0f), 10);
        Run(hand, TestWorld.Cmd(steer: -1, aimYaw: 1.0f), 10);
        Assert.True(auto.Tanks[0].Bloom > 0f);
        Assert.Equal(hand.Tanks[0].Bloom, auto.Tanks[0].Bloom, 1e-5f);
    }
}

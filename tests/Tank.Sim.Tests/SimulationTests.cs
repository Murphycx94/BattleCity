using System.Numerics;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class SimulationTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;

    private static Simulation NewSim(out int tankId)
    {
        var sim = new Simulation(TestWorld.FlatField());
        tankId = sim.AddTank(Spec, new Vector2(100, 100), 0f);
        return sim;
    }

    private static List<GameEvent> Run(Simulation sim, InputCmd cmd, int ticks)
    {
        var events = new List<GameEvent>();
        var inputs = Enumerable.Repeat(cmd, sim.TankCount).ToArray();
        for (int i = 0; i < ticks; i++)
        {
            sim.Step(inputs);
            events.AddRange(sim.Events);
        }
        return events;
    }

    [Fact]
    public void Step_AdvancesTick()
    {
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(), 3);
        Assert.Equal(3, sim.Tick);
    }

    [Fact]
    public void Step_RejectsWrongInputCount()
    {
        var sim = NewSim(out _);
        Assert.Throws<ArgumentException>(() => sim.Step(Array.Empty<InputCmd>()));
    }

    [Fact]
    public void AddTank_ClampsIntoPlayableArea()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(Spec, new Vector2(-50, 500), 0f);
        Assert.Equal(new Vector2(TestWorld.Border, TestWorld.Size - TestWorld.Border), sim.Tanks[0].Position);
    }

    [Fact]
    public void Fire_SpawnsShellAtMuzzle_AndEmitsEvent()
    {
        var sim = NewSim(out int tankId);
        var events = Run(sim, TestWorld.Cmd(fire: true), 1);

        Assert.Equal(1, sim.ShellCount);
        GameEvent fired = Assert.Single(events, e => e.Type == GameEventType.ShellFired);
        Assert.Equal(tankId, fired.TankId);
        var expectedMuzzle = new Vector3(100, Spec.TrunnionHeight, 100 - Spec.TurretOffset - Spec.GunLength);
        Assert.True(Vector3.Distance(expectedMuzzle, fired.Position) < 1e-3f, $"muzzle at {fired.Position}");
        Assert.Equal(Spec.ShellSpeed, sim.Shells[0].Velocity.Length(), 0.5f);
        Assert.Equal(Spec.ReloadTime, sim.Tanks[0].Reload, 1e-4f);
    }

    [Fact]
    public void Reload_GatesTheNextShot()
    {
        var sim = NewSim(out _);
        var events = Run(sim, TestWorld.Cmd(fire: true), 200);
        var fireTicks = events.Where(e => e.Type == GameEventType.ShellFired).Select(e => e.Tick).ToArray();
        Assert.Equal(new[] { 1, 1 + (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate) }, fireTicks);
    }

    [Fact]
    public void Sanitize_ClampsAxes_AndReplacesNonFiniteAim()
    {
        var tank = TestWorld.Tank(100, 100, yaw: 0.3f) with { TurretYaw = 0.2f };
        InputCmd clean = Simulation.Sanitize(new InputCmd(1, 100, -7, float.NaN, float.PositiveInfinity, true), tank);
        Assert.Equal(1, clean.Throttle);
        Assert.Equal(-1, clean.Steer);
        Assert.Equal(0.5f, clean.AimYaw, 1e-6f);
        Assert.Equal(0f, clean.AimPitch);
        Assert.True(clean.Fire);
    }

    [Fact]
    public void Fire_WithBarrelInsideARock_ExplodesAtTheRockFace()
    {
        // Rock face 3m ahead of the trunnion; the 4.2m barrel pokes into it.
        float face = 100 - Spec.TurretOffset - 3f;
        var rock = new Tank.Sim.Maps.Prop(1, Tank.Sim.Maps.PropKind.Rock,
            new Tank.Sim.Geometry.Obb2(new Vector2(100, face - 2), new Vector2(3, 2), 0), -1, 6);
        var sim = new Simulation(TestWorld.FlatField(rock));
        sim.AddTank(Spec, new Vector2(100, 100), 0f);

        var events = Run(sim, TestWorld.Cmd(fire: true), 1);

        GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(face, boom.Position.Z, 0.01f);
        Assert.Equal(0, sim.ShellCount);
    }

    [Fact]
    public void TanksDrivingInConvoy_MoveTheSameRegardlessOfSlotOrder()
    {
        // Two tanks nose-to-tail heading north; whichever slot the leader is in, the follower ends at the same place.
        float FollowerZ(bool leaderFirst)
        {
            var sim = new Simulation(TestWorld.FlatField());
            Vector2 leader = new(100, 100), follower = new(100, 100 + Spec.HullLength + 0.05f); // 5cm gap: contact within a few ticks
            sim.AddTank(Spec, leaderFirst ? leader : follower, 0f);
            sim.AddTank(Spec, leaderFirst ? follower : leader, 0f);
            Run(sim, TestWorld.Cmd(throttle: 1), 60);
            return sim.Tanks[leaderFirst ? 1 : 0].Position.Y;
        }
        Assert.Equal(FollowerZ(true), FollowerZ(false), 1e-4f);
    }

    [Fact]
    public void ShellAimedAtTheGround_ExplodesAheadOfTheTank()
    {
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(aimPitch: -0.3f), 30); // let the gun settle at full depression
        var events = Run(sim, TestWorld.Cmd(aimPitch: -0.3f, fire: true), 60);

        GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(0f, boom.Position.Y, 0.05f);
        Assert.InRange(boom.Position.Z, 60f, 95f);
        Assert.Equal(0, sim.ShellCount);
    }

    [Fact]
    public void ShellFlyingOffTheMap_IsRetiredWithoutAnExplosion()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(Spec, new Vector2(100, TestWorld.Size - TestWorld.Border), 0f);
        float south = MathF.PI;
        Run(sim, TestWorld.Cmd(aimYaw: south), 120); // swing the turret round to face the map edge
        var events = Run(sim, TestWorld.Cmd(aimYaw: south, fire: true), 1);
        events.AddRange(Run(sim, TestWorld.Cmd(aimYaw: south), 5));

        Assert.Single(events, e => e.Type == GameEventType.ShellFired);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(0, sim.ShellCount);
    }

    [Fact]
    public void Reticle_PredictsTheExactImpactOfTheNextShot()
    {
        var sim = NewSim(out _);
        var aim = TestWorld.Cmd(aimPitch: -0.05f);
        Run(sim, aim, 30);
        var launch = Tank.Sim.Combat.Ballistics.Aim(sim.Tanks[0], sim.Field);
        Assert.False(launch.Blocked);
        Assert.True(Tank.Sim.Combat.Ballistics.PredictImpact(launch.Origin, launch.Velocity, sim.Field, out Vector3 predicted));

        var events = Run(sim, TestWorld.Cmd(aimPitch: -0.05f, fire: true), 90);
        GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(predicted, boom.Position);
    }
}

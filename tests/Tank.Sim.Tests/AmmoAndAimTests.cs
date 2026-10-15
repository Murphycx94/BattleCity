using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class AmmoAndAimTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;
    private static readonly int ReloadTicks = (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate);

    private static Simulation NewSim(out int tankId)
    {
        var sim = new Simulation(TestWorld.FlatField());
        tankId = sim.AddTank(Spec, new Vector2(100, 100), 0f);
        return sim;
    }

    private static List<GameEvent> Run(Simulation sim, InputCmd cmd, int ticks)
    {
        var events = new List<GameEvent>();
        var inputs = new[] { cmd };
        for (int i = 0; i < ticks; i++)
        {
            sim.Step(inputs);
            events.AddRange(sim.Events);
        }
        return events;
    }

    [Fact]
    public void Firing_SpendsARoundOfTheLoadedType_AndTagsTheShell()
    {
        var sim = NewSim(out _);
        var events = Run(sim, TestWorld.Cmd(fire: true), 1);
        Assert.Equal(39, sim.Tanks[0].ApLeft);
        Assert.Equal(15, sim.Tanks[0].HeLeft);
        Assert.Equal(AmmoType.AP, Assert.Single(events, e => e.Type == GameEventType.ShellFired).Ammo);
        Assert.Equal(AmmoType.AP, sim.Shells[0].Ammo);
        Assert.Equal(Spec.Ap, sim.Shells[0].Round);
    }

    [Fact]
    public void SwitchingAmmo_RestartsTheReload_ThenFiresTheNewType()
    {
        var sim = NewSim(out _);
        var he = TestWorld.Cmd(fire: true, ammo: AmmoType.HE);
        var events = Run(sim, he, 1);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.ShellFired);   // swapping rounds, not firing
        Assert.Equal(AmmoType.HE, sim.Tanks[0].Ammo);
        Assert.Equal(Spec.ReloadTime, sim.Tanks[0].Reload, 1e-4f);

        events = Run(sim, he, ReloadTicks + 1);
        GameEvent fired = Assert.Single(events, e => e.Type == GameEventType.ShellFired);
        Assert.Equal(1 + ReloadTicks, fired.Tick);
        Assert.Equal(AmmoType.HE, fired.Ammo);
        Assert.Equal(Spec.He.Speed, fired.Velocity.Length(), 0.5f);
        Assert.Equal(14, sim.Tanks[0].HeLeft);
    }

    [Fact]
    public void SwitchingBackMidReload_StartsAFreshFullReload()
    {
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(fire: true), 1);            // fire AP: reload 6.5 s
        Run(sim, TestWorld.Cmd(), 60);                     // 2 s in
        Run(sim, TestWorld.Cmd(ammo: AmmoType.HE), 1);     // swap
        Assert.Equal(Spec.ReloadTime, sim.Tanks[0].Reload, 1e-4f);
    }

    [Fact]
    public void AnEmptyRack_CantFire_UntilRestocked()
    {
        var sim = NewSim(out int id);
        sim.TankRef(id).ApLeft = 0;
        Assert.DoesNotContain(Run(sim, TestWorld.Cmd(fire: true), 5), e => e.Type == GameEventType.ShellFired);

        sim.Restock(id);
        Assert.Equal(40, sim.Tanks[0].ApLeft);
        Assert.Equal(15, sim.Tanks[0].HeLeft);
        Assert.Single(Run(sim, TestWorld.Cmd(fire: true), 1), e => e.Type == GameEventType.ShellFired);
    }

    [Fact]
    public void Restock_RejectsAnUnknownTank()
    {
        var sim = NewSim(out _);
        Assert.Throws<ArgumentException>(() => sim.Restock(99));
    }

    [Fact]
    public void Sanitize_KeepsTheLoadedRound_ForAnUnknownAmmoType()
    {
        var tank = TestWorld.Tank(100, 100) with { Ammo = AmmoType.HE };
        Assert.Equal(AmmoType.HE, Simulation.Sanitize(TestWorld.Cmd(ammo: (AmmoType)7), tank).Ammo);
        Assert.Equal(AmmoType.AP, Simulation.Sanitize(TestWorld.Cmd(ammo: AmmoType.AP), tank).Ammo);
    }

    [Fact]
    public void Firing_OpensTheAimCircle_WhichThenCloses()
    {
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(fire: true), 1);
        Assert.Equal(Spec.Bloom.Fire, sim.Tanks[0].Bloom, 1e-4f);
        Run(sim, TestWorld.Cmd(), (int)MathF.Round(Spec.AimTime * SimConstants.TickRate));
        Assert.InRange(sim.Tanks[0].Bloom, 0f, Spec.Bloom.Fire * 0.06f);
    }

    [Fact]
    public void Driving_OpensTheAimCircle_StoppingClosesIt()
    {
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(throttle: 1), 150);   // 5 s: at top speed
        Assert.Equal(Spec.Bloom.Moving, sim.Tanks[0].Bloom, 0.05f);
        Run(sim, TestWorld.Cmd(), 90);               // braked to a stop well inside 3 s, then settling
        Assert.True(sim.Tanks[0].Bloom < 0.5f, $"bloom still {sim.Tanks[0].Bloom}");
    }

    [Fact]
    public void TurningOnTheSpot_OpensTheAimCircle_ForTheHullAndTheTurretHoldingTheAim()
    {
        // Steering without throttle swings the hull at its full traverse. The aim stays put, so the turret turns the other
        // way relative to the hull; it can't keep up (40°/s against 42°/s), so it turns flat out and counts in full too.
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(steer: 1), 15);

        Assert.Equal(new Vector2(100, 100), sim.Tanks[0].Position);
        Assert.Equal(0f, sim.Tanks[0].Speed);
        float turret = MathF.Min(1f, Spec.HullTraverse / Spec.TurretTraverse);
        Assert.Equal(Spec.Bloom.HullTurn + Spec.Bloom.TurretTurn * turret, sim.Tanks[0].Bloom, 1e-3f);   // 1.5 + 1.0
    }

    [Fact]
    public void SwingingTheTurret_OpensTheAimCircle_WhileItTurns()
    {
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(aimYaw: 1.5f), 15);   // half a second into a swing of about 2.1 s

        Assert.Equal(15 * Spec.TurretTraverse * SimConstants.Dt, sim.Tanks[0].TurretYaw, 1e-4f);   // still turning, flat out
        Assert.Equal(0f, sim.Tanks[0].Yaw);
        Assert.Equal(Spec.Bloom.TurretTurn, sim.Tanks[0].Bloom, 1e-3f);
    }

    [Fact]
    public void Dispersion_IsTheSameForTheSameShot()
    {
        Vector3 FirstShot()
        {
            var sim = NewSim(out _);
            Run(sim, TestWorld.Cmd(throttle: 1), 30);   // open the circle so the offset is large
            return Assert.Single(Run(sim, TestWorld.Cmd(throttle: 1, fire: true), 1), e => e.Type == GameEventType.ShellFired).Velocity;
        }
        Assert.Equal(FirstShot(), FirstShot());
    }
}

using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

public sealed class TankHitTests
{
    private static readonly Vector3 HullFront = new(0, 1.02f, -3.1f);     // hull-space centre of the front plate
    private static readonly Vector3 TurretFront = new(0, 2.24f, -0.3f - 1.45f);

    [Fact]
    public void Ap_HeadOn_Penetrates_AndTakesHealth()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        var events = duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));

        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(duel.ShooterId, hit.TankId);
        Assert.Equal(duel.TargetId, hit.TargetTankId);
        Assert.Equal(HitResult.Penetrated, hit.Result);
        Assert.Equal(AmmoType.AP, hit.Ammo);
        Assert.InRange(hit.Damage, 165, 275);
        Assert.Equal(103.1f, hit.Position.Z, 0.01f);                  // on the front plate, which faces the shooter (+Z)
        Assert.Equal(1f, hit.Velocity.Z, 1e-3f);                       // Velocity carries the plate's normal
        Assert.Equal(900 - hit.Damage, duel.Target.Health);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.ShellExploded);   // AP doesn't burst
        Assert.Equal(0, duel.Sim.ShellCount);
    }

    [Fact]
    public void Ap_GlancingOffTheFrontPlate_Ricochets()
    {
        var duel = new Duel(targetYaw: MathF.PI + Angles.Deg(75));   // front plate turned 75° away from the shot
        var events = duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));

        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(HitResult.Ricochet, hit.Result);
        Assert.Equal(0, hit.Damage);
        Assert.Equal(900, duel.Target.Health);
    }

    [Fact]
    public void Ap_AgainstTheAngledTurretFront_DoesNotPenetrate()
    {
        // 120 mm at about 60° is about 240 mm effective, beyond the best roll of 175 × 1.25.
        var duel = new Duel(targetYaw: MathF.PI + Angles.Deg(60));
        var events = duel.Fire(duel.AimAt(duel.OnTarget(TurretFront)));

        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(HitResult.NotPenetrated, hit.Result);
        Assert.Equal(0, hit.Damage);
        Assert.Equal(900, duel.Target.Health);
    }

    [Fact]
    public void ARockInTheWay_TakesTheShellFirst()
    {
        var rock = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 125), new Vector2(3, 1), 0), -1, 6);
        var duel = new Duel(MathF.PI, rock);
        var events = duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));

        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankHit);
        GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(126f, boom.Position.Z, 0.01f);
    }

    [Fact]
    public void OfTwoTanksInLine_TheNearerTakesTheShell_WhateverTheirSlots()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(Duel.Spec, Duel.ShooterAt, 0f);
        sim.AddTank(Duel.Spec, new Vector2(100, 85), MathF.PI);              // the far one, checked first
        int near = sim.AddTank(Duel.Spec, Duel.TargetAt, MathF.PI);
        var pose = Tank.Sim.Vehicles.TankPose.Compute(sim.Tanks[0], sim.Field.Terrain);
        var (yaw, pitch) = Angles.AimAngles(new Vector3(100, 1.02f, 103.1f) - pose.Trunnion(Duel.Spec));
        var aim = new InputCmd(0, 0, 0, yaw, pitch, false);
        var hold = new InputCmd(0, 0, 0, MathF.PI, 0, false);
        var events = new List<GameEvent>();
        for (int i = 0; i < 80; i++)
        {
            sim.Step(new[] { i == 60 ? aim with { Fire = true } : aim, hold, hold });
            events.AddRange(sim.Events.ToArray());
        }
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(near, hit.TargetTankId);
    }

    [Fact]
    public void LastHitPoints_WreckTheTank()
    {
        var duel = new Duel(MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        var events = duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));

        int hit = events.FindIndex(e => e.Type == GameEventType.TankHit);
        int destroyed = events.FindIndex(e => e.Type == GameEventType.TankDestroyed);
        Assert.True(hit >= 0 && destroyed > hit, "expected TankHit then TankDestroyed");
        GameEvent wreck = events[destroyed];
        Assert.Equal(duel.ShooterId, wreck.TankId);
        Assert.Equal(duel.TargetId, wreck.TargetTankId);
        Assert.Equal(100f, wreck.Position.Z, 1e-3f);
        Assert.Equal(0, duel.Target.Health);
        Assert.True(duel.Target.IsDestroyed);
    }

    [Fact]
    public void AWreck_IgnoresInput_StillStopsShells_AndTakesNoDamage()
    {
        var duel = new Duel(MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront));
        duel.Fire(aim);
        Assert.True(duel.Target.IsDestroyed);

        // Its driver floors it and fires: nothing happens.
        var wreckInput = new InputCmd(0, 1, 1, 0, 0, true);
        var events = duel.Run(aim, 200, wreckInput);   // past the shooter's reload too
        Assert.Equal(new Vector2(100, 100), duel.Target.Position);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.ShellFired && e.TankId == duel.TargetId);

        // A second shell stops on the wreck: a puff, no hit, no damage.
        events = duel.Fire(aim);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankHit);
        GameEvent puff = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(103.1f, puff.Position.Z, 0.01f);
        Assert.Equal(0, duel.Target.Health);
    }

    [Fact]
    public void AWreck_StillBlocksOtherTanks()
    {
        var duel = new Duel(MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));
        duel.Run(new InputCmd(0, 1, 0, 0, 0, false), 300);   // the shooter drives north into it
        Assert.True(duel.Shooter.Position.Y > 100 + Duel.Spec.HullLength - 0.01f, $"drove into the wreck: {duel.Shooter.Position}");
    }

    [Fact]
    public void Revive_RestoresTheTank_AndTheNextStepReportsIt()
    {
        var duel = new Duel(MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));

        duel.Sim.Revive(duel.TargetId);
        Assert.Equal(900, duel.Target.Health);
        Assert.False(duel.Target.IsDestroyed);
        Assert.Equal(0f, duel.Target.Reload);
        Assert.Equal(0f, duel.Target.Bloom);

        var events = duel.Run(default, 1, new InputCmd(0, 1, 0, MathF.PI, 0, false));
        GameEvent revived = Assert.Single(events, e => e.Type == GameEventType.TankRevived);
        Assert.Equal(duel.TargetId, revived.TargetTankId);
        Assert.Equal(duel.Sim.Tick, revived.Tick);
        duel.Run(default, 30, new InputCmd(0, 1, 0, MathF.PI, 0, false));
        Assert.True(duel.Target.Position.Y > 100.5f, "a revived tank drives again");
    }

    [Fact]
    public void Revive_OnALiveTank_JustTopsItUp()
    {
        var duel = new Duel(MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 500;
        duel.Sim.Revive(duel.TargetId);
        Assert.Equal(900, duel.Target.Health);
        Assert.False(duel.Target.IsDestroyed);
    }

    [Fact]
    public void Revive_RejectsAnUnknownTank()
    {
        var duel = new Duel(MathF.PI);
        Assert.Throws<ArgumentException>(() => duel.Sim.Revive(99));
    }

    [Fact]
    public void SameShot_SameResult()
    {
        GameEvent Shoot()
        {
            var duel = new Duel(MathF.PI);
            return Assert.Single(duel.Fire(duel.AimAt(duel.OnTarget(HullFront))), e => e.Type == GameEventType.TankHit);
        }
        Assert.Equal(Shoot(), Shoot());
    }
}

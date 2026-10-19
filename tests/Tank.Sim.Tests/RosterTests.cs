using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>Tanks coming and going during a battle (stage 4 design §3): removal, respawn, spawn protection.</summary>
public sealed class RosterTests
{
    private const int Tps = SimConstants.TickRate;
    private static readonly Vector3 HullFront = new(0, 1.02f, -3.1f);

    private static InputCmd Hold(in TankState t) => new(0, 0, 0, t.Yaw, 0f, false);

    private static void StepAll(Simulation sim)
    {
        Span<InputCmd> inputs = stackalloc InputCmd[sim.TankCount];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = Hold(sim.Tanks[i]);
        sim.Step(inputs);
    }

    [Fact]
    public void Sixteen_teams_fit_a_free_for_all()
    {
        var sim = new Simulation(TestWorld.FlatField());
        for (byte team = 0; team < SimConstants.MaxTeams; team++) sim.AddTank(TankCatalog.Light, new Vector2(20 + team * 10, 100), 0f, team);
        Assert.Equal(16, sim.TankCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.AddTank(TankCatalog.Light, new Vector2(100, 150), 0f, SimConstants.MaxTeams));
    }

    [Fact]
    public void Removing_a_tank_closes_its_slot_and_keeps_the_others_ids()
    {
        var sim = new Simulation(TestWorld.FlatField());
        int a = sim.AddTank(TankCatalog.Light, new Vector2(50, 100), 0f, 0);
        int b = sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f, 1);
        int c = sim.AddTank(TankCatalog.Heavy, new Vector2(150, 100), 0f, 1);
        sim.RemoveTank(b);
        Assert.Equal(2, sim.TankCount);
        Assert.Equal(new[] { a, c }, new[] { sim.Tanks[0].Id, sim.Tanks[1].Id });
        Assert.Equal(TankCatalog.Heavy, sim.Tanks[1].Spec);
        Assert.Equal(0, sim.SlotOf(a));
        Assert.Equal(1, sim.SlotOf(c));
        Assert.Equal(-1, sim.SlotOf(b));
        Assert.Throws<ArgumentException>(() => sim.RemoveTank(b));
        StepAll(sim);   // two inputs now
        int d = sim.AddTank(TankCatalog.Td, new Vector2(100, 100), 0f, 2);
        Assert.Equal(2, sim.SlotOf(d));
        Assert.True(d > c, "ids never come back");
    }

    [Fact]
    public void A_removed_tanks_shell_flies_on_and_still_counts_as_its()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront));
        duel.Run(aim with { Fire = true }, 1);
        Assert.Equal(1, duel.Sim.ShellCount);
        duel.Sim.RemoveTank(duel.ShooterId);
        var events = new List<GameEvent>();
        for (int i = 0; i < 15; i++)
        {
            StepAll(duel.Sim);
            events.AddRange(duel.Sim.Events);
        }
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(duel.ShooterId, hit.TankId);
        Assert.Equal(duel.TargetId, hit.TargetTankId);
        Assert.True(hit.Damage > 0);
    }

    [Fact]
    public void Respawn_puts_a_wreck_back_fresh_somewhere_else_and_the_next_step_reports_it()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));
        Assert.True(duel.Target.IsDestroyed);
        int shots = duel.Shooter.ShotCount;
        duel.Sim.TankRef(duel.ShooterId).ApLeft = 3;

        duel.Sim.Respawn(duel.TargetId, new Vector2(60, 60), MathF.PI / 2);
        duel.Sim.Respawn(duel.ShooterId, new Vector2(140, 140), 0f);
        TankState target = duel.Target, shooter = duel.Shooter;
        Assert.False(target.IsDestroyed);
        Assert.Equal(target.Spec.MaxHealth, target.Health);
        Assert.Equal(new Vector2(60, 60), target.Position);
        Assert.Equal(MathF.PI / 2, target.Yaw, 1e-5f);
        Assert.Equal(0f, target.Speed);
        Assert.Equal(AmmoType.AP, target.Ammo);
        Assert.Equal(TankState.NeverFired, target.LastFired);
        Assert.Equal(shooter.Spec.Ap.Capacity, shooter.ApLeft);
        Assert.Equal(shots, shooter.ShotCount);   // kept: shot seeds stay unique
        Assert.Equal(duel.TargetId, target.Id);
        Assert.False(duel.Sim.IsProtected(target));

        var events = duel.Run(Hold(shooter), 1);
        Assert.Equal(2, events.Count(e => e.Type == GameEventType.TankRevived));
        Assert.Contains(events, e => e.Type == GameEventType.TankRevived && e.TargetTankId == duel.TargetId && e.Position.X == 60f);
    }

    [Fact]
    public void A_protected_tank_cannot_fire_until_the_protection_ends()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront));
        duel.Sim.Protect(duel.ShooterId, 10);
        Assert.True(duel.Sim.IsProtected(duel.Shooter));
        var events = duel.Run(aim with { Fire = true }, 10);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.ShellFired);
        Assert.Equal(0, duel.Shooter.ShotCount);
        events = duel.Run(aim with { Fire = true }, 1);   // tick ProtectedUntil: the protection is over
        Assert.Single(events, e => e.Type == GameEventType.ShellFired);
        Assert.False(duel.Sim.IsProtected(duel.Shooter));
    }

    [Fact]
    public void A_protected_tank_stops_the_shell_and_reports_the_hit_but_takes_no_damage()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront));
        duel.Sim.Protect(duel.TargetId, 5 * Tps);
        var events = duel.Fire(aim);
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(HitResult.Penetrated, hit.Result);
        Assert.Equal(0, hit.Damage);
        Assert.Equal(900, duel.Target.Health);
        Assert.Equal(0, duel.Sim.ShellCount);   // the shell stopped on the armour

        duel.Run(aim, 5 * Tps);   // the protection runs out
        events = duel.Fire(aim);
        hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.True(hit.Damage > 0);
        Assert.Equal(900 - hit.Damage, duel.Target.Health);
    }

    [Fact]
    public void A_protected_tank_is_reported_splashed_but_takes_no_damage()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        int bystander = duel.AddBystander(Duel.TargetAt + new Vector2(3.4f, 3.1f), MathF.PI);   // beside the target's front, where the shell bursts
        duel.Sim.Protect(bystander, 20 * Tps);   // outlasts the aiming and the HE reload
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront), AmmoType.HE);
        duel.Run(aim, (int)(Duel.Spec.ReloadTime * Tps) + 1);   // the HE swap costs a reload
        var events = duel.Fire(aim);
        Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        GameEvent splash = Assert.Single(events, e => e.Type == GameEventType.SplashDamage && e.TargetTankId == bystander);
        Assert.Equal(0, splash.Damage);
        Assert.Equal(900, duel.Sim.Tanks[2].Health);
    }

    [Fact]
    public void Vision_follows_tanks_when_a_slot_is_removed_and_another_added()
    {
        // A tank destroyer (team 0) at (100, 190) looks north; a light dummy (team 1) at 50 m is seen and doesn't watch;
        // a far light tank (team 1) at 180 m is out of range both ways (a still TD is seen from 172 m, a still light tank
        // from 162 m). Remove the observer's team mate in slot 1: the dummy's rows shift.
        var sim = new Simulation(TestWorld.FlatField());
        int observer = sim.AddTank(TankCatalog.Td, new Vector2(100, 190), 0f, 0);
        int mate = sim.AddTank(TankCatalog.Medium, new Vector2(60, 190), 0f, 0);
        int dummy = sim.AddTank(TankCatalog.Light, new Vector2(100, 140), MathF.PI / 2, 1);
        int far = sim.AddTank(TankCatalog.Light, new Vector2(100, 10), MathF.PI / 2, 1);
        var vision = new VisionTracker(sim);
        vision.SetWatching(dummy, false);
        StepAll(sim);
        vision.Update();
        Assert.True(vision.IsVisible(dummy, 0));
        Assert.False(vision.IsVisible(far, 0));
        Assert.False(vision.IsVisible(observer, 1), "the dummy doesn't watch");

        sim.RemoveTank(mate);
        sim.TankRef(dummy).Position = new Vector2(100, 12);   // out of range now, but the linger keeps it in view
        for (int i = 0; i < 4; i++) { StepAll(sim); vision.Update(); }
        Assert.True(vision.IsVisible(dummy, 0), "lingering: its row moved with it");
        int newcomer = sim.AddTank(TankCatalog.Heavy, new Vector2(100, 140), MathF.PI / 2, 1);
        Assert.False(vision.IsVisible(newcomer, 0), "a new tank starts unseen");
        for (int i = 0; i < 6; i++) { StepAll(sim); vision.Update(); }
        Assert.True(vision.IsVisible(newcomer, 0));
        Assert.True(vision.IsVisible(observer, 1), "the newcomer watches, and sees the observer 50 m away");
        Assert.False(vision.IsVisible(far, 0));
    }

    [Fact]
    public void A_tank_respawned_somewhere_else_starts_unseen_while_one_revived_where_it_lay_stays_in_view()
    {
        // A medium observer (team 0) at (190, 190); an enemy medium wrecked 50 m away is seen as a wreck. Respawned at
        // (10, 10), 255 m off and beyond detection, it must not be given away for the linger; revived in place it stays.
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(TankCatalog.Medium, new Vector2(190, 190), 0f, 0);
        int enemy = sim.AddTank(TankCatalog.Medium, new Vector2(190, 140), MathF.PI, 1);
        var vision = new VisionTracker(sim);
        sim.TankRef(enemy).IsDestroyed = true;
        sim.TankRef(enemy).Health = 0;
        StepAll(sim);
        vision.Update();
        Assert.True(vision.IsVisible(enemy, 0), "a wreck is seen by all");

        sim.Respawn(enemy, new Vector2(10, 10), 0f);
        Assert.False(vision.IsVisible(enemy, 0), "respawned elsewhere: unseen at once, even before the next update");
        vision.Update();   // same tick: the server asks right after the respawn
        Assert.False(vision.IsVisible(enemy, 0));
        Assert.Equal(TankCatalog.Medium.Camo.Still, vision.Inspect(sim.Tanks[0].Id, enemy).Camo, 1e-5f);   // its new heading isn't a turn: still, not moving
        for (int i = 0; i < 12; i++) { StepAll(sim); vision.Update(); }
        Assert.False(vision.IsVisible(enemy, 0), "and still unseen 255 m away");

        sim.TankRef(enemy).IsDestroyed = true;
        sim.TankRef(enemy).Health = 0;
        sim.TankRef(enemy).Position = new Vector2(190, 140);
        for (int i = 0; i < 6; i++) { StepAll(sim); vision.Update(); }
        Assert.True(vision.IsVisible(enemy, 0));
        sim.Revive(enemy);
        vision.Update();
        Assert.True(vision.IsVisible(enemy, 0), "revived where it lay: in view for the linger");
    }
}

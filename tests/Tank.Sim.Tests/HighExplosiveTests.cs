using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class HighExplosiveTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;
    private static readonly Vector3 HullFront = new(0, 1.02f, -3.1f);
    private static readonly int ReloadTicks = (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate);

    /// <summary>
    /// A narrow rock whose face is 3 m ahead of the trunnion of a tank at (100, 100) facing north: that tank's barrel
    /// pokes into it, so HE it fires bursts at the face.
    /// </summary>
    private static Simulation BehindARock(out float face)
    {
        face = 100 - Spec.TurretOffset - 3f;
        var rock = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, face - 2), new Vector2(1.5f, 2), 0), -1, 6);
        return new Simulation(TestWorld.FlatField(rock));
    }

    [Fact]
    public void DirectHit_OnThickArmour_DealsReducedDamage_AndBursts()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront), AmmoType.HE);
        duel.Run(aim, ReloadTicks);   // swapping to HE restarts the reload
        var events = duel.Fire(aim);

        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(AmmoType.HE, hit.Ammo);
        Assert.Equal(HitResult.NotPenetrated, hit.Result);
        // 280 × 0.5 − 90 × 1.1 / cos θ. Exact only because the shot lands almost head-on (θ ≈ 1.3°, giving 40.97);
        // beyond about 5.7° it would round to 40.
        Assert.Equal(41, hit.Damage);
        Assert.Equal(900 - 41, duel.Target.Health);
        GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(hit.Position, boom.Position);
        Assert.True(events.IndexOf(boom) > events.IndexOf(hit), "the hit is reported before the burst");
        Assert.DoesNotContain(events, e => e.Type == GameEventType.SplashDamage);   // the struck tank isn't splashed too
    }

    [Fact]
    public void DirectHit_SplashesATankAlongside_ButNotTheOneItStruck()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        int bystander = duel.AddBystander(new Vector2(103.4f, 100), MathF.PI);   // 0.2 m off the target's side
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront), AmmoType.HE);
        duel.Run(aim, ReloadTicks);
        var events = duel.Fire(aim);

        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(duel.TargetId, hit.TargetTankId);
        GameEvent splash = Assert.Single(events, e => e.Type == GameEventType.SplashDamage);
        Assert.Equal(bystander, splash.TargetTankId);
        Assert.True(splash.Damage > 0);
        Assert.Equal(900 - hit.Damage, duel.Target.Health);
        Assert.Equal(900 - splash.Damage, duel.Sim.Tanks[2].Health);
    }

    [Fact]
    public void DirectHit_OnAWreck_StillSplashesALiveTankAlongside()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        int bystander = duel.AddBystander(new Vector2(103.4f, 100), MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        InputCmd ap = duel.AimAt(duel.OnTarget(HullFront));
        duel.Fire(ap);
        Assert.True(duel.Target.IsDestroyed);

        InputCmd he = ap with { Ammo = AmmoType.HE };
        duel.Run(he, ReloadTicks);   // swapping to HE restarts the reload
        var events = duel.Fire(he);

        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankHit);
        GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(103.1f, boom.Position.Z, 0.01f);                  // on the wreck's front plate
        GameEvent splash = Assert.Single(events, e => e.Type == GameEventType.SplashDamage);
        Assert.Equal(bystander, splash.TargetTankId);
        TankState neighbour = duel.Sim.Tanks[2];
        float distance = TankPose.Compute(neighbour, duel.Sim.Field.Terrain).HullBox(Spec).DistanceTo(boom.Position);
        Assert.Equal(Penetration.Splash(Spec.He, distance, 20), splash.Damage);
        Assert.True(splash.Damage > 0);
        Assert.Equal(900 - splash.Damage, neighbour.Health);
        Assert.Equal(0, duel.Target.Health);
    }

    [Fact]
    public void GroundBurst_SplashesTheTankNearby_FadingWithDistance()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        InputCmd aim = duel.AimAt(duel.OnTarget(new Vector3(0, 0, -4.1f)), AmmoType.HE);   // the ground 1 m before its nose
        duel.Run(aim, ReloadTicks);
        var events = duel.Fire(aim);

        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankHit);
        GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        GameEvent splash = Assert.Single(events, e => e.Type == GameEventType.SplashDamage);
        Assert.Equal(boom.Position, splash.Position);
        Assert.Equal(duel.TargetId, splash.TargetTankId);
        Assert.Equal(duel.ShooterId, splash.TankId);
        float distance = TankPose.Compute(duel.Target, duel.Sim.Field.Terrain).HullBox(Spec).DistanceTo(boom.Position);
        // Only "clear of the hull, inside the splash radius": the shell grazes the ground, so the dispersion (seeded by
        // the firing tick, which moves with the reload time) shifts where it lands along the line by a metre or more.
        Assert.InRange(distance, 0.3f, Spec.He.SplashRadius);
        Assert.Equal(Penetration.Splash(Spec.He, distance, 20), splash.Damage);
        Assert.True(splash.Damage > 0);
        Assert.Equal(900 - splash.Damage, duel.Target.Health);
    }

    [Fact]
    public void BlockedBarrel_Burst_SplashesANeighbour_ButNeverTheShooter()
    {
        Simulation sim = BehindARock(out float face);
        int shooter = sim.AddTank(Spec, new Vector2(100, 100), 0f);
        int neighbour = sim.AddTank(Spec, new Vector2(103.3f, 99), 0f);   // just clear of the shooter's right side
        var he = new InputCmd(0, 0, 0, 0, 0, false, AmmoType.HE);
        var inputs = new[] { he, new InputCmd(0, 0, 0, 0, 0, false) };
        for (int i = 0; i < ReloadTicks + 1; i++) sim.Step(inputs);
        inputs[0] = he with { Fire = true };
        sim.Step(inputs);

        GameEvent boom = Assert.Single(sim.Events.ToArray(), e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(face, boom.Position.Z, 0.01f);
        GameEvent splash = Assert.Single(sim.Events.ToArray(), e => e.Type == GameEventType.SplashDamage);
        Assert.Equal(neighbour, splash.TargetTankId);
        Assert.Equal(shooter, splash.TankId);
        Assert.True(splash.Damage > 0);
        Assert.Equal(900, sim.Tanks[0].Health);
    }

    [Fact]
    public void BlockedBarrel_Burst_GoesOffAfterEveryTankHasHadItsTurn()
    {
        // The neighbour, in the higher slot and on its last hit point, pulls away and fires on the very tick the shooter
        // fires into the rock. The burst goes off with the shells, after every tank has acted, so the neighbour still
        // drives and fires that tick before it is wrecked.
        Simulation sim = BehindARock(out _);
        sim.AddTank(Spec, new Vector2(100, 100), 0f);
        int neighbour = sim.AddTank(Spec, new Vector2(103.3f, 99), 0f);
        sim.TankRef(neighbour).Health = 1;
        var he = new InputCmd(0, 0, 0, 0, 0, false, AmmoType.HE);
        var inputs = new[] { he, new InputCmd(0, 0, 0, 0, 0, false) };
        for (int i = 0; i < ReloadTicks + 1; i++) sim.Step(inputs);
        Vector2 before = sim.Tanks[1].Position;
        sim.Step(new[] { he with { Fire = true }, new InputCmd(0, 1, 0, 0, 0, true) });

        GameEvent[] events = sim.Events.ToArray();
        GameEvent wreck = Assert.Single(events, e => e.Type == GameEventType.TankDestroyed);
        Assert.Equal(neighbour, wreck.TargetTankId);
        Assert.True(sim.Tanks[1].Position.Y < before.Y, $"the neighbour stayed at {sim.Tanks[1].Position} on the tick it was wrecked");
        Assert.Single(events, e => e.Type == GameEventType.ShellFired && e.TankId == neighbour);
    }

    [Fact]
    public void Splash_CanWreckATank_ButSkipsWrecks()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        InputCmd aim = duel.AimAt(duel.OnTarget(new Vector3(0, 0, -4.1f)), AmmoType.HE);
        duel.Run(aim, ReloadTicks);
        var events = duel.Fire(aim);
        int splash = events.FindIndex(e => e.Type == GameEventType.SplashDamage);
        int destroyed = events.FindIndex(e => e.Type == GameEventType.TankDestroyed);
        Assert.True(splash >= 0 && destroyed > splash, "expected SplashDamage then TankDestroyed");

        duel.Run(aim, ReloadTicks);
        events = duel.Fire(aim);
        Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.SplashDamage);
    }
}

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

    [Fact]
    public void DirectHit_OnThickArmour_DealsReducedDamage_AndBursts()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront), AmmoType.HE);
        duel.Run(aim, (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate));   // swapping to HE restarts the reload
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
    public void GroundBurst_SplashesTheTankNearby_FadingWithDistance()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        InputCmd aim = duel.AimAt(duel.OnTarget(new Vector3(0, 0, -4.1f)), AmmoType.HE);   // the ground 1 m before its nose
        duel.Run(aim, (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate));
        var events = duel.Fire(aim);

        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankHit);
        GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        GameEvent splash = Assert.Single(events, e => e.Type == GameEventType.SplashDamage);
        Assert.Equal(boom.Position, splash.Position);
        Assert.Equal(duel.TargetId, splash.TargetTankId);
        Assert.Equal(duel.ShooterId, splash.TankId);
        float distance = TankPose.Compute(duel.Target, duel.Sim.Field.Terrain).HullBox(Spec).DistanceTo(boom.Position);
        Assert.InRange(distance, 0.3f, 2f);
        Assert.Equal(Penetration.Splash(Spec.He, distance, 20), splash.Damage);
        Assert.True(splash.Damage > 0);
        Assert.Equal(900 - splash.Damage, duel.Target.Health);
    }

    [Fact]
    public void BlockedBarrel_Burst_SplashesANeighbour_ButNeverTheShooter()
    {
        // A narrow rock face 3 m ahead of the shooter's trunnion: the barrel pokes into it and HE bursts at the face.
        float face = 100 - Spec.TurretOffset - 3f;
        var rock = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, face - 2), new Vector2(1.5f, 2), 0), -1, 6);
        var sim = new Simulation(TestWorld.FlatField(rock));
        int shooter = sim.AddTank(Spec, new Vector2(100, 100), 0f);
        int neighbour = sim.AddTank(Spec, new Vector2(103.3f, 99), 0f);   // just clear of the shooter's right side
        var he = new InputCmd(0, 0, 0, 0, 0, false, AmmoType.HE);
        var inputs = new[] { he, new InputCmd(0, 0, 0, 0, 0, false) };
        for (int i = 0; i < (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate) + 1; i++) sim.Step(inputs);
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
    public void Splash_CanWreckATank_ButSkipsWrecks()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        InputCmd aim = duel.AimAt(duel.OnTarget(new Vector3(0, 0, -4.1f)), AmmoType.HE);
        duel.Run(aim, (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate));
        var events = duel.Fire(aim);
        int splash = events.FindIndex(e => e.Type == GameEventType.SplashDamage);
        int destroyed = events.FindIndex(e => e.Type == GameEventType.TankDestroyed);
        Assert.True(splash >= 0 && destroyed > splash, "expected SplashDamage then TankDestroyed");

        duel.Run(aim, (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate));
        events = duel.Fire(aim);
        Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.SplashDamage);
    }
}

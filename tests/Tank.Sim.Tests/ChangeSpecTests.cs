using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class ChangeSpecTests
{
    private static readonly TankSpec Medium = TankCatalog.Medium;

    /// <summary>A different vehicle: more health, a bigger gun, a smaller AP rack, a harder-hitting round.</summary>
    private static readonly TankSpec Big = Medium with
    {
        Id = "big",
        MaxHealth = 1400,
        Caliber = 122,
        Ap = Medium.Ap with { Damage = 330, Capacity = 25 },
        He = Medium.He with { Capacity = 12 },
    };

    /// <summary>
    /// A medium tank that has driven into the map border, turned its gun, fired three times (AP twice, then HE once the
    /// switch's reload was done), taken a hit and is reloading.
    /// </summary>
    private static Simulation Used(out int id)
    {
        var sim = new Simulation(TestWorld.FlatField());
        id = sim.AddTank(Medium, new Vector2(100, 100), 0.4f);
        var inputs = new[] { TestWorld.Cmd(throttle: 1, aimYaw: 1.2f, aimPitch: 0.1f, fire: true) };
        for (int i = 0; i < 150; i++) sim.Step(inputs);
        inputs[0] = inputs[0] with { Ammo = AmmoType.HE };
        for (int i = 0; i < 150; i++) sim.Step(inputs);
        sim.TankRef(id).Health = 300;
        return sim;
    }

    [Fact]
    public void KeepsIdPlaceHeadingAndShotCount()
    {
        var sim = Used(out int id);
        TankState before = sim.Tanks[0];
        Assert.True(before.ShotCount >= 2);
        sim.ChangeSpec(id, Big);
        TankState after = sim.Tanks[0];
        Assert.Same(Big, after.Spec);
        Assert.Equal(id, after.Id);
        Assert.Equal(before.Position, after.Position);
        Assert.Equal(before.Yaw, after.Yaw);
        Assert.Equal(before.ShotCount, after.ShotCount);
    }

    [Fact]
    public void StartsEverythingElseFresh()
    {
        var sim = Used(out int id);
        TankState used = sim.Tanks[0];
        Assert.Equal(AmmoType.HE, used.Ammo);
        Assert.True(used.Reload > 0f);
        Assert.NotEqual(0f, used.TurretYaw);
        Assert.True(used.Bloom > 0f);
        Assert.Equal(300, used.Health);
        sim.ChangeSpec(id, Big);
        TankState t = sim.Tanks[0];
        Assert.Equal(1400, t.Health);
        Assert.False(t.IsDestroyed);
        Assert.Equal(AmmoType.AP, t.Ammo);
        Assert.Equal(25, t.ApLeft);
        Assert.Equal(12, t.HeLeft);
        Assert.Equal(0f, t.Reload);
        Assert.Equal(0f, t.Bloom);
        Assert.Equal(0f, t.Speed);
        Assert.Equal(0f, t.TurretYaw);
        Assert.Equal(0f, t.GunPitch);
    }

    [Fact]
    public void TurretTank_BecomingACasemate_StartsWithItsGunStraightAhead()
    {
        var sim = new Simulation(TestWorld.FlatField());
        int id = sim.AddTank(Medium, new Vector2(100, 100), 0f);
        var inputs = new[] { TestWorld.Cmd(aimYaw: 2.0f) };
        for (int i = 0; i < 120; i++) sim.Step(inputs);
        Assert.Equal(2.0f, sim.Tanks[0].TurretYaw, 1e-3f);

        TankSpec casemate = Medium with { Id = "casemate", GunArc = Angles.Deg(12) };
        sim.ChangeSpec(id, casemate);
        Assert.Equal(0f, sim.Tanks[0].TurretYaw);

        for (int i = 0; i < 60; i++)
        {
            sim.Step(inputs);
            Assert.True(MathF.Abs(sim.Tanks[0].TurretYaw) <= casemate.GunArc + 1e-4f);
        }
    }

    [Fact]
    public void BringsBackAWreck()
    {
        var sim = Used(out int id);
        ref TankState tank = ref sim.TankRef(id);
        tank.Health = 0;
        tank.IsDestroyed = true;
        sim.ChangeSpec(id, Medium);
        Assert.False(sim.Tanks[0].IsDestroyed);
        Assert.Equal(900, sim.Tanks[0].Health);
    }

    [Fact]
    public void ShellsInFlight_KeepTheirOwnRound()
    {
        var sim = new Simulation(TestWorld.FlatField());
        int id = sim.AddTank(Medium, new Vector2(100, 100), 0f);
        sim.Step(new[] { TestWorld.Cmd(aimPitch: 0.2f, fire: true) });
        sim.ChangeSpec(id, Big);
        sim.Step(new[] { TestWorld.Cmd() });
        Shell shell = Assert.Single(sim.Shells.ToArray());
        Assert.Equal(Medium.Ap, shell.Round);
        Assert.Equal(Medium.Caliber, shell.Caliber);
    }

    [Fact]
    public void UnknownTank_Throws()
    {
        var sim = new Simulation(TestWorld.FlatField());
        Assert.Throws<ArgumentException>(() => sim.ChangeSpec(7, Big));
    }
}

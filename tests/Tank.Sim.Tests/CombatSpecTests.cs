using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class CombatSpecTests
{
    public static TheoryData<string> SpecIds()
    {
        var ids = new TheoryData<string>();
        foreach (TankSpec spec in TankCatalog.All) ids.Add(spec.Id);
        return ids;
    }

    private static TankSpec Get(string id) => TankCatalog.All.Single(s => s.Id == id);

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void CombatNumbers_ArePositive(string id)
    {
        TankSpec s = Get(id);
        Assert.True(s.MaxHealth > 0);
        Assert.All(new[] { s.Caliber, s.Dispersion, s.AimTime, s.Bloom.Moving, s.Bloom.HullTurn, s.Bloom.TurretTurn, s.Bloom.Fire },
            v => Assert.True(v > 0, $"{id} has a non-positive combat value"));
        foreach (ShellSpec round in new[] { s.Ap, s.He })
        {
            Assert.True(round.Penetration > 0 && round.Damage > 0 && round.Speed > 0 && round.Capacity > 0, $"{id}: {round}");
            Assert.True(round.SplashRadius >= 0, $"{id}: {round}");
        }
        Assert.True(s.He.SplashRadius > 0, $"{id}: HE must splash");
        foreach (ArmorBox box in new[] { s.HullArmor, s.TurretArmor })
        {
            Armor a = box.Armor;
            Assert.All(new[] { box.Width, box.Length, box.Top - box.Bottom, a.Front, a.Side, a.Rear, a.Top, a.Bottom },
                v => Assert.True(v > 0, $"{id}: {box}"));
        }
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void ArmorBoxes_SitInsideTheFootprint_TurretOnTop(string id)
    {
        TankSpec s = Get(id);
        Assert.True(s.HullArmor.Width <= s.HullWidth && s.HullArmor.Length <= s.HullLength, $"{id}: hull box sticks out");
        Assert.True(s.TurretArmor.Width <= s.HullWidth, $"{id}: turret box wider than the hull");
        Assert.True(s.TurretOffset + s.TurretArmor.Length / 2 <= s.HullLength / 2, $"{id}: turret box past the nose");
        Assert.True(s.HullArmor.Bottom >= 0, $"{id}: hull box under the ground");
        Assert.True(s.TurretArmor.Bottom > s.HullArmor.Bottom && s.TurretArmor.Top > s.HullArmor.Top, $"{id}: turret box not above the hull box");
        Assert.True(s.TrunnionHeight > s.TurretArmor.Bottom && s.TrunnionHeight < s.TurretArmor.Top, $"{id}: gun pivot outside the turret box");
    }

    [Fact]
    public void Medium_MatchesTheDesignTable()
    {
        TankSpec m = TankCatalog.Medium;
        Assert.Equal(900, m.MaxHealth);
        Assert.Equal(85f, m.Caliber);
        Assert.Equal(new ArmorBox(Width: 3.2f, Length: 6.2f, Bottom: 0.3f, Top: 1.74f, new Armor(90, 60, 40, 20, 20)), m.HullArmor);
        Assert.Equal(new ArmorBox(Width: 2.9f, Length: 2.9f, Bottom: 1.68f, Top: 2.8f, new Armor(120, 80, 50, 25, 25)), m.TurretArmor);
        Assert.Equal(new ShellSpec(175, 220, 780, 0, 40), m.Ap);
        Assert.Equal(new ShellSpec(45, 280, 624, 3, 15), m.He);
        Assert.Equal(0.36f, m.Dispersion);
        Assert.Equal(2.1f, m.AimTime);
        Assert.Equal(new BloomFactors(2.0f, 1.5f, 1.0f, 3.0f), m.Bloom);
    }

    [Fact]
    public void Armor_LooksUpFaces_AndTheThinnest()
    {
        var armor = new Armor(Front: 90, Side: 60, Rear: 40, Top: 20, Bottom: 25);
        Assert.Equal(60f, armor[ArmorFace.Side]);
        Assert.Equal(25f, armor[ArmorFace.Bottom]);
        Assert.Equal(20f, armor.Thinnest);
        Assert.Equal(ArmorFace.Front, Armor.FaceOf(Geometry.BoxFace.NegZ));
        Assert.Equal(ArmorFace.Rear, Armor.FaceOf(Geometry.BoxFace.PosZ));
        Assert.Equal(ArmorFace.Top, Armor.FaceOf(Geometry.BoxFace.PosY));
        Assert.Equal(ArmorFace.Bottom, Armor.FaceOf(Geometry.BoxFace.NegY));
        Assert.Equal(ArmorFace.Side, Armor.FaceOf(Geometry.BoxFace.PosX));
        Assert.Equal(ArmorFace.Side, Armor.FaceOf(Geometry.BoxFace.NegX));
    }

    [Fact]
    public void ArmorBox_CentreAndHalfSizes()
    {
        var box = new ArmorBox(Width: 3.2f, Length: 6.2f, Bottom: 0.3f, Top: 1.74f, default);
        Assert.Equal(new Vector3(1.6f, 0.72f, 3.1f), box.HalfExtents);
        Assert.Equal(new Vector3(0, 1.02f, 0), box.LocalCenter);
    }

    [Fact]
    public void NewTank_HasFullHealthAndAmmo_WithApLoaded()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0);
        TankState t = sim.Tanks[0];
        Assert.Equal(900, t.Health);
        Assert.False(t.IsDestroyed);
        Assert.Equal(AmmoType.AP, t.Ammo);
        Assert.Equal(40, t.ApLeft);
        Assert.Equal(15, t.HeLeft);
        Assert.Equal(0f, t.Bloom);
    }

    [Fact]
    public void Launch_UsesTheLoadedRoundsSpeed()
    {
        TankState tank = TestWorld.Tank(100, 100) with { Ammo = AmmoType.HE };
        var launch = Ballistics.Aim(tank, TestWorld.FlatField());
        Assert.Equal(624f, launch.Velocity.Length(), 0.01f);
    }
}

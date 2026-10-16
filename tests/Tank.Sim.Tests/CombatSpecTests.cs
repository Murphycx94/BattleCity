using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
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
        float anchor = s.HasTurret ? s.TurretOffset : 0f;   // a casemate box hangs off the hull origin
        Assert.True(anchor + s.TurretArmor.Length / 2 <= s.HullLength / 2, $"{id}: turret box past the nose");
        Assert.True(s.TurretOffset < s.HullLength / 2, $"{id}: gun pivot past the nose");
        Assert.True(s.TurretOffset + s.GunLength > s.HullLength / 2, $"{id}: muzzle inside the hull");
        Assert.True(s.HullArmor.Bottom >= 0, $"{id}: hull box under the ground");
        Assert.True(s.TurretArmor.Bottom > s.HullArmor.Bottom && s.TurretArmor.Top > s.HullArmor.Top, $"{id}: turret box not above the hull box");
        Assert.True(s.TrunnionHeight > s.TurretArmor.Bottom && s.TrunnionHeight < s.TurretArmor.Top, $"{id}: gun pivot outside the turret box");
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void GunArc_IsAtMostHalfATurn(string id)
    {
        TankSpec s = Get(id);
        Assert.True(s.GunArc > 0 && s.GunArc <= TankSpec.FullTraverse, $"{id}: gun arc {s.GunArc}");
    }

    /// <summary>F1–F4 in the sandbox pick the tanks by their place in <see cref="TankCatalog.All"/>.</summary>
    [Fact]
    public void All_RunsLightMediumHeavyTd()
    {
        Assert.Equal(new[] { "light", "medium", "heavy", "td" }, TankCatalog.All.Select(s => s.Id));
    }

    [Fact]
    public void ById_FindsEveryTank()
    {
        foreach (TankSpec spec in TankCatalog.All) Assert.Same(spec, TankCatalog.ById(spec.Id));
    }

    [Fact]
    public void ById_UnknownName_ListsTheKnownOnes()
    {
        var error = Assert.Throws<KeyNotFoundException>(() => TankCatalog.ById("tiger"));
        Assert.Contains("'tiger'", error.Message);
        Assert.Contains("medium", error.Message);
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
        Assert.True(m.HasTurret);
    }

    [Fact]
    public void Light_MatchesTheDesignTable()
    {
        TankSpec l = TankCatalog.Light;
        Assert.Equal(600, l.MaxHealth);
        Assert.Equal(75f, l.Caliber);
        Assert.Equal(2.5f, l.ReloadTime);
        Assert.Equal(60f, l.MaxForwardSpeed * 3.6f, 1e-3f);
        Assert.Equal(20f, l.MaxReverseSpeed * 3.6f, 1e-3f);
        Assert.Equal(Angles.Deg(50), l.HullTraverse);
        Assert.Equal(Angles.Deg(48), l.TurretTraverse);
        Assert.Equal(new Armor(30, 25, 20, 15, 15), l.HullArmor.Armor);
        Assert.Equal(new Armor(40, 30, 25, 15, 15), l.TurretArmor.Armor);
        Assert.Equal(new ShellSpec(140, 130, 800, 0, 40), l.Ap);
        Assert.Equal(new ShellSpec(40, 170, 640, 2.5f, 15), l.He);
        Assert.Equal(0.40f, l.Dispersion);
        Assert.Equal(1.8f, l.AimTime);
        Assert.Equal(new BloomFactors(1.5f, 1.2f, 0.8f, 3.0f), l.Bloom);
        Assert.True(l.HasTurret);
    }

    [Fact]
    public void Heavy_MatchesTheDesignTable()
    {
        TankSpec h = TankCatalog.Heavy;
        Assert.Equal(1400, h.MaxHealth);
        Assert.Equal(122f, h.Caliber);
        Assert.Equal(6f, h.ReloadTime);
        Assert.Equal(35f, h.MaxForwardSpeed * 3.6f, 1e-3f);
        Assert.Equal(12f, h.MaxReverseSpeed * 3.6f, 1e-3f);
        Assert.Equal(Angles.Deg(26), h.HullTraverse);
        Assert.Equal(Angles.Deg(24), h.TurretTraverse);
        Assert.Equal(new Armor(150, 90, 60, 30, 25), h.HullArmor.Armor);
        Assert.Equal(new Armor(200, 120, 80, 40, 40), h.TurretArmor.Armor);
        Assert.Equal(new ShellSpec(225, 330, 720, 0, 25), h.Ap);
        Assert.Equal(new ShellSpec(65, 420, 576, 4, 15), h.He);
        Assert.Equal(0.44f, h.Dispersion);
        Assert.Equal(2.9f, h.AimTime);
        Assert.Equal(new BloomFactors(2.5f, 2.0f, 1.2f, 3.5f), h.Bloom);
        Assert.True(h.HasTurret);
    }

    [Fact]
    public void Td_MatchesTheDesignTable()
    {
        TankSpec td = TankCatalog.Td;
        Assert.Equal(950, td.MaxHealth);
        Assert.Equal(105f, td.Caliber);
        Assert.Equal(6.5f, td.ReloadTime);
        Assert.Equal(40f, td.MaxForwardSpeed * 3.6f, 1e-3f);
        Assert.Equal(15f, td.MaxReverseSpeed * 3.6f, 1e-3f);
        Assert.Equal(Angles.Deg(32), td.HullTraverse);
        Assert.Equal(Angles.Deg(30), td.TurretTraverse);
        Assert.Equal(Angles.Deg(12), td.GunArc);
        Assert.False(td.HasTurret);
        Assert.Equal(2.8f, td.TurretOffset + td.GunLength - td.HullLength / 2, 1e-4f);   // the muzzle 2.8 m past the nose
        Assert.Equal(new Armor(140, 50, 35, 20, 20), td.HullArmor.Armor);
        Assert.Equal(new Armor(140, 50, 35, 20, 20), td.TurretArmor.Armor);
        Assert.Equal(new ShellSpec(250, 380, 850, 0, 30), td.Ap);
        Assert.Equal(new ShellSpec(55, 480, 680, 3.5f, 12), td.He);
        Assert.Equal(0.32f, td.Dispersion);
        Assert.Equal(1.9f, td.AimTime);
        Assert.Equal(new BloomFactors(2.5f, 2.0f, 0.8f, 3.0f), td.Bloom);
    }

    /// <summary>The 2B balance: tanks differ in the size of each hit, not in how much damage they deal over time.</summary>
    [Theory]
    [MemberData(nameof(SpecIds))]
    public void EveryTank_Deals52To58ApDamageASecond(string id)
    {
        TankSpec s = Get(id);
        Assert.InRange(MathF.Round(s.Ap.Damage / s.ReloadTime), 52f, 58f);   // the design table's whole numbers (TD: 58.46)
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

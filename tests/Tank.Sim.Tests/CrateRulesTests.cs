using System.Numerics;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>The crate numbers (stage 6 design §4.2) and the match's crate table, with no simulation involved.</summary>
public sealed class CrateRulesTests
{
    private static TankState Medium() => new() { Id = 1, Spec = TankCatalog.Medium, Health = 900, ApLeft = 40, HeLeft = 15 };

    private static Crate[] Three() => new Crate[]
    {
        new(0, new Vector2(100, 100), CrateKind.Repair),
        new(1, new Vector2(130, 100), CrateKind.Ammo),
        new(2, new Vector2(160, 100), CrateKind.Armor),
    };

    [Theory]
    [InlineData("light", 180)]
    [InlineData("medium", 270)]
    [InlineData("heavy", 420)]
    [InlineData("td", 285)]
    public void A_repair_crate_restores_three_tenths_of_max_health_rounded_up(string tank, int amount) =>
        Assert.Equal(amount, CrateRules.RepairAmount(TankCatalog.ById(tank).MaxHealth));

    [Theory]
    [InlineData(40, 20)]
    [InlineData(15, 8)]
    [InlineData(25, 13)]
    [InlineData(30, 15)]
    [InlineData(12, 6)]
    public void An_ammo_crate_adds_half_the_rack_rounded_up(int capacity, int amount) =>
        Assert.Equal(amount, CrateRules.AmmoAmount(capacity));

    [Fact]
    public void Armour_layers_take_a_tenth_each_off_shell_damage()
    {
        Assert.Equal(1f, CrateRules.ArmorFactor(0), 1e-6f);
        Assert.Equal(0.9f, CrateRules.ArmorFactor(1), 1e-6f);
        Assert.Equal(0.8f, CrateRules.ArmorFactor(2), 1e-6f);
        Assert.Equal(0.7f, CrateRules.ArmorFactor(3), 1e-6f);
    }

    [Theory]
    [InlineData(100, 1, 90)]
    [InlineData(100, 0, 100)]
    [InlineData(5, 3, 4)]      // 3.5 rounds up
    [InlineData(1, 3, 1)]      // never below 1 for a hit that did damage
    [InlineData(0, 3, 0)]
    [InlineData(-3, 2, -3)]    // damage ≤ 0 stays as it is
    public void Shell_damage_is_scaled_by_the_layers_rounded_half_up_and_never_below_one(int damage, int layers, int scaled) =>
        Assert.Equal(scaled, CrateRules.ScaleShellDamage(damage, layers));

    [Fact]
    public void Reload_layers_take_eight_percent_each_off_the_reload_time()
    {
        Assert.Equal(4f, CrateRules.ReloadTime(TankCatalog.Medium, 0));
        Assert.Equal(3.68f, CrateRules.ReloadTime(TankCatalog.Medium, 1), 1e-5f);
        Assert.Equal(3.36f, CrateRules.ReloadTime(TankCatalog.Medium, 2), 1e-5f);
        Assert.Equal(3.04f, CrateRules.ReloadTime(TankCatalog.Medium, 3), 1e-5f);
    }

    [Fact]
    public void Optics_layers_add_a_tenth_each_to_the_view_range()
    {
        TankState t = Medium();
        Assert.Equal(270f, CrateRules.ViewRange(t));
        t.OpticsLayers = 1;
        Assert.Equal(297f, CrateRules.ViewRange(t), 1e-3f);
        t.OpticsLayers = 3;
        Assert.Equal(351f, CrateRules.ViewRange(t), 1e-3f);
    }

    [Fact]
    public void A_crate_is_useful_only_when_it_would_do_something()
    {
        TankState t = Medium();
        Assert.False(CrateRules.IsUseful(t, CrateKind.Repair));
        Assert.False(CrateRules.IsUseful(t, CrateKind.Ammo));
        Assert.True(CrateRules.IsUseful(t, CrateKind.Armor));
        Assert.True(CrateRules.IsUseful(t, CrateKind.Reload));
        Assert.True(CrateRules.IsUseful(t, CrateKind.Optics));

        t.Health = 899;
        Assert.True(CrateRules.IsUseful(t, CrateKind.Repair));
        t.HeLeft = 14;
        Assert.True(CrateRules.IsUseful(t, CrateKind.Ammo));
        t.HeLeft = 15;
        t.ApLeft = 39;
        Assert.True(CrateRules.IsUseful(t, CrateKind.Ammo));

        t.ArmorLayers = 3;
        Assert.False(CrateRules.IsUseful(t, CrateKind.Armor));
        t.ReloadLayers = 2;
        Assert.True(CrateRules.IsUseful(t, CrateKind.Reload));
        t.ReloadLayers = 3;
        Assert.False(CrateRules.IsUseful(t, CrateKind.Reload));
        t.OpticsLayers = 3;
        Assert.False(CrateRules.IsUseful(t, CrateKind.Optics));
    }

    [Fact]
    public void Layers_reads_the_tanks_layer_of_that_kind_and_zero_for_repair_and_ammo()
    {
        TankState t = Medium();
        t.ArmorLayers = 1;
        t.ReloadLayers = 2;
        t.OpticsLayers = 3;
        Assert.Equal(1, CrateRules.Layers(t, CrateKind.Armor));
        Assert.Equal(2, CrateRules.Layers(t, CrateKind.Reload));
        Assert.Equal(3, CrateRules.Layers(t, CrateKind.Optics));
        Assert.Equal(0, CrateRules.Layers(t, CrateKind.Repair));
        Assert.Equal(0, CrateRules.Layers(t, CrateKind.Ammo));
    }

    [Fact]
    public void Every_kind_has_an_id()
    {
        Assert.Equal(5, CrateRules.Kinds);
        Assert.Equal(new[] { "repair", "ammo", "armor", "reload", "optics" },
            Enum.GetValues<CrateKind>().Select(CrateRules.Id).ToArray());
    }

    [Fact]
    public void A_take_is_wasted_when_it_did_nothing()
    {
        Assert.True(new CrateTake(CrateKind.Repair, 0, 0, 0).Wasted);
        Assert.True(new CrateTake(CrateKind.Armor, 0, 0, 3).Wasted);
        Assert.False(new CrateTake(CrateKind.Ammo, 0, 3, 0).Wasted);
        Assert.False(new CrateTake(CrateKind.Armor, 1, 0, 1).Wasted);
    }

    [Fact]
    public void The_set_wants_ids_0_to_n_minus_1_in_order_and_at_most_64()
    {
        Crate[] three = Three();
        var set = new CrateSet(three);
        Assert.Equal(3, set.Count);
        Assert.Equal(three, set.All.ToArray());
        Assert.Equal(three[1], set[1]);

        Crate[] skipped = Three();
        skipped[2] = skipped[2] with { Id = 3 };
        Assert.Throws<ArgumentException>(() => new CrateSet(skipped));
        Crate[] swapped = { three[1], three[0], three[2] };
        Assert.Throws<ArgumentException>(() => new CrateSet(swapped));
        Crate[] tooMany = Enumerable.Range(0, CrateRules.MaxCrates + 1).Select(i => new Crate((byte)i, new Vector2(i, 0), CrateKind.Ammo)).ToArray();
        Assert.Throws<ArgumentException>(() => new CrateSet(tooMany));
        Crate[] most = tooMany.Take(CrateRules.MaxCrates).ToArray();
        Assert.Equal(CrateRules.MaxCrates, new CrateSet(most).Count);
        Assert.Equal(ulong.MaxValue, new CrateSet(most).StandingMask);
        Assert.Equal(0, new CrateSet(Array.Empty<Crate>()).Count);
    }

    [Fact]
    public void Taking_a_crate_clears_it_once_names_the_eater_and_bumps_the_version()
    {
        var set = new CrateSet(Three());
        Assert.Equal(3, set.StandingCount);
        Assert.Equal(0b111ul, set.StandingMask);
        Assert.Equal(0, set.Version);
        Assert.True(set.IsStanding(1));
        Assert.Equal(0, set.TakenBy(1));

        Assert.True(set.Take(1, 7));
        Assert.False(set.IsStanding(1));
        Assert.Equal(7, set.TakenBy(1));
        Assert.Equal(2, set.StandingCount);
        Assert.Equal(0b101ul, set.StandingMask);
        Assert.Equal(1, set.Version);

        Assert.False(set.Take(1, 9));   // gone already: nothing changes
        Assert.Equal(7, set.TakenBy(1));
        Assert.Equal(1, set.Version);

        Assert.True(set.Take(0, 2));
        Assert.True(set.Take(2, 2));
        Assert.Equal(0, set.StandingCount);
        Assert.Equal(0ul, set.StandingMask);
        Assert.Equal(3, set.Version);
        Assert.Empty(set.Pickups.ToArray());
    }

    [Theory]
    [InlineData(3)]     // one past the last crate
    [InlineData(64)]    // a shift count C# would mask to 0: must not touch crate 0
    [InlineData(-1)]
    public void Taking_an_id_the_set_has_no_crate_for_does_nothing(int id)
    {
        var set = new CrateSet(Three());
        Assert.False(set.IsStanding(id));
        Assert.False(set.Take(id, 7));
        Assert.Equal(0b111ul, set.StandingMask);
        Assert.True(set.IsStanding(0));
        Assert.Equal(0, set.TakenBy(0));
        Assert.Equal(0, set.Version);
    }

    [Fact]
    public void Asking_who_took_or_for_the_crate_of_an_id_the_set_has_no_crate_for_throws()
    {
        var set = new CrateSet(Three());
        Assert.Throws<ArgumentOutOfRangeException>(() => set.TakenBy(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => set[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => set.TakenBy(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => set[-1]);
        Assert.Equal(2, set[2].Id);
    }

    [Fact]
    public void Collecting_without_a_simulation_throws() =>
        Assert.Throws<ArgumentNullException>(() => new CrateSet(Three()).Collect(null!));
}

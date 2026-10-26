using Tank.Client.UI;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Art.Tests;

public class ModeTextTests
{
    [Fact]
    public void Every_mode_has_a_name_and_a_rule_line()
    {
        foreach (BattleMode mode in ModeRules.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(ModeText.Name(mode)));
            Assert.NotEqual(mode.ToString(), ModeText.Name(mode));
            Assert.False(string.IsNullOrWhiteSpace(ModeText.Rule(mode)));
            Assert.DoesNotContain('\n', ModeText.Rule(mode));
        }
    }

    [Fact]
    public void The_three_modes_are_called_what_the_design_calls_them()
    {
        Assert.Equal("团队对战", ModeText.Name(BattleMode.TeamBattle));
        Assert.Equal("团队歼灭", ModeText.Name(BattleMode.Annihilation));
        Assert.Equal("各自为战", ModeText.Name(BattleMode.FreeForAll));
    }

    [Fact]
    public void The_rule_lines_carry_the_numbers_of_the_mode_rules()
    {
        Assert.Contains("10 秒后复活", ModeText.Rule(BattleMode.TeamBattle));
        Assert.Contains("7 分钟", ModeText.Rule(BattleMode.TeamBattle));
        Assert.Contains("7 分钟", ModeText.Rule(BattleMode.Annihilation));
        Assert.Contains("10 分钟", ModeText.Rule(BattleMode.FreeForAll));
    }

    [Fact]
    public void The_standard_battle_is_called_standard_and_its_rule_names_the_clock_and_the_points()
    {
        Assert.Equal("标准战", ModeText.Name(BattleMode.Standard));
        Assert.Contains("10 分钟", ModeText.Rule(BattleMode.Standard));
        Assert.Contains("100 分", ModeText.Rule(BattleMode.Standard));
    }

    [Fact]
    public void A_mode_from_another_version_has_a_name_and_no_rule()
    {
        var unknown = (BattleMode)200;

        Assert.Equal("200", ModeText.Name(unknown));
        Assert.Equal(string.Empty, ModeText.Rule(unknown));
    }

    [Fact]
    public void Every_catalog_tank_has_a_name_and_an_unknown_index_does_not_throw()
    {
        for (int i = 0; i < TankCatalog.All.Count; i++) Assert.Equal(TankCatalog.All[i].Name, ModeText.TankName(i));

        Assert.Equal("未知车型", ModeText.TankName(TankCatalog.All.Count));
        Assert.Equal("未知车型", ModeText.TankName(-1));
    }
}

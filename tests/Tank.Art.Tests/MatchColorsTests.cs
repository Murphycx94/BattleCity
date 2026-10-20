using Tank.Client.Art;
using Tank.Client.Match;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class MatchColorsTests
{
    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    [InlineData(BattleMode.FreeForAll)]
    public void The_own_tank_is_own_in_every_mode(BattleMode mode) =>
        Assert.Equal(Side.Own, MatchColors.SideOf(mode, ownTeam: 1, team: 1, isOwn: true));

    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    public void Same_team_not_own_is_an_ally_in_a_team_mode(BattleMode mode) =>
        Assert.Equal(Side.Ally, MatchColors.SideOf(mode, ownTeam: 1, team: 1, isOwn: false));

    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    public void The_other_team_is_the_enemy(BattleMode mode) =>
        Assert.Equal(Side.Enemy, MatchColors.SideOf(mode, ownTeam: 0, team: 1, isOwn: false));

    [Fact]
    public void In_a_free_for_all_everyone_else_is_an_enemy_even_with_an_equal_team_number() =>
        Assert.Equal(Side.Enemy, MatchColors.SideOf(BattleMode.FreeForAll, ownTeam: 3, team: 3, isOwn: false));

    [Fact]
    public void Body_colours_follow_the_palette_and_differ_per_side()
    {
        Assert.Equal(Palette.TankBody, MatchColors.Body(Side.Own));
        Assert.Equal(Palette.AllyBody, MatchColors.Body(Side.Ally));
        Assert.Equal(Palette.EnemyBody, MatchColors.Body(Side.Enemy));
        Assert.NotEqual(MatchColors.Body(Side.Own), MatchColors.Body(Side.Ally));
        Assert.NotEqual(MatchColors.Body(Side.Own), MatchColors.Body(Side.Enemy));
        Assert.NotEqual(MatchColors.Body(Side.Ally), MatchColors.Body(Side.Enemy));
    }

    [Fact]
    public void Ui_colours_differ_per_side()
    {
        Assert.NotEqual(MatchColors.Ui(Side.Own), MatchColors.Ui(Side.Ally));
        Assert.NotEqual(MatchColors.Ui(Side.Own), MatchColors.Ui(Side.Enemy));
        Assert.NotEqual(MatchColors.Ui(Side.Ally), MatchColors.Ui(Side.Enemy));
    }

    [Fact]
    public void Paint_puts_the_side_colour_on_the_body_over_the_common_tracks_and_hubs()
    {
        var paint = MatchColors.Paint(Side.Enemy);
        Assert.Equal(Palette.EnemyBody, paint.Body);
        Assert.Equal(Palette.Track, paint.Track);
        Assert.Equal(Palette.TankHub, paint.Hub);
    }
}

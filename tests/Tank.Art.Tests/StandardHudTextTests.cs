using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class StandardHudTextTests
{
    private static PlayerStats[] Roster(params (byte Team, bool Alive)[] seats)
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        for (int i = 0; i < seats.Length; i++) players[i] = new PlayerStats { Active = true, Team = seats[i].Team, Alive = seats[i].Alive };
        return players;
    }

    private static ResultsParts Results(BattleMode mode, byte winnerTeam, MatchEndReason reason, PlateValues plate) =>
        HudText.Results(mode, 0, ownTeam: 0, new ResultsInfo(winnerTeam, MatchRules.NoSlot, 0, false, reason), plate, Roster(),
            new string?[BattleProtocol.MaxPlayers]);

    [Fact]
    public void The_standard_plate_counts_the_living_like_annihilation()
    {
        PlayerStats[] players = Roster((0, true), (0, true), (0, true), (0, false), (1, false), (1, true), (1, false), (1, false));

        PlateValues mine = HudText.Plate(BattleMode.Standard, 0, ownTeam: 0, default, players);
        PlateValues theirs = HudText.Plate(BattleMode.Standard, 4, ownTeam: 1, default, players);

        Assert.Equal(new PlateValues(PlateKind.TeamAlive, 3, 4, 1, 4), mine);
        Assert.Equal(new PlateValues(PlateKind.TeamAlive, 1, 4, 3, 4), theirs);
        PlateParts parts = HudText.Parts(mine);
        Assert.Equal(("我方存活", "3/4", "敌方存活", "1/4"), (parts.LeftCaption, parts.LeftValue, parts.RightCaption, parts.RightValue));
    }

    [Fact]
    public void A_player_who_joined_mid_match_counts_in_the_standard_plate()
    {
        PlayerStats[] players = Roster((0, true), (1, true), (0, true));   // slot 2 came in after the start

        Assert.Equal(new PlateValues(PlateKind.TeamAlive, 2, 2, 1, 1), HudText.Plate(BattleMode.Standard, 0, 0, default, players));
    }

    [Theory]
    [InlineData((byte)0, MatchEndReason.Captured, "胜利", "占领了敌方基地", ResultTone.Win)]
    [InlineData((byte)1, MatchEndReason.Captured, "失败", "我方基地被占领", ResultTone.Lose)]
    [InlineData((byte)0, MatchEndReason.Eliminated, "胜利", "全歼敌人", ResultTone.Win)]
    [InlineData((byte)1, MatchEndReason.Eliminated, "失败", "我方全灭", ResultTone.Lose)]
    [InlineData(MatchRules.NoTeam, MatchEndReason.TimeUp, "平局", "时间到，平局", ResultTone.Neutral)]
    [InlineData(MatchRules.NoTeam, MatchEndReason.None, "平局", "时间到，平局", ResultTone.Neutral)]
    [InlineData(MatchRules.NoTeam, MatchEndReason.Eliminated, "平局", "双方同归于尽", ResultTone.Neutral)]
    [InlineData(MatchRules.NoTeam, MatchEndReason.Captured, "平局", "双方同时占领了对方基地", ResultTone.Neutral)]
    public void Standard_results_name_why_the_match_ended(byte winnerTeam, MatchEndReason reason, string title, string subtitle, ResultTone tone)
    {
        ResultsParts parts = Results(BattleMode.Standard, winnerTeam, reason, new PlateValues(PlateKind.TeamAlive, 2, 4, 1, 4));

        Assert.Equal(new ResultsParts(title, subtitle, tone), parts);
    }

    [Fact]
    public void Standard_results_read_from_the_players_side_on_team_one()
    {
        var info = new ResultsInfo(0, MatchRules.NoSlot, 0, false, MatchEndReason.Captured);

        ResultsParts parts = HudText.Results(BattleMode.Standard, 1, ownTeam: 1, info, default, Roster(), new string?[BattleProtocol.MaxPlayers]);

        Assert.Equal(("失败", "我方基地被占领"), (parts.Title, parts.Subtitle));
    }

    [Fact]
    public void The_other_modes_results_ignore_the_reason()
    {
        var alive = new PlateValues(PlateKind.TeamAlive, 2, 5, 0, 5);
        var timeUp = new PlateValues(PlateKind.TeamAlive, 2, 5, 3, 5);

        Assert.Equal("敌方全军覆没", Results(BattleMode.Annihilation, 0, MatchEndReason.Captured, alive).Subtitle);
        Assert.Equal("时间到，胜负未分", Results(BattleMode.Annihilation, MatchRules.NoTeam, MatchEndReason.Captured, timeUp).Subtitle);
        Assert.Equal("击毁 12 : 9",
            Results(BattleMode.TeamBattle, 0, MatchEndReason.Captured, new PlateValues(PlateKind.TeamKills, 12, 0, 9, 0)).Subtitle);
    }

    [Fact]
    public void A_mode_from_another_version_gets_words_without_throwing()
    {
        var unknown = (BattleMode)200;

        PlateValues plate = HudText.Plate(unknown, 0, 0, default, Roster((0, true)));
        ResultsParts parts = HudText.Results(unknown, 0, 0, new ResultsInfo(MatchRules.NoTeam, MatchRules.NoSlot, 0, false, MatchEndReason.Captured),
            plate, Roster((0, true)), new string?[BattleProtocol.MaxPlayers]);

        Assert.False(string.IsNullOrEmpty(parts.Title));
        Assert.Equal(0, CaptureText.Bars(unknown, 0, new MatchBlock { BaseProgress0 = 50 }).Count);
    }
}

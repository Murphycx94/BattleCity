using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class HudTextTests
{
    private const int Hz = SimConstants.TickRate;

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(1, "0:01")]
    [InlineData(Hz, "0:01")]
    [InlineData(Hz + 1, "0:02")]
    [InlineData(59 * Hz, "0:59")]
    [InlineData(60 * Hz, "1:00")]
    [InlineData(60 * Hz + 1, "1:01")]
    [InlineData(7 * 60 * Hz, "7:00")]
    [InlineData(10 * 60 * Hz, "10:00")]
    [InlineData(-5, "0:00")]
    public void The_clock_rounds_up_to_the_next_second(int ticks, string expected) =>
        Assert.Equal(expected, HudText.Timer(ticks));

    [Theory]
    [InlineData(1, "1 秒后复活")]
    [InlineData(6 * Hz, "6 秒后复活")]
    [InlineData(6 * Hz - 1, "6 秒后复活")]
    [InlineData(10 * Hz, "10 秒后复活")]
    public void The_respawn_line_counts_whole_seconds_rounded_up(int ticks, string expected) =>
        Assert.Equal(expected, HudText.RespawnLine(ticks));

    private static float TenPerChar(string text) => text.Length * 10f;   // a stand-in for the font: every UTF-16 unit 10 wide

    [Theory]
    [InlineData("阿成", 20f, "阿成")]          // fits exactly: unchanged
    [InlineData("abcdefghij", 55f, "abcd…")]   // five units with the ellipsis is the most that fits
    [InlineData("abcdefghij", 10f, "a…")]      // never less than one character
    [InlineData("x", 5f, "x")]                 // nothing to cut a single one down to
    public void Fit_cuts_a_text_to_its_budget_with_an_ellipsis(string text, float budget, string expected) =>
        Assert.Equal(expected, HudText.Fit(text, budget, TenPerChar));

    [Fact]
    public void Fit_returns_a_text_that_fits_as_the_same_instance()
    {
        string name = "Sergeant_Wolfgang_Becker";
        Assert.Same(name, HudText.Fit(name, 1000f, TenPerChar));
    }

    [Fact]
    public void Fit_never_cuts_a_surrogate_pair_in_half()
    {
        // a, b, the rocket's two halves, c, d: "ab🚀…" is 50 wide, so the cut falls before the rocket, not inside it.
        Assert.Equal("ab…", HudText.Fit("ab🚀cd", 45f, TenPerChar));
    }

    [Fact]
    public void The_spectate_line_names_the_target_and_the_keys() =>
        Assert.Equal("观战 机器人 2 · Q/E 切换", HudText.SpectateLine("机器人 2"));

    private static PlayerStats[] Roster(params (byte Team, bool Alive)[] seats)
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        for (int i = 0; i < seats.Length; i++) players[i] = new PlayerStats { Active = true, Team = seats[i].Team, Alive = seats[i].Alive };
        return players;
    }

    [Fact]
    public void Team_battle_shows_own_kills_first_for_team_zero()
    {
        var match = new MatchBlock { TeamScore0 = 4, TeamScore1 = 2 };
        PlateValues plate = HudText.Plate(BattleMode.TeamBattle, 0, ownTeam: 0, match, Roster());
        Assert.Equal(new PlateValues(PlateKind.TeamKills, 4, 0, 2, 0), plate);
    }

    [Fact]
    public void Team_battle_flips_the_scores_for_a_player_on_team_one()
    {
        var match = new MatchBlock { TeamScore0 = 4, TeamScore1 = 2 };
        PlateValues plate = HudText.Plate(BattleMode.TeamBattle, 3, ownTeam: 1, match, Roster());
        Assert.Equal(2, plate.Left);
        Assert.Equal(4, plate.Right);
    }

    [Fact]
    public void Annihilation_counts_the_living_of_each_side_out_of_those_who_started()
    {
        PlayerStats[] players = Roster((0, true), (0, true), (0, false), (1, true), (1, false), (1, false), (1, false));
        players[7] = new PlayerStats { Active = false, Team = 1, Alive = true };   // someone who left counts for nothing

        PlateValues plate = HudText.Plate(BattleMode.Annihilation, 0, ownTeam: 0, default, players);
        PlateValues flipped = HudText.Plate(BattleMode.Annihilation, 3, ownTeam: 1, default, players);

        Assert.Equal(new PlateValues(PlateKind.TeamAlive, 2, 3, 1, 4), plate);
        Assert.Equal(new PlateValues(PlateKind.TeamAlive, 1, 4, 2, 3), flipped);
    }

    [Fact]
    public void Free_for_all_shows_the_players_own_kills_and_the_field_left()
    {
        PlayerStats[] players = Roster((0, true), (1, true), (2, false), (3, true));
        players[0].Kills = 3;
        players[1].Kills = 9;

        PlateValues plate = HudText.Plate(BattleMode.FreeForAll, ownSlot: 0, ownTeam: 0, default, players);

        Assert.Equal(new PlateValues(PlateKind.SoloKills, 3, 0, 3, 4), plate);
    }

    [Fact]
    public void The_plate_words_follow_the_mode()
    {
        PlateParts team = HudText.Parts(new PlateValues(PlateKind.TeamKills, 4, 0, 2, 0));
        PlateParts alive = HudText.Parts(new PlateValues(PlateKind.TeamAlive, 3, 5, 2, 5));
        PlateParts solo = HudText.Parts(new PlateValues(PlateKind.SoloKills, 3, 0, 4, 8));

        Assert.Equal(("我方", "4", "敌方", "2"), (team.LeftCaption, team.LeftValue, team.RightCaption, team.RightValue));
        Assert.Equal(("我方存活", "3/5", "敌方存活", "2/5"), (alive.LeftCaption, alive.LeftValue, alive.RightCaption, alive.RightValue));
        Assert.Equal(("击毁", "3", "存活", "4/8"), (solo.LeftCaption, solo.LeftValue, solo.RightCaption, solo.RightValue));
    }

    [Fact]
    public void The_player_slot_out_of_range_shows_no_kills_instead_of_throwing()
    {
        PlateValues plate = HudText.Plate(BattleMode.FreeForAll, ownSlot: MatchRules.NoSlot, ownTeam: 0, default, Roster((0, true)));
        Assert.Equal(0, plate.Left);
    }
    private static ResultsInfo Verdict(byte winnerTeam = MatchRules.NoTeam, byte winnerSlot = MatchRules.NoSlot, int ticksLeft = 0, bool isHost = false) =>
        new(winnerTeam, winnerSlot, ticksLeft, isHost);

    private static ResultsParts TeamResults(BattleMode mode, byte ownTeam, byte winnerTeam, PlateValues plate) =>
        HudText.Results(mode, 0, ownTeam, Verdict(winnerTeam), plate, Roster(), new string?[BattleProtocol.MaxPlayers]);

    [Theory]
    [InlineData(0, 0, "胜利", ResultTone.Win)]
    [InlineData(0, 1, "失败", ResultTone.Lose)]
    [InlineData(1, 1, "胜利", ResultTone.Win)]
    [InlineData(1, 0, "失败", ResultTone.Lose)]
    [InlineData(0, MatchRules.NoTeam, "平局", ResultTone.Neutral)]
    [InlineData(1, MatchRules.NoTeam, "平局", ResultTone.Neutral)]
    public void The_team_results_title_is_win_lose_or_draw_from_the_own_side(byte ownTeam, byte winnerTeam, string title, ResultTone tone)
    {
        ResultsParts parts = TeamResults(BattleMode.TeamBattle, ownTeam, winnerTeam, new PlateValues(PlateKind.TeamKills, 5, 0, 5, 0));

        Assert.Equal(title, parts.Title);
        Assert.Equal(tone, parts.Tone);
    }

    [Fact]
    public void Team_battle_results_show_the_kills_own_team_first()
    {
        ResultsParts parts = TeamResults(BattleMode.TeamBattle, 0, 0, new PlateValues(PlateKind.TeamKills, 12, 0, 9, 0));

        Assert.Equal("击毁 12 : 9", parts.Subtitle);
    }

    [Fact]
    public void Annihilation_results_say_who_was_wiped_out()
    {
        var alive = new PlateValues(PlateKind.TeamAlive, 2, 5, 0, 5);
        var wiped = new PlateValues(PlateKind.TeamAlive, 0, 5, 3, 5);
        var both = new PlateValues(PlateKind.TeamAlive, 0, 5, 0, 5);
        var timeUp = new PlateValues(PlateKind.TeamAlive, 2, 5, 3, 5);

        Assert.Equal("敌方全军覆没", TeamResults(BattleMode.Annihilation, 0, 0, alive).Subtitle);
        Assert.Equal("我方全军覆没", TeamResults(BattleMode.Annihilation, 0, 1, wiped).Subtitle);
        Assert.Equal("双方同归于尽", TeamResults(BattleMode.Annihilation, 0, MatchRules.NoTeam, both).Subtitle);
        Assert.Equal("时间到，胜负未分", TeamResults(BattleMode.Annihilation, 0, MatchRules.NoTeam, timeUp).Subtitle);
    }

    private static PlayerStats[] Ranked(byte ownRank, byte winner, bool winnerAlive, int alive = 1)
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        for (int i = 0; i < 6; i++) players[i] = new PlayerStats { Active = true, Team = (byte)i, Alive = i < alive, Rank = (byte)(i + 1) };
        players[0].Rank = ownRank;
        players[winner] = players[winner] with { Alive = winnerAlive, Rank = 1 };
        return players;
    }

    [Fact]
    public void Free_for_all_results_give_the_own_place_and_name_the_survivor()
    {
        var names = new string?[BattleProtocol.MaxPlayers];
        names[4] = "机器人 2";
        PlayerStats[] players = Ranked(ownRank: 3, winner: 4, winnerAlive: true, alive: 0);
        players[4].Alive = true;

        ResultsParts parts = HudText.Results(BattleMode.FreeForAll, 0, 0, Verdict(winnerSlot: 4), default, players, names);

        Assert.Equal("第 3 名", parts.Title);
        Assert.Equal("机器人 2 是最后的幸存者", parts.Subtitle);
        Assert.Equal(ResultTone.Neutral, parts.Tone);
    }

    [Fact]
    public void Coming_first_in_a_free_for_all_is_a_win()
    {
        PlayerStats[] players = Ranked(ownRank: 1, winner: 0, winnerAlive: true, alive: 1);
        var names = new string?[BattleProtocol.MaxPlayers];

        ResultsParts parts = HudText.Results(BattleMode.FreeForAll, 0, 0, Verdict(winnerSlot: 0), default, players, names);

        Assert.Equal("第 1 名", parts.Title);
        Assert.Equal(ResultTone.Win, parts.Tone);
        Assert.Equal("玩家 1 是最后的幸存者", parts.Subtitle);   // no name in the room: the slot's number
    }

    [Fact]
    public void A_free_for_all_nobody_survived_says_so()
    {
        PlayerStats[] players = Ranked(ownRank: 2, winner: 3, winnerAlive: false, alive: 0);
        var names = new string?[BattleProtocol.MaxPlayers];

        Assert.Equal("没有人活到最后", HudText.Results(BattleMode.FreeForAll, 0, 0, Verdict(winnerSlot: 3), default, players, names).Subtitle);
        Assert.Equal("没有人活到最后", HudText.Results(BattleMode.FreeForAll, 0, 0, Verdict(winnerSlot: MatchRules.NoSlot), default, players, names).Subtitle);
    }

    [Fact]
    public void A_free_for_all_that_ran_out_of_time_names_the_health_leader_not_a_survivor()
    {
        PlayerStats[] players = Ranked(ownRank: 2, winner: 3, winnerAlive: true, alive: 3);
        var names = new string?[BattleProtocol.MaxPlayers];
        names[3] = "小雨";

        Assert.Equal("时间到，小雨 血量领先", HudText.Results(BattleMode.FreeForAll, 0, 0, Verdict(winnerSlot: 3), default, players, names).Subtitle);
    }

    [Fact]
    public void A_player_without_a_rank_just_sees_the_match_is_over()
    {
        PlayerStats[] players = Ranked(ownRank: 0, winner: 4, winnerAlive: true);

        Assert.Equal("对局结束", HudText.Results(BattleMode.FreeForAll, 0, 0, Verdict(winnerSlot: 4), default, players, new string?[BattleProtocol.MaxPlayers]).Title);
    }

    private static PlayerStats[] RoyaleField(byte ownRank, int alive = 1)
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        for (int i = 0; i < players.Length; i++) players[i] = new PlayerStats { Active = true, Team = (byte)i, Alive = i < alive, Rank = (byte)(i + 1) };
        players[0].Rank = ownRank;
        return players;
    }

    private static ResultsParts RoyaleResults(byte ownRank, int entrants = 16) =>
        HudText.Results(BattleMode.Royale, 0, 0, new ResultsInfo(MatchRules.NoTeam, 0, 0, false, MatchEndReason.Eliminated, entrants), default, RoyaleField(ownRank),
            new string?[BattleProtocol.MaxPlayers]);

    [Fact]
    public void Coming_first_in_a_royale_is_the_chicken_dinner()
    {
        ResultsParts parts = RoyaleResults(ownRank: 1);

        Assert.Equal(("吃鸡！", "共 16 名", ResultTone.Win), (parts.Title, parts.Subtitle, parts.Tone));
        Assert.Equal(HudText.RoyaleWinTitle, parts.Title);
    }

    [Fact]
    public void Any_other_place_in_a_royale_is_the_place_of_the_entrants()
    {
        ResultsParts parts = RoyaleResults(ownRank: 7);

        Assert.Equal(("第 7 名", "共 16 名", ResultTone.Neutral), (parts.Title, parts.Subtitle, parts.Tone));
        Assert.Equal("共 4 名", RoyaleResults(ownRank: 2, entrants: 4).Subtitle);
        Assert.Equal(("第 7 名", ""), (RoyaleResults(ownRank: 7, entrants: 0).Title, RoyaleResults(ownRank: 7, entrants: 0).Subtitle));   // no count rather than "共 0 名"
    }

    [Fact]
    public void No_rank_in_a_royale_just_says_the_match_is_over()
    {
        ResultsParts parts = RoyaleResults(ownRank: 0);

        Assert.Equal(("对局结束", "共 16 名", ResultTone.Neutral), (parts.Title, parts.Subtitle, parts.Tone));
    }

    [Fact]
    public void A_royale_plate_is_the_own_kills_and_the_field_left_like_a_free_for_all()
    {
        PlayerStats[] players = RoyaleField(ownRank: 0, alive: 12);
        for (int i = 0; i < players.Length; i++) players[i].Rank = 0;
        players[0].Kills = 3;

        PlateValues plate = HudText.Plate(BattleMode.Royale, ownSlot: 0, ownTeam: 0, default, players);
        PlateParts parts = HudText.Parts(plate);

        Assert.Equal(new PlateValues(PlateKind.SoloKills, 3, 0, 12, 16), plate);
        Assert.Equal(("击毁", "3", "存活", "12/16"), (parts.LeftCaption, parts.LeftValue, parts.RightCaption, parts.RightValue));
    }

    [Theory]
    [InlineData(8 * Hz, false, "8 秒后返回房间")]
    [InlineData(8 * Hz - 1, false, "8 秒后返回房间")]
    [InlineData(1, false, "1 秒后返回房间")]
    [InlineData(0, false, "0 秒后返回房间")]
    [InlineData(5 * Hz, true, "5 秒后返回房间 · Enter 立即返回")]
    public void The_footer_counts_down_and_tells_the_host_about_enter(int ticks, bool host, string expected) =>
        Assert.Equal(expected, HudText.ReturnLine(ticks, host));

    [Fact]
    public void The_results_clock_counts_the_servers_results_time_from_the_frame_the_match_ended()
    {
        var clock = new ResultsClock();

        Assert.Equal(0, clock.Observe(MatchPhase.Playing, 1.0));
        Assert.Equal(ModeRules.ResultsTicks - 3, clock.Observe(MatchPhase.Ended, 0.1));
        Assert.Equal(ModeRules.ResultsTicks - 15, clock.Observe(MatchPhase.Ended, 0.4));
        Assert.Equal(0, clock.Observe(MatchPhase.Ended, 30));
        Assert.Equal(0, clock.Observe(MatchPhase.Playing, 1.0));   // a new match starts over
        Assert.Equal(ModeRules.ResultsTicks, clock.Observe(MatchPhase.Ended, 0.0));
    }
}

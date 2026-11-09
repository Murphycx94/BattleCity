using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

/// <summary>The duos side of the match HUD's logic (stage 6 design §5.2): the ally colours, the board grouped by team, the plate, the results.</summary>
public class DuoHudTests
{
    // --- colours ---

    [Fact]
    public void In_duos_the_own_team_is_an_ally_and_every_other_team_an_enemy()
    {
        Assert.Equal(Side.Ally, MatchColors.SideOf(BattleMode.Duos, ownTeam: 3, team: 3, isOwn: false));
        Assert.Equal(Side.Enemy, MatchColors.SideOf(BattleMode.Duos, ownTeam: 3, team: 4, isOwn: false));
        Assert.Equal(Side.Own, MatchColors.SideOf(BattleMode.Duos, ownTeam: 3, team: 3, isOwn: true));
    }

    [Fact]
    public void A_royale_has_no_allies_even_with_an_equal_team_number_and_the_team_modes_are_as_before()
    {
        Assert.Equal(Side.Enemy, MatchColors.SideOf(BattleMode.Royale, ownTeam: 3, team: 3, isOwn: false));
        Assert.Equal(Side.Enemy, MatchColors.SideOf(BattleMode.FreeForAll, ownTeam: 3, team: 3, isOwn: false));
        Assert.Equal(Side.Ally, MatchColors.SideOf(BattleMode.TeamBattle, ownTeam: 1, team: 1, isOwn: false));
        Assert.Equal(Side.Enemy, MatchColors.SideOf(BattleMode.TeamBattle, ownTeam: 0, team: 1, isOwn: false));
        Assert.Equal(Side.Ally, MatchColors.SideOf(BattleMode.Standard, ownTeam: 0, team: 0, isOwn: false));
    }

    // --- the scoreboard ---

    /// <summary>Team of each slot as a duos room seats four people and twelve bots: the people on teams 0–3, the bots fill those, then pair up.</summary>
    private static readonly byte[] Teams = { 0, 1, 2, 3, 0, 1, 2, 3, 4, 4, 5, 5, 6, 6, 7, 7 };

    private static string? NameOf(byte slot) => null;

    private static PlayerStats[] Field(Func<int, int> kills, Func<int, bool> alive, Func<int, byte>? rank = null)
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        for (int i = 0; i < Teams.Length; i++)
            players[i] = new PlayerStats { Active = true, Team = Teams[i], TankId = i + 1, Kills = kills(i), Deaths = alive(i) ? 0 : 1, Alive = alive(i), Rank = rank?.Invoke(i) ?? 0 };
        return players;
    }

    /// <summary>
    /// Team kills: 0 → 1+2 = 3, 1 → 0, 2 → 4, 3 → 3+0, 4 → 0, 5 → 2, 6 → 1, 7 → 0. Out: team 3 (slots 3, 7) and team 6
    /// (12, 13); half out: team 4 (slot 9) and team 0 (slot 4, the player's team-mate).
    /// </summary>
    private static readonly int[] RunningKills = { 1, 0, 4, 3, 2, 0, 0, 0, 0, 0, 2, 0, 1, 0, 0, 0 };

    private static bool RunningAlive(int slot) => slot is not (3 or 7 or 12 or 13 or 9 or 4);

    [Fact]
    public void While_duos_run_the_board_groups_by_team_the_living_teams_first_then_team_kills_then_team_number()
    {
        var rows = ScoreboardModel.Build(BattleMode.Duos, 0, 0, Field(i => RunningKills[i], RunningAlive), NameOf, ended: false, 0);

        Assert.Equal(16, rows.Count);
        // Living teams by kills: 2 (4), 0 (3), 5 (2), then 1, 4 and 7 on 0 by team number; the teams out: 3 (3 kills), 6 (1).
        Assert.Equal(new byte[] { 2, 2, 0, 0, 5, 5, 1, 1, 4, 4, 7, 7, 3, 3, 6, 6 }, rows.Select(r => r.Team));
        Assert.Equal(new byte[] { 2, 6, 0, 4, 10, 11, 1, 5, 8, 9, 14, 15, 3, 7, 12, 13 }, rows.Select(r => r.Slot));   // by slot within a team
        Assert.Equal(new[] { 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8 }, rows.Select(r => r.Rank));   // the group's place, both alike
    }

    [Fact]
    public void Once_duos_have_ended_the_board_follows_the_servers_team_rank()
    {
        byte[] teamRank = { 3, 4, 1, 7, 5, 2, 8, 6 };
        PlayerStats[] players = Field(i => RunningKills[i], i => Teams[i] == 2, i => teamRank[Teams[i]]);

        var rows = ScoreboardModel.Build(BattleMode.Duos, 0, 0, players, NameOf, ended: true, 0);

        Assert.Equal(new byte[] { 2, 2, 5, 5, 0, 0, 1, 1, 4, 4, 7, 7, 3, 3, 6, 6 }, rows.Select(r => r.Team));
        Assert.Equal(new byte[] { 2, 6, 10, 11, 0, 4, 1, 5, 8, 9, 14, 15, 3, 7, 12, 13 }, rows.Select(r => r.Slot));
        Assert.Equal(new[] { 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8 }, rows.Select(r => r.Rank));
    }

    [Fact]
    public void The_board_marks_the_player_the_team_mate_and_the_rest()
    {
        var rows = ScoreboardModel.Build(BattleMode.Duos, 0, 0, Field(i => RunningKills[i], RunningAlive), NameOf, ended: false, 0);

        Assert.Equal(Side.Own, rows.Single(r => r.Slot == 0).Side);
        Assert.Equal(Side.Ally, rows.Single(r => r.Slot == 4).Side);
        Assert.All(rows.Where(r => r.Team != 0), r => Assert.Equal(Side.Enemy, r.Side));
    }

    [Fact]
    public void While_duos_run_a_team_mates_damage_shows_and_an_enemys_stays_blank()
    {
        PlayerStats[] players = Field(i => RunningKills[i], RunningAlive);
        players[4].Damage = 900;   // the team-mate: the server sends the viewer's own team's damage
        players[2].Damage = 0;     // an enemy: sent as 0 until the end

        var rows = ScoreboardModel.Build(BattleMode.Duos, 0, 0, players, NameOf, ended: false, 0);

        Assert.Equal("900", ScoreboardModel.DamageCell(rows.Single(r => r.Slot == 4), ended: false));
        Assert.Equal("", ScoreboardModel.DamageCell(rows.Single(r => r.Slot == 2), ended: false));

        PlayerStats[] royale = Field(_ => 0, _ => true);
        for (int i = 0; i < Teams.Length; i++) royale[i].Team = (byte)i;   // a royale: each seat its own team
        royale[4].Damage = 900;
        var royaleRows = ScoreboardModel.Build(BattleMode.Royale, 0, 0, royale, NameOf, ended: false, 0);
        Assert.Equal("", ScoreboardModel.DamageCell(royaleRows.Single(r => r.Slot == 4), ended: false));   // a royale has no team-mates
    }

    [Fact]
    public void While_duos_run_a_team_with_a_live_tank_comes_before_a_team_out_whatever_their_kills()
    {
        // Team 0 (slots 0, 4) out with 5 kills; team 1 (slots 1, 5) alive with none.
        PlayerStats[] players = Pairs((0, 5, false), (4, 0, false), (1, 0, true), (5, 0, true));

        var rows = ScoreboardModel.Build(BattleMode.Duos, 1, 1, players, NameOf, ended: false, 0);

        Assert.Equal(new byte[] { 1, 1, 0, 0 }, rows.Select(r => r.Team));
    }

    [Fact]
    public void While_duos_run_of_two_live_teams_the_one_with_more_kills_comes_first()
    {
        // Team 0 alive with 1 kill, team 1 alive with 3.
        PlayerStats[] players = Pairs((0, 1, true), (4, 0, false), (1, 2, false), (5, 1, true));

        var rows = ScoreboardModel.Build(BattleMode.Duos, 0, 0, players, NameOf, ended: false, 0);

        Assert.Equal(new byte[] { 1, 1, 0, 0 }, rows.Select(r => r.Team));
    }

    /// <summary>The given seats (slot, kills, alive) active, each on its team in <see cref="Teams"/>; every other slot empty.</summary>
    private static PlayerStats[] Pairs(params (int Slot, int Kills, bool Alive)[] seats)
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        foreach ((int slot, int kills, bool alive) in seats)
            players[slot] = new PlayerStats { Active = true, Team = Teams[slot], TankId = slot + 1, Kills = kills, Deaths = alive ? 0 : 1, Alive = alive };
        return players;
    }

    [Fact]
    public void Once_duos_have_ended_the_servers_team_ranks_stand_as_sent_even_with_gaps_and_a_team_never_ranked_comes_last()
    {
        byte[] teamRank = { 5, 2, 9, 0, 3, 12, 7, 4 };   // team 3 was never ranked
        PlayerStats[] players = Field(i => RunningKills[i], i => Teams[i] == 1, i => teamRank[Teams[i]]);

        var rows = ScoreboardModel.Build(BattleMode.Duos, 0, 0, players, NameOf, ended: true, 0);

        Assert.Equal(new byte[] { 1, 1, 4, 4, 7, 7, 0, 0, 6, 6, 2, 2, 5, 5, 3, 3 }, rows.Select(r => r.Team));
        Assert.Equal(new[] { 2, 2, 3, 3, 4, 4, 5, 5, 7, 7, 9, 9, 12, 12, 8, 8 }, rows.Select(r => r.Rank));   // the unranked team: its group's place
    }

    [Theory]
    [InlineData(12)]   // a wire team past the eight (4 bits on the wire)
    [InlineData(33)]   // past a 32-bit mask too: a shift by it would wrap onto team 1's bit (alive here)
    public void A_seat_on_a_team_past_the_eight_does_not_throw_and_counts_as_no_team(int stray)
    {
        // Team 0 (slots 0, 4) out with no kills; team 1 (slots 1, 5) alive with none; the stray seat alive with 5 kills.
        PlayerStats[] players = Pairs((0, 0, false), (4, 0, false), (1, 0, true), (5, 0, true));
        players[8] = new PlayerStats { Active = true, Team = (byte)stray, TankId = 9, Kills = 5, Alive = true };

        var rows = ScoreboardModel.Build(BattleMode.Duos, 0, 0, players, NameOf, ended: false, 0);
        PlateValues plate = HudText.Plate(BattleMode.Duos, 0, 0, new MatchBlock { Entrants = 2 }, players);

        Assert.Equal(new byte[] { 1, 1, 0, 0, (byte)stray }, rows.Select(r => r.Team));   // neither live nor on kills: after every real team
        Assert.Equal(1, plate.Right);   // teams left: team 1 only
        Assert.Equal(0, plate.Left);    // the stray seat's kills are no team's
    }

    // --- the rule between groups ---

    [Fact]
    public void A_row_starts_a_group_in_duos_where_the_team_changes_and_never_first_or_in_another_mode()
    {
        static ScoreRow Row(byte slot, byte team) => new(slot, "", team, "", 0, 0, true, 1, Side.Enemy, "");
        ScoreRow[] rows = { Row(0, 2), Row(4, 2), Row(1, 5), Row(5, 5) };

        Assert.Equal(new[] { false, false, true, false }, Enumerable.Range(0, rows.Length).Select(i => ScoreboardModel.StartsGroup(BattleMode.Duos, rows, i)));
        foreach (BattleMode mode in ModeRules.All.Where(m => m != BattleMode.Duos))
            Assert.All(Enumerable.Range(0, rows.Length), i => Assert.False(ScoreboardModel.StartsGroup(mode, rows, i)));
    }

    [Fact]
    public void Building_a_duos_board_into_a_buffer_allocates_nothing_once_warm()
    {
        Allocation.AssertNone(() =>
        {
            PlayerStats[] players = Field(i => RunningKills[i], RunningAlive);
            var buffer = new ScoreRow[BattleProtocol.MaxPlayers];
            Func<byte, string?> nameOf = NameOf;
            for (int i = 0; i < 10; i++) ScoreboardModel.Build(BattleMode.Duos, 0, 0, players, nameOf, false, 0, buffer);

            Allocation.Window window = Allocation.Start();
            int count = 0;
            for (int i = 0; i < 100; i++) count = ScoreboardModel.Build(BattleMode.Duos, 0, 0, players, nameOf, false, 0, buffer);
            Allocation.Measured measured = window.Stop();

            Assert.Equal(16, count);
            return measured;
        });
    }

    // --- the plate ---

    [Fact]
    public void The_duos_plate_shows_the_team_kills_and_the_teams_left_of_those_that_started()
    {
        // Own team 0: the player 2 kills, the team-mate (slot 4) 1. Alive: teams 0, 2 and 5; the rest out.
        int[] kills = { 2, 0, 1, 3, 1, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0 };
        PlayerStats[] players = Field(i => kills[i], i => Teams[i] is 0 or 2 or 5 && i != 4);
        var match = new MatchBlock { Entrants = 8 };

        PlateValues plate = HudText.Plate(BattleMode.Duos, ownSlot: 0, ownTeam: 0, match, players);
        PlateParts parts = HudText.Parts(plate);

        Assert.Equal(new PlateValues(PlateKind.DuoTeams, 3, 0, 3, 8), plate);
        Assert.Equal(new PlateParts("本队击毁", "3", "存活队伍", "3/8"), parts);
    }

    [Fact]
    public void Working_out_the_duos_plate_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            PlayerStats[] players = Field(i => RunningKills[i], RunningAlive);
            var match = new MatchBlock { Entrants = 8 };
            HudText.Plate(BattleMode.Duos, 0, 0, match, players);

            Allocation.Window window = Allocation.Start();
            PlateValues plate = default;
            for (int i = 0; i < 100; i++) plate = HudText.Plate(BattleMode.Duos, 0, 0, match, players);
            Allocation.Measured measured = window.Stop();

            Assert.Equal(new PlateValues(PlateKind.DuoTeams, 3, 0, 6, 8), plate);
            return measured;
        });
    }

    // --- the results ---

    [Theory]
    [InlineData(1, "吃鸡！", ResultTone.Win)]
    [InlineData(3, "第 3 名", ResultTone.Neutral)]
    public void The_duos_results_give_the_teams_place_of_the_teams_that_started(byte rank, string title, ResultTone tone)
    {
        PlayerStats[] players = Field(_ => 0, i => i == 0 && rank == 1, i => Teams[i] == 0 ? rank : (byte)0);
        var info = new ResultsInfo(0, MatchRules.NoSlot, 100, false, MatchEndReason.Eliminated, Entrants: 8);
        var plate = HudText.Plate(BattleMode.Duos, 0, 0, new MatchBlock { Entrants = 8 }, players);

        ResultsParts words = HudText.Results(BattleMode.Duos, 0, 0, info, plate, players, new string?[BattleProtocol.MaxPlayers]);

        Assert.Equal(new ResultsParts(title, "共 8 名", tone), words);
    }

    [Fact]
    public void The_duos_results_without_a_place_say_the_match_is_over_and_without_entrants_give_no_count()
    {
        PlayerStats[] players = Field(_ => 0, _ => false);   // no ranks at all
        var plate = HudText.Plate(BattleMode.Duos, 0, 0, new MatchBlock { Entrants = 8 }, players);
        var names = new string?[BattleProtocol.MaxPlayers];

        ResultsParts unranked = HudText.Results(BattleMode.Duos, 0, 0, new ResultsInfo(1, MatchRules.NoSlot, 100, false, MatchEndReason.Eliminated, Entrants: 8), plate, players, names);
        ResultsParts noEntrants = HudText.Results(BattleMode.Duos, 0, 0, new ResultsInfo(1, MatchRules.NoSlot, 100, false, MatchEndReason.Eliminated, Entrants: 0), plate, players, names);

        Assert.Equal(new ResultsParts("对局结束", "共 8 名", ResultTone.Neutral), unranked);
        Assert.Equal(new ResultsParts("对局结束", "", ResultTone.Neutral), noEntrants);   // never "共 0 名"
    }
}

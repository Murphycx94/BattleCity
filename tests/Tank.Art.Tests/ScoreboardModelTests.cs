using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class ScoreboardModelTests
{
    private static readonly string?[] Names = { "阿成", "小林", "老周", "小雨", "机器人 1", "机器人 2", "机器人 3", "机器人 4", null, null };

    private static string? NameOf(byte slot) => slot < Names.Length ? Names[slot] : null;   // a royale's seats past the tenth get "玩家 N"

    private static PlayerStats[] Field(params (byte Team, int Kills, int Deaths, bool Alive)[] seats)
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        for (int i = 0; i < seats.Length; i++)
            players[i] = new PlayerStats { Active = true, Team = seats[i].Team, TankId = i + 1, Kills = seats[i].Kills, Deaths = seats[i].Deaths, Alive = seats[i].Alive };
        return players;
    }

    private static IReadOnlyList<ScoreRow> Team(PlayerStats[] players, byte ownSlot = 0, byte ownTeam = 0, int respawnIn = 0) =>
        ScoreboardModel.Build(BattleMode.TeamBattle, ownSlot, ownTeam, players, NameOf, ended: false, respawnIn);

    [Fact]
    public void Standard_rows_carry_each_players_capture()
    {
        PlayerStats[] players = Field((0, 1, 0, true), (1, 0, 0, true));
        players[0].Capture = 41;

        var rows = ScoreboardModel.Build(BattleMode.Standard, 0, 0, players, NameOf, ended: false, 0);

        Assert.Equal(new[] { 41, 0 }, rows.Select(r => r.Capture));
        Assert.Equal(new byte[] { 0, 1 }, rows.Select(r => r.Slot));   // ordered like the team modes
    }

    [Fact]
    public void The_capture_cell_leaves_enemies_blank_until_the_match_has_ended()
    {
        var own = new ScoreRow(0, "阿成", 0, "中坦", 0, 0, true, 1, Side.Own, "存活", Capture: 41);
        var ally = own with { Slot = 1, Side = Side.Ally, Capture = 0 };
        var enemy = own with { Slot = 2, Team = 1, Side = Side.Enemy, Capture = 0 };

        Assert.Equal(("41", "0", ""), (ScoreboardModel.CaptureCell(own, ended: false), ScoreboardModel.CaptureCell(ally, false),
            ScoreboardModel.CaptureCell(enemy, false)));
        Assert.Equal(("41", "0", "12"), (ScoreboardModel.CaptureCell(own, ended: true), ScoreboardModel.CaptureCell(ally, true),
            ScoreboardModel.CaptureCell(enemy with { Capture = 12 }, true)));
        Assert.Equal("100", ScoreboardModel.CaptureCell(own with { Capture = 100 }, false));
        Assert.Same(ScoreboardModel.CaptureCell(own, false), ScoreboardModel.CaptureCell(own, false));   // no string per frame
    }

    /// <summary>Sixteen royale seats, each its own team: slot i has i kills and 100 i damage; the first <paramref name="alive"/> are standing.</summary>
    private static PlayerStats[] RoyaleField(int alive, bool ended = false)
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = new PlayerStats { Active = true, Team = (byte)i, TankId = i + 1, Kills = i, Alive = i < alive, Damage = ended || i == 0 ? 100 * i : 0, Lasted = ended ? 600 - 30 * i : 0 };
            if (ended) players[i].Rank = (byte)(BattleProtocol.MaxPlayers - i);   // slot 15 first, slot 0 last
        }
        return players;
    }

    [Fact]
    public void Royale_rows_carry_damage_and_time_lasted_and_sixteen_fit()
    {
        var rows = ScoreboardModel.Build(BattleMode.Royale, 0, 0, RoyaleField(alive: 12, ended: true), NameOf, ended: true, 0);

        Assert.Equal(16, rows.Count);
        Assert.Equal(Enumerable.Range(1, 16), rows.Select(r => r.Rank));                      // the server's ranks, in order
        Assert.Equal(new byte[] { 15, 14, 13, 12 }, rows.Take(4).Select(r => r.Slot));
        Assert.Equal(1500, rows[0].Damage);
        Assert.Equal(600 - 30 * 15, rows[0].Lasted);
        Assert.Equal(0, rows[15].Damage);
        Assert.Single(rows, r => r.Side == Side.Own);
        Assert.All(rows.Where(r => r.Side != Side.Own), r => Assert.Equal(Side.Enemy, r.Side));
    }

    [Fact]
    public void A_royale_while_it_runs_puts_the_living_first_like_a_free_for_all()
    {
        var rows = ScoreboardModel.Build(BattleMode.Royale, 0, 0, RoyaleField(alive: 12), NameOf, ended: false, 0);

        Assert.Equal(16, rows.Count);
        Assert.True(rows.Take(12).All(r => r.Alive) && rows.Skip(12).All(r => !r.Alive));
        Assert.Equal(11, rows[0].Slot);   // the most kills among the living
        Assert.Equal(Enumerable.Range(1, 16), rows.Select(r => r.Rank));
    }

    [Fact]
    public void The_damage_cell_leaves_an_enemys_blank_until_the_match_has_ended()
    {
        var own = new ScoreRow(0, "阿成", 0, "中坦", 3, 0, true, 1, Side.Own, "存活", Damage: 1240, Lasted: 552);
        var enemy = own with { Slot = 1, Team = 1, Side = Side.Enemy, Damage = 0 };
        var ally = own with { Slot = 2, Side = Side.Ally, Damage = 0 };

        Assert.Equal(("1240", "", "0"), (ScoreboardModel.DamageCell(own, ended: false), ScoreboardModel.DamageCell(enemy, false), ScoreboardModel.DamageCell(ally, false)));
        Assert.Equal(("1240", "0", "870"), (ScoreboardModel.DamageCell(own, ended: true), ScoreboardModel.DamageCell(enemy, true), ScoreboardModel.DamageCell(ally with { Damage = 870 }, true)));
        Assert.Equal("0", ScoreboardModel.DamageCell(own with { Damage = 0 }, false));
    }

    [Fact]
    public void Every_damage_cell_is_a_number_once_a_royale_has_ended_and_only_the_own_one_before()
    {
        var running = ScoreboardModel.Build(BattleMode.Royale, 0, 0, RoyaleField(alive: 12), NameOf, ended: false, 0);
        var over = ScoreboardModel.Build(BattleMode.Royale, 0, 0, RoyaleField(alive: 1, ended: true), NameOf, ended: true, 0);

        Assert.Equal("0", ScoreboardModel.DamageCell(running.Single(r => r.Side == Side.Own), ended: false));
        Assert.All(running.Where(r => r.Side != Side.Own), r => Assert.Equal("", ScoreboardModel.DamageCell(r, ended: false)));
        Assert.All(over, r => Assert.Equal(r.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture), ScoreboardModel.DamageCell(r, ended: true)));
    }

    [Theory]
    [InlineData(552, "9:12")]
    [InlineData(48, "0:48")]
    [InlineData(0, "0:00")]
    [InlineData(600, "10:00")]
    public void The_lasted_cell_is_the_seconds_as_a_clock(int lasted, string expected)
    {
        var row = new ScoreRow(0, "阿成", 0, "中坦", 0, 0, false, 1, Side.Own, "已毁", Lasted: lasted);
        Assert.Equal(expected, ScoreboardModel.LastedCell(row));
    }

    [Fact]
    public void Team_modes_list_the_own_team_first_then_the_other()
    {
        PlayerStats[] players = Field((1, 9, 0, true), (0, 1, 0, true), (1, 2, 0, true), (0, 0, 0, true));

        var rows = Team(players, ownSlot: 1, ownTeam: 0);

        Assert.Equal(new byte[] { 1, 3, 0, 2 }, rows.Select(r => r.Slot));
        Assert.Equal(new byte[] { 0, 0, 1, 1 }, rows.Select(r => r.Team));
    }

    [Fact]
    public void A_player_on_team_one_sees_team_one_first()
    {
        PlayerStats[] players = Field((0, 5, 0, true), (1, 1, 0, true), (0, 2, 0, true), (1, 0, 0, true));

        var rows = Team(players, ownSlot: 1, ownTeam: 1);

        Assert.Equal(new byte[] { 1, 3, 0, 2 }, rows.Select(r => r.Slot));
    }

    [Fact]
    public void Sides_are_relative_to_the_player()
    {
        PlayerStats[] players = Field((0, 0, 0, true), (0, 0, 0, true), (1, 0, 0, true));

        var rows = Team(players, ownSlot: 0, ownTeam: 0);

        Assert.Equal(new[] { Side.Own, Side.Ally, Side.Enemy }, rows.Select(r => r.Side));
    }

    [Fact]
    public void In_a_free_for_all_everyone_else_is_an_enemy_even_on_a_matching_team_number()
    {
        PlayerStats[] players = Field((0, 0, 0, true), (0, 0, 0, true));

        var rows = ScoreboardModel.Build(BattleMode.FreeForAll, 0, 0, players, NameOf, false, 0);

        Assert.Equal(new[] { Side.Own, Side.Enemy }, rows.Select(r => r.Side));
    }

    [Fact]
    public void Within_a_team_kills_decide_then_fewer_deaths_then_the_lower_slot()
    {
        PlayerStats[] players = Field((0, 3, 2, true), (0, 5, 4, true), (0, 3, 1, true), (0, 3, 1, true), (0, 0, 0, true));

        var rows = Team(players);

        Assert.Equal(new byte[] { 1, 2, 3, 0, 4 }, rows.Select(r => r.Slot));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows.Select(r => r.Rank));
    }

    [Fact]
    public void Team_ranks_count_within_the_team()
    {
        PlayerStats[] players = Field((0, 3, 0, true), (1, 2, 0, true), (0, 1, 0, true), (1, 0, 0, true));

        var rows = Team(players);

        Assert.Equal(new[] { 1, 2, 1, 2 }, rows.Select(r => r.Rank));
    }

    [Fact]
    public void A_free_for_all_puts_the_living_first_while_it_runs()
    {
        PlayerStats[] players = Field((0, 5, 0, false), (1, 1, 3, true), (2, 2, 1, true), (3, 9, 0, false));
        for (int i = 0; i < 4; i++) players[i].Team = (byte)i;

        var rows = ScoreboardModel.Build(BattleMode.FreeForAll, 0, 0, players, NameOf, ended: false, 0);

        Assert.Equal(new byte[] { 2, 1, 3, 0 }, rows.Select(r => r.Slot));
        Assert.Equal(new[] { 1, 2, 3, 4 }, rows.Select(r => r.Rank));   // the standing, not yet a server rank
    }

    [Fact]
    public void After_a_free_for_all_ends_the_servers_rank_decides_and_is_shown()
    {
        PlayerStats[] players = Field((0, 9, 0, false), (1, 0, 5, false), (2, 4, 1, true), (3, 4, 1, false));
        byte[] ranks = { 3, 4, 1, 2 };
        for (int i = 0; i < 4; i++) { players[i].Team = (byte)i; players[i].Rank = ranks[i]; }

        var rows = ScoreboardModel.Build(BattleMode.FreeForAll, 0, 0, players, NameOf, ended: true, 0);

        Assert.Equal(new byte[] { 2, 3, 0, 1 }, rows.Select(r => r.Slot));
        Assert.Equal(new[] { 1, 2, 3, 4 }, rows.Select(r => r.Rank));
    }

    [Fact]
    public void An_unranked_player_after_the_end_goes_last_by_slot_and_takes_the_place_it_stands_in()
    {
        PlayerStats[] players = Field((0, 0, 0, false), (1, 0, 0, false), (2, 0, 0, false));
        players[0].Team = 0; players[1].Team = 1; players[2].Team = 2;
        players[1].Rank = 1;

        var rows = ScoreboardModel.Build(BattleMode.FreeForAll, 0, 0, players, NameOf, ended: true, 0);

        Assert.Equal(new byte[] { 1, 0, 2 }, rows.Select(r => r.Slot));
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.Rank));
    }

    [Fact]
    public void Players_who_left_are_not_listed()
    {
        PlayerStats[] players = Field((0, 1, 0, true), (0, 9, 0, true), (1, 2, 0, true));
        players[1].Active = false;

        var rows = Team(players);

        Assert.Equal(new byte[] { 0, 2 }, rows.Select(r => r.Slot));
    }

    [Fact]
    public void A_missing_name_falls_back_to_the_slot_number()
    {
        PlayerStats[] players = Field((0, 0, 0, true));
        players[8] = new PlayerStats { Active = true, Team = 0, Alive = true };
        players[9] = new PlayerStats { Active = true, Team = 0, Alive = true };
        Names[9] = "   ";

        var rows = Team(players);
        Names[9] = null;

        Assert.Equal(new[] { "阿成", "玩家 9", "玩家 10" }, rows.Select(r => r.Name));
    }

    [Fact]
    public void The_tank_column_names_the_vehicle()
    {
        PlayerStats[] players = Field((0, 0, 0, true), (0, 0, 0, true));
        players[1].Spec = 2;

        var rows = Team(players);

        Assert.Equal("轻型坦克", rows[0].TankName);
        Assert.Equal("重型坦克", rows[1].TankName);
    }

    [Fact]
    public void Status_is_alive_or_destroyed_and_the_own_row_counts_down_its_respawn()
    {
        PlayerStats[] players = Field((0, 0, 0, false), (0, 0, 0, false), (1, 0, 0, true));

        var waiting = Team(players, ownSlot: 0, respawnIn: 7 * Tank.Sim.SimConstants.TickRate - 1);
        var noCountdown = Team(players, ownSlot: 0, respawnIn: 0);

        Assert.Equal("复活中 7 秒", waiting.Single(r => r.Slot == 0).Status);   // rounded up
        Assert.Equal("已毁", waiting.Single(r => r.Slot == 1).Status);          // others' respawn times are not known
        Assert.Equal("存活", waiting.Single(r => r.Slot == 2).Status);
        Assert.Equal("已毁", noCountdown.Single(r => r.Slot == 0).Status);
        Assert.All(waiting.Concat(noCountdown), r => Assert.Equal(r.Alive, r.Status == "存活"));
    }

    [Fact]
    public void A_living_own_tank_ignores_a_stale_respawn_time()
    {
        PlayerStats[] players = Field((0, 0, 0, true));

        Assert.Equal("存活", Team(players, respawnIn: 300)[0].Status);
    }

    [Fact]
    public void Building_into_a_buffer_allocates_nothing_once_warm()
    {
        Allocation.AssertNone(() =>
        {
            PlayerStats[] players = Field((0, 3, 1, true), (1, 2, 0, true), (0, 1, 2, false), (1, 0, 3, true), (0, 4, 0, true), (1, 5, 1, false));
            var buffer = new ScoreRow[BattleProtocol.MaxPlayers];
            Func<byte, string?> nameOf = NameOf;
            for (int i = 0; i < 10; i++) ScoreboardModel.Build(BattleMode.TeamBattle, 0, 0, players, nameOf, false, 0, buffer);

            Allocation.Window window = Allocation.Start();
            int count = 0;
            for (int i = 0; i < 100; i++) count = ScoreboardModel.Build(BattleMode.TeamBattle, 0, 0, players, nameOf, false, 0, buffer);
            Allocation.Measured measured = window.Stop();

            Assert.Equal(6, count);
            return measured;
        });
    }

    [Fact]
    public void A_buffer_too_small_for_the_slots_is_refused()
    {
        PlayerStats[] players = Field((0, 0, 0, true));

        Assert.Throws<ArgumentException>(() => ScoreboardModel.Build(BattleMode.TeamBattle, 0, 0, players, NameOf, false, 0, new ScoreRow[2]));
    }
}

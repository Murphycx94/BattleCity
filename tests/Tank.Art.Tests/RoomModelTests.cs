using Tank.Client.UI;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class RoomModelTests
{
    private static RoomPlayer P(byte slot, string name, byte team, byte tank = 1, bool ready = false, bool connected = true, bool bot = false) =>
        new(slot, name, team, tank, ready, connected, bot);

    private static RoomStateMessage Room(BattleMode mode, byte host, int bots, bool running, params RoomPlayer[] players) =>
        new(mode, host, (byte)bots, running, players);

    [Fact]
    public void Team_columns_are_mine_and_theirs_whatever_the_team_number()
    {
        RoomStateMessage room = Room(BattleMode.TeamBattle, 0, 0, false,
            P(0, "Ann", 0), P(1, "Bo", 1), P(2, "Cy", 1), P(3, "Di", 0));

        RoomModel fromBo = RoomModel.Build(room, 1);
        RoomModel fromAnn = RoomModel.Build(room, 0);

        Assert.Equal(new[] { "Bo", "Cy" }, fromBo.Columns[0].Rows.Select(r => r.Name));
        Assert.Equal(new[] { "Ann", "Di" }, fromBo.Columns[1].Rows.Select(r => r.Name));
        Assert.Equal(ColumnSide.Mine, fromBo.Columns[0].Side);
        Assert.Equal(ColumnSide.Other, fromBo.Columns[1].Side);
        Assert.StartsWith("我方", fromBo.Columns[0].Title);
        Assert.StartsWith("敌方", fromBo.Columns[1].Title);
        Assert.Equal(new[] { "Ann", "Di" }, fromAnn.Columns[0].Rows.Select(r => r.Name));
        Assert.Equal((byte)0, fromBo.OtherTeam);
        Assert.Equal((byte)1, fromAnn.OtherTeam);
    }

    [Fact]
    public void Free_for_all_is_one_column_without_a_team_switch()
    {
        RoomStateMessage room = Room(BattleMode.FreeForAll, 0, 0, false, P(0, "Ann", 0), P(1, "Bo", 1), P(2, "Cy", 2));

        RoomModel model = RoomModel.Build(room, 2);

        RoomColumnView only = Assert.Single(model.Columns);
        Assert.Equal(ColumnSide.Everyone, only.Side);
        Assert.Equal(3, only.Rows.Count);
        Assert.False(model.HasTeams);
        Assert.False(model.CanSwitchTeam);
    }

    [Fact]
    public void Only_the_host_sets_the_mode_and_bots_and_starts()
    {
        RoomStateMessage room = Room(BattleMode.TeamBattle, 0, 0, false, P(0, "Ann", 0), P(1, "Bo", 1));

        RoomModel host = RoomModel.Build(room, 0);
        RoomModel guest = RoomModel.Build(room, 1);

        Assert.True(host.CanSetMode && host.CanSetBots && host.ShowStart && host.CanStart);
        Assert.False(guest.CanSetMode || guest.CanSetBots || guest.ShowStart || guest.CanStart);
        Assert.True(guest.CanSwitchTeam);
        Assert.Equal("", guest.StartHint);
    }

    [Fact]
    public void The_start_button_says_why_it_is_disabled()
    {
        RoomStateMessage alone = Room(BattleMode.TeamBattle, 0, 0, false, P(0, "Ann", 0));

        RoomModel model = RoomModel.Build(alone, 0);

        Assert.False(model.CanStart);
        Assert.Equal(RoomRules.StartBlockReason(alone), model.StartHint);
    }

    [Fact]
    public void With_a_match_running_the_room_waits_for_the_next_one()
    {
        RoomStateMessage room = Room(BattleMode.Annihilation, 0, 0, true, P(0, "Ann", 0), P(1, "Bo", 1));

        RoomModel host = RoomModel.Build(room, 0);

        Assert.Equal("对局进行中，等下一局", host.WaitingText);
        Assert.False(host.CanStart);
        Assert.False(host.CanSetMode);
        Assert.False(host.CanSetBots);
        Assert.Null(RoomModel.Build(Room(BattleMode.Annihilation, 0, 0, false, P(0, "Ann", 0)), 0).WaitingText);
    }

    [Fact]
    public void Rows_carry_name_tags_tank_and_state()
    {
        RoomStateMessage room = Room(BattleMode.TeamBattle, 0, 0, false,
            P(0, "Ann", 0, tank: 2, ready: true), P(1, "Bo", 0, connected: false), P(2, "Cy", 0), P(3, "Bot", 0, bot: true));

        IReadOnlyList<RoomRowView> rows = RoomModel.Build(room, 1).Columns[0].Rows;

        RoomRowView ann = rows.Single(r => r.Slot == 0);
        Assert.Equal("房主", ann.Tags);
        Assert.Equal("重型坦克", ann.TankName);
        Assert.Equal("已准备", ann.State);
        Assert.Equal(RowTone.Good, ann.Tone);
        RoomRowView bo = rows.Single(r => r.Slot == 1);
        Assert.Equal("你", bo.Tags);
        Assert.True(bo.IsMe);
        Assert.Equal("重连中", bo.State);
        Assert.Equal("未准备", rows.Single(r => r.Slot == 2).State);
        Assert.True(rows.Single(r => r.Slot == 3).IsBot);
        Assert.Equal("房主 你", RoomModel.Build(room, 0).Columns[0].Rows.Single(r => r.Slot == 0).Tags);
    }

    [Fact]
    public void Bots_to_be_added_show_as_a_placeholder_row_in_the_team_they_will_fill()
    {
        RoomStateMessage room = Room(BattleMode.TeamBattle, 0, 2, false, P(0, "Ann", 0), P(1, "Bo", 0));

        RoomModel model = RoomModel.Build(room, 0);

        Assert.Equal(2, model.Columns[0].Rows.Count);   // my team: the two people
        Assert.Equal("我方 · 2", model.Columns[0].Title);
        Assert.Equal("敌方 · 2", model.Columns[1].Title);   // the titles count tanks, the bots to come included
        RoomRowView ghost = Assert.Single(model.Columns[1].Rows);
        Assert.True(ghost.Ghost);
        Assert.Contains("× 2", ghost.Name);
        Assert.True(model.CanStart);
    }

    [Fact]
    public void The_signature_changes_with_what_is_on_screen_and_not_otherwise()
    {
        RoomStateMessage room = Room(BattleMode.TeamBattle, 0, 0, false, P(0, "Ann", 0), P(1, "Bo", 1));
        RoomStateMessage same = Room(BattleMode.TeamBattle, 0, 0, false, P(0, "Ann", 0), P(1, "Bo", 1));
        RoomStateMessage readied = Room(BattleMode.TeamBattle, 0, 0, false, P(0, "Ann", 0, ready: true), P(1, "Bo", 1));

        Assert.Equal(RoomModel.Build(room, 0).Signature, RoomModel.Build(same, 0).Signature);
        Assert.NotEqual(RoomModel.Build(room, 0).Signature, RoomModel.Build(readied, 0).Signature);
        Assert.NotEqual(RoomModel.Build(room, 0).Signature, RoomModel.Build(room, 1).Signature);
    }

    [Fact]
    public void The_ready_button_toggles_and_the_tank_choice_is_the_players_own()
    {
        RoomStateMessage room = Room(BattleMode.TeamBattle, 0, 0, false, P(0, "Ann", 0, tank: 3, ready: true), P(1, "Bo", 1, tank: 0));

        RoomModel ann = RoomModel.Build(room, 0);
        RoomModel bo = RoomModel.Build(room, 1);

        Assert.True(ann.MyReady);
        Assert.Equal("取消准备", ann.ReadyLabel);
        Assert.Equal(3, ann.MyTankIndex);
        Assert.Equal("准备", bo.ReadyLabel);
        Assert.Equal(0, bo.MyTankIndex);
    }
}

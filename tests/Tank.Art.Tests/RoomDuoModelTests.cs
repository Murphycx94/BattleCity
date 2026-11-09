using Tank.Client.UI;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

/// <summary>
/// The room in duos (stage 6 design §5.1, plan 6C task 6): two columns, teams 1–4 and 5–8, each team's people then the bots the
/// server will add to it; a team picker of eight with the full ones (not the viewer's own) marked; and the start blocked
/// until at least two teams would have tanks.
/// </summary>
public class RoomDuoModelTests
{
    private static RoomPlayer Person(byte slot, byte team) => new(slot, $"P{slot}", team, 1, false, true, false);

    private static RoomStateMessage Room(int bots, params RoomPlayer[] players) =>
        new(BattleMode.Duos, 0, (byte)bots, false, players, "riverland");

    private static IEnumerable<(string Group, string Name)> Lines(RoomColumnView column) => column.Rows.Select(r => (r.Group, r.Name));

    [Fact]
    public void A_host_alone_and_fifteen_bots_fill_all_eight_teams_in_two_columns()
    {
        RoomModel model = RoomModel.Build(Room(15, Person(0, 0)), 0);

        Assert.True(model.IsDuos);
        Assert.Equal(2, model.Columns.Count);
        Assert.Equal("队伍 · 8", model.Columns[0].Title);
        Assert.Equal("", model.Columns[1].Title);
        Assert.Equal(new[]
        {
            ("第 1 队", "P0"), ("第 1 队", "机器人 × 1"),
            ("第 2 队", "机器人 × 2"), ("第 3 队", "机器人 × 2"), ("第 4 队", "机器人 × 2"),
        }, Lines(model.Columns[0]));
        Assert.Equal(new[] { ("第 5 队", "机器人 × 2"), ("第 6 队", "机器人 × 2"), ("第 7 队", "机器人 × 2"), ("第 8 队", "机器人 × 2") },
            Lines(model.Columns[1]));
        Assert.True(model.Columns[0].Rows[1].Ghost);
        Assert.Equal("开局时补入", model.Columns[0].Rows[1].State);
        Assert.True(model.Columns[0].Rows[0].IsMe);

        Assert.Equal(0, model.MyTeam);
        Assert.True(model.CanPickTeam);
        Assert.False(model.CanSwitchTeam);
        Assert.Equal(8, model.TeamChoices.Count);
        Assert.Equal(Enumerable.Range(1, 8).Select(n => $"第 {n} 队"), model.TeamChoices.Select(c => c.Name));
        Assert.Equal(Enumerable.Range(0, 8).Select(t => (byte)t), model.TeamChoices.Select(c => c.Team));
        Assert.All(model.TeamChoices, c => Assert.False(c.Full));
        Assert.True(model.CanStart);
    }

    [Fact]
    public void Three_people_and_three_bots_the_bots_partner_the_lone_one_first_then_open_a_team()
    {
        RoomStateMessage room = Room(3, Person(0, 0), Person(1, 0), Person(2, 5));
        RoomModel host = RoomModel.Build(room, 0);
        RoomModel third = RoomModel.Build(room, 2);

        Assert.Equal("队伍 · 3", host.Columns[0].Title);
        Assert.Equal(new[] { ("第 1 队", "P0"), ("第 1 队", "P1"), ("第 2 队", "机器人 × 2") }, Lines(host.Columns[0]));
        Assert.Equal(new[] { ("第 6 队", "P2"), ("第 6 队", "机器人 × 1") }, Lines(host.Columns[1]));

        Assert.False(host.TeamChoices[0].Full);   // its own team, full but not to the host
        Assert.True(third.TeamChoices[0].Full);
        Assert.False(third.TeamChoices[5].Full);
        Assert.All(third.TeamChoices.Where(c => c.Team is not 0), c => Assert.False(c.Full));   // bots to come don't fill a team
        Assert.Equal(5, third.MyTeam);
    }

    [Fact]
    public void A_column_lists_only_teams_with_someone_and_keeps_its_place_when_empty()
    {
        RoomModel model = RoomModel.Build(Room(0, Person(0, 6), Person(1, 2)), 0);

        Assert.Equal(new[] { ("第 3 队", "P1") }, Lines(model.Columns[0]));
        Assert.Equal(new[] { ("第 7 队", "P0") }, Lines(model.Columns[1]));
        Assert.Equal("队伍 · 2", model.Columns[0].Title);

        RoomModel allLow = RoomModel.Build(Room(0, Person(0, 0), Person(1, 1)), 0);
        Assert.Equal(2, allLow.Columns.Count);
        Assert.Empty(allLow.Columns[1].Rows);
    }

    [Fact]
    public void A_column_without_rows_is_not_shown_and_one_with_rows_is()
    {
        RoomModel low = RoomModel.Build(Room(0, Person(0, 0), Person(1, 1)), 1);   // a guest's view: teams 5–8 empty
        RoomModel high = RoomModel.Build(Room(0, Person(0, 0), Person(1, 5)), 1);

        Assert.Equal((2, 0), (low.Columns[0].Rows.Count, low.Columns[1].Rows.Count));
        Assert.Equal((true, false), (low.Columns[0].Shown, low.Columns[1].Shown));
        Assert.Equal((true, true), (high.Columns[0].Shown, high.Columns[1].Shown));
    }

    [Fact]
    public void Royale_and_team_rooms_show_every_column_as_before()
    {
        RoomPlayer[] two = { Person(0, 0), Person(1, 1) };
        foreach (BattleMode mode in ModeRules.All.Where(m => !ModeRules.IsDuos(m)))
        {
            RoomModel model = RoomModel.Build(new RoomStateMessage(mode, 0, 0, false, two, "riverland"), 0);
            Assert.All(model.Columns, c => Assert.True(c.Shown));
        }
        RoomModel lonely = RoomModel.Build(new RoomStateMessage(BattleMode.TeamBattle, 0, 0, false, new[] { Person(0, 0) }, "riverland"), 0);
        Assert.Empty(lonely.Columns[1].Rows);
        Assert.True(lonely.Columns[1].Shown);   // a team room's empty 敌方 keeps its box
    }

    [Fact]
    public void A_seat_on_a_team_past_the_eight_is_listed_last_as_unteamed()
    {
        RoomModel model = RoomModel.Build(Room(0, Person(0, 0), Person(1, 12), Person(2, 6)), 0);

        Assert.Equal(new[] { ("第 1 队", "P0") }, Lines(model.Columns[0]));
        Assert.Equal(new[] { ("第 7 队", "P2"), ("未分队", "P1") }, Lines(model.Columns[1]));
        Assert.Equal("队伍 · 2", model.Columns[0].Title);   // not one of the eight teams
        Assert.Equal(RoomModel.UnteamedGroup, model.Columns[1].Rows[1].Group);

        RoomModel alone = RoomModel.Build(Room(0, Person(0, 0), Person(1, 12)), 1);
        Assert.Equal(new[] { ("未分队", "P1") }, Lines(alone.Columns[1]));
        Assert.True(alone.Columns[1].Shown);
        Assert.Equal(12, alone.MyTeam);
    }

    [Fact]
    public void One_person_and_one_bot_make_one_team_and_cannot_start_two_bots_can()
    {
        RoomModel one = RoomModel.Build(Room(1, Person(0, 0)), 0);
        RoomModel two = RoomModel.Build(Room(2, Person(0, 0)), 0);

        Assert.False(one.CanStart);
        Assert.StartsWith("至少要有两队有车", one.StartHint);
        Assert.Equal("队伍 · 1", one.Columns[0].Title);
        Assert.True(two.CanStart);
        Assert.Equal("队伍 · 2", two.Columns[0].Title);
    }

    [Fact]
    public void A_guest_picks_a_team_too_and_the_signature_follows_the_teams()
    {
        RoomStateMessage room = Room(0, Person(0, 0), Person(1, 1));
        RoomModel guest = RoomModel.Build(room, 1);

        Assert.True(guest.CanPickTeam);
        Assert.Equal(1, guest.MyTeam);
        Assert.False(guest.ShowStart);
        Assert.NotEqual(guest.Signature, RoomModel.Build(Room(0, Person(0, 0), Person(1, 2)), 1).Signature);
        Assert.NotEqual(guest.Signature, RoomModel.Build(Room(0, Person(0, 0), Person(1, 1), Person(2, 1)), 1).Signature);
        Assert.Equal(guest.Signature, RoomModel.Build(room, 1).Signature);
        Assert.False(RoomModel.Build(room, 9).CanPickTeam);   // a slot not in the room
    }

    [Fact]
    public void Group_names_are_one_based()
    {
        Assert.Equal("第 1 队", RoomModel.GroupName(0));
        Assert.Equal("第 8 队", RoomModel.GroupName(7));
    }

    [Fact]
    public void A_royale_room_has_no_groups_and_no_team_choices()
    {
        var room = new RoomStateMessage(BattleMode.Royale, 0, 15, false, new[] { Person(0, 0) }, "riverland");
        RoomModel model = RoomModel.Build(room, 0);

        Assert.False(model.IsDuos);
        Assert.False(model.CanPickTeam);
        Assert.Empty(model.TeamChoices);
        Assert.Equal("玩家 · 16", Assert.Single(model.Columns).Title);
        Assert.All(model.Columns.SelectMany(c => c.Rows), r => Assert.Equal("", r.Group));
    }

    [Fact]
    public void A_team_room_has_no_team_choices_and_swaps_sides_as_before()
    {
        var room = new RoomStateMessage(BattleMode.TeamBattle, 0, 0, false, new[] { Person(0, 1), Person(1, 0) }, "riverland");
        RoomModel model = RoomModel.Build(room, 0);

        Assert.False(model.CanPickTeam);
        Assert.Empty(model.TeamChoices);
        Assert.True(model.CanSwitchTeam);
        Assert.Equal(1, model.MyTeam);
        Assert.Equal(0, model.OtherTeam);
    }

    [Fact]
    public void The_hint_under_the_map_names_duos_not_the_royale()
    {
        RoomModel model = RoomModel.Build(Room(15, Person(0, 0)), 0);

        Assert.Equal("双人吃鸡只在河谷开", model.MapHint);
    }
}

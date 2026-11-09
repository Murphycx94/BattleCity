using Tank.Client.UI;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

/// <summary>
/// The room in a royale (stage 6 design §2, plan task 7): 16 seats, so bots up to 15 and the players in two columns past
/// eight rows; the map locked to the riverland with a hint saying so; and the mode list saying why a mode can't be
/// switched to while the room holds more people than it seats.
/// </summary>
public class RoomRoyaleModelTests
{
    private const string MapHint = "吃鸡只在河谷开";

    private static RoomPlayer[] Humans(int n) =>
        Enumerable.Range(0, n).Select(i => new RoomPlayer((byte)i, $"P{i}", 0, 1, false, true, false)).ToArray();

    private static RoomStateMessage Room(BattleMode mode, int bots, params RoomPlayer[] players) =>
        new(mode, 0, (byte)bots, false, players, "riverland");

    [Fact]
    public void A_royale_room_seats_fifteen_bots_on_the_riverland_only()
    {
        RoomModel model = RoomModel.Build(Room(BattleMode.Royale, 15, Humans(1)), 0);

        Assert.Equal(15, model.MaxBots);
        Assert.False(model.CanSetMap);
        Assert.Equal(MapHint, model.MapHint);
        Assert.Equal("吃鸡 · 河谷", model.Title);
        RoomColumnView only = Assert.Single(model.Columns);
        Assert.Equal("玩家 · 16", only.Title);
        Assert.Equal(2, only.Rows.Count);
        Assert.Equal("P0", only.Rows[0].Name);
        Assert.Equal("机器人 × 15", only.Rows[1].Name);
        Assert.Equal("开局时补入", only.Rows[1].State);
        Assert.True(only.Rows[1].Ghost);
        Assert.True(model.CanStart);
    }

    [Fact]
    public void A_guest_sees_the_map_hint_too()
    {
        RoomModel guest = RoomModel.Build(Room(BattleMode.Royale, 0, Humans(2)), 1);

        Assert.Equal(MapHint, guest.MapHint);
        Assert.False(guest.CanSetMap);
        Assert.Equal(15, guest.MaxBots);
    }

    [Fact]
    public void A_team_room_is_as_before()
    {
        RoomModel model = RoomModel.Build(Room(BattleMode.TeamBattle, 0, Humans(2)), 0);

        Assert.Equal(9, model.MaxBots);
        Assert.Null(model.MapHint);
        Assert.True(model.CanSetMap);
        Assert.Equal(2, model.Columns.Count);
        Assert.Equal(ColumnSide.Mine, model.Columns[0].Side);
        Assert.Equal(ColumnSide.Other, model.Columns[1].Side);
        Assert.All(model.ModeChoices, c => Assert.Null(c.Blocked));
    }

    [Theory]
    [InlineData(9, 7, 5, 5)]    // nine people and a ghost row for the seven bots: ten rows
    [InlineData(16, 0, 8, 8)]
    [InlineData(16, 3, 8, 8)]   // no seat left for a bot: no ghost row
    [InlineData(10, 0, 5, 5)]
    public void More_than_eight_rows_without_teams_go_in_two_columns(int humans, int bots, int first, int second)
    {
        RoomModel model = RoomModel.Build(Room(BattleMode.Royale, bots, Humans(humans)), 0);

        Assert.Equal(2, model.Columns.Count);
        Assert.Equal($"玩家 · {humans + Math.Min(bots, 16 - humans)}", model.Columns[0].Title);   // the tanks that would be seated, as before
        Assert.Equal("", model.Columns[1].Title);
        Assert.All(model.Columns, c => Assert.Equal(ColumnSide.Everyone, c.Side));
        Assert.Equal(first, model.Columns[0].Rows.Count);
        Assert.Equal(second, model.Columns[1].Rows.Count);
        // The rows run on from the first column to the second, in slot order, the ghost last.
        List<RoomRowView> rows = model.Columns.SelectMany(c => c.Rows).ToList();
        Assert.Equal(Enumerable.Range(0, humans).Select(i => $"P{i}"), rows.Take(humans).Select(r => r.Name));
        Assert.Equal(bots > 0 && humans < 16 ? 1 : 0, rows.Count(r => r.Ghost));
    }

    [Fact]
    public void Eight_rows_or_fewer_without_teams_stay_in_one_column()
    {
        RoomModel eight = RoomModel.Build(Room(BattleMode.Royale, 1, Humans(7)), 0);   // seven people and a ghost
        RoomModel ffa = RoomModel.Build(Room(BattleMode.FreeForAll, 0, Humans(2)), 0);

        Assert.Equal(8, Assert.Single(eight.Columns).Rows.Count);
        Assert.Equal(2, Assert.Single(ffa.Columns).Rows.Count);
        Assert.Equal("玩家 · 2", ffa.Columns[0].Title);
    }

    [Fact]
    public void A_free_for_all_of_ten_goes_in_two_columns_too()
    {
        RoomModel model = RoomModel.Build(Room(BattleMode.FreeForAll, 0, Humans(10)), 0);

        Assert.Equal(2, model.Columns.Count);
        Assert.Equal("玩家 · 10", model.Columns[0].Title);
        Assert.Equal((5, 5), (model.Columns[0].Rows.Count, model.Columns[1].Rows.Count));
    }

    [Fact]
    public void The_mode_list_says_why_a_mode_cant_be_switched_to()
    {
        RoomModel model = RoomModel.Build(Room(BattleMode.Royale, 0, Humans(11)), 0);

        Assert.Equal(ModeRules.All, model.ModeChoices.Select(c => c.Mode));
        Assert.Equal("真人超过 10 个，不能换到团队对战。", model.ModeChoices[0].Blocked);
        Assert.Equal("真人超过 10 个，不能换到团队歼灭。", model.ModeChoices[1].Blocked);
        Assert.Equal("真人超过 10 个，不能换到各自为战。", model.ModeChoices[2].Blocked);
        Assert.Equal("真人超过 10 个，不能换到标准战。", model.ModeChoices[3].Blocked);
        Assert.Null(model.ModeChoices[4].Blocked);
        Assert.Equal("吃鸡", model.ModeChoices[4].Name);
        Assert.Equal("团队对战", model.ModeChoices[0].Name);
        Assert.True(model.CanStart);   // the start is not what's blocked
    }

    [Fact]
    public void Ten_people_may_switch_to_any_mode_and_bots_dont_count_but_a_dropped_player_does()
    {
        RoomPlayer[] tenAndBots = Humans(10).Concat(new[] { new RoomPlayer(10, "Bot", 0, 1, false, true, true) }).ToArray();
        RoomPlayer[] elevenOneDropped = Humans(10).Concat(new[] { new RoomPlayer(10, "P10", 0, 1, false, false, false) }).ToArray();

        Assert.All(RoomModel.Build(Room(BattleMode.Royale, 0, tenAndBots), 0).ModeChoices, c => Assert.Null(c.Blocked));
        Assert.NotNull(RoomModel.Build(Room(BattleMode.Royale, 0, elevenOneDropped), 0).ModeChoices[0].Blocked);
    }

    [Fact]
    public void The_current_mode_is_never_blocked()
    {
        RoomModel model = RoomModel.Build(Room(BattleMode.TeamBattle, 0, Humans(11)), 0);   // more than the server lets in, but the room is what it is

        Assert.Null(model.ModeChoices[0].Blocked);
        Assert.NotNull(model.ModeChoices[2].Blocked);
        Assert.Null(model.ModeChoices[4].Blocked);
    }

    [Fact]
    public void The_signature_follows_the_mode_list_the_map_hint_and_the_columns()
    {
        string royale = RoomModel.Build(Room(BattleMode.Royale, 0, Humans(2)), 0).Signature;

        Assert.NotEqual(royale, RoomModel.Build(Room(BattleMode.FreeForAll, 0, Humans(2)), 0).Signature);
        Assert.Equal(royale, RoomModel.Build(Room(BattleMode.Royale, 0, Humans(2)), 0).Signature);
        Assert.NotEqual(RoomModel.Build(Room(BattleMode.Royale, 0, Humans(8)), 0).Signature, RoomModel.Build(Room(BattleMode.Royale, 1, Humans(8)), 0).Signature);
    }
}

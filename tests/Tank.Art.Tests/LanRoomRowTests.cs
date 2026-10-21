using System.Net;
using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class LanRoomRowTests
{
    private static LanRoomRow Row(string name = "Alpha", BattleMode mode = BattleMode.TeamBattle, int players = 3, bool running = false,
        int version = BattleProtocol.Version, int ping = 7) =>
        LanRoomRow.From(new DiscoveredRoom(new IPEndPoint(IPAddress.Parse("192.168.1.20"), 47001),
            new RoomInfoMessage(version, name, mode, players, BattleProtocol.MaxPlayers, running, 47001), ping, 0));

    [Fact]
    public void A_waiting_room_with_space_is_joinable_and_its_line_shows_the_columns()
    {
        LanRoomRow row = Row();
        Assert.True(row.Joinable);
        Assert.Null(row.WhyNot);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.1.20"), 47001), row.EndPoint);
        Assert.Contains("Alpha", row.Text);
        Assert.Contains("团队对战", row.Text);
        Assert.Contains("3/10", row.Text);
        Assert.Contains("等待中", row.Text);
        Assert.Contains("  7ms", row.Text);
        Assert.EndsWith("192.168.1.20", row.Text);
    }

    [Fact]
    public void A_room_on_another_protocol_version_cannot_be_joined_and_says_why()
    {
        LanRoomRow row = Row(version: BattleProtocol.Version + 1);
        Assert.False(row.Joinable);
        Assert.Contains("版本不符", row.WhyNot);
        Assert.Contains($"v{BattleProtocol.Version + 1}", row.WhyNot);
        Assert.Contains("版本不符", row.Text);
    }

    [Fact]
    public void A_full_room_cannot_be_joined()
    {
        LanRoomRow row = Row(players: BattleProtocol.MaxPlayers);
        Assert.False(row.Joinable);
        Assert.Equal("房间已满", row.WhyNot);
    }

    [Fact]
    public void A_version_mismatch_is_reported_before_a_full_room()
    {
        Assert.Contains("版本不符", Row(players: BattleProtocol.MaxPlayers, version: 1).WhyNot);
    }

    [Fact]
    public void A_running_match_in_a_mode_without_joins_can_be_entered_but_the_row_notes_the_wait()
    {
        LanRoomRow row = Row(mode: BattleMode.Annihilation, running: true);
        Assert.True(row.Joinable);
        Assert.Contains("等下一局", row.WhyNot);
        Assert.Contains("对战中", row.Text);
    }

    [Fact]
    public void A_running_team_battle_takes_newcomers_so_it_carries_no_note()
    {
        LanRoomRow row = Row(mode: BattleMode.TeamBattle, running: true);
        Assert.True(row.Joinable);
        Assert.Null(row.WhyNot);
        Assert.Contains("对战中", row.Text);
    }

    [Fact]
    public void Cjk_room_names_keep_the_columns_aligned()
    {
        LanRoomRow cjk = Row(name: "火狐的房间");
        LanRoomRow ascii = Row(name: "Fox");
        int ColumnOfMode(LanRoomRow r) => TextColumns.Width(r.Text[..r.Text.IndexOf("团队对战", StringComparison.Ordinal)]);
        Assert.Equal(ColumnOfMode(ascii), ColumnOfMode(cjk));
    }

    [Theory]
    [InlineData("abc", 6, "abc   ")]
    [InlineData("房间", 6, "房间  ")]
    [InlineData("房间a", 6, "房间a ")]
    [InlineData("房间房间房间", 6, "房间房间房间")]
    [InlineData("", 3, "   ")]
    public void PadColumns_counts_wide_characters_as_two_columns(string text, int columns, string expected)
    {
        Assert.Equal(expected, TextColumns.PadColumns(text, columns));
    }

    [Theory]
    [InlineData("abc", 3)]
    [InlineData("坦克3D", 6)]
    [InlineData("ＡＢ", 4)]   // full-width Latin
    [InlineData("", 0)]
    public void Width_is_in_monospace_columns(string text, int width)
    {
        Assert.Equal(width, TextColumns.Width(text));
    }
}

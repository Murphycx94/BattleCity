using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Net.Tests.Battle;

/// <summary>The server has 16 seats for the royale, but the four older modes keep their 10 (deviation 22): players and bots alike.</summary>
public sealed class ModeSeatTests
{
    /// <summary>A room in <paramref name="mode"/> that <paramref name="players"/> join, then one more; that last one.</summary>
    private static (BattleHarness H, BattleClient Extra) FillThenOneMore(BattleMode mode, int players)
    {
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Mode = mode, Map = mode == BattleMode.Royale ? Tank.Sim.Maps.MapCatalog.Riverland : Tank.Sim.Maps.MapCatalog.Sandbox });
        for (int i = 0; i < players; i++) h.AddClient($"P{i}");
        h.Run(0.5);
        BattleClient extra = h.AddClient("Extra");
        h.Run(0.5);
        return (h, extra);
    }

    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    [InlineData(BattleMode.FreeForAll)]
    [InlineData(BattleMode.Standard)]
    public void A_ten_seat_room_turns_the_eleventh_player_away(BattleMode mode)
    {
        var (h, extra) = FillThenOneMore(mode, 10);
        Assert.Equal(10, h.Clients.Count(c => c.Phase == BattlePhase.Room));
        Assert.Equal(BattlePhase.Disconnected, extra.Phase);
        Assert.Contains("房间已满", extra.DisconnectReason);
    }

    [Fact]
    public void A_royale_room_takes_sixteen_players_and_turns_the_seventeenth_away()
    {
        var (h, extra) = FillThenOneMore(BattleMode.Royale, 16);
        Assert.Equal(16, h.Clients.Count(c => c.Phase == BattlePhase.Room));
        Assert.Equal(BattlePhase.Disconnected, extra.Phase);
        Assert.Contains("房间已满", extra.DisconnectReason);
    }

    [Fact]
    public void Bots_seated_in_a_running_ten_seat_match_count_against_its_seats()
    {
        Tank.Sim.Maps.MapDefinition small = TestMaps.Small("small");
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Map = small, Maps = new[] { small } });
        h.AddClient("A");
        h.StartMatch(BattleMode.TeamBattle, bots: 9);
        Assert.True(h.Server.MatchRunning);
        Assert.Equal(10, h.Server.Simulation!.TankCount);
        BattleClient late = h.AddClient("Late");
        h.Run(0.5);
        Assert.Equal(BattlePhase.Disconnected, late.Phase);
        Assert.Contains("房间已满", late.DisconnectReason);
        Assert.Equal(10, h.Server.Simulation.TankCount);
    }

    [Fact]
    public void A_ten_seat_room_keeps_at_most_nine_bots()
    {
        Assert.Equal(9, new BattleHarness(new BattleServerOptions { Seed = 1, Mode = BattleMode.TeamBattle, Bots = 15 }).Server.Bots);
        Assert.Equal(15, new BattleHarness(new BattleServerOptions { Seed = 1, Mode = BattleMode.Royale, Bots = 15, Map = Tank.Sim.Maps.MapCatalog.Riverland }).Server.Bots);

        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Mode = BattleMode.TeamBattle });
        BattleClient host = h.AddClient("Host");
        h.Run(0.3);
        host.SetBots(15);
        h.Run(0.2);
        Assert.Equal(9, h.Server.Bots);
        Assert.Equal(9, host.Room!.Bots);
    }

    [Fact]
    public void Switching_to_a_ten_seat_mode_cuts_the_bots_to_nine()
    {
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Mode = BattleMode.Royale, Bots = 15, Map = Tank.Sim.Maps.MapCatalog.Riverland });
        BattleClient host = h.AddClient("Host");
        h.Run(0.3);
        host.SetMode(BattleMode.FreeForAll);
        h.Run(0.2);
        Assert.Equal((BattleMode.FreeForAll, 9), (h.Server.Mode, h.Server.Bots));
        host.SetMode(BattleMode.Royale);   // back: the bots stay at 9 until set again
        h.Run(0.2);
        Assert.Equal((BattleMode.Royale, 9), (h.Server.Mode, h.Server.Bots));
        host.SetBots(15);
        h.Run(0.2);
        Assert.Equal(15, h.Server.Bots);
    }
}

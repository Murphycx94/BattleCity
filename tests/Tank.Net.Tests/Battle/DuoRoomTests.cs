using Tank.Net.Battle;
using Tank.Net.Protocol;
using Tank.Net.Transport;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// A duos room (stage 6 design §5.1): eight teams of two. A newcomer gets the lowest empty team (then the lowest single),
/// a seat may move to any team with room, switching into duos re-teams everyone that way in slot order and switching out
/// splits them over the two teams again; at the start the bots partner the singles first, then pair up; a match needs two
/// teams manned. The roster carries the teams 0–7.
/// </summary>
public sealed class DuoRoomTests
{
    /// <summary>A room on <paramref name="map"/> among the catalogue's maps, in <paramref name="mode"/>.</summary>
    private static BattleHarness Room(List<string> log, BattleMode mode = BattleMode.Duos, MapDefinition? map = null) =>
        new(new BattleServerOptions { Seed = 1, Mode = mode, Map = map ?? MapCatalog.Riverland, Maps = MapCatalog.All }, log: log.Add);

    private static BattleHarness WithPlayers(List<string> log, int players, BattleMode mode = BattleMode.Duos, MapDefinition? map = null)
    {
        BattleHarness h = Room(log, mode, map);
        for (int i = 0; i < players; i++)
        {
            h.AddClient($"P{i}");
            h.Run(0.1);   // one at a time: they come in in slot order
        }
        h.Run(0.3);
        return h;
    }

    private static BattleClient Host(BattleHarness h) => h.Clients.Single(c => c.IsHost);

    private static void SetMode(BattleHarness h, BattleMode mode)
    {
        Host(h).SetMode(mode);
        h.Run(0.2);
    }

    /// <summary>Client <paramref name="client"/> (an index into <see cref="BattleHarness.Clients"/>) asks for team <paramref name="arg"/>; runs 0.2 s. Shared with <see cref="DuoServerTests"/>.</summary>
    internal static void SetTeam(BattleHarness h, int client, string arg)
    {
        var w = new BitWriter();
        BattleMessages.Write(w, new RoomCommandMessage(RoomCommandKind.SetTeam, arg));
        h.Links[client].Send(w.Data, Channel.Reliable);
        h.Run(0.2);
    }

    /// <summary>The teams of the roster as the host sees it, in slot order.</summary>
    private static byte[] Teams(BattleHarness h) => Host(h).Room!.Players.OrderBy(p => p.Slot).Select(p => p.Team).ToArray();

    private static bool Start(BattleHarness h, int bots)
    {
        BattleClient host = Host(h);
        host.SetBots(bots);
        h.Run(0.2);
        host.StartMatch();
        h.Run(1.0);
        return h.Server.MatchRunning;
    }

    // ---- teams in the room ----

    [Fact]
    public void Newcomers_take_empty_teams_and_a_seat_moves_only_to_a_team_with_room()
    {
        var log = new List<string>();
        BattleHarness h = WithPlayers(log, 3);
        Assert.Equal(new byte[] { 0, 1, 2 }, Teams(h));

        SetTeam(h, 1, "0");   // B joins A
        Assert.Equal(new byte[] { 0, 0, 2 }, Teams(h));

        SetTeam(h, 2, "0");   // team 0 is full
        Assert.Equal(new byte[] { 0, 0, 2 }, Teams(h));
        SetTeam(h, 2, "7");
        Assert.Equal(new byte[] { 0, 0, 7 }, Teams(h));
        foreach (string bad in new[] { "8", "15", "255", "x", "-1", " 3", "" })
        {
            SetTeam(h, 2, bad);
            Assert.Equal(new byte[] { 0, 0, 7 }, Teams(h));
        }

        SetTeam(h, 0, "0");   // A's own team: full, but A is one of the two
        Assert.Equal(new byte[] { 0, 0, 7 }, Teams(h));

        BattleClient d = h.AddClient("D");   // a fourth: the lowest empty team
        h.Run(0.3);
        Assert.Equal(new byte[] { 0, 0, 7, 1 }, Teams(h));
        Assert.All(h.Clients, c => Assert.Equal(Teams(h), c.Room!.Players.OrderBy(p => p.Slot).Select(p => p.Team)));
        Assert.Equal(BattlePhase.Room, d.Phase);
    }

    [Fact]
    public void Nine_newcomers_fill_the_eight_teams_then_partner_the_first()
    {
        var log = new List<string>();
        BattleHarness h = WithPlayers(log, 9);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 0 }, Teams(h));
    }

    [Fact]
    public void Switching_into_duos_re_teams_everyone_and_switching_out_splits_them_over_two_teams()
    {
        var log = new List<string>();
        BattleHarness h = WithPlayers(log, 4, BattleMode.TeamBattle, MapCatalog.Sandbox);
        Assert.Equal(new byte[] { 0, 1, 0, 1 }, Teams(h));
        SetTeam(h, 1, "0");
        SetTeam(h, 2, "1");
        Assert.Equal(new byte[] { 0, 0, 1, 1 }, Teams(h));

        SetMode(h, BattleMode.Royale);   // no teams to pick: nobody is moved
        SetMode(h, BattleMode.TeamBattle);
        Assert.Equal(new byte[] { 0, 0, 1, 1 }, Teams(h));

        SetMode(h, BattleMode.Duos);
        Assert.Equal(BattleMode.Duos, h.Server.Mode);
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, Teams(h));

        SetTeam(h, 3, "0");   // in duos the teams are 0, 1, 2, 0 now
        Assert.Equal(new byte[] { 0, 1, 2, 0 }, Teams(h));
        SetMode(h, BattleMode.Duos);   // already duos: nobody is moved
        Assert.Equal(new byte[] { 0, 1, 2, 0 }, Teams(h));

        SetMode(h, BattleMode.TeamBattle);   // out of duos: split again in slot order
        Assert.Equal(new byte[] { 0, 1, 0, 1 }, Teams(h));

        SetTeam(h, 1, "0");
        SetMode(h, BattleMode.Royale);
        SetMode(h, BattleMode.Duos);   // royale → duos: teamed afresh
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, Teams(h));

        SetMode(h, BattleMode.FreeForAll);   // out of duos to a mode without teams: still back to 0 and 1 underneath
        SetMode(h, BattleMode.Standard);
        Assert.Equal(new byte[] { 0, 1, 0, 1 }, Teams(h));
    }

    [Fact]
    public void Switching_to_duos_moves_the_room_to_the_riverland()
    {
        var log = new List<string>();
        BattleHarness h = WithPlayers(log, 1, BattleMode.TeamBattle, MapCatalog.Sandbox);
        BattleClient host = Host(h);

        SetMode(h, BattleMode.Duos);

        Assert.Equal(BattleMode.Duos, host.Room!.Mode);
        Assert.Equal(RiverlandMap.Id, host.Room.Map);
        Assert.Same(MapCatalog.Riverland, h.Server.RoomMap);
        host.SetMap(SandboxMap.Id);
        h.Run(0.2);
        Assert.Equal(RiverlandMap.Id, host.Room!.Map);
    }

    [Fact]
    public void An_eleven_player_royale_room_switches_to_duos_but_not_to_a_team_battle()
    {
        var log = new List<string>();
        BattleHarness h = WithPlayers(log, 11, BattleMode.Royale);

        SetMode(h, BattleMode.Duos);
        Assert.Equal(BattleMode.Duos, h.Server.Mode);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2 }, Teams(h));

        SetMode(h, BattleMode.TeamBattle);
        Assert.Equal(BattleMode.Duos, h.Server.Mode);
        Assert.Contains(log, l => l.Contains("can't switch to teambattle: 11 players"));
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2 }, Teams(h));   // a refused switch moves nobody
    }

    [Fact]
    public void A_duos_room_seats_sixteen()
    {
        var log = new List<string>();
        BattleHarness h = WithPlayers(log, 16);
        Assert.Equal(16, h.Server.ConnectedCount);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2, 3, 4, 5, 6, 7 }, Teams(h));
        BattleClient extra = h.AddClient("Extra");
        h.Run(0.5);
        Assert.Equal(BattlePhase.Disconnected, extra.Phase);
    }

    // ---- the two-team modes and a mode without teams ----

    [Fact]
    public void In_a_team_battle_a_seat_moves_only_to_team_0_or_1()
    {
        var log = new List<string>();
        BattleHarness h = WithPlayers(log, 2, BattleMode.TeamBattle, MapCatalog.Sandbox);
        Assert.Equal(new byte[] { 0, 1 }, Teams(h));

        foreach (string bad in new[] { "2", "7", "255", "x" })
        {
            SetTeam(h, 1, bad);
            Assert.Equal(new byte[] { 0, 1 }, Teams(h));
        }
        SetTeam(h, 1, "0");
        Assert.Equal(new byte[] { 0, 0 }, Teams(h));
    }

    [Fact]
    public void In_a_standard_battle_a_seat_moves_only_to_team_0_or_1()
    {
        var log = new List<string>();
        BattleHarness h = WithPlayers(log, 2, BattleMode.Standard, MapCatalog.Sandbox);
        Assert.Equal(new byte[] { 0, 1 }, Teams(h));

        SetTeam(h, 1, "2");
        Assert.Equal(new byte[] { 0, 1 }, Teams(h));
        SetTeam(h, 1, "0");
        Assert.Equal(new byte[] { 0, 0 }, Teams(h));
    }

    [Fact]
    public void In_a_free_for_all_a_seat_keeps_its_team()
    {
        var log = new List<string>();
        BattleHarness h = WithPlayers(log, 2, BattleMode.FreeForAll, MapCatalog.Sandbox);
        Assert.Equal(new byte[] { 0, 1 }, Teams(h));   // the slots

        SetTeam(h, 0, "1");
        Assert.Equal(new byte[] { 0, 1 }, Teams(h));
        SetMode(h, BattleMode.TeamBattle);   // a switch that moves nobody shows the teams underneath: slot 0's is still 0
        Assert.Equal(new byte[] { 0, 1 }, Teams(h));
    }

    // ---- the start ----

    [Fact]
    public void Fifteen_bots_partner_the_player_then_pair_up_on_the_other_seven_teams()
    {
        var log = new List<string>();
        BattleHarness h = WithPlayers(log, 1);

        Assert.True(Start(h, 15));

        Assert.Equal(16, h.Server.Simulation!.TankCount);
        byte[] expected = { 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7 };
        Assert.Equal(expected, Teams(h));
        Assert.Equal("机器人 1", Host(h).Room!.Players.Single(p => p.Slot == 1).Name);
        for (byte slot = 0; slot < 16; slot++)
        {
            Assert.True(h.Server.Rules!.Of(slot).Active);
            Assert.Equal(expected[slot], h.Server.Rules.Of(slot).Team);
        }
    }

    [Fact]
    public void One_team_alone_does_not_start_and_its_bot_goes_again()
    {
        var log = new List<string>();
        BattleHarness h = WithPlayers(log, 1);

        Assert.False(Start(h, 1));   // the player and its bot partner: one team
        Assert.Contains(log, l => l == "can't start: fewer than two teams");
        Assert.Single(Host(h).Room!.Players);

        Assert.True(Start(h, 2));   // a second bot: team 1
        Assert.Equal(new byte[] { 0, 0, 1 }, Teams(h));
        Assert.Equal(1, h.Server.Rules!.Of(2).Team);
    }

    [Fact]
    public void Two_players_on_one_team_need_another_team_to_start()
    {
        var log = new List<string>();
        BattleHarness h = WithPlayers(log, 2);
        SetTeam(h, 0, "3");
        SetTeam(h, 1, "3");
        Assert.Equal(new byte[] { 3, 3 }, Teams(h));

        Assert.False(Start(h, 0));
        Assert.Contains(log, l => l == "can't start: fewer than two teams");

        SetTeam(h, 1, "4");
        Assert.True(Start(h, 0));
        Assert.Equal(((byte)3, (byte)4), (h.Server.Rules!.Of(0).Team, h.Server.Rules.Of(1).Team));
    }
}

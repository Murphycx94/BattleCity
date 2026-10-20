using Tank.Client.Match;
using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class MatchHudFrameTests
{
    private const double Frame = 1.0 / 60;

    private static void Step(BattleSession session, AutoRoom auto)
    {
        BattleClient client = session.Client;
        session.Advance(Frame);
        auto.Next(client.Room, client.Slot, client.IsHost, 1).SendTo(client);
        client.Update(Frame, LocalCommand.None);
    }

    [Fact]
    public void A_frame_from_a_running_match_has_the_mode_the_clock_and_the_roster()
    {
        var settings = new HostSettings("Tester", "Tester's room", Port: 47101, BattleMode.TeamBattle, Bots: 3, Seed: 7, MatchSeconds: null);
        using BattleSession session = BattleSession.HostLoopback(settings, _ => { });
        var auto = new AutoRoom(null, BattleMode.TeamBattle, 3, autoStart: true);
        double startedAt = -1;
        for (int i = 0; i < 12 * 60 && (startedAt < 0 || session.Now - startedAt < 5); i++)
        {
            Step(session, auto);
            if (startedAt < 0 && session.Client.Phase == BattlePhase.InMatch && session.Client.Latest is not null) startedAt = session.Now;
        }
        Assert.True(startedAt >= 0, "the match never started");
        BattleClient client = session.Client;
        var names = new string?[BattleProtocol.MaxPlayers];

        MatchHudFrame frame = MatchHudFrame.From(client, session, ownTeam: 0, names, fps: 60);

        Assert.Equal(BattleMode.TeamBattle, frame.Mode);
        Assert.Equal(MatchPhase.Playing, frame.Phase);
        int expected = ModeRules.DurationTicks(BattleMode.TeamBattle) - 5 * Tank.Sim.SimConstants.TickRate;
        Assert.InRange(frame.TicksLeft, expected - 3 * Tank.Sim.SimConstants.TickRate, expected + 3 * Tank.Sim.SimConstants.TickRate);
        Assert.Matches("^6:[45][0-9]$|^7:00$", HudText.Timer(frame.TicksLeft));
        Assert.Equal(client.Slot, frame.OwnSlot);
        Assert.Equal(4, frame.Players.Count(p => p.Active));   // the player and three bots
        Assert.Same(names, frame.Names);
        Assert.Equal(BannerKind.None, frame.Banner);
        Assert.Equal(PlateKind.TeamKills, frame.Plate.Kind);
        Assert.False(frame.Net.HasServerTick);   // a loopback host has no tick thread to time
        Assert.Equal(60, frame.Net.Fps);
    }

    [Fact]
    public void Before_any_snapshot_the_frame_is_empty_but_safe()
    {
        var settings = new HostSettings("Tester", "Tester's room", Port: 47102, BattleMode.FreeForAll, Bots: 0, Seed: 7, MatchSeconds: null);
        using BattleSession session = BattleSession.HostLoopback(settings, _ => { });

        MatchHudFrame frame = MatchHudFrame.From(session.Client, session, 0, new string?[BattleProtocol.MaxPlayers], fps: 0);

        Assert.Equal(0, frame.TicksLeft);
        Assert.Equal(BannerKind.None, frame.Banner);
        Assert.Equal(BattleProtocol.MaxPlayers, frame.Players.Length);
    }

    [Fact]
    public void The_spectate_target_turns_the_banner_to_the_spectate_bar()
    {
        var settings = new HostSettings("Tester", "Tester's room", Port: 47103, BattleMode.Annihilation, Bots: 0, Seed: 7, MatchSeconds: null);
        using BattleSession session = BattleSession.HostLoopback(settings, _ => { });

        MatchHudFrame frame = MatchHudFrame.From(session.Client, session, 0, new string?[BattleProtocol.MaxPlayers], 60, spectateTarget: "机器人 1");

        Assert.Equal(BannerKind.Spectate, frame.Banner);
        Assert.Equal("机器人 1", frame.SpectateTarget);
    }

    [Fact]
    public void A_tank_id_finds_its_slot_among_the_active_players_only()
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        players[2] = new PlayerStats { Active = true, TankId = 7 };
        players[4] = new PlayerStats { Active = false, TankId = 9 };

        Assert.Equal(2, MatchHudFrame.SlotOf(players, 7));
        Assert.Equal(-1, MatchHudFrame.SlotOf(players, 9));
        Assert.Equal(-1, MatchHudFrame.SlotOf(players, 0));
    }
}

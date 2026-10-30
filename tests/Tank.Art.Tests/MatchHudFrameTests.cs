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
    public void The_first_frame_of_a_standard_match_has_no_bars_no_reason_and_the_living_on_the_plate()
    {
        var settings = new HostSettings("Tester", "Tester's room", Port: 47104, BattleMode.Standard, Bots: 3, Seed: 7, MatchSeconds: null);
        using BattleSession session = BattleSession.HostLoopback(settings, _ => { });
        var auto = new AutoRoom(null, BattleMode.Standard, 3, autoStart: true);
        BattleClient client = session.Client;
        for (int i = 0; i < 12 * 60 && !(client.Phase == BattlePhase.InMatch && client.Latest is not null); i++) Step(session, auto);
        Assert.True(client.Phase == BattlePhase.InMatch && client.Latest is not null, "the match never started");

        MatchHudFrame frame = MatchHudFrame.From(client, session, ownTeam: 0, new string?[BattleProtocol.MaxPlayers], fps: 60);

        Assert.Equal(BattleMode.Standard, frame.Mode);
        Assert.Equal(0, frame.Capture.Count);
        Assert.Equal(MatchEndReason.None, frame.Results.Reason);
        Assert.Equal(PlateKind.TeamAlive, frame.Plate.Kind);
        Assert.Equal(4, frame.Plate.LeftOf + frame.Plate.RightOf);   // the player and three bots, none of them down yet
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
    public void The_zone_is_handed_on_as_given_and_is_off_by_default()
    {
        var settings = new HostSettings("Tester", "Tester's room", Port: 47106, BattleMode.FreeForAll, Bots: 0, Seed: 7, MatchSeconds: null);
        using BattleSession session = BattleSession.HostLoopback(settings, _ => { });
        var names = new string?[BattleProtocol.MaxPlayers];
        var zone = new ZoneHud(true, 3, ZonePhase.Shrinking, 30, true, 4);

        MatchHudFrame given = MatchHudFrame.From(session.Client, session, 0, names, 60, zone: zone);
        MatchHudFrame plain = MatchHudFrame.From(session.Client, session, 0, names, 60);

        Assert.Equal(zone, given.Zone);
        Assert.Equal(default, plain.Zone);
        Assert.False(plain.Zone.On);
        Assert.Equal(0, plain.Results.Entrants);
    }

    /// <summary>A royale on the riverland, the player and three bots: the frame's results carry the four entrants from the start (deviation 8).</summary>
    [Fact]
    public void A_royale_frame_counts_its_entrants()
    {
        var settings = new HostSettings("Tester", "Tester's room", Port: 47107, BattleMode.Royale, Bots: 3, Seed: 7, MatchSeconds: null, Map: Tank.Sim.Maps.RiverlandMap.Id);
        using BattleSession session = BattleSession.HostLoopback(settings, _ => { });
        var auto = new AutoRoom(null, BattleMode.Royale, 3, autoStart: true);
        BattleClient client = session.Client;
        double startedAt = -1;
        for (int i = 0; i < 20 * 60 && (startedAt < 0 || session.Now - startedAt < 3); i++)
        {
            Step(session, auto);
            if (startedAt < 0 && client.Phase == BattlePhase.InMatch && client.Latest is not null) startedAt = session.Now;
        }
        Assert.True(startedAt >= 0, "the match never started");

        MatchHudFrame frame = MatchHudFrame.From(client, session, 0, new string?[BattleProtocol.MaxPlayers], fps: 60);

        Assert.Equal(BattleMode.Royale, frame.Mode);
        Assert.Equal(MatchPhase.Playing, frame.Phase);
        Assert.Equal(4, frame.Players.Count(p => p.Active));
        Assert.Equal(4, frame.Results.Entrants);
        Assert.Equal(PlateKind.SoloKills, frame.Plate.Kind);
        Assert.Equal((4, 4), (frame.Plate.Right, frame.Plate.RightOf));
        Assert.True(client.HasZone, "the royale's match start carried its zone");
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

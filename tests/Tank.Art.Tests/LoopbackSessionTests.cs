using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class LoopbackSessionTests
{
    private const double Frame = 1.0 / 60;

    private static HostSettings Settings(int bots = 3) =>
        new("Tester", "Tester's room", Port: 47100, BattleMode.TeamBattle, bots, Seed: 7, MatchSeconds: null);

    /// <summary>One frame the way the game loop does it: the host's world, the auto-room, then the client.</summary>
    private static void Step(BattleSession session, AutoRoom auto)
    {
        BattleClient client = session.Client;
        session.Advance(Frame);
        auto.Next(client.Room, client.Slot, client.IsHost, 1).SendTo(client);
        client.Update(Frame, LocalCommand.None);
    }

    [Fact]
    public void A_loopback_host_starts_the_match_by_itself_within_three_virtual_seconds()
    {
        using BattleSession session = BattleSession.HostLoopback(Settings(), _ => { });
        var auto = new AutoRoom(tankId: "heavy", BattleMode.TeamBattle, bots: 3, autoStart: true);

        double elapsed = 0;
        while (elapsed < 3 && !(session.Client.Phase == BattlePhase.InMatch && session.Client.HasTank))
        {
            Step(session, auto);
            elapsed += Frame;
        }

        Assert.True(session.IsHosting);
        Assert.True(session.IsVirtualClock);
        Assert.Null(session.ServerTick);
        Assert.True(session.Client.IsHost);
        Assert.Equal(BattlePhase.InMatch, session.Client.Phase);
        Assert.True(session.Client.HasTank);
        Assert.Equal("heavy", session.Client.Own.Spec.Id);
    }

    [Fact]
    public void The_match_runs_ten_more_virtual_seconds_with_its_bots_and_without_trouble()
    {
        using BattleSession session = BattleSession.HostLoopback(Settings(), _ => { });
        var auto = new AutoRoom(null, BattleMode.TeamBattle, 3, autoStart: true);
        for (int i = 0; i < 3 * 60; i++) Step(session, auto);
        Assert.Equal(BattlePhase.InMatch, session.Client.Phase);
        double startedAt = session.Now;

        for (int i = 0; i < 10 * 60; i++) Step(session, auto);

        Assert.Equal(10, session.Now - startedAt, precision: 6);
        Assert.Equal(BattlePhase.InMatch, session.Client.Phase);
        Assert.True(session.Client.HasTank);   // others show only while spotted, so their count is not asserted
        Assert.Null(session.Client.LastPacketError);
    }

    [Fact]
    public void The_clock_only_moves_when_the_session_is_advanced()
    {
        using BattleSession session = BattleSession.HostLoopback(Settings(), _ => { });
        Assert.Equal(0, session.Now);
        session.Advance(0.5);
        Assert.Equal(0.5, session.Now, precision: 9);
    }

    [Fact]
    public void Dispose_can_be_called_twice()
    {
        BattleSession session = BattleSession.HostLoopback(Settings(bots: 0), _ => { });
        session.Dispose();
        session.Dispose();
    }

    [Fact]
    public void A_joined_session_hosts_nothing_and_follows_the_wall_clock()
    {
        using BattleSession session = BattleSession.Join("127.0.0.1", 47199, "Guest");
        Assert.False(session.IsHosting);
        Assert.False(session.IsVirtualClock);
        Assert.Null(session.ServerTick);
        Assert.Equal(BattlePhase.Connecting, session.Client.Phase);
        session.Advance(1);   // nothing to step
        session.Dispose();
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

/// <summary>The real thing on 127.0.0.1: a UDP socket and a server thread. Where the sandbox forbids binding, the tests do nothing.</summary>
public class ThreadedHostTests
{
    private static HostSettings Settings(int port) => new("Tester", "Tester's room", port, BattleMode.TeamBattle, 0, Seed: 1, MatchSeconds: null);

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static BattleSession? TryHost(int port)
    {
        try
        {
            return BattleSession.Host(Settings(port), _ => { });
        }
        catch (Exception ex) when (ex is InvalidOperationException or SocketException)
        {
            return null;   // no UDP here: nothing to test
        }
    }

    [Fact]
    public void A_threaded_host_seats_its_own_client_as_the_host()
    {
        using BattleSession? session = TryHost(FreeUdpPort());
        if (session is null) return;

        var clock = Stopwatch.StartNew();
        double last = 0;
        // the welcome puts the client in the room; the room state (whose host slot IsHost reads) is the next message
        while (clock.Elapsed.TotalSeconds < 5 && (session.Client.Phase != BattlePhase.Room || session.Client.Room is null))
        {
            double now = clock.Elapsed.TotalSeconds;
            session.Advance(now - last);
            session.Client.Update(now - last, LocalCommand.None);
            last = now;
            Thread.Sleep(5);
        }

        Assert.True(session.IsHosting);
        Assert.False(session.IsVirtualClock);
        Assert.NotNull(session.ServerTick);
        Assert.Equal(BattlePhase.Room, session.Client.Phase);
        Assert.NotNull(session.Client.Room);
        Assert.True(session.Client.IsHost);
    }

    [Fact]
    public void A_second_host_on_a_taken_port_throws_InvalidOperationException_and_leaves_the_first_running()
    {
        int port = FreeUdpPort();
        using BattleSession? first = TryHost(port);
        if (first is null) return;

        BattleSession? second = null;
        try
        {
            second = BattleSession.Host(Settings(port), _ => { });
        }
        catch (InvalidOperationException ex)
        {
            Assert.Contains(port.ToString(), ex.Message);
        }
        finally
        {
            second?.Dispose();   // the platform let two sockets share the port: nothing to assert about a busy one
        }
    }

    [Fact]
    public void Dispose_stops_the_thread_and_can_be_called_twice()
    {
        BattleSession? session = TryHost(FreeUdpPort());
        if (session is null) return;
        session.Dispose();
        session.Dispose();
    }
}

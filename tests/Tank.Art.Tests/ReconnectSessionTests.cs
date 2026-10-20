using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Xunit.Abstractions;

namespace Tank.Art.Tests;

/// <summary>
/// The reconnect drill (blueprint §5 T10), over real UDP on 127.0.0.1: a guest joins a threaded host, the match starts,
/// a client's connection is torn down, and <see cref="BattleSession.Reconnect"/> — a new client with the same session
/// token — gets the same seat back mid-match (4A's token reconnect and <c>TakeOver</c>): the guest's, and the host's own
/// client, whose server keeps running on its thread meanwhile. The server's log goes to the test output, so a detailed
/// run is the drill's record. Where the sandbox forbids binding, the tests do nothing, like <see cref="ThreadedHostTests"/>.
/// </summary>
public class ReconnectSessionTests
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _serverLog = new();   // written from the server thread: lock it

    public ReconnectSessionTests(ITestOutputHelper output) => _output = output;

    private void ServerLog(string line)
    {
        lock (_serverLog) _serverLog.Add(line);
        _output.WriteLine($"[server] {line}");
    }

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private BattleSession? TryHost(int port)
    {
        try
        {
            return BattleSession.Host(new HostSettings("Host", "Host's room", port, BattleMode.FreeForAll, Bots: 0, Seed: 7, MatchSeconds: null), ServerLog);
        }
        catch (Exception ex) when (ex is InvalidOperationException or SocketException)
        {
            return null;   // no UDP here: nothing to test
        }
    }

    /// <summary>Pumps both sessions on the wall clock until <paramref name="done"/> or <paramref name="seconds"/> run out.</summary>
    private static bool Pump(BattleSession host, AutoRoom hostAuto, BattleSession guest, double seconds, Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        double last = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            if (done()) return true;
            double now = clock.Elapsed.TotalSeconds;
            double dt = now - last;
            last = now;
            host.Advance(dt);
            hostAuto.Next(host.Client.Room, host.Client.Slot, host.Client.IsHost, humansWanted: 2).SendTo(host.Client);
            host.Client.Update(dt, LocalCommand.None);
            guest.Advance(dt);
            guest.Client.Update(dt, LocalCommand.None);
            Thread.Sleep(5);
        }
        return done();
    }

    [Fact]
    public void A_guest_reconnecting_mid_match_with_its_token_gets_the_same_seat_back()
    {
        int port = FreeUdpPort();
        using BattleSession? host = TryHost(port);
        if (host is null) return;
        var hostAuto = new AutoRoom(null, BattleMode.FreeForAll, bots: null, autoStart: true);
        using BattleSession guest = BattleSession.Join("127.0.0.1", port, "Guest");

        Assert.True(Pump(host, hostAuto, guest, 10, () => guest.Client.Phase == BattlePhase.InMatch && guest.Client.HasTank),
            $"the match never started: host {host.Client.Phase}, guest {guest.Client.Phase}");
        byte slot = guest.Client.Slot;
        uint token = guest.Client.SessionToken;
        Assert.NotEqual(0u, token);
        _output.WriteLine($"[guest] in the match as slot {slot}, tank {guest.Client.OwnTankId}, token {token:x8}; reconnecting");

        guest.Reconnect();   // a fresh client and connection, the same token: the server's TakeOver path

        Assert.True(Pump(host, hostAuto, guest, 10, () => guest.Client.Phase == BattlePhase.InMatch && guest.Client.HasTank),
            $"the guest never made it back: {guest.Client.Phase} ({guest.Client.DisconnectReason})");
        _output.WriteLine($"[guest] back in the match as slot {guest.Client.Slot}, tank {guest.Client.OwnTankId}, token {guest.Client.SessionToken:x8}");
        Assert.Equal(slot, guest.Client.Slot);
        Assert.Equal(token, guest.Client.SessionToken);
        // Which line the server writes depends on whether the old connection's goodbye beats the new hello: a take-over
        // ("reconnecting over peer") or a seat found disconnected ("reconnected"). Either way it found the seat by token.
        lock (_serverLog) Assert.Contains(_serverLog, l => l.StartsWith($"slot {slot} ") && l.Contains("reconnect"));
    }

    [Fact]
    public void A_host_reconnecting_its_own_client_mid_match_keeps_its_seat_and_the_room()
    {
        int port = FreeUdpPort();
        using BattleSession? host = TryHost(port);
        if (host is null) return;
        var hostAuto = new AutoRoom(null, BattleMode.FreeForAll, bots: null, autoStart: true);
        using BattleSession guest = BattleSession.Join("127.0.0.1", port, "Guest");

        Assert.True(Pump(host, hostAuto, guest, 10, () => host.Client.Phase == BattlePhase.InMatch && host.Client.HasTank && guest.Client.HasTank),
            $"the match never started: host {host.Client.Phase}, guest {guest.Client.Phase}");
        byte slot = host.Client.Slot;
        uint token = host.Client.SessionToken;
        Assert.True(host.Client.IsHost);
        _output.WriteLine($"[host] in the match as slot {slot}, tank {host.Client.OwnTankId}, token {token:x8}; reconnecting its own client");

        host.Reconnect();   // what Main does after the host's own link timed out: the server thread never stopped

        Assert.True(Pump(host, hostAuto, guest, 10, () => host.Client.Phase == BattlePhase.InMatch && host.Client.HasTank && host.Client.IsHost),
            $"the host never made it back: {host.Client.Phase} ({host.Client.DisconnectReason})");
        _output.WriteLine($"[host] back in the match as slot {host.Client.Slot}, tank {host.Client.OwnTankId}, host {host.Client.IsHost}");
        Assert.True(host.IsHosting);
        Assert.Equal(slot, host.Client.Slot);
        Assert.Equal(token, host.Client.SessionToken);
        Assert.Equal(BattlePhase.InMatch, guest.Client.Phase);   // the room never closed under the guest
        lock (_serverLog) Assert.Contains(_serverLog, l => l.StartsWith($"slot {slot} ") && l.Contains("reconnect"));
    }
}

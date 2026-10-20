using System.Diagnostics;
using Tank.Net.Battle;
using Tank.Net.Server;
using Tank.Net.Transport;
using Tank.Sim;

namespace Tank.Client.Online;

/// <summary>
/// The listen server of a real room: a UDP transport and a <see cref="BattleServer"/> stepped at the simulation rate on
/// its own thread, so a hitch of the window (a drag, a shader compile) never stalls the other players. The host's client
/// connects to it over 127.0.0.1 like everyone else. The ticks are timed by a <see cref="TickProbe"/> for the F3 panel.
/// </summary>
internal sealed class ThreadedHost : IHostBackend
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly LiteNetServerTransport _transport;
    private readonly ServerRunner _runner;
    private readonly TickProbe _probe;
    private readonly int _port;
    private bool _disposed;

    /// <summary>Binds the port and starts the server thread. Throws <see cref="InvalidOperationException"/> when the port is taken.</summary>
    public ThreadedHost(HostSettings settings, uint ownerToken, Action<string> log)
    {
        BattleServer? server = null;
        // Discovery replies come from the runner thread's Poll, after `server` is assigned below.
        var transport = new LiteNetServerTransport(settings.Port, null, BattleProtocol.MaxPlayers, BattleProtocol.ConnectKey,
            w => server!.WriteRoomInfo(w));
        try
        {
            transport.Start();
            _port = settings.Port == 0 ? transport.Manager.LocalPort : settings.Port;
            server = new BattleServer(transport, new BattleServerOptions
            {
                RoomName = settings.RoomName,
                Port = _port,
                Mode = settings.Mode,
                Bots = settings.Bots,
                OwnerToken = ownerToken,
                Seed = settings.Seed,
                MatchSeconds = settings.MatchSeconds,
            }, log);
            _probe = new TickProbe(server, () => server!.MatchRunning);
            _runner = new ServerRunner(_probe, log, SimConstants.TickRate);
            _runner.Start();
        }
        catch
        {
            transport.Dispose();
            throw;
        }
        _transport = transport;
    }

    public bool IsVirtualClock => false;

    public double Now => _clock.Elapsed.TotalSeconds;

    public (double AvgMs, double MaxMs)? ServerTick => (_probe.AverageMs, _probe.MaxMs);

    public IClientTransport ConnectLocal() => new LiteNetClientTransport("127.0.0.1", _port, BattleProtocol.ConnectKey);

    public void Advance(double dt) { }

    /// <summary>Stops the thread first (up to 2 s), then closes the socket: closing it under a running poll would race.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runner.Dispose();
        _transport.Dispose();
    }
}

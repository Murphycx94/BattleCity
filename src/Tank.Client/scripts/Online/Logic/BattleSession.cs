using System.Diagnostics;
using System.Security.Cryptography;
using Tank.Net.Battle;
using Tank.Net.Transport;

namespace Tank.Client.Online;

/// <summary>
/// One way into a battle: a <see cref="BattleClient"/> plus, when this process hosts the room, the server it talks to.
/// Hosting runs the server on its own thread (<see cref="Host"/>) or, for tests and screenshots, on a virtual network
/// stepped from the main thread (<see cref="HostLoopback"/>). The owner calls <see cref="Advance"/> every frame and
/// <c>Client.Update</c> itself; nothing here touches the engine.
/// </summary>
public sealed class BattleSession : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly IHostBackend? _backend;
    private readonly Func<IClientTransport> _connect;
    private readonly string _playerName;
    private readonly Func<double> _now;
    private bool _disposed;

    private BattleSession(IHostBackend? backend, Func<IClientTransport> connect, string playerName, uint token)
    {
        _backend = backend;
        _connect = connect;
        _playerName = playerName;
        _now = () => Now;
        Client = new BattleClient(connect(), playerName, _now, token);
    }

    public BattleClient Client { get; private set; }

    public bool IsHosting => _backend is not null;

    /// <summary>True for a loopback host: time only moves in <see cref="Advance"/>, so the caller steps frames by a fixed amount.</summary>
    public bool IsVirtualClock => _backend?.IsVirtualClock ?? false;

    /// <summary>Seconds, monotonic; the clock the client runs on.</summary>
    public double Now => _backend?.Now ?? _clock.Elapsed.TotalSeconds;

    /// <summary>Average and worst server tick in ms; only when hosting on a thread.</summary>
    public (double AvgMs, double MaxMs)? ServerTick => _backend?.ServerTick;

    /// <summary>
    /// Starts a listen server and joins it as its owner. Throws <see cref="InvalidOperationException"/> (the message is
    /// fit for the menu's status line) when the UDP port is taken, e.g. by a second instance on this machine.
    /// </summary>
    public static BattleSession Host(HostSettings settings, Action<string> log) =>
        Start(settings, owner => new ThreadedHost(settings, owner, log));

    /// <summary>A host on a virtual network and clock: no sockets, no threads, the same match for the same seed.</summary>
    public static BattleSession HostLoopback(HostSettings settings, Action<string> log) =>
        Start(settings, owner => new LoopbackHost(settings, owner, log));

    /// <summary>Connects to a room. A non-zero <paramref name="token"/> reclaims the seat it was issued for.</summary>
    public static BattleSession Join(string host, int port, string name, uint token = 0) =>
        new(null, () => new LiteNetClientTransport(host, port, BattleProtocol.ConnectKey), name, token);

    /// <summary>Moves the host's world on: a loopback host steps its network and server; a threaded host runs by itself.</summary>
    public void Advance(double dt) => _backend?.Advance(dt);

    /// <summary>
    /// Replaces a dead client with a fresh one over a new connection, carrying the same session token: the server finds
    /// the seat by it within its reconnect grace window and hands the same slot back (blueprint §5 T10). The old client
    /// is disposed. When and how often to try is the caller's (<see cref="Reconnector"/>'s) business.
    /// </summary>
    public void Reconnect()
    {
        BattleClient old = Client;
        Client = new BattleClient(_connect(), _playerName, _now, old.SessionToken);
        old.Dispose();
    }

    /// <summary>Closes the client, then stops the server thread, then the socket — the server even if the client throws. Safe to call again.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            Client.Dispose();
        }
        finally
        {
            _backend?.Dispose();
        }
    }

    private static BattleSession Start(HostSettings settings, Func<uint, IHostBackend> startBackend)
    {
        uint owner = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);   // it reclaims the host's seat, so as unguessable as the server's own tokens
        IHostBackend backend = startBackend(owner);
        try
        {
            return new BattleSession(backend, backend.ConnectLocal, settings.PlayerName, owner);
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }
}

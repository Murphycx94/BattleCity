using System.Diagnostics;
using Tank.Core.World;
using Tank.Net.Client;
using Tank.Net.Server;
using Tank.Net.Transport;

namespace Tank.Client.Bootstrap;

/// <summary>
/// Owns the network client and, when hosting, the embedded listen server. The server runs on its own thread
/// (ServerRunner) and the host connects to it over UDP loopback, exactly like everyone else (docs/03 §8.2).
/// </summary>
public sealed class Session : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private LiteNetServerTransport? _serverTransport;
    private ServerRunner? _runner;
    private GameServer? _server;

    public GameClient? Client { get; private set; }
    public bool IsHosting => _server is not null;
    public ServerRunner? Runner => _runner;
    public double Now => _clock.Elapsed.TotalSeconds;

    /// <summary>Starts a listen server and joins it. Throws InvalidOperationException if the port is taken.</summary>
    public void Host(string playerName, string roomName, int port, GameMode mode)
    {
        Leave();
        GameServer? server = null;
        var transport = new LiteNetServerTransport(port, new RoomInfoRelay(() => server!));
        uint ownerToken = (uint)Random.Shared.Next(1, int.MaxValue);
        server = new GameServer(transport, new ServerOptions { RoomName = roomName, Port = port, Mode = mode, OwnerToken = ownerToken },
            msg => Godot.GD.Print($"[server] {msg}"));
        transport.Start();
        _serverTransport = transport;
        _server = server;
        _runner = new ServerRunner(server, msg => Godot.GD.PrintErr($"[server] {msg}"));
        _runner.Start();
        Join("127.0.0.1", port, playerName, ownerToken);
    }

    public void Join(string host, int port, string playerName, uint token = 0)
    {
        Client?.Dispose();
        Client = new GameClient(new LiteNetClientTransport(host, port), playerName, () => Now, token);
    }

    public void Leave()
    {
        Client?.Dispose();
        Client = null;
        _runner?.Dispose();
        _runner = null;
        _serverTransport?.Dispose();
        _serverTransport = null;
        _server = null;
    }

    public void Dispose() => Leave();
}

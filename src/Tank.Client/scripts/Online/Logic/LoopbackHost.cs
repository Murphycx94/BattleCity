using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Maps;

namespace Tank.Client.Online;

/// <summary>
/// A room without sockets or threads: the server and its clients share a virtual <see cref="LoopbackNetwork"/>, and
/// <see cref="Advance"/> moves its clock and ticks the server every 1/30 s. The same input gives the same match, which
/// is what smoke runs and screenshots need. Mirrors the netcode tests' harness.
/// </summary>
internal sealed class LoopbackHost : IHostBackend
{
    private const double ServerDt = 1.0 / SimConstants.TickRate;
    private const double TickSlack = 1e-9;   // a frame length of 1/60 summed twice must still make one 1/30 tick

    private readonly LoopbackNetwork _net;
    private readonly BattleServer _server;
    private double _accumulator;

    public LoopbackHost(HostSettings settings, uint ownerToken, Action<string> log)
    {
        _net = new LoopbackNetwork(settings.Seed ?? 1);
        _server = new BattleServer(_net.CreateServer(), new BattleServerOptions
        {
            RoomName = settings.RoomName,
            Port = settings.Port,
            Mode = settings.Mode,
            Bots = settings.Bots,
            OwnerToken = ownerToken,
            Seed = settings.Seed,
            MatchSeconds = settings.MatchSeconds,
            Map = MapCatalog.Get(settings.Map),   // generated right here, before the first tick: a loopback run is a script and can wait
        }, log);
    }

    public bool IsVirtualClock => true;

    public double Now => _net.Now;

    public (double AvgMs, double MaxMs)? ServerTick => null;

    public IClientTransport ConnectLocal() => _net.Connect();

    public void Advance(double dt)
    {
        _net.AdvanceTime(dt);
        _accumulator += dt;
        while (_accumulator >= ServerDt - TickSlack)
        {
            _server.Tick();
            _accumulator -= ServerDt;
        }
    }

    public void Dispose() { }
}

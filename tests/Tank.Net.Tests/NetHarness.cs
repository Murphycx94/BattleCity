using Tank.Core;
using Tank.Core.World;
using Tank.Net.Client;
using Tank.Net.Server;
using Tank.Net.Transport;

namespace Tank.Net.Tests;

/// <summary>One server and N clients on a deterministic simulated network with a virtual clock.</summary>
internal sealed class NetHarness
{
    private const double ServerDt = 1.0 / Units.TickRate;
    private double _serverAccumulator;

    public NetHarness(ServerOptions? options = null, uint networkSeed = 1)
    {
        Net = new LoopbackNetwork(networkSeed);
        Server = new GameServer(Net.CreateServer(), options ?? new ServerOptions { Seed = 1, EnemyTotal = 0 });
    }

    public LoopbackNetwork Net { get; }
    public GameServer Server { get; }
    public List<GameClient> Clients { get; } = new();
    public List<LoopbackClientTransport> Links { get; } = new();

    public GameClient AddClient(string name, LinkConditions? up = null, LinkConditions? down = null, uint token = 0)
    {
        var link = Net.Connect(up, down);
        var client = new GameClient(link, name, () => Net.Now, token);
        Links.Add(link);
        Clients.Add(client);
        return client;
    }

    public void Run(double seconds, Func<int, double, LocalInput>? input = null, double frameDt = 1.0 / 120)
    {
        int frames = (int)System.Math.Round(seconds / frameDt);
        for (int f = 0; f < frames; f++)
        {
            Net.AdvanceTime(frameDt);
            _serverAccumulator += frameDt;
            while (_serverAccumulator >= ServerDt - 1e-9)
            {
                Server.Tick();
                _serverAccumulator -= ServerDt;
            }
            for (int i = 0; i < Clients.Count; i++)
                Clients[i].Update(frameDt, input?.Invoke(i, Net.Now) ?? LocalInput.None);
        }
    }

    public void StartMatch(GameMode mode = GameMode.Coop)
    {
        Run(0.3);
        // Whoever's hello arrived first is host; with jitter that is not necessarily client 0.
        var host = Clients.Single(c => c.IsHost);
        host.SetMode(mode);
        host.StartMatch();
        Run(1.0);
    }

    public TankState ServerTankOf(GameClient client)
    {
        var s = Server.Simulation!.State;
        int index = s.FindTankIndex(s.Players[client.Slot].TankId);
        Assert.True(index >= 0, "client has no tank on the server");
        return s.Tanks[index];
    }
}

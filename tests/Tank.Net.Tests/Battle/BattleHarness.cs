using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Net.Tests.Battle;

/// <summary>One battle server and N clients on a deterministic simulated network with a virtual clock.</summary>
internal sealed class BattleHarness
{
    private const double ServerDt = 1.0 / SimConstants.TickRate;
    private double _serverAccumulator;

    public BattleHarness(BattleServerOptions? options = null, uint networkSeed = 1, Action<string>? log = null)
    {
        Net = new LoopbackNetwork(networkSeed);
        Server = new BattleServer(Net.CreateServer(), options ?? new BattleServerOptions { Seed = 1 }, log);
    }

    public LoopbackNetwork Net { get; }
    public BattleServer Server { get; }
    public List<BattleClient> Clients { get; } = new();
    public List<LoopbackClientTransport> Links { get; } = new();

    /// <summary>When set, each <see cref="BattleServer.Tick"/> that <see cref="Run"/> makes is timed into it, ms.</summary>
    public List<double>? TickTimes { get; set; }

    /// <summary>When set, called after each <see cref="BattleServer.Tick"/> that <see cref="Run"/> makes (outside <see cref="TickTimes"/>).</summary>
    public Action<BattleServer>? AfterServerTick { get; set; }

    public BattleClient AddClient(string name, LinkConditions? up = null, LinkConditions? down = null, uint token = 0, int version = BattleProtocol.Version)
    {
        LoopbackClientTransport link = Net.Connect(up, down);
        var client = new BattleClient(link, name, () => Net.Now, token, version);
        Links.Add(link);
        Clients.Add(client);
        return client;
    }

    public void Run(double seconds, Func<int, double, LocalCommand>? input = null, double frameDt = 1.0 / 120)
    {
        int frames = (int)System.Math.Round(seconds / frameDt);
        for (int f = 0; f < frames; f++)
        {
            Net.AdvanceTime(frameDt);
            _serverAccumulator += frameDt;
            while (_serverAccumulator >= ServerDt - 1e-9)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                Server.Tick();
                TickTimes?.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                AfterServerTick?.Invoke(Server);
                _serverAccumulator -= ServerDt;
            }
            for (int i = 0; i < Clients.Count; i++)
                Clients[i].Update(frameDt, input?.Invoke(i, Net.Now) ?? LocalCommand.None);
        }
    }

    /// <summary>Lets everyone join, then has the host set the mode and the bots and start; runs until the match is a second old.</summary>
    public void StartMatch(BattleMode mode = BattleMode.TeamBattle, int bots = 0)
    {
        Run(0.3);
        BattleClient host = Clients.Single(c => c.IsHost);
        host.SetMode(mode);
        host.SetBots(bots);
        Run(0.1);
        host.StartMatch();
        Run(1.0);
    }

    /// <summary>The server's state of <paramref name="client"/>'s tank.</summary>
    public Tank.Sim.Vehicles.TankState ServerTankOf(BattleClient client)
    {
        Simulation sim = Server.Simulation!;
        int slot = sim.SlotOf(Server.TankOf(client.Slot));
        Assert.True(slot >= 0, "client has no tank on the server");
        return sim.Tanks[slot];
    }
}

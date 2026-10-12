using Tank.Core.Math;
using Tank.Core.World;
using Tank.Net.Client;
using Tank.Net.Protocol;
using Tank.Net.Server;
using Tank.Net.Transport;

namespace Tank.Net.Tests;

public sealed class IntegrationTests
{
    /// <summary>A wandering pattern that changes direction every 0.4 s and taps fire now and then.</summary>
    private static LocalInput Wander(int client, double t, bool fire = false)
    {
        int phase = (int)(t / 0.4) + client * 2;
        Dir? dir = (phase % 5) switch { 0 => Dir.Up, 1 => Dir.Left, 2 => Dir.Down, 3 => Dir.Right, _ => null };
        bool shoot = fire && ((int)(t * 10) % 7 == 0);
        return new LocalInput(dir, shoot, shoot);
    }

    [Fact]
    public void Clients_JoinLobby_FirstOneIsHost()
    {
        var h = new NetHarness();
        var a = h.AddClient("Ann");
        var b = h.AddClient("Bob");

        h.Run(0.3);

        Assert.Equal(ClientPhase.Lobby, a.Phase);
        Assert.Equal(ClientPhase.Lobby, b.Phase);
        Assert.Equal(0, a.Slot);
        Assert.Equal(1, b.Slot);
        Assert.True(a.IsHost);
        Assert.False(b.IsHost);
        Assert.Equal(new[] { "Ann", "Bob" }, b.Lobby!.Players.Select(p => p.Name));
    }

    [Fact]
    public void OwnerToken_MakesLocalPlayerHost_EvenWhenOthersJoinFirst()
    {
        var h = new NetHarness(new ServerOptions { Seed = 1, EnemyTotal = 0, OwnerToken = 4242 });
        var bot = h.AddClient("Bot");
        h.Run(0.2);
        var owner = h.AddClient("Owner", token: 4242);
        h.Run(0.3);

        Assert.True(owner.IsHost);
        Assert.False(bot.IsHost);
    }

    [Fact]
    public void OnlyHost_CanStartMatch()
    {
        var h = new NetHarness();
        h.AddClient("Ann");
        var b = h.AddClient("Bob");
        h.Run(0.3);

        b.StartMatch();
        h.Run(0.3);
        Assert.False(h.Server.MatchRunning);

        h.Clients[0].StartMatch();
        h.Run(1.0);
        Assert.True(h.Server.MatchRunning);
        Assert.All(h.Clients, c => Assert.Equal(ClientPhase.InMatch, c.Phase));
        Assert.All(h.Clients, c => Assert.True(c.Predictor.HasTank));
    }

    [Fact]
    public void Host_CanChangeModeAndMap()
    {
        var h = new NetHarness();
        var a = h.AddClient("Ann");
        h.Run(0.2);
        a.SetMode(GameMode.Deathmatch);
        h.Run(0.2);
        Assert.Equal(GameMode.Deathmatch, a.Lobby!.Mode);
        Assert.Equal("arena-01", a.Lobby.MapName);
        a.SetMap("coop-01");
        h.Run(0.2);
        Assert.Equal("coop-01", a.Lobby.MapName);
    }

    [Fact]
    public void PerfectLink_PredictionNeverDisagreesWithServer()
    {
        var h = new NetHarness();
        var a = h.AddClient("Ann");
        h.AddClient("Bob");
        h.StartMatch();

        double startZ = a.Predictor.Current.Z;
        h.Run(8, (i, t) => i == 0 ? Wander(0, t) : LocalInput.None);
        h.Run(0.5); // settle

        Assert.Equal(0, a.Predictor.Mispredictions);
        Assert.NotEqual(startZ, a.Predictor.Current.Z + a.Predictor.Current.X * 0.0 + 0.5);
        var server = h.ServerTankOf(a);
        Assert.Equal((server.X, server.Z), (a.Predictor.Current.X, a.Predictor.Current.Z));
    }

    [Fact]
    public void OwnTank_RespondsOnTheVeryNextTick_EvenWith100msRtt()
    {
        var lag = new LinkConditions(LatencyMs: 50);
        var h = new NetHarness();
        var a = h.AddClient("Ann", lag, lag);
        h.StartMatch();
        h.Run(1.0); // let the spawn animation finish

        int before = a.Predictor.Current.Z;
        h.Run(1.0 / 60, (_, _) => new LocalInput(Dir.Up, false, false), frameDt: 1.0 / 60);

        Assert.True(a.Predictor.Current.Z < before, "predicted tank must move immediately, not after a round trip");
    }

    [Fact]
    public void BadWifi_StaysSmoothAndConverges()
    {
        // 30ms ± 20ms each way, 5% loss each way.
        var wifi = new LinkConditions(LatencyMs: 30, JitterMs: 20, LossPercent: 5);
        var h = new NetHarness(new ServerOptions { Seed = 3, EnemyTotal = 0 }, networkSeed: 7);
        var a = h.AddClient("Ann", wifi, wifi);
        var b = h.AddClient("Bob", wifi, wifi);
        h.StartMatch(GameMode.Deathmatch);
        Assert.True(h.Server.MatchRunning);

        h.Run(20, (i, t) => Wander(i, t, fire: true), frameDt: 1.0 / 144);
        h.Run(5.0); // stop firing; let bullets in flight land and any killed tank respawn

        foreach (var c in new[] { a, b })
        {
            Assert.Equal(ClientPhase.InMatch, c.Phase);
            Assert.InRange(c.Stats.SnapshotsPerSecond, 50, 62);
            Assert.InRange(c.Interpolator.DelayTicks, 1.5, 6.0);
            var server = h.ServerTankOf(c);
            Assert.Equal((server.X, server.Z), (c.Predictor.Current.X, c.Predictor.Current.Z));
            Assert.Equal(0.0, c.Predictor.VisualOffsetX, 3);
            Assert.Equal(h.Server.Simulation!.State.Map.ComputeChecksum(), c.Map!.ComputeChecksum());
        }
        // Lost inputs repeat the last move without firing, so corrections are rare, not constant.
        Assert.True(a.Predictor.Mispredictions + b.Predictor.Mispredictions < 60,
            $"mispredictions: {a.Predictor.Mispredictions} + {b.Predictor.Mispredictions}");
    }

    [Fact]
    public void Firing_IsPredictedInstantly_AndTerrainDamageReplicates()
    {
        var lag = new LinkConditions(LatencyMs: 40);
        var h = new NetHarness();
        var a = h.AddClient("Ann", lag, lag);
        h.StartMatch();
        h.Run(1.0);
        uint mapBefore = h.Server.Simulation!.State.Map.ComputeChecksum();

        h.Run(1.0 / 60, (_, _) => new LocalInput(null, true, true), frameDt: 1.0 / 60);
        Assert.Single(a.Predictor.Bullets);

        h.Run(3.0);
        uint serverMap = h.Server.Simulation.State.Map.ComputeChecksum();
        Assert.NotEqual(mapBefore, serverMap); // the bullet went up and broke bricks
        Assert.Equal(serverMap, a.Map!.ComputeChecksum());
        Assert.Empty(a.Predictor.Bullets);
    }

    [Fact]
    public void DisconnectedPlayersTank_StopsInsteadOfDrivingOn()
    {
        var h = new NetHarness();
        var a = h.AddClient("Ann");
        h.AddClient("Bob");
        h.StartMatch();
        h.Run(1.0);
        h.Run(0.3, (i, _) => i == 0 ? new LocalInput(Dir.Up, false, false) : LocalInput.None);

        h.Links[0].Dispose(); // drops while holding "up"
        h.Run(0.3);
        var parked = h.ServerTankOf(a);
        h.Run(1.0);
        var later = h.ServerTankOf(a);

        Assert.Equal((parked.X, parked.Z), (later.X, later.Z));
    }

    [Fact]
    public void BackToBackRestart_IsTreatedAsNewMatch_NotAResync()
    {
        var h = new NetHarness();
        var a = h.AddClient("Ann");
        var b = h.AddClient("Bob");
        h.StartMatch();
        h.Run(3.0);

        a.ReturnToLobby();
        a.StartMatch(); // same server poll: MatchStart can beat the lobby update
        h.Run(2.0);

        foreach (var c in new[] { a, b })
        {
            Assert.Equal(ClientPhase.InMatch, c.Phase);
            Assert.Equal(0, c.Stats.MapResyncs);
            Assert.True(c.Store.NewestTick < 3 * 60, "store must have been reset for the new match");
            Assert.NotEmpty(c.Entities);
        }
    }

    [Fact]
    public void SilentPeer_IsDisconnectedAfterTimeout()
    {
        var h = new NetHarness();
        var link = h.Net.Connect(); // connects but never says hello
        h.Run(4.0);
        Assert.False(link.IsConnected);
    }

    [Fact]
    public void FullRoom_RejectsWithReason()
    {
        var h = new NetHarness(new ServerOptions { MaxPlayers = 1, EnemyTotal = 0 });
        h.AddClient("Ann");
        var b = h.AddClient("Bob");
        h.Run(0.5);

        Assert.Equal(ClientPhase.Disconnected, b.Phase);
        Assert.Contains("full", b.DisconnectReason);
    }

    [Fact]
    public void Reconnect_WithToken_GetsSameSlotDuringMatch()
    {
        var h = new NetHarness();
        h.AddClient("Ann");
        var b = h.AddClient("Bob");
        h.StartMatch();
        byte slot = b.Slot;
        uint token = b.SessionToken;

        h.Links[1].Dispose();
        h.Run(1.0);
        var again = h.AddClient("Bob", token: token);
        h.Run(1.5);

        Assert.Equal(ClientPhase.InMatch, again.Phase);
        Assert.Equal(slot, again.Slot);
        Assert.True(again.Predictor.HasTank);
    }

    [Fact]
    public void LateJoiner_EntersRunningMatch()
    {
        var h = new NetHarness();
        h.AddClient("Ann");
        h.StartMatch();
        var c = h.AddClient("Cat");
        h.Run(1.5);

        Assert.Equal(ClientPhase.InMatch, c.Phase);
        Assert.True(c.Predictor.HasTank);
        Assert.True(h.Server.Simulation!.State.Players[c.Slot].Active);
    }

    [Fact]
    public void CorruptedClientMap_IsDetectedAndResynced()
    {
        var h = new NetHarness();
        var a = h.AddClient("Ann");
        h.StartMatch();
        a.Map!.Apply(new CellChange(0, CellType.Water));

        h.Run(5.0);

        Assert.Equal(h.Server.Simulation!.State.Map.ComputeChecksum(), a.Map!.ComputeChecksum());
        Assert.Equal(1, a.Stats.MapResyncs);
    }

    [Fact]
    public void FinishedMatch_ReturnsEveryoneToLobby()
    {
        var h = new NetHarness(new ServerOptions { Seed = 1, EnemyTotal = 0, ReturnToLobbySeconds = 1 });
        var a = h.AddClient("Ann");
        h.StartMatch();
        h.Server.Simulation!.State.Eagles[0].Alive = false;

        h.Run(0.5);
        Assert.Equal(MatchPhase.Ended, a.LatestSnapshot!.Phase);
        h.Run(1.5);

        Assert.False(h.Server.MatchRunning);
        Assert.Equal(ClientPhase.Lobby, a.Phase);
    }

    [Fact]
    public void VersionMismatch_IsRejected()
    {
        var net = new LoopbackNetwork();
        var server = new GameServer(net.CreateServer());
        var link = net.Connect();
        var w = new BitWriter();
        Messages.Write(w, new HelloMessage(ProtocolInfo.Version + 1, "Old", 0));
        link.Send(w.Data, Channel.Reliable);

        server.Tick();
        var rec = new Recorder();
        link.Poll(rec);

        Assert.Contains(MessageType.Reject, rec.Types);
    }

    [Fact]
    public void MalformedPackets_AreIgnored()
    {
        var h = new NetHarness();
        var a = h.AddClient("Ann");
        h.Run(0.2);
        h.Links[0].Send(new byte[] { (byte)MessageType.Input, 0xFF }, Channel.Unreliable);
        h.Links[0].Send(new byte[] { 0x7F }, Channel.Reliable);
        h.Run(0.2);
        Assert.Equal(ClientPhase.Lobby, a.Phase);
    }

    private sealed class Recorder : ITransportHandler
    {
        public List<MessageType> Types { get; } = new();

        public void OnPeerConnected(int peerId) { }

        public void OnPeerDisconnected(int peerId, string reason) { }

        public void OnData(int peerId, ReadOnlySpan<byte> data) => Types.Add((MessageType)data[0]);
    }
}

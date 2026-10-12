using Tank.Core;
using Tank.Core.Math;
using Tank.Core.World;
using Tank.Net.Client;
using Tank.Net.Protocol;

namespace Tank.Net.Tests;

public sealed class ClientPredictorTests
{
    private static readonly GameMap Open = new(64, 64);

    private static Snapshot SnapWithTank(int tick, int x, int z, MatchPhase phase = MatchPhase.Playing)
    {
        var s = new Snapshot { Tick = tick, Phase = phase, EntityCount = 1 };
        s.Entities[0] = new NetEntity { Id = 7, Kind = EntityKind.Tank, Type = (byte)TankKind.Player, Team = 0, Slot = 0, X = x, Z = z, Dir = (byte)Dir.Up, Hp = 1 };
        s.Players[0] = new PlayerInfo { Active = true, TankId = 7 };
        return s;
    }

    private static InputCmd Up(int tick) => new(tick, InputCmd.Encode(Dir.Up), false);

    [Fact]
    public void Reconcile_ReplaysUnackedInputs_WithoutCorrection()
    {
        var p = new ClientPredictor();
        var events = new List<GameEvent>();
        p.Reconcile(SnapWithTank(10, 1024, 2048), new ClientHeader { LastProcessedInputTick = 10 }, 0, Open, default, events);
        for (int t = 11; t <= 14; t++) p.Step(Up(t), Open, default, default, false, events);

        // Server confirms tick 12 exactly where we predicted it; 13 and 14 get replayed.
        p.Reconcile(SnapWithTank(12, 1024, 2048 - 24), new ClientHeader { LastProcessedInputTick = 12 }, 0, Open, default, events);

        Assert.Equal(0, p.Mispredictions);
        Assert.Equal(2048 - 48, p.Current.Z);
    }

    [Fact]
    public void Reconcile_HidesCorrectionBehindDecayingOffset()
    {
        var p = new ClientPredictor();
        var events = new List<GameEvent>();
        p.Reconcile(SnapWithTank(10, 1024, 2048), new ClientHeader { LastProcessedInputTick = 10 }, 0, Open, default, events);
        p.Step(Up(11), Open, default, default, false, events);

        p.Reconcile(SnapWithTank(11, 1024, 2048), new ClientHeader { LastProcessedInputTick = 11 }, 0, Open, default, events); // server: blocked

        Assert.Equal(1, p.Mispredictions);
        Assert.Equal(2048, p.Current.Z);
        Assert.Equal(-12, p.VisualOffsetZ, 3);
        for (int i = 0; i < 60; i++) p.DecayVisualOffset(1.0 / 60);
        Assert.Equal(0, p.VisualOffsetZ);
    }

    [Fact]
    public void EndedMatch_DoesNotReplayOrSlide()
    {
        var p = new ClientPredictor();
        var events = new List<GameEvent>();
        p.Reconcile(SnapWithTank(10, 1024, 2048), new ClientHeader { LastProcessedInputTick = 10 }, 0, Open, default, events);
        p.Step(Up(11), Open, default, default, false, events);

        // Ended at 11 with leftover ice momentum on the server: the server will never move us again.
        var header = new ClientHeader { LastProcessedInputTick = 11, OwnSlideTicks = 15 };
        p.Reconcile(SnapWithTank(11, 1024, 2036, MatchPhase.Ended), header, 0, Open, default, events);
        p.Step(InputCmd.Idle(12), Open, default, default, frozen: true, events);
        p.Reconcile(SnapWithTank(12, 1024, 2036, MatchPhase.Ended), header with { LastProcessedInputTick = 12 }, 0, Open, default, events);

        Assert.Equal(0, p.Mispredictions);
        Assert.Equal(2036, p.Current.Z);
    }

    [Fact]
    public void Fire_PredictsBulletImmediately_AndServerRejectionRemovesIt()
    {
        var p = new ClientPredictor();
        var events = new List<GameEvent>();
        p.Reconcile(SnapWithTank(10, 1024, 2048), new ClientHeader { LastProcessedInputTick = 10 }, 0, Open, default, events);

        p.Step(new InputCmd(11, 0, true), Open, default, default, false, events);
        Assert.Single(p.Bullets);
        Assert.Contains(events, e => e.Type == GameEventType.Fired);

        // Snapshot for tick 11 has no such bullet: the server never fired it.
        p.Reconcile(SnapWithTank(11, 1024, 2048), new ClientHeader { LastProcessedInputTick = 11 }, 0, Open, default, events);
        Assert.Empty(p.Bullets);
    }

    [Fact]
    public void RecentInputs_ReturnsNewestFirst()
    {
        var p = new ClientPredictor();
        var events = new List<GameEvent>();
        for (int t = 1; t <= 6; t++) p.Step(Up(t), Open, default, default, false, events);
        var buf = new InputCmd[4];
        Assert.Equal(4, p.RecentInputs(buf));
        Assert.Equal(new[] { 6, 5, 4, 3 }, buf.Select(i => i.Tick));
    }
}

using System.Diagnostics;
using System.Net;
using Tank.Client.Online;
using Tank.Client.UI;
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

/// <summary>
/// The room's map (stage 5 design §2): what the room screen shows and lets the host change, the Start button held back
/// while the server is still generating the map (cross-checked against a real server), the LAN list's map column, the
/// auto-start waiting for the map and the host settings' default.
/// </summary>
public class RoomMapModelTests
{
    private const double Frame = 1.0 / 60;
    private const double ServerDt = 1.0 / SimConstants.TickRate;
    private const string Generating = "地图生成中…";
    private const string Failed = "地图生成失败，换一张图。";
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(20);

    private static RoomPlayer P(byte slot, string name, byte team = 0) => new(slot, name, team, 1, false, true, false);

    /// <summary>Two people on either team, host in slot 0: a room that can start once its map is ready.</summary>
    private static RoomStateMessage Room(string map = "riverland", MapState state = MapState.Ready, bool running = false,
        BattleMode mode = BattleMode.TeamBattle) =>
        new(mode, 0, 0, running, new[] { P(0, "Ann", 0), P(1, "Bo", 1) }, map, state);

    [Fact]
    public void The_host_sees_the_map_and_may_change_it_in_the_room()
    {
        RoomModel model = RoomModel.Build(Room(), 0);

        Assert.Equal("riverland", model.MapId);
        Assert.Equal("河谷", model.MapName);
        Assert.True(model.CanSetMap);
        Assert.Equal("团队对战 · 河谷", model.Title);
        Assert.Equal("各自为战 · 试驾场地图", RoomModel.Build(Room("sandbox", mode: BattleMode.FreeForAll), 0).Title);
    }

    [Fact]
    public void Guests_and_running_matches_may_not_change_the_map()
    {
        Assert.False(RoomModel.Build(Room(), 1).CanSetMap);
        Assert.False(RoomModel.Build(Room(running: true), 0).CanSetMap);
        Assert.Equal("河谷", RoomModel.Build(Room(), 1).MapName);   // a guest still sees which map it is
    }

    [Fact]
    public void A_map_still_generating_blocks_the_start()
    {
        RoomStateMessage generating = Room(state: MapState.Generating);
        RoomModel host = RoomModel.Build(generating, 0);

        Assert.Equal(Generating, RoomRules.StartBlockReason(generating));
        Assert.False(host.CanStart);
        Assert.Equal(Generating, host.StartHint);
        Assert.Equal("对局进行中，等这一局结束。", RoomRules.StartBlockReason(Room(state: MapState.Generating, running: true)));
        Assert.Equal(Failed, RoomRules.StartBlockReason(Room(state: MapState.Failed)));
        Assert.Null(RoomRules.StartBlockReason(Room()));
    }

    [Fact]
    public void The_signature_follows_the_map_and_its_readiness()
    {
        string ready = RoomModel.Build(Room(), 0).Signature;

        Assert.NotEqual(ready, RoomModel.Build(Room("sandbox"), 0).Signature);
        Assert.NotEqual(ready, RoomModel.Build(Room(state: MapState.Generating), 0).Signature);
        Assert.NotEqual(RoomModel.Build(Room(), 1).Signature, RoomModel.Build(Room(state: MapState.Generating), 1).Signature);   // a guest's too
        Assert.Equal(ready, RoomModel.Build(Room(), 0).Signature);
    }

    /// <summary>A real server on a virtual network that prepares its maps on a worker, with one host in it and one bot asked for.</summary>
    private sealed class ServerRoom
    {
        private readonly LoopbackNetwork _net = new(1);
        private double _accumulator;

        public ServerRoom(params MapDefinition[] extraMaps)
        {
            Server = new BattleServer(_net.CreateServer(), new BattleServerOptions
            {
                Seed = 5,
                Map = MapCatalog.Sandbox,
                Maps = new[] { MapCatalog.Sandbox }.Concat(extraMaps).ToArray(),
                PrepareMapsInBackground = true,
            });
            Host = new BattleClient(_net.Connect(), "P0", () => _net.Now);
            RunUntil(() => Host.Room is { MapState: MapState.Ready });
            Host.SetBots(1);
            Run(0.5);
        }

        public BattleServer Server { get; }
        public BattleClient Host { get; }

        public void Run(double seconds)
        {
            for (int f = 0; f < (int)Math.Round(seconds / Frame); f++) Step();
        }

        /// <summary>Steps until <paramref name="condition"/> holds; the map worker runs on wall time, so this sleeps a little between steps.</summary>
        public void RunUntil(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.True(clock.Elapsed < WaitLimit, "the room never got there");
                Step();
                Thread.Sleep(1);
            }
        }

        /// <summary>The start rule's verdict on the host's room state, then a real start: (the reason, whether the server started).</summary>
        public (string? Reason, bool Started) TryStart()
        {
            string? reason = RoomRules.StartBlockReason(Host.Room!);
            Host.StartMatch();
            Run(0.5);
            return (reason, Server.MatchRunning);
        }

        private void Step()
        {
            _net.AdvanceTime(Frame);
            _accumulator += Frame;
            while (_accumulator >= ServerDt - 1e-9)
            {
                Server.Tick();
                _accumulator -= ServerDt;
            }
            Host.Update(Frame, LocalCommand.None);
        }
    }

    [Fact]
    public void The_start_rule_matches_the_server_while_a_map_generates()
    {
        using var gate = new ManualResetEventSlim();
        var gated = new MapDefinition("gated", "gated", () =>
        {
            if (!gate.Wait(WaitLimit)) throw new TimeoutException("the gate was never opened");
            return SandboxMap.Create();
        }, SandboxMap.Spawns, SandboxMap.Waypoints);
        var room = new ServerRoom(gated);

        room.Host.SetMap("Gated");
        room.Run(0.5);
        Assert.Equal(("gated", MapState.Generating), (room.Host.Room!.Map, room.Host.Room.MapState));
        (string? whileGenerating, bool startedWhileGenerating) = room.TryStart();
        gate.Set();
        room.RunUntil(() => room.Host.Room!.MapState == MapState.Ready);
        (string? onceReady, bool startedOnceReady) = room.TryStart();

        Assert.Equal(Generating, whileGenerating);
        Assert.False(startedWhileGenerating);
        Assert.Null(onceReady);
        Assert.True(startedOnceReady);
    }

    [Fact]
    public void The_start_rule_matches_the_server_when_a_map_fails()
    {
        var broken = new MapDefinition("broken", "broken", () => throw new InvalidOperationException("no ground"),
            SandboxMap.Spawns, SandboxMap.Waypoints);
        var room = new ServerRoom(broken);

        room.Host.SetMap("broken");
        room.RunUntil(() => room.Host.Room!.MapState == MapState.Failed);
        (string? reason, bool started) = room.TryStart();

        Assert.Equal(Failed, reason);
        Assert.False(started);
    }

    private static LanRoomRow LanRow(int version, string map) =>
        LanRoomRow.From(new DiscoveredRoom(new IPEndPoint(IPAddress.Parse("192.168.1.20"), 47001),
            new RoomInfoMessage(version, "Alpha", BattleMode.TeamBattle, 3, ModeRules.MaxPlayersFor(BattleMode.TeamBattle), false, 47001, map), 7, 0));

    [Fact]
    public void The_lan_row_shows_the_map()
    {
        LanRoomRow current = LanRow(BattleProtocol.Version, "riverland");
        LanRoomRow old = LanRow(2, "");   // a v2 answer has no map: the reader leaves it empty

        Assert.Contains("团队对战", current.Text);
        Assert.Contains("河谷", current.Text);
        Assert.True(current.Text.IndexOf("团队对战", StringComparison.Ordinal) < current.Text.IndexOf("河谷", StringComparison.Ordinal));
        Assert.Contains("—", old.Text);
        Assert.Contains("版本不符", old.Text);
        Assert.False(old.Joinable);
        // Every map's name fits its column: the player counts line up whichever map a room is on.
        int PlayersColumn(string text) => TextColumns.Width(text[..text.IndexOf("3/10", StringComparison.Ordinal)]);
        Assert.Equal(PlayersColumn(current.Text), PlayersColumn(LanRow(BattleProtocol.Version, "sandbox").Text));
        Assert.Equal(PlayersColumn(current.Text), PlayersColumn(old.Text));
    }

    [Fact]
    public void Auto_room_waits_for_the_map_before_starting()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        RoomStateMessage Alone(MapState state) => new(BattleMode.FreeForAll, 0, 1, false, new[] { P(0, "Ann") }, "riverland", state);

        Assert.Equal(AutoStep.Wait, auto.Next(Alone(MapState.Generating), 0, true, 1));
        Assert.Equal(AutoStep.Wait, auto.Next(Alone(MapState.Failed), 0, true, 1));
        Assert.Equal(new AutoStep(AutoStepKind.StartMatch), auto.Next(Alone(MapState.Ready), 0, true, 1));
    }

    [Fact]
    public void Host_settings_default_to_the_sandbox_map()
    {
        Assert.Equal("sandbox", new HostSettings("A", "A的房间", 0, BattleMode.TeamBattle, 0, null, null).Map);
        Assert.Equal("riverland", new HostSettings("A", "A的房间", 0, BattleMode.TeamBattle, 0, null, null, MapCatalog.Riverland.Id).Map);
    }
}

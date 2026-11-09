using Tank.Client.UI;
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

/// <summary>
/// <see cref="RoomRules.StartBlockReason"/> is a mirror of the server's start checks, so it is tested against the server:
/// a real <see cref="BattleServer"/> on a virtual network is given each room, the host asks to start, and the room
/// state the host saw just before must predict whether the match began.
/// </summary>
public class RoomRulesTests
{
    private const double Frame = 1.0 / 60;
    private const double ServerDt = 1.0 / SimConstants.TickRate;

    /// <summary>One server, its clients, and the stepping the game loop does; the first client is the host.</summary>
    private sealed class Room
    {
        private readonly LoopbackNetwork _net = new(1);
        private readonly List<BattleClient> _clients = new();
        private double _accumulator;

        /// <param name="map">The room's first map; null for the server's default (the sandbox).</param>
        /// <param name="maps">The maps the room may switch to; null for <paramref name="map"/> alone (the server's default maps without a map).</param>
        public Room(int humans, MapDefinition? map = null, IReadOnlyList<MapDefinition>? maps = null)
        {
            Server = new BattleServer(_net.CreateServer(), map is null ? new BattleServerOptions { Seed = 5 } : new BattleServerOptions { Seed = 5, Map = map, Maps = maps ?? new[] { map } });
            for (int i = 0; i < humans; i++) _clients.Add(new BattleClient(_net.Connect(), $"P{i}", () => _net.Now));
            Run(0.5);
        }

        public BattleServer Server { get; }
        public BattleClient Host => _clients[0];
        public IReadOnlyList<BattleClient> Clients => _clients;

        public void Run(double seconds)
        {
            for (int f = 0; f < (int)Math.Round(seconds / Frame); f++)
            {
                _net.AdvanceTime(Frame);
                _accumulator += Frame;
                while (_accumulator >= ServerDt - 1e-9)
                {
                    Server.Tick();
                    _accumulator -= ServerDt;
                }
                foreach (BattleClient c in _clients) c.Update(Frame, LocalCommand.None);
            }
        }
    }

    /// <summary>The sandbox without its bases: a map a standard battle can't be played on.</summary>
    private static readonly MapDefinition NoBases = new("nobases", "无基地", SandboxMap.Create, SandboxMap.Spawns, SandboxMap.Waypoints);

    /// <summary>Builds the room, lets the rules predict, then starts it for real; returns (predicted, actual, the reason).</summary>
    /// <param name="map">The room's first map; null for the server's default maps.</param>
    /// <param name="maps">The maps the room may switch to; null for <paramref name="map"/> alone.</param>
    private static (bool Predicted, bool Actual, string? Reason) Try(BattleMode mode, int humans, int bots, byte[]? teams, MapDefinition? map = null,
        IReadOnlyList<MapDefinition>? maps = null)
    {
        var room = new Room(humans, map, maps);
        room.Host.SetMode(mode);
        room.Host.SetBots(bots);
        if (teams is not null)
            for (int i = 0; i < humans; i++) room.Clients[i].SetTeam(teams[i]);
        room.Run(0.5);

        RoomStateMessage state = room.Host.Room!;
        Assert.Equal(mode, state.Mode);
        Assert.Equal(bots, state.Bots);
        Assert.Equal(humans, state.Players.Count);
        string? reason = map is null ? RoomRules.StartBlockReason(state) : RoomRules.StartBlockReason(state, maps ?? new[] { map });

        room.Host.StartMatch();
        room.Run(0.5);
        Assert.Equal(room.Server.MatchRunning, room.Host.Room!.MatchRunning);
        return (reason is null, room.Server.MatchRunning, reason);
    }

    /// <summary>
    /// Every mode on both real maps (the room starts on the map, the catalogue's maps to switch to): a royale room asked for on the
    /// sandbox moves to the riverland, as the server has it (<see cref="A_royale_on_another_map_is_blocked_with_the_reason"/> covers
    /// the sandbox itself, which a server never holds a royale room on).
    /// </summary>
    public static IEnumerable<object[]> Combinations()
    {
        byte[] split = { 0, 1 };
        byte[] together = { 0, 0 };
        foreach (string map in new[] { SandboxMap.Id, RiverlandMap.Id })
        foreach (BattleMode mode in ModeRules.All)
        {
            yield return new object[] { mode, 1, 0, null!, map };
            yield return new object[] { mode, 1, 2, null!, map };
            yield return new object[] { mode, 2, 0, split, map };
            yield return new object[] { mode, 2, 2, split, map };
            yield return new object[] { mode, 2, 0, together, map };   // duos: one team, refused ("fewer than two teams")
            yield return new object[] { mode, 2, 1, together, map };
        }
        yield return new object[] { BattleMode.TeamBattle, 1, 1, null!, SandboxMap.Id };
        yield return new object[] { BattleMode.FreeForAll, 1, 1, null!, SandboxMap.Id };
        yield return new object[] { BattleMode.TeamBattle, 2, 9, split, SandboxMap.Id };
        yield return new object[] { BattleMode.Royale, 2, 14, null!, RiverlandMap.Id };
        // Duos: one person and one bot make one team (refused); a third tank opens a second; people spread and partnered.
        yield return new object[] { BattleMode.Duos, 1, 1, null!, RiverlandMap.Id };
        yield return new object[] { BattleMode.Duos, 1, 15, null!, RiverlandMap.Id };
        yield return new object[] { BattleMode.Duos, 3, 3, new byte[] { 0, 0, 5 }, RiverlandMap.Id };
        yield return new object[] { BattleMode.Duos, 3, 0, new byte[] { 4, 4, 4 }, RiverlandMap.Id };   // the third can't join a full team: two teams
        yield return new object[] { BattleMode.Duos, 2, 0, new byte[] { 7, 7 }, RiverlandMap.Id };
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void The_rules_agree_with_the_server_on_whether_the_match_starts(BattleMode mode, int humans, int bots, byte[]? teams, string mapId)
    {
        Assert.True(MapCatalog.TryGet(mapId, out MapDefinition? map));
        (bool predicted, bool actual, string? reason) = Try(mode, humans, bots, teams, map, MapCatalog.All);

        Assert.True(predicted == actual, $"rules said {(predicted ? "can start" : $"blocked ({reason})")}, the server {(actual ? "started" : "refused")}");
    }

    [Fact]
    public void A_royale_on_another_map_is_blocked_with_the_reason()
    {
        RoomPlayer[] two = { new(0, "P0", 0, 1, false, true, false), new(1, "P1", 1, 1, false, true, false) };
        foreach (BattleMode mode in ModeRules.All)
        {
            var onSandbox = new RoomStateMessage(mode, 0, 0, false, two, SandboxMap.Id, MapState.Ready);
            var onRiverland = onSandbox with { Map = RiverlandMap.Id };
            Assert.Null(RoomRules.StartBlockReason(onRiverland));
            if (ModeRules.HasZone(mode)) Assert.Equal("吃鸡只在河谷开：换到河谷。", RoomRules.StartBlockReason(onSandbox));   // a royale, duos
            else Assert.Null(RoomRules.StartBlockReason(onSandbox));
        }
    }

    [Fact]
    public void A_royale_room_on_the_sandbox_moves_to_the_riverland_and_starts_there()
    {
        var room = new Room(1, MapCatalog.Sandbox, MapCatalog.All);
        room.Host.SetMode(BattleMode.Royale);
        room.Host.SetBots(15);
        room.Run(0.5);

        RoomStateMessage state = room.Host.Room!;
        Assert.Equal((BattleMode.Royale, RiverlandMap.Id, (byte)15), (state.Mode, state.Map, state.Bots));
        Assert.Null(RoomRules.StartBlockReason(state));
        Assert.Equal(15, RoomRules.PlanFor(state).Bots);
        room.Host.StartMatch();
        room.Run(0.5);
        Assert.True(room.Server.MatchRunning);
        Assert.Equal(16, room.Server.Simulation!.TankCount);
    }

    [Fact]
    public void The_plan_caps_the_bots_by_the_modes_seats()
    {
        RoomPlayer[] one = { new(0, "P0", 0, 1, false, true, false) };
        Assert.Equal(15, RoomRules.PlanFor(new RoomStateMessage(BattleMode.Royale, 0, 15, false, one, RiverlandMap.Id, MapState.Ready)).Bots);
        Assert.Equal(9, RoomRules.PlanFor(new RoomStateMessage(BattleMode.TeamBattle, 0, 15, false, one, RiverlandMap.Id, MapState.Ready)).Bots);   // an older room's 15
    }

    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    [InlineData(BattleMode.FreeForAll)]
    [InlineData(BattleMode.Standard)]
    public void On_a_map_without_bases_only_a_standard_battle_is_held_on_both_sides(BattleMode mode)
    {
        var (predicted, actual, reason) = Try(mode, 2, 0, new byte[] { 0, 1 }, NoBases);

        Assert.Equal(predicted, actual);
        Assert.Equal(!ModeRules.HasBases(mode), actual);
        if (ModeRules.HasBases(mode)) Assert.Contains("没有基地", reason);
    }

    [Fact]
    public void On_both_real_maps_a_standard_battle_can_start()
    {
        foreach (MapDefinition map in MapCatalog.All)
        {
            var room = new RoomStateMessage(BattleMode.Standard, 0, 1, false, new[] { new RoomPlayer(0, "P0", 0, 1, false, true, false) }, map.Id, MapState.Ready);
            Assert.Null(RoomRules.StartBlockReason(room));
        }
    }

    [Fact]
    public void A_map_the_client_does_not_know_is_left_to_the_server()
    {
        var room = new RoomStateMessage(BattleMode.Standard, 0, 1, false, new[] { new RoomPlayer(0, "P0", 0, 1, false, true, false) }, "elsewhere", MapState.Ready);

        Assert.Null(RoomRules.StartBlockReason(room));
    }

    [Fact]
    public void One_person_alone_cannot_start_and_the_reason_says_two_tanks_are_needed()
    {
        var (predicted, actual, reason) = Try(BattleMode.TeamBattle, 1, 0, null);

        Assert.False(predicted);
        Assert.False(actual);
        Assert.Contains("两辆坦克", reason);
    }

    [Fact]
    public void Two_people_on_one_team_leave_the_other_empty_until_a_bot_fills_it()
    {
        var blocked = Try(BattleMode.TeamBattle, 2, 0, new byte[] { 0, 0 });
        var filled = Try(BattleMode.TeamBattle, 2, 1, new byte[] { 0, 0 });

        Assert.False(blocked.Actual);
        Assert.Contains("一队没有坦克", blocked.Reason);
        Assert.True(filled.Actual);
        Assert.Null(filled.Reason);
    }

    [Fact]
    public void Without_teams_two_people_start_whatever_their_team_field_says()
    {
        var (predicted, actual, _) = Try(BattleMode.FreeForAll, 2, 0, new byte[] { 0, 0 });

        Assert.True(predicted);
        Assert.True(actual);
    }

    [Fact]
    public void Bots_fill_the_thinner_team_first()
    {
        // Two people on team 0 and four bots: the bots fill team 1 until it matches, then alternate (team 0 on a tie).
        var room = new Room(2);
        room.Host.SetBots(4);
        room.Clients[0].SetTeam(0);
        room.Clients[1].SetTeam(0);
        room.Run(0.5);

        StartPlan plan = RoomRules.PlanFor(room.Host.Room!);

        Assert.Equal(new StartPlan(2, 4, 3, 3, 1, 3), plan);
        room.Host.StartMatch();
        room.Run(0.5);
        Assert.True(room.Server.MatchRunning);
        var seated = Enumerable.Range(0, BattleProtocol.MaxPlayers).Select(i => room.Server.Rules!.Of((byte)i)).Where(p => p.Active).ToList();
        Assert.Equal(plan.Team0, seated.Count(p => p.Team == 0));
        Assert.Equal(plan.Team1, seated.Count(p => p.Team == 1));
    }

    [Theory]
    [InlineData(1, 15, new byte[] { 0 })]
    [InlineData(3, 3, new byte[] { 0, 0, 5 })]
    [InlineData(2, 5, new byte[] { 6, 1 })]
    [InlineData(4, 15, new byte[] { 0, 1, 2, 2 })]   // only twelve places left: the plan caps the bots as the server does
    public void Duos_bots_go_to_the_teams_the_plan_says(int humans, int bots, byte[] teams)
    {
        var room = new Room(humans, MapCatalog.Riverland, MapCatalog.All);
        room.Host.SetMode(BattleMode.Duos);
        room.Host.SetBots(bots);
        room.Run(0.5);
        for (int i = 0; i < humans; i++) room.Clients[i].SetTeam(teams[i]);
        room.Run(0.5);

        RoomStateMessage state = room.Host.Room!;
        Assert.Equal(teams, state.Players.OrderBy(p => p.Slot).Select(p => p.Team));
        int[] planned = RoomRules.DuoBots(state);
        StartPlan plan = RoomRules.PlanFor(state);
        room.Host.StartMatch();
        room.Run(0.5);

        Assert.True(room.Server.MatchRunning);
        var seated = Enumerable.Range(0, BattleProtocol.MaxPlayers).Select(i => room.Server.Rules!.Of((byte)i)).Where(p => p.Active).ToList();
        int[] botsByTeam = new int[ModeRules.DuoTeams];
        foreach (RoomPlayer bot in room.Host.Room!.Players.Where(p => p.IsBot)) botsByTeam[room.Server.Rules!.Of(bot.Slot).Team]++;
        Assert.Equal(planned, botsByTeam);
        Assert.Equal(plan.Bots, botsByTeam.Sum());
        Assert.Equal(plan.TeamsManned, seated.Select(p => p.Team).Distinct().Count());
        Assert.Equal((0, 0, 0, 0), (plan.Team0, plan.Team1, plan.BotsToTeam0, plan.BotsToTeam1));
    }

    [Fact]
    public void Duos_needs_two_teams_with_tanks_and_says_so_after_the_tank_count()
    {
        RoomPlayer[] one = { new(0, "P0", 0, 1, false, true, false) };
        RoomPlayer[] pair = { new(0, "P0", 3, 1, false, true, false), new(1, "P1", 3, 1, false, true, false) };

        Assert.Equal("至少要有两辆坦克：等朋友加入，或加几个机器人。", RoomRules.StartBlockReason(Duos(0, one)));
        Assert.Equal("至少要有两队有车：让朋友选别的队，或加几个机器人。", RoomRules.StartBlockReason(Duos(1, one)));
        Assert.Null(RoomRules.StartBlockReason(Duos(2, one)));
        Assert.Equal("至少要有两队有车：让朋友选别的队，或加几个机器人。", RoomRules.StartBlockReason(Duos(0, pair)));
        Assert.Null(RoomRules.StartBlockReason(Duos(1, pair)));
        Assert.Equal(new[] { 1, 0, 0, 0, 0, 0, 0, 0 }, RoomRules.DuoBots(Duos(1, one)));
        Assert.Equal(new[] { 1, 1, 0, 0, 0, 0, 0, 0 }, RoomRules.DuoBots(Duos(2, one)));
        Assert.Equal(new[] { 1, 0, 0, 0, 0, 0, 0, 0 }, RoomRules.DuoBots(Duos(1, pair)));
        Assert.Equal(new StartPlan(2, 1, 0, 0, 0, 0, 2), RoomRules.PlanFor(Duos(1, pair)));

        static RoomStateMessage Duos(int bots, RoomPlayer[] players) => new(BattleMode.Duos, 0, (byte)bots, false, players, RiverlandMap.Id, MapState.Ready);
    }

    [Fact]
    public void A_duos_seat_on_a_team_past_the_eight_is_on_no_team_for_the_bots_and_the_count()
    {
        // The server re-teams every seat on a switch to duos, so it can't happen today; the 4-bit wire team allows 8–15.
        RoomPlayer[] players = { new(0, "P0", 0, 1, false, true, false), new(1, "P1", 12, 1, false, true, false) };
        var room = new RoomStateMessage(BattleMode.Duos, 0, 2, false, players, RiverlandMap.Id, MapState.Ready);

        Assert.Equal(new[] { 1, 1, 0, 0, 0, 0, 0, 0 }, RoomRules.DuoBots(room));   // the first bot partners P0, the second opens team 1
        Assert.Equal(new StartPlan(2, 2, 0, 0, 0, 0, 2), RoomRules.PlanFor(room));     // teams 0 and 1; the stray seat counts as a person only
        Assert.Null(RoomRules.StartBlockReason(room));
    }

    [Fact]
    public void Outside_duos_no_teams_are_counted_and_no_duo_bots_planned()
    {
        RoomPlayer[] two = { new(0, "P0", 0, 1, false, true, false), new(1, "P1", 1, 1, false, true, false) };
        foreach (BattleMode mode in ModeRules.All.Where(m => !ModeRules.IsDuos(m)))
        {
            var room = new RoomStateMessage(mode, 0, 3, false, two, RiverlandMap.Id, MapState.Ready);
            Assert.Equal(0, RoomRules.PlanFor(room).TeamsManned);
            Assert.All(RoomRules.DuoBots(room), n => Assert.Equal(0, n));
        }
    }

    [Fact]
    public void A_running_match_blocks_the_start()
    {
        var room = new Room(2);
        room.Host.StartMatch();
        room.Run(0.5);
        Assert.True(room.Server.MatchRunning);

        Assert.NotNull(RoomRules.StartBlockReason(room.Host.Room!));
    }

    [Fact]
    public void Bots_are_capped_by_the_free_places()
    {
        var state = new RoomStateMessage(BattleMode.FreeForAll, 0, 9, false,
            Enumerable.Range(0, 4).Select(i => new RoomPlayer((byte)i, $"P{i}", (byte)i, 0, false, true, false)).ToList());

        StartPlan plan = RoomRules.PlanFor(state);

        Assert.Equal(6, plan.Bots);
        Assert.Equal(10, plan.Seated);
    }
}

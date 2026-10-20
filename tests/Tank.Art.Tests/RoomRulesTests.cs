using Tank.Client.UI;
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;
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

        public Room(int humans)
        {
            Server = new BattleServer(_net.CreateServer(), new BattleServerOptions { Seed = 5 });
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

    /// <summary>Builds the room, lets the rules predict, then starts it for real; returns (predicted, actual, the reason).</summary>
    private static (bool Predicted, bool Actual, string? Reason) Try(BattleMode mode, int humans, int bots, byte[]? teams)
    {
        var room = new Room(humans);
        room.Host.SetMode(mode);
        room.Host.SetBots(bots);
        if (teams is not null)
            for (int i = 0; i < humans; i++) room.Clients[i].SetTeam(teams[i]);
        room.Run(0.5);

        RoomStateMessage state = room.Host.Room!;
        Assert.Equal(mode, state.Mode);
        Assert.Equal(bots, state.Bots);
        Assert.Equal(humans, state.Players.Count);
        string? reason = RoomRules.StartBlockReason(state);

        room.Host.StartMatch();
        room.Run(0.5);
        Assert.Equal(room.Server.MatchRunning, room.Host.Room!.MatchRunning);
        return (reason is null, room.Server.MatchRunning, reason);
    }

    public static IEnumerable<object[]> Combinations()
    {
        byte[] split = { 0, 1 };
        byte[] together = { 0, 0 };
        foreach (BattleMode mode in ModeRules.All)
        {
            yield return new object[] { mode, 1, 0, null! };
            yield return new object[] { mode, 1, 2, null! };
            yield return new object[] { mode, 2, 0, split };
            yield return new object[] { mode, 2, 2, split };
            yield return new object[] { mode, 2, 0, together };
            yield return new object[] { mode, 2, 1, together };
        }
        yield return new object[] { BattleMode.TeamBattle, 1, 1, null! };
        yield return new object[] { BattleMode.FreeForAll, 1, 1, null! };
        yield return new object[] { BattleMode.TeamBattle, 2, 9, split };
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void The_rules_agree_with_the_server_on_whether_the_match_starts(BattleMode mode, int humans, int bots, byte[]? teams)
    {
        (bool predicted, bool actual, string? reason) = Try(mode, humans, bots, teams);

        Assert.True(predicted == actual, $"rules said {(predicted ? "can start" : $"blocked ({reason})")}, the server {(actual ? "started" : "refused")}");
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

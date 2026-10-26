using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Art.Tests;

public class AutoRoomTests
{
    private const byte Me = 0;

    private static RoomPlayer Human(byte slot, string tank = "medium", bool connected = true) =>
        new(slot, $"P{slot}", 0, (byte)IndexOf(tank), false, connected, false);

    private static int IndexOf(string tank) => TankCatalog.All.Select((t, i) => (t, i)).First(x => x.t.Id == tank).i;

    private static RoomStateMessage Room(BattleMode mode = BattleMode.TeamBattle, int bots = 0, bool running = false, params RoomPlayer[] players) =>
        new(mode, HostSlot: 0, (byte)bots, running, players.Length > 0 ? players : new[] { Human(0) });

    private static AutoRoom Everything() => new("heavy", BattleMode.FreeForAll, 4, autoStart: true);

    [Fact]
    public void Nothing_happens_before_the_first_room_state()
    {
        Assert.Equal(AutoStep.Wait, Everything().Next(null, Me, true, 1));
    }

    [Fact]
    public void A_host_sends_tank_then_mode_then_bots_then_start_each_once_as_the_room_catches_up()
    {
        var auto = Everything();

        AutoStep tank = auto.Next(Room(), Me, true, 1);
        AutoStep mode = auto.Next(Room(), Me, true, 1);
        AutoStep bots = auto.Next(Room(BattleMode.FreeForAll), Me, true, 1);
        AutoStep notYet = auto.Next(Room(BattleMode.FreeForAll, 4), Me, true, 1);   // the tank has not come back yet
        AutoStep start = auto.Next(Room(BattleMode.FreeForAll, 4, false, Human(0, "heavy")), Me, true, 1);
        AutoStep after = auto.Next(Room(BattleMode.FreeForAll, 4, false, Human(0, "heavy")), Me, true, 1);

        Assert.Equal(new AutoStep(AutoStepKind.SetTank, TankId: "heavy"), tank);
        Assert.Equal(new AutoStep(AutoStepKind.SetMode, Mode: BattleMode.FreeForAll), mode);
        Assert.Equal(new AutoStep(AutoStepKind.SetBots, Bots: 4), bots);
        Assert.Equal(AutoStepKind.Wait, notYet.Kind);
        Assert.Equal(AutoStepKind.StartMatch, start.Kind);
        Assert.Equal(AutoStepKind.Done, after.Kind);
    }

    [Fact]
    public void The_start_waits_until_the_room_shows_the_mode_the_bots_and_the_tank()
    {
        var auto = Everything();
        for (int i = 0; i < 3; i++) auto.Next(Room(), Me, true, 1);   // tank, mode, bots sent

        Assert.Equal(AutoStepKind.Wait, auto.Next(Room(BattleMode.TeamBattle, 4, false, Human(0, "heavy")), Me, true, 1).Kind);
        Assert.Equal(AutoStepKind.Wait, auto.Next(Room(BattleMode.FreeForAll, 0, false, Human(0, "heavy")), Me, true, 1).Kind);
        Assert.Equal(AutoStepKind.Wait, auto.Next(Room(BattleMode.FreeForAll, 4, false, Human(0, "light")), Me, true, 1).Kind);
        Assert.Equal(AutoStepKind.StartMatch, auto.Next(Room(BattleMode.FreeForAll, 4, false, Human(0, "heavy")), Me, true, 1).Kind);
    }

    [Fact]
    public void Players_two_does_not_start_with_one_human_and_counts_only_connected_people()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        RoomPlayer bot = new(1, "Bot", 1, 0, true, true, IsBot: true);
        RoomPlayer gone = Human(2, connected: false);

        Assert.Equal(AutoStepKind.Wait, auto.Next(Room(players: new[] { Human(0), bot, gone }), Me, true, 2).Kind);
        Assert.Equal(AutoStepKind.StartMatch, Settle(auto, Room(players: new[] { Human(0), Human(3), bot, gone }), 2).Kind);
    }

    [Fact]
    public void Waiting_for_others_the_host_starts_only_after_the_room_has_been_ready_for_the_settle_calls()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        RoomStateMessage full = Room(players: new[] { Human(0), Human(1) });

        for (int i = 1; i < AutoRoom.SettleCalls; i++)
            Assert.Equal(AutoStepKind.Wait, auto.Next(full, Me, true, 2).Kind);   // a guest just came in: its --tank is on its way

        Assert.Equal(AutoStepKind.StartMatch, auto.Next(full, Me, true, 2).Kind);
    }

    [Fact]
    public void The_settle_count_starts_over_when_the_room_stops_being_ready()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        RoomStateMessage full = Room(players: new[] { Human(0), Human(1) });
        RoomStateMessage left = Room(players: new[] { Human(0), Human(1, connected: false) });
        for (int i = 1; i < AutoRoom.SettleCalls; i++) auto.Next(full, Me, true, 2);

        Assert.Equal(AutoStepKind.Wait, auto.Next(left, Me, true, 2).Kind);   // the guest dropped just before the start

        for (int i = 1; i < AutoRoom.SettleCalls; i++)
            Assert.Equal(AutoStepKind.Wait, auto.Next(full, Me, true, 2).Kind);
        Assert.Equal(AutoStepKind.StartMatch, auto.Next(full, Me, true, 2).Kind);
    }

    [Fact]
    public void A_host_that_waits_for_nobody_starts_at_once()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        Assert.Equal(AutoStepKind.StartMatch, auto.Next(Room(), Me, true, 1).Kind);
    }

    [Fact]
    public void The_start_is_not_sent_while_a_match_is_running()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        Assert.Equal(AutoStepKind.Wait, auto.Next(Room(running: true), Me, true, 1).Kind);
        Assert.False(auto.Started);
    }

    [Fact]
    public void A_map_that_failed_to_generate_holds_the_start_and_warns_once()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        RoomStateMessage failed = Room() with { Map = "riverland", MapState = MapState.Failed };
        Assert.Null(auto.TakeWarning());

        Assert.Equal(AutoStepKind.Wait, auto.Next(failed, Me, true, 1).Kind);
        string? warning = auto.TakeWarning();
        Assert.NotNull(warning);
        Assert.Contains("riverland", warning);
        for (int i = 0; i < 5; i++) Assert.Equal(AutoStepKind.Wait, auto.Next(failed, Me, true, 1).Kind);
        Assert.Null(auto.TakeWarning());
        Assert.False(auto.Started);
    }

    [Fact]
    public void A_map_still_generating_holds_the_start_without_a_warning()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        Assert.Equal(AutoStepKind.Wait, auto.Next(Room() with { MapState = MapState.Generating }, Me, true, 1).Kind);
        Assert.Null(auto.TakeWarning());
        Assert.Equal(AutoStepKind.StartMatch, auto.Next(Room(), Me, true, 1).Kind);
    }

    [Fact]
    public void Without_autostart_the_room_is_set_up_but_never_started()
    {
        var auto = new AutoRoom(null, BattleMode.Annihilation, 2, autoStart: false);
        Assert.Equal(AutoStepKind.SetMode, auto.Next(Room(), Me, true, 1).Kind);
        Assert.Equal(AutoStepKind.SetBots, auto.Next(Room(), Me, true, 1).Kind);
        Assert.Equal(AutoStepKind.Done, auto.Next(Room(BattleMode.Annihilation, 2), Me, true, 1).Kind);
    }

    [Fact]
    public void A_guest_only_picks_its_tank_and_never_starts_or_changes_the_room()
    {
        var auto = Everything();
        Assert.Equal(AutoStepKind.SetTank, auto.Next(Room(), 1, isHost: false, 1).Kind);
        for (int i = 0; i < 3; i++)
            Assert.Equal(AutoStepKind.Done, auto.Next(Room(BattleMode.FreeForAll, 4, false, Human(1, "heavy")), 1, isHost: false, 1).Kind);
    }

    [Fact]
    public void A_guest_without_a_tank_choice_is_done_at_once()
    {
        Assert.Equal(AutoStepKind.Done, new AutoRoom(null, BattleMode.FreeForAll, 3, true).Next(Room(), 1, isHost: false, 1).Kind);
    }

    [Fact]
    public void A_tank_the_room_already_shows_is_not_sent()
    {
        var auto = new AutoRoom("heavy", null, null, autoStart: false);
        Assert.Equal(AutoStepKind.Done, auto.Next(Room(players: Human(1, "heavy")), 1, isHost: false, 1).Kind);
    }

    [Fact]
    public void Reset_sends_the_tank_again_only_when_the_room_does_not_show_it()
    {
        // A guest whose SetTank reached the server after the host's start: the server ignored it (the seat was in the match).
        var lost = new AutoRoom("heavy", null, null, autoStart: false);
        Assert.Equal(AutoStepKind.SetTank, lost.Next(Room(players: Human(1)), 1, isHost: false, 1).Kind);
        Assert.Equal(AutoStepKind.Done, lost.Next(Room(running: true, players: Human(1)), 1, isHost: false, 1).Kind);
        lost.Reset();   // back in the room after the match, still in the medium tank
        Assert.Equal(new AutoStep(AutoStepKind.SetTank, TankId: "heavy"), lost.Next(Room(players: Human(1)), 1, isHost: false, 1));

        var kept = new AutoRoom("heavy", null, null, autoStart: false);
        Assert.Equal(AutoStepKind.SetTank, kept.Next(Room(players: Human(1)), 1, isHost: false, 1).Kind);
        kept.Reset();   // the choice made it: nothing to send
        Assert.Equal(AutoStepKind.Done, kept.Next(Room(players: Human(1, "heavy")), 1, isHost: false, 1).Kind);
    }

    [Fact]
    public void Reset_allows_a_second_start_without_repeating_the_setup()
    {
        var auto = new AutoRoom(null, BattleMode.TeamBattle, 1, autoStart: true);
        RoomStateMessage room = Room(BattleMode.TeamBattle, 1);
        auto.Next(room, Me, true, 1);
        auto.Next(room, Me, true, 1);
        Assert.Equal(AutoStepKind.StartMatch, auto.Next(room, Me, true, 1).Kind);
        Assert.True(auto.Started);
        Assert.Equal(AutoStepKind.Done, auto.Next(room, Me, true, 1).Kind);

        auto.Reset();

        Assert.False(auto.Started);
        Assert.Equal(AutoStepKind.StartMatch, auto.Next(room, Me, true, 1).Kind);
    }

    /// <summary>Asks <paramref name="auto"/> with the same room until it stops waiting (at most the settle calls) and returns that step.</summary>
    private static AutoStep Settle(AutoRoom auto, RoomStateMessage room, int humansWanted)
    {
        AutoStep step = AutoStep.Wait;
        for (int i = 0; i < AutoRoom.SettleCalls && step.Kind == AutoStepKind.Wait; i++) step = auto.Next(room, Me, true, humansWanted);
        return step;
    }
}

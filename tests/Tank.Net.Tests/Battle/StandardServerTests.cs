using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// The server running standard battles (stage 5 design §3.1, §3.3): a match needs the map's bases, the match start
/// carries them, the snapshots carry both bases' progress, every player's capture and why the match ended, and players
/// join a running match — but nobody gets a second tank in it (no respawns, not even by leaving and coming back).
/// </summary>
public sealed class StandardServerTests
{
    private const float InTheEnemyBaseX = 60f;   // team 1's base is centred on x 75 with a 25 m radius
    private const double FrameDt = 1.0 / 120;

    private static BattleHarness Harness(MapDefinition map, List<string> log, double? matchSeconds = null, double graceSeconds = 30) =>
        new(new BattleServerOptions { Seed = 1, Map = map, Maps = new[] { map }, MatchSeconds = matchSeconds, ReconnectGraceSeconds = graceSeconds }, log: log.Add);

    /// <summary>Full throttle east until the tank is past x <see cref="InTheEnemyBaseX"/>, then off the throttle and standing.</summary>
    private static LocalCommand IntoTheEnemyBase(BattleClient c) =>
        c.HasTank && c.Own.Position.X < InTheEnemyBaseX ? new LocalCommand(1, 0, c.Own.Yaw, 0f, false) : LocalCommand.None;

    /// <summary>
    /// <paramref name="shooter"/>'s gun laid on <paramref name="target"/>'s hull front, firing: worked out from the
    /// server's tanks (the shooter's client may not see the target), sent as the shooter's own input.
    /// </summary>
    private static LocalCommand Attack(BattleHarness h, BattleClient shooter, BattleClient target)
    {
        Simulation sim = h.Server.Simulation!;
        int si = sim.SlotOf(h.Server.TankOf(shooter.Slot)), ti = sim.SlotOf(h.Server.TankOf(target.Slot));
        if (si < 0 || ti < 0) return LocalCommand.None;
        TankState s = sim.Tanks[si], t = sim.Tanks[ti];
        float ground = sim.Field.Terrain.HeightAt(t.Position.X, t.Position.Y);
        Vector3 front = new Vector3(t.Position.X, ground + 1.02f, t.Position.Y) + Vector3.Transform(new Vector3(0, 0, -3.1f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, t.Yaw));
        var (yaw, pitch) = Angles.AimAngles(front - TankPose.Compute(s, sim.Field.Terrain).Trunnion(s.Spec), s.Yaw);
        return new LocalCommand(0, 0, yaw, pitch, true);
    }

    /// <summary>Has <paramref name="shooter"/> fire at <paramref name="target"/> until the server has it destroyed (60 s at most).</summary>
    private static void Destroy(BattleHarness h, BattleClient shooter, BattleClient target)
    {
        int shooterIndex = h.Clients.IndexOf(shooter);
        for (int second = 0; second < 60 && h.Server.Rules!.Of(target.Slot).Alive; second++)
            h.Run(1.0, (c, _) => c == shooterIndex ? Attack(h, shooter, target) : LocalCommand.None);
        Assert.False(h.Server.Rules!.Of(target.Slot).Alive, "the target outlived a minute of fire");
    }

    /// <summary>Joins the clients, puts each on its team, then has the host start <paramref name="mode"/>.</summary>
    private static void Start(BattleHarness h, BattleMode mode, params byte[] teams)
    {
        h.Run(0.3);
        for (int i = 0; i < teams.Length; i++) h.Clients[i].SetTeam(teams[i]);
        h.StartMatch(mode);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
    }

    // ---- starting ----

    [Fact]
    public void A_standard_match_starts_with_the_maps_bases()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log);
        h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.Standard);

        Assert.True(h.Server.MatchRunning);
        Assert.All(h.Clients, c => Assert.Equal(TestMaps.StandardBases, c.MatchInfo!.Bases));

        h.Clients.Single(c => c.IsHost).ReturnToRoom();
        h.Run(0.5);
        h.StartMatch(BattleMode.TeamBattle);
        Assert.True(h.Server.MatchRunning);
        Assert.All(h.Clients, c => Assert.Empty(c.MatchInfo!.Bases!));
    }

    [Fact]
    public void A_map_without_bases_holds_a_standard_start()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Small("x"), log);
        h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.Standard, bots: 2);

        Assert.False(h.Server.MatchRunning);
        Assert.Contains("can't start: the map has no bases", log);
        BattleClient host = h.Clients.Single(c => c.IsHost);
        Assert.Equal(2, host.Room!.Players.Count);   // no bots seated and left behind

        h.StartMatch(BattleMode.Annihilation, bots: 2);
        Assert.True(h.Server.MatchRunning);
    }

    // ---- capture, sight and the end ----

    [Fact]
    public void Three_attackers_take_the_enemy_base_through_the_network()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log);
        BattleClient a = h.AddClient("A"), b = h.AddClient("B"), c = h.AddClient("C"), d = h.AddClient("D");
        Start(h, BattleMode.Standard, 0, 0, 0, 1);
        var seenByD = new List<byte>();
        int mateCapture = 0;

        for (int frame = 0; frame < 45 * 120 && h.Server.MatchRunning && h.Server.Rules!.Phase == MatchPhase.Playing; frame++)
        {
            h.Run(FrameDt, (i, _) => i < 3 ? IntoTheEnemyBase(h.Clients[i]) : LocalCommand.None, FrameDt);
            if (d.Latest is { } seen)
            {
                seenByD.Add(d.Match.BaseProgress1);
                // while the match is on, a defender learns nothing of who is in the base (stage 5 design §3.3)
                if (seen.Match.Phase == MatchPhase.Playing)
                    foreach (BattleClient attacker in new[] { a, b, c })
                        Assert.Equal(0, seen.Players[attacker.Slot].Capture);
            }
            if (a.Latest is { Match.Phase: MatchPhase.Playing } mine) mateCapture = System.Math.Max(mateCapture, mine.Players[b.Slot].Capture);
        }
        h.Run(0.5);

        foreach (BattleClient client in h.Clients)
        {
            Assert.Equal(MatchPhase.Ended, client.Match.Phase);
            Assert.Equal(0, client.Match.WinnerTeam);
            Assert.Equal(MatchEndReason.Captured, client.Match.EndReason);
            Assert.Equal(100, client.Match.BaseProgress1);
        }
        Assert.True(mateCapture >= 10, $"A read B's capture as {mateCapture} at most while the match was on");
        foreach (BattleClient attacker in new[] { a, b, c })   // once it is over, the defender reads everyone's capture
            Assert.True(d.Latest!.Players[attacker.Slot].Capture >= 30, $"slot {attacker.Slot} held {d.Latest.Players[attacker.Slot].Capture}");
        for (int i = 1; i < seenByD.Count; i++) Assert.True(seenByD[i] >= seenByD[i - 1], $"D's progress fell from {seenByD[i - 1]} to {seenByD[i]}");
        Assert.Contains("match ended: standard, team 0, captured", log);
    }

    [Fact]
    public void Base_progress_reaches_a_team_that_cannot_see_the_attackers()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.StandardWalled("w"), log);
        BattleClient a = h.AddClient("A"), d = h.AddClient("D");
        Start(h, BattleMode.Standard, 0, 1);
        int before = 0, after = 0;
        byte last = 0;

        for (int frame = 0; frame < 15 * 120; frame++)
        {
            h.Run(FrameDt, (i, _) => i == 0 ? IntoTheEnemyBase(a) : LocalCommand.None, FrameDt);
            if (d.Latest is not { } snapshot) continue;
            Assert.False(snapshot.TryFind(a.OwnTankId, out _), $"D saw A at progress {d.Match.BaseProgress1}");
            Assert.True(d.Match.BaseProgress1 >= last);
            last = d.Match.BaseProgress1;
            if (last < 5) before++;
            else after++;
        }

        Assert.True(Vector2.Distance(h.ServerTankOf(a).Position, h.ServerTankOf(d).Position) > 15f);
        Assert.True(before > 0 && after > 0, $"{before} snapshots before 5 points, {after} after");
        Assert.True(last >= 5, $"the progress only reached {last}");
    }

    [Fact]
    public void An_attackers_capture_reaches_the_enemy_only_once_the_match_is_over_and_team_mates_all_along()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log, matchSeconds: 10);
        BattleClient a = h.AddClient("A"), b = h.AddClient("B"), d = h.AddClient("D");
        Start(h, BattleMode.Standard, 0, 0, 1);
        byte serverBest = 0, mateRead = 0, ownRead = 0;

        while (h.Server.Rules!.Phase == MatchPhase.Playing)
        {
            h.Run(FrameDt, (i, _) => i == 0 ? IntoTheEnemyBase(a) : LocalCommand.None, FrameDt);
            serverBest = h.Server.Rules.CaptureBest(a.Slot);
            if (d.Latest is { Match.Phase: MatchPhase.Playing } enemy) Assert.Equal(0, enemy.Players[a.Slot].Capture);
            if (b.Latest is { Match.Phase: MatchPhase.Playing } mate) mateRead = System.Math.Max(mateRead, mate.Players[a.Slot].Capture);
            if (a.Latest is { Match.Phase: MatchPhase.Playing } own) ownRead = System.Math.Max(ownRead, own.Players[a.Slot].Capture);
        }
        h.Run(0.5);
        Assert.Equal(serverBest, h.Server.Rules.CaptureBest(a.Slot));   // nothing counts once the match is over

        Assert.Equal(MatchEndReason.TimeUp, h.Server.Rules.EndReason);
        Assert.True(serverBest >= 3, $"A only held {serverBest} points");
        Assert.InRange((int)mateRead, serverBest - 1, (int)serverBest);   // a snapshot or two behind at most
        Assert.InRange((int)ownRead, serverBest - 1, (int)serverBest);
        foreach (BattleClient viewer in h.Clients)
        {
            Assert.Equal(MatchPhase.Ended, viewer.Match.Phase);
            Assert.Equal(serverBest, viewer.Latest!.Players[a.Slot].Capture);
        }
    }

    [Fact]
    public void A_standard_match_runs_out_of_time_in_a_draw()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log, matchSeconds: 3);
        h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.Standard);
        h.Run(3.0);

        Assert.All(h.Clients, c =>
        {
            Assert.Equal(MatchPhase.Ended, c.Match.Phase);
            Assert.Equal(MatchRules.NoTeam, c.Match.WinnerTeam);
            Assert.Equal(MatchEndReason.TimeUp, c.Match.EndReason);
        });
        Assert.Contains("match ended: standard, draw, time up", log);
    }

    [Fact]
    public void Wiping_out_the_other_side_ends_it_for_everyone()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log);
        BattleClient a = h.AddClient("A"), d = h.AddClient("D");
        Start(h, BattleMode.Standard, 0, 1);

        Destroy(h, a, d);
        h.Run(0.5);

        Assert.All(h.Clients, c =>
        {
            Assert.Equal(MatchPhase.Ended, c.Match.Phase);
            Assert.Equal(0, c.Match.WinnerTeam);
            Assert.Equal(MatchEndReason.Eliminated, c.Match.EndReason);
        });
        Assert.Contains("match ended: standard, team 0, eliminated", log);
    }

    // ---- joining ----

    [Fact]
    public void A_player_joining_a_running_standard_match_gets_a_new_tank()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log);
        h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.Standard);
        h.Run(2.0);   // StartMatch ran the match's first second

        BattleClient late = h.AddClient("Late");
        h.Run(1.0);

        Assert.Equal(BattlePhase.InMatch, late.Phase);
        Assert.Equal(TestMaps.StandardBases, late.MatchInfo!.Bases);
        Assert.NotEqual(0, late.OwnTankId);
        Assert.Equal(0, h.Server.Rules!.Of(late.Slot).Team);   // a tie goes to team 0
        PlayerStats row = late.Latest!.Players[late.Slot];
        Assert.Equal(1, row.Spec);   // the medium
        Assert.True(row.Alive);
    }

    [Fact]
    public void An_eliminated_player_who_rejoins_after_the_grace_period_waits_for_the_next_match()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log, graceSeconds: 1);
        BattleClient a = h.AddClient("A"), b = h.AddClient("B"), d = h.AddClient("D");
        Start(h, BattleMode.Standard, 0, 0, 1);
        Destroy(h, d, b);
        int tanks = h.Server.Simulation!.TankCount;

        h.Links[h.Clients.IndexOf(b)].Dispose();   // quits to the menu
        h.Run(2.0);                                  // and stays away past the grace period: the seat is freed
        Assert.Equal(BattlePhase.Disconnected, b.Phase);
        Assert.DoesNotContain(h.Clients[0].Room!.Players, p => p.Slot == b.Slot && p.Name == "B");
        BattleClient back = h.AddClient("B", token: b.SessionToken);
        h.Run(1.5);

        Assert.Equal(BattlePhase.Room, back.Phase);
        Assert.True(back.Room!.MatchRunning);
        Assert.Equal(0, h.Server.TankOf(back.Slot));
        Assert.False(h.Server.Rules!.Of(back.Slot).Active);
        Assert.Equal(tanks - 1, h.Server.Simulation!.TankCount);   // the wreck went with the freed seat; no new tank came
        Assert.Contains(log, line => line.Contains("already had a tank in this match"));

        h.Clients.Single(c => c.IsHost).ReturnToRoom();
        h.Run(0.5);
        h.StartMatch(BattleMode.Standard);
        Assert.Equal(BattlePhase.InMatch, back.Phase);   // the next match is a new one: a tank again
        Assert.NotEqual(0, h.Server.TankOf(back.Slot));
    }

    [Fact]
    public void An_eliminated_player_who_rejoins_within_the_grace_period_gets_the_wreck_back_not_a_new_tank()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log);
        BattleClient a = h.AddClient("A"), b = h.AddClient("B"), d = h.AddClient("D");
        Start(h, BattleMode.Standard, 0, 0, 1);
        Destroy(h, d, b);
        int wreck = h.Server.TankOf(b.Slot);
        int tanks = h.Server.Simulation!.TankCount;

        h.Links[h.Clients.IndexOf(b)].Dispose();
        h.Run(1.0);
        BattleClient back = h.AddClient("B", token: b.SessionToken);
        h.Run(1.5);

        Assert.Equal(BattlePhase.InMatch, back.Phase);
        Assert.Equal(b.Slot, back.Slot);
        Assert.Equal(TestMaps.StandardBases, back.MatchInfo!.Bases);
        Assert.Equal(wreck, back.OwnTankId);
        Assert.True(h.Server.Simulation!.Tanks[h.Server.Simulation.SlotOf(wreck)].IsDestroyed);
        Assert.False(back.Latest!.Players[back.Slot].Alive);
        Assert.Equal(tanks, h.Server.Simulation.TankCount);
    }

    [Fact]
    public void A_player_who_left_a_standard_match_alive_cannot_come_back_with_a_fresh_tank()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log, graceSeconds: 1);
        BattleClient a = h.AddClient("A"), b = h.AddClient("B"), d = h.AddClient("D");
        Start(h, BattleMode.Standard, 0, 0, 1);

        h.Links[h.Clients.IndexOf(b)].Dispose();
        h.Run(2.0);
        BattleClient back = h.AddClient("B", token: b.SessionToken);
        h.Run(1.5);

        Assert.Equal(BattlePhase.Room, back.Phase);
        Assert.Equal(0, h.Server.TankOf(back.Slot));
        Assert.Equal(2, h.Server.Simulation!.TankCount);
    }

    [Fact]
    public void A_new_player_still_joins_a_standard_match_after_one_was_turned_away()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log, graceSeconds: 1);
        BattleClient a = h.AddClient("A"), b = h.AddClient("B"), d = h.AddClient("D");
        Start(h, BattleMode.Standard, 0, 0, 1);
        Destroy(h, d, b);
        h.Links[h.Clients.IndexOf(b)].Dispose();
        h.Run(2.0);
        BattleClient back = h.AddClient("B", token: b.SessionToken);
        h.Run(1.0);
        Assert.Equal(0, h.Server.TankOf(back.Slot));

        BattleClient fresh = h.AddClient("E");             // nobody this match has seen
        BattleClient stranger = h.AddClient("F", token: 12345);   // a token the server never issued
        h.Run(1.5);

        Assert.NotEqual(0u, stranger.SessionToken);
        Assert.NotEqual(12345u, stranger.SessionToken);   // the server issued its own, not the forged one
        foreach (BattleClient newcomer in new[] { fresh, stranger })
        {
            Assert.Equal(BattlePhase.InMatch, newcomer.Phase);
            Assert.NotEqual(0, newcomer.OwnTankId);
            Assert.True(newcomer.Latest!.Players[newcomer.Slot].Alive);
        }
    }

    [Fact]
    public void In_a_team_battle_a_player_who_left_comes_back_with_a_tank()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log, graceSeconds: 1);
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        Start(h, BattleMode.TeamBattle, 0, 1);

        h.Links[h.Clients.IndexOf(b)].Dispose();
        h.Run(2.0);
        BattleClient back = h.AddClient("B", token: b.SessionToken);
        h.Run(1.5);

        Assert.Equal(BattlePhase.InMatch, back.Phase);   // respawns: leaving and coming back is no way round anything
        Assert.NotEqual(0, back.OwnTankId);
    }

    [Fact]
    public void In_annihilation_a_player_back_within_the_grace_period_gets_the_same_seat_and_tank_and_no_second_seat()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log);
        BattleClient a = h.AddClient("A"), b = h.AddClient("B"), d = h.AddClient("D");
        Start(h, BattleMode.Annihilation, 0, 0, 1);
        int tank = h.Server.TankOf(b.Slot);
        int tanks = h.Server.Simulation!.TankCount;
        Assert.NotEqual(0, tank);

        h.Links[h.Clients.IndexOf(b)].Dispose();   // quits to the menu
        h.Run(1.0);                                  // and joins again from it, with the token the game kept
        BattleClient back = h.AddClient("B", token: b.SessionToken);
        h.Run(1.5);

        Assert.Equal(BattlePhase.InMatch, back.Phase);
        Assert.Equal(b.Slot, back.Slot);
        Assert.Equal(b.SessionToken, back.SessionToken);
        Assert.Equal(tank, back.OwnTankId);
        Assert.Equal(tanks, h.Server.Simulation!.TankCount);
        Assert.Equal(3, a.Room!.Players.Count);
        Assert.Single(a.Room.Players, p => p.Name == "B");
        Assert.Contains($"slot {b.Slot} reconnected as 'B'", log);
    }

    [Fact]
    public void A_match_ends_once_no_human_is_in_it_even_with_one_waiting_in_the_room()
    {
        var log = new List<string>();
        var h = Harness(TestMaps.Standard("s"), log, graceSeconds: 1);
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        h.Run(0.3);
        a.SetTeam(0);
        b.SetTeam(0);
        h.StartMatch(BattleMode.Standard, bots: 4);   // team 0: A, B and a bot; team 1: three bots
        Assert.True(h.Server.MatchRunning);

        h.Links[h.Clients.IndexOf(b)].Dispose();
        h.Run(2.0);                                   // B's seat is freed: B is out of this match
        BattleClient back = h.AddClient("B", token: b.SessionToken);
        h.Run(1.0);
        Assert.Equal(BattlePhase.Room, back.Phase);   // barred: waits in the room for the next match
        Assert.True(h.Server.MatchRunning);

        h.Links[h.Clients.IndexOf(a)].Dispose();
        h.Run(0.5);                                   // A is away but within the grace period: still in the match
        Assert.True(h.Server.MatchRunning, "the match ended while A could still come back");
        Assert.Equal(MatchPhase.Playing, h.Server.Rules!.Phase);

        h.Run(1.0);                                   // A's seat is freed: only bots are left in the match
        Assert.False(h.Server.MatchRunning);
        Assert.Contains("nobody left in the match", log);
        Assert.False(back.Room!.MatchRunning);
    }

    // ---- the riverland ----

    [Fact]
    public void Riverland_standard_with_bots_runs_and_ends_cleanly()
    {
        var log = new List<string>();
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Map = MapCatalog.Riverland, MatchSeconds = 20 }, log: log.Add);
        h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.Standard, bots: 3);
        Assert.True(h.Server.MatchRunning);
        Assert.All(h.Clients, c => Assert.Equal(RiverlandMap.Bases, c.MatchInfo!.Bases));

        h.Run(20.0);

        Assert.All(h.Clients, c =>
        {
            Assert.Equal(MatchPhase.Ended, c.Match.Phase);
            Assert.Contains(c.Match.EndReason, new[] { MatchEndReason.TimeUp, MatchEndReason.Eliminated, MatchEndReason.Captured });
            Assert.Null(c.LastPacketError);
        });
        Assert.DoesNotContain(log, line => line.Contains("error", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(log, line => line.StartsWith("match ended: standard", StringComparison.Ordinal));
    }
}

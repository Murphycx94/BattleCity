using System.Numerics;
using Tank.Net.Battle;
using Tank.Net.Protocol;
using Tank.Sim;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// The server's royale (stage 6 design §2, §3): the room switches to it only onto the riverland and with no more players
/// than it seats, a match starts with the rings drawn and the first two known, each later ring goes out as its waiting
/// begins (and with any resync), the scoreboard keeps an opponent's damage and the living's time lasted to itself until
/// the end, and the eliminated watch every tank.
/// </summary>
public sealed class RoyaleServerTests
{
    private const double FrameDt = 1.0 / 120;
    private const float SpawnReach = 20f;

    /// <summary>A room on the riverland among the catalogue's maps, in <paramref name="mode"/>.</summary>
    private static BattleHarness Riverland(List<string> log, BattleMode mode = BattleMode.Royale, int bots = 0, uint seed = 1) =>
        new(new BattleServerOptions { Seed = seed, Mode = mode, Bots = bots, Map = MapCatalog.Riverland, Maps = MapCatalog.All }, log: log.Add);

    /// <summary>A royale room on <see cref="TestMaps.RoyaleStandIn"/>, the only map it has.</summary>
    private static BattleHarness StandIn(List<string> log, double? matchSeconds = null)
    {
        MapDefinition map = TestMaps.RoyaleStandIn();
        return new(new BattleServerOptions { Seed = 1, Mode = BattleMode.Royale, Map = map, Maps = new[] { map }, MatchSeconds = matchSeconds }, log: log.Add);
    }

    private static void SetMode(BattleHarness h, BattleMode mode)
    {
        h.Clients.Single(c => c.IsHost).SetMode(mode);
        h.Run(0.2);
    }

    private static RoomInfoMessage RoomInfoOf(BattleServer server)
    {
        var w = new BitWriter();
        Assert.True(server.WriteRoomInfo(w));
        var r = new BitReader(w.Data);
        Assert.Equal((byte)BattleMessage.RoomInfo, r.ReadByte());
        return BattleMessages.ReadRoomInfo(ref r);
    }

    // ---- the room ----

    [Fact]
    public void Switching_to_a_royale_moves_the_room_to_the_riverland_and_keeps_it_there()
    {
        var log = new List<string>();
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Map = MapCatalog.Sandbox, Maps = MapCatalog.All }, log: log.Add);
        BattleClient host = h.AddClient("Host");
        h.Run(0.3);

        SetMode(h, BattleMode.Royale);
        Assert.Equal(BattleMode.Royale, h.Server.Mode);
        Assert.Equal(BattleMode.Royale, host.Room!.Mode);
        Assert.Equal(RiverlandMap.Id, host.Room.Map);
        Assert.Same(MapCatalog.Riverland, h.Server.RoomMap);

        host.SetMap(SandboxMap.Id);
        h.Run(0.2);
        Assert.Equal(RiverlandMap.Id, host.Room!.Map);

        host.SetBots(15);
        h.Run(0.2);
        Assert.Equal(15, h.Server.Bots);
        Assert.Equal(15, host.Room!.Bots);
    }

    [Fact]
    public void A_royale_room_with_eleven_players_cannot_switch_to_a_ten_seat_mode()
    {
        var log = new List<string>();
        var h = Riverland(log);
        for (int i = 0; i < 11; i++) h.AddClient($"P{i}");
        h.Run(0.5);
        Assert.Equal(11, h.Server.ConnectedCount);

        SetMode(h, BattleMode.TeamBattle);

        Assert.Equal(BattleMode.Royale, h.Server.Mode);
        Assert.All(h.Clients, c => Assert.Equal(BattleMode.Royale, c.Room!.Mode));
        Assert.Contains(log, l => l.Contains("can't switch to teambattle: 11 players"));

        h.Links[10].Dispose();   // one leaves: ten fit a team battle
        h.Run(0.5);
        SetMode(h, BattleMode.TeamBattle);
        Assert.Equal(BattleMode.TeamBattle, h.Server.Mode);
    }

    [Fact]
    public void Without_the_riverland_a_room_never_turns_royale()
    {
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Map = MapCatalog.Sandbox, Maps = new[] { MapCatalog.Sandbox } });
        BattleClient host = h.AddClient("Host");
        h.Run(0.3);

        SetMode(h, BattleMode.Royale);

        Assert.Equal(BattleMode.TeamBattle, h.Server.Mode);
        Assert.Equal(SandboxMap.Id, host.Room!.Map);
    }

    [Fact]
    public void A_royale_on_another_map_is_refused_by_the_options()
    {
        var net = new Tank.Net.Transport.LoopbackNetwork(1);
        var ex = Assert.Throws<ArgumentException>(() =>
            new BattleServer(net.CreateServer(), new BattleServerOptions { Mode = BattleMode.Royale, Map = MapCatalog.Sandbox }));
        Assert.Contains("royale is played on the riverland only", ex.Message);
    }

    [Fact]
    public void The_lan_answer_gives_the_seats_of_the_rooms_mode()
    {
        var log = new List<string>();
        Assert.Equal(10, RoomInfoOf(Riverland(log, BattleMode.TeamBattle).Server).MaxPlayers);
        Assert.Equal(16, RoomInfoOf(Riverland(log, BattleMode.Royale).Server).MaxPlayers);
    }

    // ---- the start ----

    [Fact]
    public void A_royale_starts_with_sixteen_tanks_on_sixteen_spawns_and_the_first_two_rings_known()
    {
        var log = new List<string>();
        var h = Riverland(log, seed: 3);
        BattleClient a = h.AddClient("A");
        h.StartMatch(BattleMode.Royale, bots: 15);
        Assert.True(h.Server.MatchRunning, "the match didn't start");

        Simulation sim = h.Server.Simulation!;
        Assert.Equal(16, sim.TankCount);
        var spawnsTaken = new HashSet<int>();
        IReadOnlyList<SpawnPoint> solo = RiverlandMap.Spawns.Solo;
        foreach (TankState t in sim.Tanks)
        {
            int nearest = Enumerable.Range(0, solo.Count).MinBy(i => Vector2.Distance(solo[i].Position, t.Position));
            Assert.True(Vector2.Distance(solo[nearest].Position, t.Position) < SpawnReach, $"tank {t.Id} started at {t.Position}, off every spawn");
            spawnsTaken.Add(nearest);
        }
        Assert.Equal(16, spawnsTaken.Count);

        MatchRules rules = h.Server.Rules!;
        Assert.Equal(6, rules.ZoneRings.Count);
        Assert.True(a.HasZone);
        Assert.Equal(2, a.ZoneRings.Length);
        Assert.Equal(rules.StartTick, a.ZoneStartTick);
        Assert.Equal(rules.ZoneRings[0], a.ZoneRings[0]);
        Assert.Equal(rules.ZoneRings[1], a.ZoneRings[1]);
        Assert.Contains(log, l => l.StartsWith("zone: ring 1 (") && l.Contains("ring 5 ("));
        Assert.Equal(16, a.Match.Entrants);   // the real count, bots included
        Assert.Null(a.LastPacketError);
    }

    [Fact]
    public void The_rings_follow_the_match_seed_and_a_team_battle_has_none()
    {
        var log = new List<string>();
        var h = Riverland(log, seed: 3);
        BattleClient a = h.AddClient("A");
        h.StartMatch(BattleMode.Royale, bots: 1);
        ZoneRing[] expected = ZonePicker.Pick(MapCatalog.Riverland.NewBattlefield(), MapCatalog.Riverland.NewNavGrid(), RiverlandMap.Spawns.Solo, 3u ^ ZonePicker.SeedSalt);
        Assert.Equal(expected, h.Server.Rules!.ZoneRings);

        a.ReturnToRoom();
        h.Run(0.5);
        h.StartMatch(BattleMode.TeamBattle, bots: 1);
        Assert.True(h.Server.MatchRunning);
        Assert.False(a.HasZone);
        Assert.Null(a.MatchInfo!.Zone);
        Assert.Empty(h.Server.Rules!.ZoneRings);
    }

    [Fact]
    public void A_zone_that_fails_to_start_leaves_the_room_as_it_was_and_the_next_start_runs()
    {
        var log = new List<string>();
        var h = StandIn(log);
        BattleClient host = h.AddClient("A");
        h.AddClient("B");
        var c = new Vector2(100, 100);
        var failures = new Func<Battlefield, Tank.Sim.Navigation.NavGrid, IReadOnlyList<SpawnPoint>, uint, ZoneRing[]>[]
        {
            (_, _, _, _) => throw new InvalidOperationException("No land."),   // the picker fails
            (_, _, _, _) => new[] { new ZoneRing(c, 90), new ZoneRing(c, 50) },   // or draws what the rules refuse (two rings, not six)
        };
        foreach (var failure in failures)
        {
            log.Clear();
            h.Server.ZonePickerOverride = failure;
            h.StartMatch(BattleMode.Royale, bots: 1);

            Assert.False(h.Server.MatchRunning);
            Assert.Contains(log, l => l.StartsWith("match not started: the zone failed"));
            Assert.Contains("can't start: the zone failed to start", log);
            Assert.Equal(BattlePhase.Room, host.Phase);
            Assert.False(host.Room!.MatchRunning);
            Assert.Equal(2, host.Room.Players.Count);   // no bot seated and left behind
            Assert.DoesNotContain(host.Room.Players, p => p.IsBot);
        }

        h.Server.ZonePickerOverride = null;
        h.StartMatch(BattleMode.Royale, bots: 1);
        Assert.True(h.Server.MatchRunning);
        Assert.Equal(3, h.Server.Simulation!.TankCount);
        Assert.Equal(6, h.Server.Rules!.ZoneRings.Count);
        h.Run(1.0);
        Assert.Equal(BattlePhase.InMatch, host.Phase);
        Assert.Null(host.LastPacketError);
    }

    // ---- the rings announced ----

    [Fact]
    public void Each_ring_goes_out_as_its_waiting_begins_and_a_rejoin_gets_all_known()
    {
        var log = new List<string>();
        var h = Riverland(log);
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        h.StartMatch(BattleMode.Royale);
        Assert.True(h.Server.MatchRunning);
        MatchRules rules = h.Server.Rules!;
        int ring2At = rules.StartTick + ZoneSchedule.WaitStart(2);

        while (h.Server.Simulation!.Tick < ring2At - 2 * SimConstants.TickRate) h.Run(1.0, frameDt: 1.0 / 30);
        while (h.Server.Simulation.Tick < ring2At - 3) h.Run(1.0 / 30, frameDt: 1.0 / 30);
        Assert.All(h.Clients, c => Assert.Equal(2, c.ZoneRings.Length));   // not a tick early
        Assert.DoesNotContain(log, l => l.StartsWith("zone ring 2 announced"));
        h.Run(0.3, frameDt: 1.0 / 30);   // 180.2 s and a little more

        Assert.Equal(3, rules.KnownRings);
        foreach (BattleClient c in h.Clients)
        {
            Assert.Equal(3, c.ZoneRings.Length);
            Assert.Equal(rules.ZoneRings[2], c.ZoneRings[2]);
        }
        Assert.Single(log, l => l.StartsWith("zone ring 2 announced: ("));

        h.Links[1].Dispose();   // B drops and comes back within the grace period
        h.Run(0.5, frameDt: 1.0 / 30);
        BattleClient back = h.AddClient("B again", token: b.SessionToken);
        h.Run(1.0, frameDt: 1.0 / 30);
        Assert.Equal(BattlePhase.InMatch, back.Phase);
        Assert.Equal(3, back.MatchInfo!.Zone!.Rings.Count);
        Assert.Equal(rules.ZoneRings.Take(3), back.ZoneRings.ToArray());
        Assert.Equal(rules.StartTick, back.ZoneStartTick);
    }

    // ---- the scoreboard and sight ----

    /// <summary>
    /// Three players on <see cref="TestMaps.RoyaleStandIn"/>: <c>Shooter</c> and <c>Target</c> west of the wall, 30 m
    /// apart, and <c>Hidden</c> east of it (the spawn order is the seed's, so the roles go by where the tanks stand).
    /// </summary>
    private static (BattleHarness H, BattleClient Shooter, BattleClient Target, BattleClient Hidden) ThreeOnTheStandIn(List<string> log, double? matchSeconds = null)
    {
        var h = StandIn(log, matchSeconds);
        for (int i = 0; i < 3; i++) h.AddClient($"P{i}");
        h.StartMatch(BattleMode.Royale);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        var byX = h.Clients.OrderBy(c => h.ServerTankOf(c).Position.X).ToArray();
        Assert.True(byX[2].Slot != byX[0].Slot && h.ServerTankOf(byX[2]).Position.X > TestMaps.StandardWallX);
        return (h, byX[0], byX[1], byX[2]);
    }

    /// <summary>The shooter's gun laid on the target's hull front, firing (as <see cref="StandardServerTests"/> does).</summary>
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

    /// <summary>Fires at the target until the server has a hit on it, then until it is destroyed (a minute at most).</summary>
    private static void Destroy(BattleHarness h, BattleClient shooter, BattleClient target, Action? afterFirstHit = null)
    {
        int shooterIndex = h.Clients.IndexOf(shooter);
        bool hit = false;
        for (int frame = 0; frame < 60 * 120 && h.Server.Rules!.Of(target.Slot).Alive; frame++)
        {
            h.Run(FrameDt, (c, _) => c == shooterIndex ? Attack(h, shooter, target) : LocalCommand.None, FrameDt);
            if (hit || h.Server.Rules.Of(shooter.Slot).Damage == 0) continue;
            hit = true;
            afterFirstHit?.Invoke();
        }
        Assert.True(hit, "no shot hit");
        Assert.False(h.Server.Rules!.Of(target.Slot).Alive, "the target outlived a minute of fire");
    }

    [Fact]
    public void An_opponents_damage_and_the_livings_time_stay_hidden_until_the_end()
    {
        var log = new List<string>();
        var (h, shooter, target, hidden) = ThreeOnTheStandIn(log, matchSeconds: 75);

        Destroy(h, shooter, target, afterFirstHit: () =>
        {
            h.Run(0.3);   // the snapshots catch up with the hit
            Assert.True(shooter.Latest!.Players[shooter.Slot].Damage > 0, "the shooter sees its own damage");
            Assert.Equal(0, target.Latest!.Players[shooter.Slot].Damage);
            Assert.Equal(0, hidden.Latest!.Players[shooter.Slot].Damage);
        });
        h.Run(1.0);

        MatchRules rules = h.Server.Rules!;
        Assert.Equal(MatchPhase.Playing, rules.Phase);
        int damage = rules.Of(shooter.Slot).Damage;
        Assert.Equal(damage, shooter.Latest!.Players[shooter.Slot].Damage);
        Assert.Equal(0, target.Latest!.Players[shooter.Slot].Damage);
        foreach (BattleClient viewer in h.Clients)
        {
            PlayerStats[] rows = viewer.Latest!.Players;
            Assert.Equal(rules.LastedTicks(target.Slot) / SimConstants.TickRate, rows[target.Slot].Lasted);   // out: how long it lasted
            Assert.True(rows[target.Slot].Lasted > 0);
            Assert.Equal(0, rows[shooter.Slot].Lasted);   // still in: nothing yet
            Assert.Equal(0, rows[hidden.Slot].Lasted);
            Assert.Equal(3, viewer.Match.Entrants);
        }

        for (int second = 0; second < 90 && rules.Phase == MatchPhase.Playing; second++) h.Run(1.0, frameDt: 1.0 / 30);
        Assert.Equal(MatchPhase.Ended, rules.Phase);
        h.Run(0.5, frameDt: 1.0 / 30);

        Assert.Equal(MatchPhase.Ended, target.Match.Phase);
        Assert.Equal(damage, target.Latest!.Players[shooter.Slot].Damage);
        Assert.Equal(3, target.Match.Entrants);
        foreach (BattleClient c in h.Clients)
            Assert.Equal(rules.LastedTicks(c.Slot) / SimConstants.TickRate, target.Latest.Players[c.Slot].Lasted);
        Assert.True(target.Latest.Players[shooter.Slot].Lasted > target.Latest.Players[target.Slot].Lasted);
    }

    [Fact]
    public void A_royale_players_time_lasted_appears_on_the_tick_it_goes_out()
    {
        var log = new List<string>();
        var (h, shooter, target, _) = ThreeOnTheStandIn(log);

        Destroy(h, shooter, target);
        h.Run(0.3);

        MatchRules rules = h.Server.Rules!;
        int fell = rules.Of(target.Slot).EliminatedAt;
        int seconds = (fell - rules.StartTick) / SimConstants.TickRate;
        Assert.True(seconds > 0);
        foreach (BattleClient viewer in h.Clients)
        {
            Assert.Equal(0, viewer.Store.Find(fell - 1)!.Players[target.Slot].Lasted);   // still in: nothing
            Assert.Equal(seconds, viewer.Store.Find(fell)!.Players[target.Slot].Lasted);  // out: the whole seconds, at once
        }
    }

    [Fact]
    public void A_zone_kill_reaches_every_client_as_the_zones_with_no_killer()
    {
        var log = new List<string>();
        var h = StandIn(log);
        // ring 0 round the two eastern spawns (50 m from its centre) and not the western one (80 m): the tank there is outside from
        // the start, and hurt from ring 1's shrink on (90 s), 1 % a second; the others stay inside every ring
        var c = new Vector2(120, 100);
        ZoneRing[] rings = { new(c, 65), new(c, 62), new(c, 60), new(c, 58), new(c, 56), new(c, 54) };
        h.Server.ZonePickerOverride = (_, _, _, _) => rings;
        for (int i = 0; i < 3; i++) h.AddClient($"P{i}");
        h.StartMatch(BattleMode.Royale);
        Assert.True(h.Server.MatchRunning, "the match didn't start");
        Assert.Equal(rings, h.Server.Rules!.ZoneRings);
        BattleClient victim = h.Clients.MinBy(cl => h.ServerTankOf(cl).Position.X)!;
        int victimTank = h.Server.TankOf(victim.Slot);
        Assert.False(rings[0].Contains(h.ServerTankOf(victim).Position));
        var kills = h.Clients.ToDictionary(cl => cl, _ => new List<GameEvent>());
        void Drain()
        {
            foreach (BattleClient cl in h.Clients)
                while (cl.TryDequeueEvent(out GameEvent e))
                    if (e.Type == GameEventType.TankDestroyed) kills[cl].Add(e);
        }

        for (int second = 0; second < 200 && h.Server.Rules.Of(victim.Slot).Alive; second++)
        {
            h.Run(1.0, frameDt: 1.0 / 30);
            Drain();
        }
        Assert.False(h.Server.Rules.Of(victim.Slot).Alive, "the zone never finished the tank off");
        h.Run(0.5, frameDt: 1.0 / 30);
        Drain();

        Assert.Equal(MatchPhase.Playing, h.Server.Rules.Phase);   // two still in
        foreach (BattleClient cl in h.Clients)
        {
            GameEvent kill = Assert.Single(kills[cl]);
            Assert.Equal((victimTank, KillCause.Zone, 0), (kill.TargetTankId, kill.Cause, kill.TankId));
            Assert.Null(cl.LastPacketError);
        }
    }

    // ---- entrants and time lasted in the other modes ----

    [Fact]
    public void A_team_battle_seat_coming_and_going_never_stalls_the_others_and_sends_no_entrants()
    {
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Mode = BattleMode.TeamBattle, ReconnectGraceSeconds = 0.5 });
        BattleClient a = h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        Assert.True(h.Server.MatchRunning);

        for (int i = 0; i < 20; i++)
        {
            h.AddClient($"X{i}");
            h.Run(1.0, frameDt: 1.0 / 30);
            h.Links[^1].Dispose();   // gone past the grace period: the seat is freed, and the next one takes it
            h.Run(1.5, frameDt: 1.0 / 30);
            int serverTick = h.Server.Simulation!.Tick;
            Assert.True(a.Latest!.Tick > serverTick - SimConstants.TickRate, $"after {i + 1} comings and goings A is at tick {a.Latest.Tick}, the server at {serverTick}");
            Assert.Null(a.LastPacketError);
        }

        Assert.True(h.Server.MatchRunning);
        Assert.Equal(3, h.Server.Rules!.Entrants);   // each seat once
        Assert.Equal(0, a.Match.Entrants);            // sent only where it means something: a royale
    }

    [Fact]
    public void A_team_battle_tank_waiting_to_respawn_shows_no_time_lasted()
    {
        var log = new List<string>();
        MapDefinition map = TestMaps.RoyaleStandIn();
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, Mode = BattleMode.TeamBattle, Map = map, Maps = new[] { map } }, log: log.Add);
        BattleClient shooter = h.AddClient("Shooter"), target = h.AddClient("Target");
        h.Run(0.3);
        shooter.SetTeam(0);
        target.SetTeam(1);
        h.StartMatch(BattleMode.TeamBattle);
        Assert.True(h.Server.MatchRunning);
        TankState s = h.ServerTankOf(shooter);
        h.Server.Simulation!.Respawn(h.Server.TankOf(target.Slot), s.Position + new Vector2(30, 0), Angles.YawOf(-Vector2.UnitX));   // in its sights

        Destroy(h, shooter, target);
        h.Run(3.0);   // a few seconds into the ten-second wait

        Assert.Equal(MatchPhase.Playing, h.Server.Rules!.Phase);
        Assert.True(target.Match.RespawnIn > 0, "the target isn't waiting to respawn");
        foreach (BattleClient viewer in h.Clients)
        {
            PlayerStats row = viewer.Latest!.Players[target.Slot];
            Assert.False(row.Alive);
            Assert.Equal(0, row.Lasted);   // a respawn mode: nothing to count, and a line that changed every second would cost every snapshot
        }
    }

    [Fact]
    public void An_eliminated_player_watches_every_tank()
    {
        var log = new List<string>();
        var (h, shooter, target, hidden) = ThreeOnTheStandIn(log);
        int hiddenTank = h.Server.TankOf(hidden.Slot);
        h.Run(0.5);
        Assert.DoesNotContain(target.Latest!.Entities.Take(target.Latest.EntityCount), e => e.Id == hiddenTank);   // the wall hides it

        Destroy(h, shooter, target);
        h.Run(0.5);

        Assert.Equal(MatchPhase.Playing, h.Server.Rules!.Phase);
        Assert.Equal(3, target.Latest!.EntityCount);
        Assert.Contains(target.Latest.Entities.Take(target.Latest.EntityCount), e => e.Id == hiddenTank);
    }
}

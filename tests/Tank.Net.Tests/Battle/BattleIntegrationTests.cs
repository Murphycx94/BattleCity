using System.Numerics;
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Net.Tests.Battle;

public sealed class BattleIntegrationTests
{
    /// <summary>Drives about, swinging the aim and tapping fire now and then: a busy human.</summary>
    private static LocalCommand Wander(int client, double t) => Wander(client, t, fire: true);

    private static LocalCommand Wander(int client, double t, bool fire)
    {
        int phase = (int)(t / 0.7) + client * 3;
        sbyte throttle = (sbyte)((phase % 4) switch { 0 => 1, 1 => 1, 2 => -1, _ => 0 });
        sbyte steer = (sbyte)((phase % 3) switch { 0 => 1, 1 => -1, _ => 0 });
        bool shoot = fire && (int)(t * 10) % 11 == 0;
        return new LocalCommand(throttle, steer, (float)(t * 0.4 + client), 0.05f, shoot);   // one round type: a swap restarts the reload
    }

    private static LocalCommand Still(int client, double t) => new(0, 0, 0f, 0f, false);

    [Fact]
    public void Clients_join_the_room_the_first_is_host_and_the_host_starts_a_match()
    {
        var h = new BattleHarness();
        BattleClient a = h.AddClient("安娜"), b = h.AddClient("Bob");
        h.Run(0.3);
        Assert.Equal(BattlePhase.Room, a.Phase);
        Assert.Equal(BattlePhase.Room, b.Phase);
        Assert.True(a.IsHost);
        Assert.False(b.IsHost);
        Assert.Equal(new[] { "安娜", "Bob" }, b.Room!.Players.Select(p => p.Name));
        Assert.Equal(new byte[] { 0, 1 }, b.Room.Players.Select(p => p.Team));   // balanced on arrival

        b.StartMatch();   // not the host
        b.SetTank("heavy");
        h.Run(0.2);
        Assert.False(h.Server.MatchRunning);
        Assert.Equal(2, a.Room!.Players[1].TankIndex);

        a.SetMode(BattleMode.Annihilation);
        a.StartMatch();
        h.Run(1.0);
        Assert.True(h.Server.MatchRunning);
        Assert.All(h.Clients, c => Assert.Equal(BattlePhase.InMatch, c.Phase));
        Assert.All(h.Clients, c => Assert.True(c.HasTank));
        Assert.Equal(TankCatalog.Heavy, b.Own.Spec);
        Assert.Equal(1, b.Own.Team);
        Assert.Equal(BattleMode.Annihilation, a.MatchInfo!.Mode);
        Assert.Equal(h.Server.Simulation!.Field.Props, a.Field!.Props);
        Assert.Equal(MapData.Of(h.Server.Simulation.Field).HeightsCm, MapData.Of(a.Field).HeightsCm);
        Assert.True(a.Match.TicksLeft > 0);
        Assert.Equal(MatchPhase.Playing, a.Match.Phase);
    }

    [Fact]
    public void On_a_perfect_link_the_prediction_is_never_corrected()
    {
        var h = new BattleHarness();
        h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        h.Run(8.0, Wander);
        h.Run(1.0, Still);   // the client predicts a few ticks ahead of the server: compare once both stand still
        foreach (BattleClient c in h.Clients)
        {
            Assert.Equal(0, c.Predictor.Corrections);
            Assert.True(c.Own.ShotCount >= 1, "fired at least once");
            Assert.Equal(h.ServerTankOf(c).Position, c.Own.Position);
        }
    }

    [Fact]
    public void Over_a_bad_link_corrections_stay_small_and_the_tank_settles_on_the_servers_position()
    {
        var bad = new LinkConditions(60, 40, 20);
        var h = new BattleHarness(networkSeed: 7);
        h.AddClient("A", bad, bad);
        h.AddClient("B", bad, bad);
        h.StartMatch(BattleMode.TeamBattle);
        h.Run(10.0, Wander);
        Assert.All(h.Clients, c => Assert.True(c.Predictor.MaxCorrection < 2f, $"corrected by {c.Predictor.MaxCorrection} m"));
        h.Run(2.0, Still);
        foreach (BattleClient c in h.Clients)
            Assert.True(Vector2.Distance(h.ServerTankOf(c).Position, c.Own.Position) < 0.01f, "settled on the server's position");
        Assert.All(h.Clients, c => Assert.Equal(BattlePhase.InMatch, c.Phase));
    }

    [Fact]
    public void An_enemy_out_of_sight_is_not_in_the_snapshots_or_the_events_and_a_shot_from_the_dark_has_no_shooter()
    {
        // Team spawns are 280 m apart: two medium tanks standing still don't see each other (206 m).
        var h = new BattleHarness();
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        int tankB = h.Server.TankOf(b.Slot), tankA = h.Server.TankOf(a.Slot);
        Assert.NotEqual(0, tankB);
        var fx = new List<GameEvent>();
        for (int i = 0; i < 100; i++)   // 10 s: the first 5 are spawn protection
        {
            h.Run(0.1, (c, t) => c == 1 ? new LocalCommand(0, 0, b.Own.Yaw, -0.02f, (int)(t * 10) % 15 == 0) : LocalCommand.None);   // B fires into the ground ahead: the bursts are events too
            while (a.TryDequeueEvent(out GameEvent e)) fx.Add(e);
            Assert.DoesNotContain(a.Others, o => o.Entity.Id == tankB);
            Assert.False(a.Latest!.TryFind(tankB, out _), "B's tank in A's snapshot");
            Assert.False(h.Server.Vision!.IsVisible(tankB, 0));
        }
        Assert.True(b.Own.ShotCount >= 2, $"B fired {b.Own.ShotCount} times");
        Assert.Contains(fx, e => e.Type == GameEventType.ShellFired && e.TankId == 0);
        Assert.Contains(fx, e => e.Type == GameEventType.ShellExploded && e.TankId == 0);   // the burst names no shooter either
        Assert.DoesNotContain(fx, e => e.TankId == tankB || e.TargetTankId == tankB);
        Assert.Contains(a.Latest!.Players, p => p.Active && p.TankId == tankB);   // the scoreboard still lists everyone
    }

    [Fact]
    public void Another_tank_in_sight_is_drawn_where_the_server_has_it_and_follows_it_as_it_drives()
    {
        // A client that ignored the snapshots, or an interpolator that sampled nothing, would show nobody (or a frozen tank).
        var h = new BattleHarness();
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        Simulation sim = h.Server.Simulation!;
        TankState tankA = h.ServerTankOf(a);
        int tankB = h.Server.TankOf(b.Slot);
        sim.Respawn(tankB, tankA.Position + new Vector2(0, -12), tankA.Yaw);   // 12 m away, in sight of A
        h.Run(1.0);
        Assert.True(h.Server.Vision!.IsVisible(tankB, 0), "A sees B");

        RemoteTank seen = Assert.Single(a.Others, o => o.Entity.Id == tankB);
        Assert.Equal(1, seen.Entity.Team);
        Assert.True(Vector2.Distance(seen.Position, h.ServerTankOf(b).Position) < 0.1f, "standing still: where the server has it");
        Vector2 start = seen.Position;
        float yaw = seen.Yaw;

        h.Run(4.0, (c, t) => c == 1 ? new LocalCommand(1, 0, 0f, 0f, false) : LocalCommand.None);   // B drives straight on
        Vector2 end = Assert.Single(a.Others, o => o.Entity.Id == tankB).Position;
        Vector2 server = h.ServerTankOf(b).Position;
        Assert.True(Vector2.Distance(start, end) > 5f, $"B moved only {Vector2.Distance(start, end):F2} m on A's screen");
        double lag = Vector2.Distance(end, server);
        Assert.True(lag < 3f, $"A draws B {lag:F2} m behind the server's B (interpolation delay)");   // at 7 m/s, ~0.2 s behind is 1.5 m
        Assert.Equal(yaw, Assert.Single(a.Others, o => o.Entity.Id == tankB).Yaw, 0.2f);   // driving straight: heading unchanged
        Assert.NotEqual(0, a.Stats.SnapshotsPerSecond);
    }

    [Fact]
    public void Prop_damage_reaches_every_client_and_a_late_joiner_gets_the_props_as_they_stand()
    {
        var h = new BattleHarness();
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        Simulation sim = h.Server.Simulation!;
        // The yard's gate (a brick wall segment just north of it) seen from team 0's spawn: lay A's gun on it and fire AP.
        Prop wall = sim.Field.Props.First(p => p.Kind == PropKind.BrickWall && p.Footprint.Center.X < 152 && p.Footprint.Center.Y > 339);
        var target = new Vector3(wall.Footprint.Center.X, sim.Field.Terrain.HeightAt(wall.Footprint.Center.X, wall.Footprint.Center.Y) + 1.5f, wall.Footprint.Center.Y);
        h.Run(6.0, (c, t) =>
        {
            if (c != 0) return LocalCommand.None;
            TankPose pose = TankPose.Compute(a.Own, a.Field!.Terrain);
            var (yaw, pitch) = Angles.AimAngles(target - pose.Trunnion(a.Own.Spec), a.Own.Yaw);
            return new LocalCommand(0, 0, yaw, pitch, t > 4.0);
        });
        int left = sim.Field.Durability(wall.Id);
        Assert.True(left < wall.MaxDurability, "the wall was not hit");
        int expected = BattleProtocol.DequantizeDurability(BattleProtocol.QuantizeDurability(left, wall.MaxDurability), wall.MaxDurability);
        Assert.Equal(expected, a.Field!.Durability(wall.Id));
        Assert.Equal(expected, b.Field!.Durability(wall.Id));

        BattleClient late = h.AddClient("Late");
        h.Run(1.0);
        Assert.Equal(BattlePhase.InMatch, late.Phase);   // a team battle takes late joiners
        Assert.True(late.HasTank);
        Assert.Equal(expected, late.Field!.Durability(wall.Id));
    }

    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    [InlineData(BattleMode.FreeForAll)]
    public void A_match_with_bots_runs_to_the_clock_and_everyone_returns_to_the_room(BattleMode mode)
    {
        var h = new BattleHarness(new BattleServerOptions { Seed = 3, MatchSeconds = 20 });
        BattleClient a = h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(mode, bots: 4);
        Assert.Equal(6, h.Server.Simulation!.TankCount);
        Assert.Equal(6, a.Room!.Players.Count);
        Assert.Equal(4, a.Room.Players.Count(p => p.IsBot));
        h.Run(18.0, Wander);
        Assert.All(h.Clients, c => Assert.Equal(MatchPhase.Playing, c.Match.Phase));
        Assert.All(h.Clients, c => Assert.True(c.Stats.BytesInPerSecond < 20 * 1024, $"{c.Stats.BytesInPerSecond / 1024:F1} KB/s"));
        h.Run(2.0, Wander);
        Assert.Equal(MatchPhase.Ended, h.Server.Rules!.Phase);
        h.Run(0.5);
        Assert.All(h.Clients, c => Assert.Equal(MatchPhase.Ended, c.Match.Phase));
        if (mode == BattleMode.FreeForAll)
        {
            Assert.NotEqual(MatchRules.NoSlot, a.Match.WinnerSlot);
            Assert.All(a.Latest!.Players.Where(p => p.Active), p => Assert.InRange(p.Rank, 1, 6));
        }
        else Assert.Equal(h.Server.Rules.WinnerTeam, a.Match.WinnerTeam);
        h.Run(ModeRules.ResultsTicks / 30.0 + 0.5);
        Assert.False(h.Server.MatchRunning);
        Assert.All(h.Clients, c => Assert.Equal(BattlePhase.Room, c.Phase));
        Assert.Equal(2, a.Room!.Players.Count);   // the bots are gone
    }

    [Fact]
    public void A_client_that_drops_reconnects_with_its_token_into_the_same_seat_and_tank()
    {
        var h = new BattleHarness();
        h.AddClient("A");
        BattleClient b = h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        int tankB = h.Server.TankOf(b.Slot);
        h.Links[1].Dispose();
        h.Run(1.0, Wander);
        Assert.Equal(BattlePhase.Disconnected, b.Phase);
        Assert.Equal(2, h.Server.Simulation!.TankCount);   // kept for the grace period

        BattleClient back = h.AddClient("B again", token: b.SessionToken);
        h.Run(1.5, Wander);
        Assert.Equal(BattlePhase.InMatch, back.Phase);
        Assert.Equal(b.Slot, back.Slot);
        Assert.Equal(tankB, back.OwnTankId);
        Assert.True(back.HasTank);
        Assert.Equal(h.ServerTankOf(back).Position, back.Own.Position);
    }

    [Fact]
    public void A_late_joiner_waits_in_the_room_during_a_match_without_respawns()
    {
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, MatchSeconds = 5 });
        h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.Annihilation);
        BattleClient late = h.AddClient("Late");
        h.Run(1.0);
        Assert.Equal(BattlePhase.Room, late.Phase);
        Assert.True(late.Room!.MatchRunning);
        Assert.Equal(0, h.Server.TankOf(late.Slot));
        h.Run(4.0 + ModeRules.ResultsTicks / 30.0 + 1.0);
        Assert.False(h.Server.MatchRunning);
        Assert.Equal(3, late.Room!.Players.Count);
    }

    [Fact]
    public void A_player_who_leaves_for_good_is_taken_out_of_the_match()
    {
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, ReconnectGraceSeconds = 1 });
        h.AddClient("A");
        BattleClient b = h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        int tankB = h.Server.TankOf(b.Slot);
        h.Links[1].Dispose();
        h.Run(2.0);
        Assert.Equal(1, h.Server.Simulation!.TankCount);
        Assert.Equal(-1, h.Server.Simulation.SlotOf(tankB));
        Assert.Single(h.Clients[0].Room!.Players);
    }

    [Fact]
    public void A_wrong_version_is_rejected_with_a_reason_and_a_full_room_turns_the_eleventh_away()
    {
        var h = new BattleHarness();
        BattleClient old = h.AddClient("Old", version: 1);
        h.Run(0.5);
        Assert.Equal(BattlePhase.Disconnected, old.Phase);
        Assert.Contains("版本", old.DisconnectReason);

        var full = new BattleHarness();
        for (int i = 0; i < BattleProtocol.MaxPlayers; i++) full.AddClient($"P{i}");
        BattleClient extra = full.AddClient("Extra");
        full.Run(0.5);
        Assert.Equal(BattleProtocol.MaxPlayers, full.Clients.Count(c => c.Phase == BattlePhase.Room));
        Assert.Equal(BattlePhase.Disconnected, extra.Phase);
        Assert.Contains("满", extra.DisconnectReason);
    }

    [Fact]
    public void Own_shots_are_predicted_once_and_confirmed_by_the_server_and_others_shots_fly_on_the_client()
    {
        var h = new BattleHarness();
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        // Put B within sight of A: a free-for-all spawns them 100 m or more apart on open ground, but team spawns are
        // out of sight; so use team battle and move B's tank next to A on the server.
        h.StartMatch(BattleMode.TeamBattle);
        Simulation sim = h.Server.Simulation!;
        TankState tankA = h.ServerTankOf(a);
        int tankB = h.Server.TankOf(b.Slot);
        sim.Respawn(tankB, tankA.Position + new Vector2(8, 0), tankA.Yaw);   // put B next to A
        h.Run(1.0);   // B's client snaps to the server's position; both teams' vision catches up
        var fxA = new List<GameEvent>();
        int seenOwnShell = 0, seenRemoteShell = 0;
        bool firing = true;
        LocalCommand Aimed(int c, double t)
        {
            foreach (ShellView s in a.Shells)   // every frame: a shell aimed at the ground lives only a few ticks
            {
                if (s.Own) seenOwnShell++;
                else seenRemoteShell++;
            }
            while (a.TryDequeueEvent(out GameEvent e)) fxA.Add(e);
            // Aimed a little below level: a shell drops into the ground within a few ticks, so the burst is in range; B faces the other way.
            return new LocalCommand(0, 0, c == 0 ? tankA.Yaw : (float)(tankA.Yaw + 3.1), -0.02f, firing && (int)(t * 10) % 25 == 0);
        }
        h.Run(12.0, Aimed);
        firing = false;
        h.Run(3.0, Aimed);   // stop shooting and let the last shots' confirmations and the orphan window pass: only then is "nothing unconfirmed" a fact about the code, not about when the run ended
        Assert.True(h.Server.Vision!.IsVisible(tankB, 0), "A sees B");
        Assert.True(a.Own.ShotCount >= 2, $"{a.Own.ShotCount} shots");
        Assert.True(seenOwnShell > 0, "own shells were shown");
        Assert.DoesNotContain(fxA, e => e.Type == GameEventType.ShellFired && e.TankId == a.OwnTankId);   // confirmed, not replayed
        Assert.Contains(fxA, e => e.Type == GameEventType.ShellExploded && e.TankId == a.OwnTankId);
        Assert.DoesNotContain(a.Predictor.Shells, s => !s.Confirmed);   // every predicted shot was confirmed by the server (and the ended ones are gone)
        Assert.True(seenRemoteShell > 0, "B's shells flew on A's client");
        Assert.Contains(fxA, e => e.Type == GameEventType.ShellFired && e.TankId == tankB);
    }
}

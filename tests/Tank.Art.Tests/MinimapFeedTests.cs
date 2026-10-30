using System.Diagnostics;
using Tank.Client.Match;
using Tank.Client.Minimap;
using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Testing;
using Xunit.Abstractions;
using SN = System.Numerics;

namespace Tank.Art.Tests;

public class MinimapFeedTests(ITestOutputHelper output)
{
    private const double Frame = 1.0 / 60;

    private static RemoteTank Remote(int id, byte team, SN.Vector2 position, byte spec = 1, TankFlags flags = TankFlags.None) =>
        new(new BattleEntity { Id = id, Team = team, Spec = spec, Flags = flags, X = (int)(position.X * 100), Z = (int)(position.Y * 100) },
            position, Yaw: 0.3f, TurretYaw: 0f, GunPitch: 0f, Speed: 0f, Extrapolated: false);

    private static readonly RemoteTank AllyA = Remote(11, team: 0, new SN.Vector2(100.25f, 200.5f), spec: 0);
    private static readonly RemoteTank EnemyB = Remote(12, team: 1, new SN.Vector2(300.75f, 50.125f), spec: 2);

    [Fact]
    public void Only_the_snapshot_s_tanks_are_drawn_on_their_sides_where_they_are_interpolated()
    {
        var frame = new MinimapFrame();
        MinimapFeed.AddOthers(frame, new[] { AllyA, EnemyB }, BattleMode.TeamBattle, ownTeam: 0, eyeTankId: 0);

        Assert.Equal(2, frame.Tanks.Length);
        Assert.Equal(new MinimapTank(AllyA.Position, Side.Ally, MinimapShape.Light, Destroyed: false), frame.Tanks[0]);
        Assert.Equal(new MinimapTank(EnemyB.Position, Side.Enemy, MinimapShape.Heavy, Destroyed: false), frame.Tanks[1]);
    }

    [Fact]
    public void A_tank_that_left_the_snapshot_leaves_no_trace_on_the_next_frame()
    {
        var frame = new MinimapFrame();
        MinimapFeed.AddOthers(frame, new[] { AllyA, EnemyB }, BattleMode.TeamBattle, 0, 0);

        frame.Clear();
        MinimapFeed.AddOthers(frame, new[] { AllyA }, BattleMode.TeamBattle, 0, 0);

        MinimapTank only = Assert.Single(frame.Tanks.ToArray());
        Assert.Equal(AllyA.Position, only.Position);
        Assert.Equal(Side.Ally, only.Side);
    }

    [Fact]
    public void In_a_free_for_all_a_tank_with_the_own_team_number_is_still_an_enemy()
    {
        var frame = new MinimapFrame();
        MinimapFeed.AddOthers(frame, new[] { AllyA, EnemyB }, BattleMode.FreeForAll, ownTeam: 0, eyeTankId: 0);

        Assert.Equal(2, frame.Tanks.Length);
        Assert.All(frame.Tanks.ToArray(), t => Assert.Equal(Side.Enemy, t.Side));
    }

    [Fact]
    public void A_destroyed_enemy_is_an_enemy_wreck()
    {
        var frame = new MinimapFrame();
        RemoteTank wreck = Remote(13, team: 1, new SN.Vector2(10, 20), flags: TankFlags.Destroyed);
        MinimapFeed.AddOthers(frame, new[] { wreck }, BattleMode.TeamBattle, 0, 0);

        MinimapTank icon = Assert.Single(frame.Tanks.ToArray());
        Assert.True(icon.Destroyed);
        Assert.Equal(Side.Enemy, icon.Side);
    }

    [Fact]
    public void The_tank_the_eye_follows_is_left_to_the_arrow()
    {
        var frame = new MinimapFrame();
        MinimapFeed.AddOthers(frame, new[] { AllyA, EnemyB }, BattleMode.TeamBattle, 0, eyeTankId: AllyA.Entity.Id);

        MinimapTank only = Assert.Single(frame.Tanks.ToArray());
        Assert.Equal(EnemyB.Position, only.Position);
    }

    [Fact]
    public void Tanks_beyond_the_frame_s_room_are_ignored_and_asserted_in_debug()
    {
        var frame = new MinimapFrame();
        var many = Enumerable.Range(1, MinimapFrame.MaxTanks + 5).Select(i => Remote(i, 1, new SN.Vector2(i, i))).ToArray();

        int failures = AssertionFailures(() => MinimapFeed.AddOthers(frame, many, BattleMode.TeamBattle, 0, 0));

        Assert.Equal(MinimapFrame.MaxTanks, frame.Tanks.Length);
        Assert.Equal(new SN.Vector2(MinimapFrame.MaxTanks, MinimapFrame.MaxTanks), frame.Tanks[^1].Position);
#if DEBUG
        Assert.Equal(5, failures);   // one per tank dropped
#else
        Assert.Equal(0, failures);
#endif
    }

    /// <summary>
    /// How many <see cref="Debug.Assert(bool, string)"/>s failed while <paramref name="action"/> ran, counted by a listener
    /// put in place of the usual ones for the while (the default listener would end the test run).
    /// </summary>
    private static int AssertionFailures(Action action)
    {
        var recorder = new FailureRecorder();
        TraceListener[] usual = Trace.Listeners.Cast<TraceListener>().ToArray();
        Trace.Listeners.Clear();
        Trace.Listeners.Add(recorder);
        try
        {
            action();
        }
        finally
        {
            Trace.Listeners.Clear();
            Trace.Listeners.AddRange(usual);
        }
        return recorder.Failures;
    }

    private sealed class FailureRecorder : TraceListener
    {
        public int Failures { get; private set; }
        public override void Fail(string? message, string? detailMessage) => Failures++;
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }
    }

    [Fact]
    public void Clear_empties_tanks_bases_the_eye_and_the_own_wreck()
    {
        var frame = new MinimapFrame
        {
            Eye = new MinimapEye(EyeKind.Arrow, new SN.Vector2(1, 2), 0.5f, 0.6f, 0.9f),
            OwnWreck = new SN.Vector2(3, 4),
        };
        frame.AddTank(new MinimapTank(SN.Vector2.Zero, Side.Ally, MinimapShape.Td, false));
        frame.AddBase(new MinimapBase(SN.Vector2.Zero, 25, true, 10));

        frame.Clear();

        Assert.Equal(0, frame.Tanks.Length);
        Assert.Equal(0, frame.Bases.Length);
        Assert.Equal(EyeKind.None, frame.Eye.Kind);
        Assert.Null(frame.OwnWreck);
    }

    [Fact]
    public void A_frame_holds_two_bases_at_most()
    {
        var frame = new MinimapFrame();
        for (int i = 0; i < 4; i++) frame.AddBase(new MinimapBase(new SN.Vector2(i, 0), 25, i == 0, (byte)i));
        Assert.Equal(2, frame.Bases.Length);
        Assert.Equal(1, frame.Bases[1].Centre.X);
    }

    [Fact]
    public void The_bases_carry_their_owner_and_how_far_each_has_been_taken()
    {
        var frame = new MinimapFrame();
        TeamBase[] bases = { new(0, new SN.Vector2(100, 900), 25), new(1, new SN.Vector2(900, 100), 25) };
        var match = new MatchBlock { BaseProgress0 = 37, BaseProgress1 = 0 };

        MinimapFeed.AddBases(frame, bases, ownTeam: 1, match);

        Assert.Equal(2, frame.Bases.Length);
        Assert.Equal(new MinimapBase(new SN.Vector2(100, 900), 25, Own: false, Points: 37), frame.Bases[0]);
        Assert.Equal(new MinimapBase(new SN.Vector2(900, 100), 25, Own: true, Points: 0), frame.Bases[1]);
    }

    [Fact]
    public void No_bases_add_nothing()
    {
        var frame = new MinimapFrame();
        MinimapFeed.AddBases(frame, null, 0, default);
        MinimapFeed.AddBases(frame, Array.Empty<TeamBase>(), 0, default);
        Assert.Equal(0, frame.Bases.Length);
    }

    [Fact]
    public void Shapes_follow_the_catalog_s_order_and_an_unknown_index_is_medium()
    {
        Assert.Equal(MinimapShape.Light, MinimapFeed.ShapeOf(0));
        Assert.Equal(MinimapShape.Medium, MinimapFeed.ShapeOf(1));
        Assert.Equal(MinimapShape.Heavy, MinimapFeed.ShapeOf(2));
        Assert.Equal(MinimapShape.Td, MinimapFeed.ShapeOf(3));
        Assert.Equal(MinimapShape.Medium, MinimapFeed.ShapeOf(4));
        Assert.Equal(MinimapShape.Medium, MinimapFeed.ShapeOf(-1));
        Assert.Equal(MinimapShape.Medium, MinimapFeed.ShapeOf(255));
        Assert.Equal(MinimapShape.Td, MinimapFeed.ShapeOf(TankCatalog.Td));
        for (int i = 0; i < TankCatalog.All.Count; i++) Assert.Equal((MinimapShape)i, MinimapFeed.ShapeOf(TankCatalog.All[i]));
    }

    private static readonly ZoneRing[] Rings =
    {
        new(new SN.Vector2(600, 600), 806.1f), new(new SN.Vector2(560, 640), 420), new(new SN.Vector2(520, 610), 250),
        new(new SN.Vector2(500, 600), 140), new(new SN.Vector2(490, 590), 60), new(new SN.Vector2(485, 585), 0),
    };

    [Fact]
    public void While_a_ring_waits_the_map_shows_the_safe_circle_and_the_ring_announced()
    {
        var frame = new MinimapFrame();
        ZoneState state = ZoneState.At(Rings, ZoneSchedule.WaitStart(2) + 10);

        MinimapFeed.AddZone(frame, state);

        Assert.Equal(new MinimapZone(Rings[1].Centre, 420, true, Rings[2].Centre, 250), frame.Zone);
    }

    [Fact]
    public void While_a_ring_shrinks_the_map_shows_the_circle_between_and_the_ring_shrunk_to()
    {
        var frame = new MinimapFrame();
        ZoneState state = ZoneState.At(Rings, ZoneSchedule.ShrinkStart(2) + ZoneSchedule.Stage(2).ShrinkTicks / 2f);

        MinimapFeed.AddZone(frame, state);

        MinimapZone zone = Assert.NotNull(frame.Zone);
        Assert.True(zone.HasNext);
        Assert.Equal(Rings[2].Centre, zone.NextCentre);
        Assert.Equal(250, zone.NextRadius);
        Assert.Equal(335, zone.Radius, 0.01f);   // halfway from 420 to 250
        Assert.Equal(state.Current.Centre, zone.Centre);
    }

    [Fact]
    public void Once_closed_the_map_shows_the_point_and_no_next_ring()
    {
        var frame = new MinimapFrame();

        MinimapFeed.AddZone(frame, ZoneState.At(Rings, ZoneSchedule.ClosedAt + 1));

        MinimapZone zone = Assert.NotNull(frame.Zone);
        Assert.False(zone.HasNext);
        Assert.Equal(0, zone.Radius);
        Assert.Equal(Rings[5].Centre, zone.Centre);
    }

    [Fact]
    public void No_zone_leaves_the_map_without_one_and_clear_takes_it_off()
    {
        var frame = new MinimapFrame();
        MinimapFeed.AddZone(frame, default);
        Assert.Null(frame.Zone);

        MinimapFeed.AddZone(frame, ZoneState.At(Rings, 0));
        Assert.NotNull(frame.Zone);
        frame.Clear();
        Assert.Null(frame.Zone);
    }

    [Fact]
    public void Adding_the_zone_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var frame = new MinimapFrame();
            ZoneState waiting = ZoneState.At(Rings, 10), shrinking = ZoneState.At(Rings, ZoneSchedule.ShrinkStart(1) + 5), closed = ZoneState.At(Rings, ZoneSchedule.ClosedAt);
            for (int round = 0; round < 3; round++)   // warm-up
            {
                frame.Clear();
                MinimapFeed.AddZone(frame, waiting);
                MinimapFeed.AddZone(frame, shrinking);
                MinimapFeed.AddZone(frame, closed);
            }

            var window = Allocation.Start();
            for (int round = 0; round < 50; round++)
            {
                frame.Clear();
                MinimapFeed.AddZone(frame, waiting);
                MinimapFeed.AddZone(frame, shrinking);
                MinimapFeed.AddZone(frame, closed);
            }
            var measured = window.Stop();
            Assert.NotNull(frame.Zone);
            return measured;
        });
    }

    [Fact]
    public void Filling_a_frame_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var frame = new MinimapFrame();
            var others = new List<RemoteTank>(Enumerable.Range(1, 10).Select(i => Remote(i, (byte)(i % 2), new SN.Vector2(i * 10, i * 5), (byte)(i % 4))));
            IReadOnlyList<RemoteTank> view = others;
            IReadOnlyList<TeamBase> bases = new TeamBase[] { new(0, new SN.Vector2(100, 900), 25), new(1, new SN.Vector2(900, 100), 25) };
            var match = new MatchBlock { BaseProgress0 = 12, BaseProgress1 = 40 };
            for (int round = 0; round < 2; round++)   // warm-up
            {
                frame.Clear();
                MinimapFeed.AddOthers(frame, view, BattleMode.Standard, 0, 3);
                MinimapFeed.AddBases(frame, bases, 0, match);
            }

            var window = Allocation.Start();
            frame.Clear();
            MinimapFeed.AddOthers(frame, view, BattleMode.Standard, 0, 3);
            MinimapFeed.AddBases(frame, bases, 0, match);
            frame.Eye = new MinimapEye(EyeKind.Ring, new SN.Vector2(5, 5), 0f, 1f, 0.8f);
            frame.OwnWreck = new SN.Vector2(5, 5);
            var measured = window.Stop();
            Assert.Equal(9, frame.Tanks.Length);
            Assert.Equal(2, frame.Bases.Length);
            return measured;
        });
    }

    /// <summary>
    /// The map shows what the snapshot shows and nothing more: in a real loopback match (team battle, the sandbox map, 3
    /// bots, seed 7, 20 virtual seconds) every frame's enemy icons are exactly the enemy entities in <c>client.Others</c>,
    /// and on some frames the scoreboard counts more living enemies than there are live enemy icons — enemies the server
    /// keeps from this player's team, which the map therefore cannot draw.
    /// </summary>
    [Fact]
    public void A_loopback_match_draws_only_the_enemies_the_server_lets_the_player_see()
    {
        var settings = new HostSettings("Tester", "Tester's room", Port: 47105, BattleMode.TeamBattle, Bots: 3, Seed: 7, MatchSeconds: null);
        using BattleSession session = BattleSession.HostLoopback(settings, _ => { });
        var auto = new AutoRoom(null, BattleMode.TeamBattle, 3, autoStart: true);
        BattleClient client = session.Client;
        var frame = new MinimapFrame();

        int framesWithHiddenEnemies = 0, framesInMatch = 0, mostHidden = 0, framesWithEnemyIcons = 0;
        for (int i = 0; i < 20 * 60; i++)
        {
            session.Advance(Frame);
            auto.Next(client.Room, client.Slot, client.IsHost, 1).SendTo(client);
            client.Update(Frame, LocalCommand.None);
            if (client.Phase != BattlePhase.InMatch || client.Latest is not { } latest || client.MatchInfo is not { } info) continue;
            framesInMatch++;

            byte ownTeam = latest.Players[client.Slot].Team;
            frame.Clear();
            MinimapFeed.AddOthers(frame, client.Others, info.Mode, ownTeam, eyeTankId: 0);

            int enemyEntities = 0;
            foreach (RemoteTank other in client.Others)
                if (MatchColors.SideOf(info.Mode, ownTeam, other.Entity.Team, isOwn: false) == Side.Enemy) enemyEntities++;

            int enemyIcons = 0, liveEnemyIcons = 0;
            foreach (MinimapTank icon in frame.Tanks)
            {
                if (icon.Side != Side.Enemy) continue;
                enemyIcons++;
                if (!icon.Destroyed) liveEnemyIcons++;
                Assert.Contains(client.Others, o => o.Position == icon.Position && o.Entity.Team != ownTeam);
            }
            Assert.Equal(enemyEntities, enemyIcons);

            int liveEnemiesOnTheBoard = 0;
            for (int slot = 0; slot < latest.Players.Length; slot++)
            {
                PlayerStats p = latest.Players[slot];
                if (slot != client.Slot && p.Active && p.Alive && p.Team != ownTeam) liveEnemiesOnTheBoard++;
            }
            if (enemyIcons > 0) framesWithEnemyIcons++;
            int hidden = liveEnemiesOnTheBoard - liveEnemyIcons;
            if (hidden > 0) framesWithHiddenEnemies++;
            mostHidden = Math.Max(mostHidden, hidden);
        }

        output.WriteLine($"{framesInMatch} frames in the match: {framesWithEnemyIcons} with enemy icons, {framesWithHiddenEnemies} with living enemies not drawn (at most {mostHidden})");
        Assert.True(framesInMatch > 15 * 60, $"only {framesInMatch} frames in the match");
        // The loopback match runs on a virtual clock, so this count is the same on every run: it shows enemies really were
        // hidden on some frames (otherwise the equality above proves nothing). If retuned bots ever make it 0, change the
        // seed or the map (as the plan's task 3 says) until some enemy spends frames out of sight again.
        Assert.True(framesWithHiddenEnemies > 0, "every living enemy was in sight on every frame: the test proves nothing");
    }
}

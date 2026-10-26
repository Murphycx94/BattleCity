using System.Diagnostics;
using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using static Tank.Net.Tests.Battle.BattleTestPackets;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// The client decodes bytes it does not trust: nothing but a counted, dropped packet may come of a bad one, nothing may
/// hang or throw out of <see cref="BattleClient.OnData"/> / <see cref="BattleClient.Update"/>, and a half-applied packet
/// must not leave the match in a state no server ever sent. Time is frozen after the match has started (the harness
/// only delivers packets while it runs), so the client sees exactly what the test feeds it.
/// </summary>
public sealed class BattleClientRobustnessTests
{
    private const double Frame = 1.0 / 60;

    private static BattleClient InMatch(out BattleHarness h)
    {
        h = new BattleHarness();
        BattleClient a = h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
        Assert.NotNull(a.Latest);
        return a;
    }

    private static void Frames(BattleClient client, BattleHarness h, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            h.Net.AdvanceTime(Frame);
            client.Update(Frame, LocalCommand.None);
        }
    }

    private static void AssertRejected(BattleClient client, int droppedBefore, string? reasonPart = null)
    {
        client.Update(0, LocalCommand.None);   // refreshes the counters
        Assert.Equal(droppedBefore + 1, client.Stats.DroppedPackets);
        if (reasonPart is not null) Assert.Contains(reasonPart, client.LastPacketError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BattlePhase.InMatch, client.Phase);
    }

    private static int Dropped(BattleClient c)
    {
        c.Update(0, LocalCommand.None);
        return c.Stats.DroppedPackets;
    }

    // ---- hostile snapshots ----

    [Fact]
    public void A_snapshot_acking_inputs_millions_of_ticks_back_is_handled_at_once_and_the_client_lives_on()
    {
        BattleClient a = InMatch(out BattleHarness h);
        a.OnData(0, Snapshot(a.Latest!, a.Latest!.Tick + 1, a.Latest.Tick));   // warm the code paths up: the timing below is not JIT
        foreach (int back in new[] { 10_000_000, int.MaxValue })
        {
            int ack = back == int.MaxValue ? int.MinValue : a.Predictor.LastPredictedTick - back;
            var sw = Stopwatch.StartNew();
            a.OnData(0, Snapshot(a.Latest!, a.Latest!.Tick + 1, ack));
            Assert.True(sw.ElapsedMilliseconds < 50, $"{sw.ElapsedMilliseconds} ms for an ack {back} ticks behind");
        }
        Assert.Equal(BattlePhase.InMatch, a.Phase);
        Assert.True(a.HasTank);
        Frames(a, h, 30);
        h.Run(0.5);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
    }

    [Fact]
    public void A_snapshot_acking_inputs_beyond_its_own_tick_is_refused()
    {
        BattleClient a = InMatch(out _);
        int before = Dropped(a);
        int newest = a.Latest!.Tick;
        a.OnData(0, Snapshot(a.Latest, newest + 1, newest + 2));
        AssertRejected(a, before, "beyond");
        Assert.Equal(newest, a.Store.NewestTick);
    }

    [Fact]
    public void Coordinates_that_would_overflow_the_distance_test_are_refused_and_the_next_update_does_not_throw()
    {
        BattleClient a = InMatch(out BattleHarness h);
        int tick = a.Latest!.Tick + 10;
        int before = Dropped(a);
        a.OnData(0, Snapshot(a.Latest, tick, a.Latest.Tick, s => SetEntity(s, Entity(77, int.MinValue, 0))));
        AssertRejected(a, before, "range");
        a.OnData(0, Snapshot(a.Latest!, tick + 1, a.Latest!.Tick, s => SetEntity(s, Entity(77, 0, 0))));
        Assert.Null(Record.Exception(() => Frames(a, h, 120)));
        Assert.DoesNotContain(a.Others, o => o.Entity.Id == 77 && o.Position.X < -1000);
    }

    [Fact]
    public void A_snapshot_from_the_far_future_is_refused_so_the_real_ones_are_not_all_too_old_afterwards()
    {
        BattleClient a = InMatch(out BattleHarness h);
        int newest = a.Latest!.Tick;
        int before = Dropped(a);
        a.OnData(0, Snapshot(a.Latest, BattleProtocol.MaxTick, newest));   // the largest tick the wire allows
        AssertRejected(a, before, "future");
        Assert.Equal(newest, a.Store.NewestTick);

        a.OnData(0, Snapshot(a.Latest, int.MaxValue - 1, newest));   // beyond the wire's bound: refused by the codec before any clock arithmetic
        AssertRejected(a, before + 1, "range");
        Assert.Equal(newest, a.Store.NewestTick);

        a.OnData(0, Snapshot(a.Latest, newest + BattleProtocol.MaxTickLead + 1, newest));   // just past the limit
        Assert.Equal(before + 3, Dropped(a));
        Assert.Equal(newest, a.Store.NewestTick);

        h.Run(0.5);   // the real ones still count
        Assert.True(a.Store.NewestTick > newest);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
    }

    [Fact]
    public void A_snapshot_within_the_limit_is_taken()
    {
        BattleClient a = InMatch(out _);
        int newest = a.Latest!.Tick;
        a.OnData(0, Snapshot(a.Latest, newest + BattleProtocol.MaxTickLead, newest));
        Assert.Equal(newest + BattleProtocol.MaxTickLead, a.Store.NewestTick);
    }

    [Fact]
    public void A_duplicate_of_the_newest_snapshot_is_neither_counted_nor_reconciled_again()
    {
        BattleClient a = InMatch(out _);
        a.Update(1.0, LocalCommand.None);
        a.Update(1.0, LocalCommand.None);   // flush the stats window
        byte[] packet = Snapshot(a.Latest!, a.Latest!.Tick + 1, a.Latest.Tick);
        a.OnData(0, packet);
        a.OnData(0, packet);
        a.Update(1.0, LocalCommand.None);
        Assert.Equal(1, a.Stats.SnapshotsPerSecond);
    }

    [Fact]
    public void Truncated_snapshots_events_and_match_starts_are_dropped_and_counted_and_the_phase_is_kept()
    {
        BattleClient a = InMatch(out BattleHarness h);
        byte[] snapshot = Snapshot(a.Latest!, a.Latest!.Tick + 1, a.Latest.Tick);
        byte[] events = Events(new EventBatch(a.Latest.Tick, new[] { new GameEvent(GameEventType.ShellExploded, a.Latest.Tick, 3, 4, Vector3.One, Vector3.Zero) }, new[] { new PropState(a.Field!.Props[0].Id, 0) }));
        byte[] start = MatchStart(a.MatchInfo!);
        foreach (byte[] packet in new[] { snapshot, events, start })
        {
            // Every cut: nothing escapes, and a cut that loses real content is counted (a cut that only loses padding may decode).
            int counted = 0, cuts = 0;
            for (int cut = 1; cut < packet.Length; cut += Math.Max(1, packet.Length / 97))
            {
                cuts++;
                int before = Dropped(a);
                a.OnData(0, packet.AsSpan(0, cut));
                if (Dropped(a) > before) counted++;
                Assert.Equal(BattlePhase.InMatch, a.Phase);
            }
            Assert.True(counted >= cuts - 3, $"only {counted} of {cuts} cuts were counted");
        }
        Assert.Equal(BattlePhase.InMatch, a.Phase);
        Assert.Null(Record.Exception(() => Frames(a, h, 60)));
    }

    [Fact]
    public void A_snapshot_against_a_baseline_the_client_never_had_is_dropped_and_asks_for_the_match_state_once_a_second()
    {
        BattleClient a = InMatch(out BattleHarness h);
        var other = new BattleSnapshot();
        other.CopyFrom(a.Latest!);
        other.Tick = 4_000;
        var w = new Tank.Net.Protocol.BitWriter(4096);
        var s = new BattleSnapshot();
        s.CopyFrom(a.Latest!);
        s.Tick = a.Latest!.Tick + 1;
        new BattleSnapshotCodec().Write(w, s, other, new ClientFeedback { LastProcessedInputTick = a.Latest.Tick });
        int dropped = Dropped(a), resyncs = a.Resyncs;
        a.OnData(0, w.Data.ToArray());
        AssertRejected(a, dropped, "baseline");
        Assert.Equal(resyncs + 1, a.Resyncs);
        a.OnData(0, w.Data.ToArray());   // again at once: rate limited
        Assert.Equal(resyncs + 1, a.Resyncs);
        h.Run(1.2);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
    }

    // ---- match starts without the map ----

    [Fact]
    public void A_resync_comes_without_the_map_and_the_client_keeps_its_own()
    {
        BattleClient a = InMatch(out BattleHarness h);
        Battlefield field = a.Field!;
        MapData map = a.MatchInfo!.Map!;
        int dropped = Dropped(a), resyncs = a.Resyncs;
        a.OnData(0, SnapshotAgainstAMissingBaseline(a));   // the client asks for the match state again
        Assert.Equal(resyncs + 1, a.Resyncs);

        h.Run(0.5);

        Assert.Equal(dropped + 1, Dropped(a));             // the bad snapshot only: the answer was taken
        Assert.Equal(BattlePhase.InMatch, a.Phase);
        Assert.NotSame(field, a.Field);                    // the resync's field: a fresh copy of the one it had…
        Assert.Same(field.Terrain, a.Field!.Terrain);      // …on the same ground, not one built from a map sent again
        Assert.Same(map, a.MatchInfo!.Map);
        Assert.True(a.HasTank);
        h.Run(0.5);
        Assert.Equal(dropped + 1, Dropped(a));
    }

    [Fact]
    public void A_match_start_without_the_map_resyncs_the_match_on_the_field_it_has()
    {
        BattleClient a = InMatch(out _);
        Battlefield field = a.Field!;
        MatchStartMessage info = a.MatchInfo!;
        Prop wall = field.Props.First(p => p.IsDestructible);
        int newest = a.Store.NewestTick;

        a.OnData(0, MatchStart(info with { Map = null, CurrentTick = newest, Props = new[] { new PropState(wall.Id, 0) } }));

        Assert.Null(a.LastPacketError);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
        Assert.Same(field.Terrain, a.Field!.Terrain);
        Assert.Equal(field.Props, a.Field.Props);
        Assert.False(a.Field.IsStanding(wall.Id));
        Assert.Same(info.Map, a.MatchInfo!.Map);           // the match info keeps the map it came with
        Assert.Equal(newest, a.Store.NewestTick);          // a resync: the store stays
    }

    [Fact]
    public void A_client_without_the_map_drops_a_match_start_without_it_and_asks_for_the_map_until_it_comes()
    {
        BattleClient a = InMatch(out BattleHarness h);
        MatchStartMessage info = a.MatchInfo!;
        a.OnData(0, RoomStatePacket(a.Room! with { MatchRunning = false }));   // somehow the client has left the match the server still runs
        Assert.Equal(BattlePhase.Room, a.Phase);
        Assert.Null(a.Field);
        int dropped = Dropped(a), resyncs = a.Resyncs;

        a.OnData(0, MatchStart(info with { Map = null }));   // what the server answers a connection it has sent the map

        Assert.Equal(dropped + 1, Dropped(a));
        Assert.Contains("without the map", a.LastPacketError);
        Assert.Equal(BattlePhase.Room, a.Phase);
        Assert.Null(a.Field);
        Assert.Equal(resyncs + 1, a.Resyncs);              // asked at once, saying it needs the map

        h.Run(1.0);

        Assert.Equal(BattlePhase.InMatch, a.Phase);
        Assert.NotNull(a.Field);
        Assert.NotNull(a.MatchInfo!.Map);
        Assert.Equal(info.MatchNumber, a.MatchInfo.MatchNumber);
        Assert.True(a.HasTank);
        int asked = a.Resyncs;
        h.Run(2.5);
        Assert.Equal(asked, a.Resyncs);                    // the map came: no more asking
    }

    [Fact]
    public void A_client_that_lacks_the_map_keeps_asking_when_it_may_not_ask_at_once()
    {
        BattleClient a = InMatch(out BattleHarness h);
        MatchStartMessage info = a.MatchInfo!;
        a.OnData(0, SnapshotAgainstAMissingBaseline(a));   // a resync just now: the next request must wait a second
        h.Run(0.1);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
        int resyncs = a.Resyncs;
        a.OnData(0, RoomStatePacket(a.Room! with { MatchRunning = false }));
        a.OnData(0, MatchStart(info with { Map = null }));
        Assert.Equal(resyncs, a.Resyncs);                  // rate-limited: not asked yet
        Assert.Equal(BattlePhase.Room, a.Phase);

        h.Run(2.5);                                        // the client asks once it may (and again if the server's own limit refused it)

        Assert.True(a.Resyncs > resyncs);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
        Assert.NotNull(a.Field);
    }

    private static byte[] RoomStatePacket(RoomStateMessage room)
    {
        var w = new Tank.Net.Protocol.BitWriter();
        BattleMessages.Write(w, room);
        return w.Data.ToArray();
    }

    private static byte[] SnapshotAgainstAMissingBaseline(BattleClient a)
    {
        var other = new BattleSnapshot();
        other.CopyFrom(a.Latest!);
        other.Tick = 4_000;
        var s = new BattleSnapshot();
        s.CopyFrom(a.Latest!);
        s.Tick = a.Latest!.Tick + 1;
        var w = new Tank.Net.Protocol.BitWriter(4096);
        new BattleSnapshotCodec().Write(w, s, other, new ClientFeedback { LastProcessedInputTick = a.Latest.Tick });
        return w.Data.ToArray();
    }

    [Fact]
    public void Random_bytes_with_any_first_byte_never_throw_out_of_the_client()
    {
        BattleClient a = InMatch(out BattleHarness h);
        var random = new Random(1234);
        var packet = new byte[300];
        for (int round = 0; round < 3000; round++)
        {
            random.NextBytes(packet);
            packet[0] = (byte)(round % 256);
            int length = random.Next(1, packet.Length);
            Assert.Null(Record.Exception(() => a.OnData(0, packet.AsSpan(0, length))));
            if (round % 100 == 0) Assert.Null(Record.Exception(() => Frames(a, h, 3)));
        }
        Assert.Null(Record.Exception(() => a.OnData(0, ReadOnlySpan<byte>.Empty)));
    }

    [Fact]
    public void Flipped_bits_in_real_snapshots_are_dropped_or_taken_but_never_throw()
    {
        BattleClient a = InMatch(out BattleHarness h);
        var random = new Random(99);
        for (int round = 0; round < 500; round++)
        {
            byte[] packet = Snapshot(a.Latest!, a.Latest!.Tick + 1, a.Latest.Tick);
            for (int flips = random.Next(1, 4); flips > 0; flips--) packet[random.Next(1, packet.Length)] ^= (byte)(1 << random.Next(8));
            Assert.Null(Record.Exception(() => a.OnData(0, packet)));
            Assert.Null(Record.Exception(() => Frames(a, h, 2)));
        }
    }

    // ---- hostile props and events ----

    [Fact]
    public void A_match_start_with_an_unknown_prop_is_refused_whole_and_the_match_goes_on_as_it_was()
    {
        BattleClient a = InMatch(out _);
        Battlefield field = a.Field!;
        MatchStartMessage info = a.MatchInfo!;
        Prop wall = field.Props.First(p => p.IsDestructible);
        int durability = field.Durability(wall.Id);
        int before = Dropped(a);

        var bad = info with { MatchNumber = info.MatchNumber + 1, MyTankId = info.MyTankId + 5, Props = new[] { new PropState(wall.Id, 0), new PropState(999_999, 3) } };
        a.OnData(0, MatchStart(bad));

        AssertRejected(a, before, "prop");
        Assert.Same(field, a.Field);                         // not replaced by the half-built one
        Assert.Same(info, a.MatchInfo);
        Assert.Equal(info.MyTankId, a.OwnTankId);
        Assert.Equal(durability, field.Durability(wall.Id));
        Assert.True(a.HasTank);
        Assert.NotEmpty(a.Latest!.EntitySpan.ToArray());     // the store was not cleared as for a new match
    }

    [Fact]
    public void An_event_batch_with_an_unknown_prop_is_refused_whole_and_applies_nothing_and_queues_nothing()
    {
        BattleClient a = InMatch(out BattleHarness h);
        Prop wall = a.Field!.Props.First(p => p.IsDestructible);
        int durability = a.Field.Durability(wall.Id);
        int before = Dropped(a);
        var explosion = new GameEvent(GameEventType.ShellExploded, a.Latest!.Tick, 3, 4242, Vector3.One, Vector3.Zero);

        a.OnData(0, Events(new EventBatch(a.Latest.Tick, new[] { explosion }, new[] { new PropState(wall.Id, 0), new PropState(999_999, 3) })));

        AssertRejected(a, before, "prop");
        Assert.Equal(durability, a.Field.Durability(wall.Id));   // the valid one before the bad one was not applied either
        Frames(a, h, 30);
        while (a.TryDequeueEvent(out GameEvent e)) Assert.NotEqual(4242, e.ShellId);
    }

    [Fact]
    public void An_event_batch_from_the_far_future_is_refused_and_does_not_block_the_ones_behind_it()
    {
        BattleClient a = InMatch(out BattleHarness h);
        int newest = a.Latest!.Tick;
        int before = Dropped(a);
        a.OnData(0, Events(new EventBatch(newest + BattleProtocol.MaxTickLead + 1, new[] { new GameEvent(GameEventType.ShellExploded, 0, 3, 1111, Vector3.One, Vector3.Zero) }, Array.Empty<PropState>())));
        AssertRejected(a, before, "future");

        a.OnData(0, Events(new EventBatch(newest - 2, new[] { new GameEvent(GameEventType.ShellExploded, newest - 2, 3, 2222, Vector3.One, Vector3.Zero) }, Array.Empty<PropState>())));
        Frames(a, h, 30);
        var seen = new List<int>();
        while (a.TryDequeueEvent(out GameEvent e)) seen.Add(e.ShellId);
        Assert.Contains(2222, seen);
        Assert.DoesNotContain(1111, seen);
    }

    [Fact]
    public void Event_batches_waiting_for_the_render_clock_are_bounded_and_the_oldest_are_dropped_and_counted()
    {
        BattleClient a = InMatch(out _);
        int future = a.Latest!.Tick + BattleProtocol.MaxTickLead - 1;   // far enough ahead that none is due
        int before = Dropped(a);
        for (int i = 0; i < 300; i++)
            a.OnData(0, Events(new EventBatch(future, new[] { new GameEvent(GameEventType.ShellExploded, future, 3, i, Vector3.One, Vector3.Zero) }, Array.Empty<PropState>())));
        Assert.Equal(before + 300 - 256, Dropped(a));
    }

    [Fact]
    public void A_shot_of_the_own_tank_the_client_never_predicted_flies_as_somebody_elses_shell()
    {
        BattleClient a = InMatch(out BattleHarness h);   // the client holds still and never fires
        int tick = a.Latest!.Tick - 10;
        Vector2 at = a.Own.Position;
        var shot = new GameEvent(GameEventType.ShellFired, tick, a.OwnTankId, 777, new Vector3(at.X, 60f, at.Y), new Vector3(0, 0, -1), Ammo: AmmoType.AP);
        a.OnData(0, Events(new EventBatch(tick, new[] { shot }, Array.Empty<PropState>())));
        Frames(a, h, 2);
        ShellView s = Assert.Single(a.Shells, v => v.ShellId == 777);
        Assert.False(s.Own);
        var fx = new List<GameEvent>();
        while (a.TryDequeueEvent(out GameEvent e)) fx.Add(e);
        Assert.Contains(fx, e => e.Type == GameEventType.ShellFired && e.ShellId == 777);   // and it is played like any shot
    }

    [Fact]
    public void Reading_the_shells_does_not_change_them_and_costs_nothing()
    {
        BattleClient a = InMatch(out BattleHarness h);
        int tick = a.Latest!.Tick - 10;
        var shot = new GameEvent(GameEventType.ShellFired, tick, 9999, 31, new Vector3(50, 60f, 50), new Vector3(0, 0, -1), Ammo: AmmoType.AP);
        a.OnData(0, Events(new EventBatch(tick, new[] { shot }, Array.Empty<PropState>())));
        Frames(a, h, 2);
        IReadOnlyList<ShellView> first = a.Shells;
        Assert.Single(first);
        Assert.Same(first, a.Shells);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = a.Shells.Count;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Single(a.Shells);
    }

    [Fact]
    public void A_fresh_client_reports_no_winner_rather_than_team_and_slot_zero()
    {
        var h = new BattleHarness();
        BattleClient a = h.AddClient("A");
        Assert.Equal(MatchRules.NoTeam, a.Match.WinnerTeam);
        Assert.Equal(MatchRules.NoSlot, a.Match.WinnerSlot);
    }
}

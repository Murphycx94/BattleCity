using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Testing;
using static Tank.Net.Tests.Battle.BattleTestPackets;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// The client's zone (stage 6 design §3.5): the rings a match start carries replace what it had, each announcement
/// appends the next ring in order and nothing else, leaving the match forgets them; <see cref="BattleClient.ZoneAt"/>
/// is the shared <see cref="ZoneState.At"/> on the server's clock. Packets are fed straight in, as a server would send them.
/// </summary>
public sealed class ZoneClientTests
{
    private const int StartTick = 120;

    private static readonly ZoneRing R0 = new(new Vector2(600f, 600f), 806.1f);
    private static readonly ZoneRing R1 = new(new Vector2(560f, 640f), 420f);
    private static readonly ZoneRing R2 = new(new Vector2(520f, 610f), 250f);
    private static readonly ZoneRing R3 = new(new Vector2(500f, 600f), 140f);
    private static readonly ZoneRing R4 = new(new Vector2(495f, 605f), 60f);

    private static BattleClient InMatch(out BattleHarness h)
    {
        h = new BattleHarness();
        BattleClient a = h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
        return a;
    }

    /// <summary>A new match (the next number) with the map and the zone given: what a royale's start looks like to the client.</summary>
    private static MatchStartMessage NewRoyale(BattleClient a, params ZoneRing[] rings) =>
        a.MatchInfo! with { Mode = BattleMode.Royale, MatchNumber = a.MatchInfo.MatchNumber + 1, Zone = new ZoneSync(StartTick, rings) };

    private static byte[] Announce(int matchNumber, byte ring, ZoneRing circle)
    {
        var w = new Tank.Net.Protocol.BitWriter();
        BattleMessages.Write(w, new ZoneMessage(matchNumber, ring, circle));
        return w.Data.ToArray();
    }

    [Fact]
    public void A_team_battle_has_no_zone()
    {
        BattleClient a = InMatch(out _);

        Assert.False(a.HasZone);
        Assert.Equal(0, a.ZoneRings.Length);
        Assert.Equal(ZonePhase.None, a.ZoneAt(5000).Phase);
        Assert.Equal(default, a.ZoneAt(5000));
    }

    [Fact]
    public void The_rings_come_with_the_match_start_and_one_by_one_after_it()
    {
        BattleClient a = InMatch(out _);
        MatchStartMessage royale = NewRoyale(a, R0, R1);
        a.OnData(0, MatchStart(royale));

        Assert.Null(a.LastPacketError);
        Assert.True(a.HasZone);
        Assert.Equal(StartTick, a.ZoneStartTick);
        Assert.Equal(new[] { R0, R1 }, a.ZoneRings.ToArray());
        Assert.Equal(ZonePhase.Waiting, a.ZoneAt(StartTick + 10).Phase);
        Assert.Equal(ZonePhase.Shrinking, a.ZoneAt(StartTick + ZoneSchedule.ShrinkStart(1)).Phase);
        Assert.Equal(ZoneState.At(new[] { R0, R1 }, 3000.5f), a.ZoneAt(StartTick + 3000.5));

        int n = royale.MatchNumber;
        a.OnData(0, Announce(n, 2, R2));
        Assert.Equal(new[] { R0, R1, R2 }, a.ZoneRings.ToArray());

        a.OnData(0, Announce(n, 2, R3));       // again: already known, kept as it was
        Assert.Equal(new[] { R0, R1, R2 }, a.ZoneRings.ToArray());

        a.OnData(0, Announce(n, 4, R4));       // ring 3 never came: not appended out of order
        Assert.Equal(3, a.ZoneRings.Length);

        a.OnData(0, Announce(n + 1, 3, R3));   // another match's
        a.OnData(0, Announce(n - 1, 3, R3));
        Assert.Equal(3, a.ZoneRings.Length);
        Assert.Null(a.LastPacketError);

        a.OnData(0, Announce(n, 3, R3));
        Assert.Equal(new[] { R0, R1, R2, R3 }, a.ZoneRings.ToArray());
    }

    [Fact]
    public void A_resync_of_the_same_match_replaces_the_rings()
    {
        BattleClient a = InMatch(out _);
        MatchStartMessage royale = NewRoyale(a, R0, R1);
        a.OnData(0, MatchStart(royale));

        a.OnData(0, MatchStart(royale with { Map = null, Zone = new ZoneSync(StartTick, new[] { R0, R1, R2, R3 }) }));

        Assert.Null(a.LastPacketError);
        Assert.Equal(royale.MatchNumber, a.MatchInfo!.MatchNumber);
        Assert.Equal(new[] { R0, R1, R2, R3 }, a.ZoneRings.ToArray());
    }

    [Fact]
    public void A_new_match_without_a_zone_drops_the_old_one()
    {
        BattleClient a = InMatch(out _);
        MatchStartMessage royale = NewRoyale(a, R0, R1);
        a.OnData(0, MatchStart(royale));
        Assert.True(a.HasZone);

        a.OnData(0, MatchStart(royale with { Mode = BattleMode.TeamBattle, MatchNumber = royale.MatchNumber + 1, Zone = null }));

        Assert.Null(a.LastPacketError);
        Assert.False(a.HasZone);
        Assert.Equal(0, a.ZoneRings.Length);
        Assert.Equal(ZonePhase.None, a.ZoneAt(StartTick + 10).Phase);
        a.OnData(0, Announce(royale.MatchNumber + 1, 2, R2));   // an announcement for a match without a zone: nothing to append to
        Assert.False(a.HasZone);
    }

    [Fact]
    public void Leaving_the_match_forgets_the_zone()
    {
        BattleClient a = InMatch(out _);
        a.OnData(0, MatchStart(NewRoyale(a, R0, R1)));
        Assert.True(a.HasZone);

        var w = new Tank.Net.Protocol.BitWriter();
        BattleMessages.Write(w, a.Room! with { MatchRunning = false });
        a.OnData(0, w.Data.ToArray());

        Assert.Equal(BattlePhase.Room, a.Phase);
        Assert.False(a.HasZone);
        Assert.Equal(0, a.ZoneRings.Length);
        Assert.Equal(0, a.ZoneStartTick);
        Assert.Equal(default, a.ZoneAt(StartTick + 10));
    }

    /// <summary>The client's zone as the tests compare it: whether it has one, and the rings.</summary>
    private static (bool, ZoneRing[]) ZoneOf(BattleClient a) => (a.HasZone, a.ZoneRings.ToArray());

    [Fact]
    public void An_announcement_that_does_not_fit_is_ignored_not_counted_as_malformed()
    {
        BattleClient a = InMatch(out _);
        MatchStartMessage royale = NewRoyale(a, R0, R1, R2);
        a.OnData(0, MatchStart(royale));
        a.Update(0, LocalCommand.None);
        int dropped = a.Stats.DroppedPackets;
        var before = ZoneOf(a);
        int n = royale.MatchNumber;

        a.OnData(0, Announce(n + 1, 3, R3));   // another match's
        a.OnData(0, Announce(n, 2, R3));       // a ring already known
        a.OnData(0, Announce(n, 1, R3));
        a.OnData(0, Announce(n, 4, R4));       // past a gap: ring 3 never came

        a.Update(0, LocalCommand.None);
        Assert.Equal(before.Item1, ZoneOf(a).Item1);
        Assert.Equal(before.Item2, ZoneOf(a).Item2);
        Assert.Equal(dropped, a.Stats.DroppedPackets);
        Assert.Null(a.LastPacketError);
    }

    [Fact]
    public void An_announcement_before_any_match_start_is_ignored()
    {
        var h = new BattleHarness();
        BattleClient a = h.AddClient("A");
        h.Run(0.3);
        Assert.Equal(BattlePhase.Room, a.Phase);
        int dropped = a.Stats.DroppedPackets;

        a.OnData(0, Announce(0, 1, R1));
        a.OnData(0, Announce(1, 1, R1));
        a.Update(0, LocalCommand.None);

        Assert.False(a.HasZone);
        Assert.Equal(0, a.ZoneRings.Length);
        Assert.Equal(dropped, a.Stats.DroppedPackets);
        Assert.Null(a.LastPacketError);
    }

    [Fact]
    public void An_announcement_after_leaving_the_match_is_ignored()
    {
        BattleClient a = InMatch(out _);
        MatchStartMessage royale = NewRoyale(a, R0, R1);
        a.OnData(0, MatchStart(royale));
        var w = new Tank.Net.Protocol.BitWriter();
        BattleMessages.Write(w, a.Room! with { MatchRunning = false });
        a.OnData(0, w.Data.ToArray());
        Assert.Equal(BattlePhase.Room, a.Phase);
        a.Update(0, LocalCommand.None);
        int dropped = a.Stats.DroppedPackets;

        a.OnData(0, Announce(royale.MatchNumber, 2, R2));
        a.Update(0, LocalCommand.None);

        Assert.False(a.HasZone);
        Assert.Equal(0, a.ZoneRings.Length);
        Assert.Equal(dropped, a.Stats.DroppedPackets);
        Assert.Null(a.LastPacketError);
    }

    [Fact]
    public void A_zone_in_the_start_of_a_mode_without_one_is_ignored()
    {
        BattleClient a = InMatch(out _);
        MatchStartMessage start = a.MatchInfo! with { MatchNumber = a.MatchInfo.MatchNumber + 1, Zone = new ZoneSync(StartTick, new[] { R0, R1 }) };
        Assert.Equal(BattleMode.TeamBattle, start.Mode);

        a.OnData(0, MatchStart(start));

        Assert.Null(a.LastPacketError);
        Assert.Equal(start.MatchNumber, a.MatchInfo!.MatchNumber);
        Assert.False(a.HasZone);
        Assert.Equal(0, a.ZoneRings.Length);
        Assert.Equal(default, a.ZoneAt(StartTick + 10));
        a.OnData(0, Announce(start.MatchNumber, 2, R2));
        Assert.False(a.HasZone);
    }

    [Fact]
    public void A_bad_announcement_is_dropped_and_counted_and_the_match_goes_on()
    {
        BattleClient a = InMatch(out _);
        a.OnData(0, MatchStart(NewRoyale(a, R0, R1)));
        a.Update(0, LocalCommand.None);
        int dropped = a.Stats.DroppedPackets;
        byte[] bad = Announce(a.MatchInfo!.MatchNumber, 2, R2);
        bad[^1] = 0xFF;   // the radius's top byte: a negative NaN
        bad[^2] = 0xFF;

        a.OnData(0, bad);
        a.OnData(0, bad.AsSpan(0, 5));   // cut short

        a.Update(0, LocalCommand.None);
        Assert.Equal(dropped + 2, a.Stats.DroppedPackets);
        Assert.NotNull(a.LastPacketError);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
        Assert.Equal(2, a.ZoneRings.Length);
    }

    [Fact]
    public void ZoneAt_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            BattleClient a = InMatch(out _);
            a.OnData(0, MatchStart(NewRoyale(a, R0, R1)));
            a.OnData(0, Announce(a.MatchInfo!.MatchNumber, 2, R2));
            double sum = 0;
            for (int i = 0; i < 100; i++) sum += a.ZoneAt(StartTick + i * 30.5).Current.Radius;   // warm-up

            Allocation.Window window = Allocation.Start();
            for (int i = 0; i < 1000; i++) sum += a.ZoneAt(StartTick + i * 17.25).Current.Radius;
            Allocation.Measured measured = window.Stop();

            Assert.True(sum > 0);
            return measured;
        });
    }
}

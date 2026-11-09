using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using static Tank.Net.Tests.Battle.BattleTestPackets;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// The client's crates (stage 6 design §4.4): a snapshot's rows feed <see cref="BattleClient.Crates"/> and its own layers
/// its own tank, a pickup message of this match is queued for the HUD and fells the crate, one that isn't ours to take
/// is ignored, a match start's known crates replace the lot, leaving the match forgets them all. Packets are fed
/// straight in, as a server would send them.
/// </summary>
public sealed class CrateClientTests
{
    private const int StartTick = 120;

    private static readonly ZoneRing R0 = new(new Vector2(600f, 600f), 806.1f);
    private static readonly ZoneRing R1 = new(new Vector2(560f, 640f), 420f);

    private static readonly Crate C0 = new(0, new Vector2(152.5f, 398.25f), CrateKind.Repair);
    private static readonly Crate C3 = new(3, new Vector2(600.5f, 712.25f), CrateKind.Ammo);
    private static readonly Crate C40 = new(40, new Vector2(1024f, 852f), CrateKind.Optics);

    private static BattleClient InMatch(out BattleHarness h)
    {
        h = new BattleHarness();
        BattleClient a = h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
        return a;
    }

    /// <summary>A new royale (the next match number) with the map, a zone and <paramref name="crates"/> known: what a royale's start looks like to the client.</summary>
    private static MatchStartMessage NewRoyale(BattleClient a, Crate[]? crates = null) =>
        a.MatchInfo! with { Mode = BattleMode.Royale, MatchNumber = a.MatchInfo.MatchNumber + 1, Zone = new ZoneSync(StartTick, new[] { R0, R1 }), Crates = crates };

    /// <summary>The client in a royale (its own tank 100 m in), its knowledge <paramref name="crates"/>.</summary>
    private static BattleClient InRoyale(out BattleHarness h, params Crate[] crates)
    {
        BattleClient a = InMatch(out h);
        a.OnData(0, MatchStart(NewRoyale(a, crates)));
        Assert.Null(a.LastPacketError);
        Assert.Equal(BattleMode.Royale, a.MatchInfo!.Mode);
        return a;
    }

    private static byte[] Pickup(CrateMessage m)
    {
        var w = new Tank.Net.Protocol.BitWriter();
        BattleMessages.Write(w, m);
        return w.Data.ToArray();
    }

    private static CrateMessage Ammo(int matchNumber, byte id = 3) => new(matchNumber, id, CrateKind.Ammo, new Vector2(600.5f, 712.25f), 20, 8, 0);

    /// <summary>A full snapshot of <paramref name="tick"/> for a royale the client just joined: a playing match, its own tank at (100, 100), rows as <paramref name="mutate"/> adds them.</summary>
    private static byte[] RoyaleSnapshot(BattleClient a, int tick, Action<BattleSnapshot>? mutate = null)
    {
        var s = new BattleSnapshot();
        s.Match = new MatchBlock { Phase = MatchPhase.Playing, TicksLeft = 18000, WinnerTeam = MatchRules.NoTeam, WinnerSlot = MatchRules.NoSlot, Entrants = 2 };
        s.HasOwn = true;
        s.Own = new OwnTank { Id = a.OwnTankId, X = 100f, Z = 100f, Health = 900, ApLeft = 10, HeLeft = 5 };
        SetEntity(s, new BattleEntity { Id = a.OwnTankId, Slot = a.Slot, Spec = 0, Team = a.Slot, X = 10_000, Z = 10_000, Health = 900 });
        return Snapshot(s, tick, 0, mutate);
    }

    private static void AddRow(BattleSnapshot s, byte id, CrateKind kind, bool standing, int xCm, int zCm)
    {
        s.Crates[s.CrateCount++] = new CrateView { Id = id, Kind = kind, Standing = standing, X = xCm, Z = zCm };
    }

    private static int Dropped(BattleClient c)
    {
        c.Update(0, LocalCommand.None);
        return c.Stats.DroppedPackets;
    }

    [Fact]
    public void A_team_battle_knows_no_crates()
    {
        BattleClient a = InMatch(out _);

        Assert.Equal((0, 0), (a.Crates.KnownCount, a.Crates.StandingKnown));
        Assert.False(a.TryDequeuePickup(out _));
        Assert.Equal((0, 0, 0), (a.Own.ArmorLayers, a.Own.ReloadLayers, a.Own.OpticsLayers));
    }

    [Fact]
    public void A_pickup_in_a_royale_is_queued_and_fells_the_crate()
    {
        BattleClient a = InRoyale(out _, C3, C40);
        int n = a.MatchInfo!.MatchNumber;
        int version = a.Crates.Version;

        a.OnData(0, Pickup(Ammo(n)));

        Assert.Null(a.LastPacketError);
        Assert.True(a.TryDequeuePickup(out CrateMessage pickup));
        Assert.Equal(Ammo(n), pickup);
        Assert.False(a.TryDequeuePickup(out _));
        Assert.True(a.Crates.All[3].Known);
        Assert.False(a.Crates.All[3].Standing);
        Assert.Equal((2, 1), (a.Crates.KnownCount, a.Crates.StandingKnown));
        Assert.Equal(version + 1, a.Crates.Version);

        // One never seen (driven over before the sight pass caught it): known now, down; the pickups come in order.
        a.OnData(0, Pickup(Ammo(n, 9) with { Kind = CrateKind.Reload, Amount = 1, Amount2 = 0, Layers = 1 }));
        a.OnData(0, Pickup(Ammo(n, 40) with { Kind = CrateKind.Optics, Amount = 0, Amount2 = 0, Layers = 3 }));
        Assert.True(a.Crates.All[9].Known && !a.Crates.All[9].Standing);
        Assert.Equal((3, 0), (a.Crates.KnownCount, a.Crates.StandingKnown));
        Assert.True(a.TryDequeuePickup(out CrateMessage first));
        Assert.True(a.TryDequeuePickup(out CrateMessage second));
        Assert.Equal((9, 40), (first.CrateId, second.CrateId));
        Assert.Equal((CrateKind.Reload, 1, (byte)1), (first.Kind, first.Amount, first.Layers));
        Assert.False(a.TryDequeuePickup(out _));
    }

    [Fact]
    public void A_pickup_that_is_not_ours_to_take_is_ignored_not_counted_as_malformed()
    {
        BattleClient a = InRoyale(out _, C3);
        int n = a.MatchInfo!.MatchNumber;
        int dropped = Dropped(a), version = a.Crates.Version;

        a.OnData(0, Pickup(Ammo(n + 1)));   // another match's
        a.OnData(0, Pickup(Ammo(n - 1)));

        a.Update(0, LocalCommand.None);
        Assert.False(a.TryDequeuePickup(out _));
        Assert.True(a.Crates.All[3].Standing);
        Assert.Equal(version, a.Crates.Version);
        Assert.Equal(dropped, a.Stats.DroppedPackets);
        Assert.Null(a.LastPacketError);
    }

    [Fact]
    public void A_pickup_in_a_mode_without_crates_is_ignored()
    {
        BattleClient a = InMatch(out _);
        int n = a.MatchInfo!.MatchNumber;
        int dropped = Dropped(a);

        a.OnData(0, Pickup(Ammo(n)));

        a.Update(0, LocalCommand.None);
        Assert.False(a.TryDequeuePickup(out _));
        Assert.Equal(0, a.Crates.KnownCount);
        Assert.Equal(dropped, a.Stats.DroppedPackets);
        Assert.Null(a.LastPacketError);
    }

    [Fact]
    public void A_pickup_before_any_match_start_is_ignored()
    {
        var h = new BattleHarness();
        BattleClient a = h.AddClient("A");
        h.Run(0.3);
        Assert.Equal(BattlePhase.Room, a.Phase);
        int dropped = Dropped(a);

        a.OnData(0, Pickup(Ammo(0)));
        a.OnData(0, Pickup(Ammo(1)));

        a.Update(0, LocalCommand.None);
        Assert.False(a.TryDequeuePickup(out _));
        Assert.Equal(0, a.Crates.KnownCount);
        Assert.Equal(dropped, a.Stats.DroppedPackets);
        Assert.Null(a.LastPacketError);
    }

    [Fact]
    public void A_pickup_after_leaving_the_match_is_ignored()
    {
        BattleClient a = InRoyale(out _, C3);
        int n = a.MatchInfo!.MatchNumber;
        var w = new Tank.Net.Protocol.BitWriter();
        BattleMessages.Write(w, a.Room! with { MatchRunning = false });
        a.OnData(0, w.Data.ToArray());
        Assert.Equal(BattlePhase.Room, a.Phase);
        int dropped = Dropped(a);

        a.OnData(0, Pickup(Ammo(n)));

        a.Update(0, LocalCommand.None);
        Assert.False(a.TryDequeuePickup(out _));
        Assert.Equal(0, a.Crates.KnownCount);
        Assert.Equal(dropped, a.Stats.DroppedPackets);
        Assert.Null(a.LastPacketError);
    }

    [Fact]
    public void A_bad_pickup_is_dropped_and_counted_and_the_match_goes_on()
    {
        BattleClient a = InRoyale(out _, C3);
        int dropped = Dropped(a);
        byte[] bad = Pickup(Ammo(a.MatchInfo!.MatchNumber));
        bad[3] = 0xFF;   // the byte after the id: kind 7 in its low bits

        a.OnData(0, bad);
        a.OnData(0, bad.AsSpan(0, 5));   // cut short

        a.Update(0, LocalCommand.None);
        Assert.Equal(dropped + 2, a.Stats.DroppedPackets);
        Assert.NotNull(a.LastPacketError);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
        Assert.False(a.TryDequeuePickup(out _));
        Assert.True(a.Crates.All[3].Standing);
    }

    [Fact]
    public void The_snapshots_crates_and_own_layers_reach_the_client()
    {
        BattleClient a = InRoyale(out _);
        int tick = a.MatchInfo!.CurrentTick + 1;

        a.OnData(0, RoyaleSnapshot(a, tick, s =>
        {
            s.Own.ArmorLayers = 1;
            s.Own.ReloadLayers = 2;
            s.Own.OpticsLayers = 3;
            AddRow(s, 3, CrateKind.Ammo, true, 60050, 71225);
            AddRow(s, 40, CrateKind.Optics, true, 102400, 85200);
        }));

        Assert.Null(a.LastPacketError);
        Assert.Equal(tick, a.Store.NewestTick);
        Assert.Equal(2, a.Latest!.CrateCount);
        Assert.Equal((2, 2), (a.Crates.KnownCount, a.Crates.StandingKnown));
        Assert.True(a.Crates.All[3].Visible && a.Crates.All[40].Visible);
        Assert.Equal((CrateKind.Ammo, new Vector2(600.5f, 712.25f)), (a.Crates.All[3].Kind, a.Crates.All[3].Position));
        Assert.True(a.HasTank);
        Assert.Equal(((byte)1, (byte)2, (byte)3), (a.Own.ArmorLayers, a.Own.ReloadLayers, a.Own.OpticsLayers));

        // The next one sees only crate 3: 40 is still known, out of sight; the layers follow the server.
        a.OnData(0, RoyaleSnapshot(a, tick + 2, s =>
        {
            s.Own.ReloadLayers = 3;
            AddRow(s, 3, CrateKind.Ammo, true, 60050, 71225);
        }));
        Assert.True(a.Crates.All[40].Known && !a.Crates.All[40].Visible && a.Crates.All[40].Standing);
        Assert.Equal((byte)3, a.Own.ReloadLayers);
        Assert.Equal((byte)0, a.Own.ArmorLayers);

        // A snapshot from before the newest, arriving late: kept as a baseline, but not what the client knows now.
        a.OnData(0, RoyaleSnapshot(a, tick + 1, s => AddRow(s, 30, CrateKind.Armor, true, 100, 100)));
        Assert.Null(a.LastPacketError);
        Assert.Equal(tick + 2, a.Store.NewestTick);
        Assert.False(a.Crates.All[30].Known);
        Assert.Equal((byte)3, a.Own.ReloadLayers);

        // Then a view of crate 40's spot, empty: it is gone.
        a.OnData(0, RoyaleSnapshot(a, tick + 3, s => AddRow(s, 40, CrateKind.Optics, false, 102400, 85200)));
        Assert.False(a.Crates.All[40].Standing);
        Assert.Equal((2, 1), (a.Crates.KnownCount, a.Crates.StandingKnown));
    }

    [Fact]
    public void A_resync_brings_the_crates_the_seat_knows()
    {
        BattleClient a = InRoyale(out _);
        int tick = a.MatchInfo!.CurrentTick + 1;
        a.OnData(0, RoyaleSnapshot(a, tick, s =>
        {
            AddRow(s, 3, CrateKind.Ammo, true, 60050, 71225);
            AddRow(s, 40, CrateKind.Optics, true, 102400, 85200);
            AddRow(s, 63, CrateKind.Reload, false, 100, 100);
        }));
        Assert.Equal((3, 2), (a.Crates.KnownCount, a.Crates.StandingKnown));

        a.OnData(0, MatchStart(a.MatchInfo with { Map = null, CurrentTick = tick, Crates = new[] { C0, C3 } }));

        Assert.Null(a.LastPacketError);
        Assert.Equal(BattlePhase.InMatch, a.Phase);
        Assert.Equal(tick, a.Store.NewestTick);   // a resync: the store stays
        Assert.Equal((2, 2), (a.Crates.KnownCount, a.Crates.StandingKnown));
        Assert.True(a.Crates.All[0].Known && a.Crates.All[0].Standing);
        Assert.True(a.Crates.All[3].Known && a.Crates.All[3].Standing);
        Assert.False(a.Crates.All[40].Known);
        Assert.False(a.Crates.All[63].Known);
        Assert.Equal((CrateKind.Repair, new Vector2(152.5f, 398.25f)), (a.Crates.All[0].Kind, a.Crates.All[0].Position));
    }

    [Fact]
    public void Crates_in_the_start_of_a_mode_without_them_are_no_crates_of_ours()
    {
        BattleClient a = InMatch(out _);
        MatchStartMessage start = a.MatchInfo! with { MatchNumber = a.MatchInfo.MatchNumber + 1, Crates = new[] { C0, C3 } };
        Assert.Equal(BattleMode.TeamBattle, start.Mode);

        a.OnData(0, MatchStart(start));

        Assert.Null(a.LastPacketError);
        Assert.Equal(start.MatchNumber, a.MatchInfo!.MatchNumber);
        Assert.Equal((0, 0), (a.Crates.KnownCount, a.Crates.StandingKnown));
    }

    [Fact]
    public void A_new_match_forgets_the_last_ones_crates_and_pickups()
    {
        BattleClient a = InRoyale(out _, C3, C40);
        a.OnData(0, Pickup(Ammo(a.MatchInfo!.MatchNumber)));
        Assert.Equal((2, 1), (a.Crates.KnownCount, a.Crates.StandingKnown));

        a.OnData(0, MatchStart(NewRoyale(a, new[] { C0 })));

        Assert.Null(a.LastPacketError);
        Assert.Equal((1, 1), (a.Crates.KnownCount, a.Crates.StandingKnown));
        Assert.True(a.Crates.All[0].Known);
        Assert.False(a.Crates.All[3].Known);
        Assert.False(a.TryDequeuePickup(out _));
    }

    [Fact]
    public void Leaving_the_match_forgets_the_crates_and_the_pickups()
    {
        BattleClient a = InRoyale(out _, C3, C40);
        a.OnData(0, Pickup(Ammo(a.MatchInfo!.MatchNumber)));
        Assert.Equal(2, a.Crates.KnownCount);

        var w = new Tank.Net.Protocol.BitWriter();
        BattleMessages.Write(w, a.Room! with { MatchRunning = false });
        a.OnData(0, w.Data.ToArray());

        Assert.Equal(BattlePhase.Room, a.Phase);
        Assert.Equal((0, 0), (a.Crates.KnownCount, a.Crates.StandingKnown));
        Assert.False(a.Crates.All[3].Known);
        Assert.False(a.TryDequeuePickup(out _));
    }
}

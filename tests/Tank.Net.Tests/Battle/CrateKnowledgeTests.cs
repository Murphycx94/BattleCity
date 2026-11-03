using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Testing;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// The client's "crates I have seen" (stage 6 design §4.4): a snapshot's rows become known with what they show, rows
/// out of sight keep what they had, a pickup message fells a crate, a match start's known crates replace the lot;
/// <see cref="CrateKnowledge.Version"/> moves only when something it draws changed, and a snapshot costs no allocation.
/// </summary>
public sealed class CrateKnowledgeTests
{
    private static readonly Crate C0 = new(0, new Vector2(152.5f, 398.25f), CrateKind.Repair);
    private static readonly Crate C3 = new(3, new Vector2(600.5f, 712.25f), CrateKind.Ammo);

    private static BattleSnapshot Snapshot(params CrateView[] rows)
    {
        var s = new BattleSnapshot { Tick = 100 };
        for (int i = 0; i < rows.Length; i++) s.Crates[i] = rows[i];
        s.CrateCount = rows.Length;
        return s;
    }

    private static CrateView Row(byte id, CrateKind kind, bool standing, int xCm, int zCm) => new() { Id = id, Kind = kind, Standing = standing, X = xCm, Z = zCm };

    [Fact]
    public void A_fresh_knowledge_knows_nothing()
    {
        var k = new CrateKnowledge();

        Assert.Equal(CrateRules.MaxCrates, k.All.Length);
        Assert.Equal((0, 0, 0), (k.KnownCount, k.StandingKnown, k.Version));
        foreach (CrateKnowledge.Entry e in k.All) Assert.False(e.Known || e.Standing || e.Visible);
    }

    [Fact]
    public void The_rows_of_a_snapshot_become_known_and_visible()
    {
        var k = new CrateKnowledge();

        k.Apply(Snapshot(Row(3, CrateKind.Ammo, true, 60050, 71225), Row(40, CrateKind.Optics, true, 102400, 85200)));

        Assert.Equal((2, 2), (k.KnownCount, k.StandingKnown));
        Assert.True(k.Version > 0);   // something drawn changed (how many bumps is the knowledge's own business)
        CrateKnowledge.Entry e3 = k.All[3], e40 = k.All[40];
        Assert.True(e3.Known && e3.Standing && e3.Visible);
        Assert.Equal((CrateKind.Ammo, new Vector2(600.5f, 712.25f)), (e3.Kind, e3.Position));
        Assert.True(e40.Known && e40.Standing && e40.Visible);
        Assert.Equal((CrateKind.Optics, new Vector2(1024f, 852f)), (e40.Kind, e40.Position));
        Assert.False(k.All[0].Known);
        Assert.False(k.All[63].Known);
    }

    [Fact]
    public void A_crate_out_of_sight_stays_known_as_it_was()
    {
        var k = new CrateKnowledge();
        k.Apply(Snapshot(Row(3, CrateKind.Ammo, true, 60050, 71225), Row(40, CrateKind.Optics, true, 102400, 85200)));
        int version = k.Version;

        k.Apply(Snapshot(Row(3, CrateKind.Ammo, true, 60050, 71225)));

        CrateKnowledge.Entry e40 = k.All[40];
        Assert.True(e40.Known);
        Assert.False(e40.Visible);
        Assert.True(e40.Standing);
        Assert.Equal((CrateKind.Optics, new Vector2(1024f, 852f)), (e40.Kind, e40.Position));
        Assert.True(k.All[3].Visible);
        Assert.Equal((2, 2), (k.KnownCount, k.StandingKnown));
        Assert.Equal(version, k.Version);   // only the visibility moved: nothing drawn changed

        k.Apply(Snapshot());
        Assert.False(k.All[3].Visible);
        Assert.Equal((2, 2), (k.KnownCount, k.StandingKnown));
        Assert.Equal(version, k.Version);
    }

    [Fact]
    public void Seeing_an_empty_spot_fells_the_crate()
    {
        var k = new CrateKnowledge();
        k.Apply(Snapshot(Row(3, CrateKind.Ammo, true, 60050, 71225), Row(40, CrateKind.Optics, true, 102400, 85200)));
        int version = k.Version;

        k.Apply(Snapshot(Row(3, CrateKind.Ammo, true, 60050, 71225), Row(40, CrateKind.Optics, false, 102400, 85200)));

        Assert.False(k.All[40].Standing);
        Assert.True(k.All[40].Known && k.All[40].Visible);
        Assert.Equal((2, 1), (k.KnownCount, k.StandingKnown));
        Assert.Equal(version + 1, k.Version);

        // A row never seen before, already empty: known, down.
        k.Apply(Snapshot(Row(9, CrateKind.Repair, false, 100, 100)));
        Assert.True(k.All[9].Known);
        Assert.False(k.All[9].Standing);
        Assert.Equal((3, 1), (k.KnownCount, k.StandingKnown));
        Assert.Equal(version + 2, k.Version);
    }

    [Fact]
    public void A_pickup_fells_the_crate_known_or_not()
    {
        var k = new CrateKnowledge();
        k.Apply(Snapshot(Row(3, CrateKind.Ammo, true, 60050, 71225)));
        int version = k.Version;

        k.Take(5);   // never seen: the player drove over it before the sight pass caught it
        Assert.True(k.All[5].Known);
        Assert.False(k.All[5].Standing);
        Assert.False(k.All[5].Visible);
        Assert.Equal((2, 1), (k.KnownCount, k.StandingKnown));
        Assert.Equal(version + 1, k.Version);

        k.Take(3);
        Assert.True(k.All[3].Known);
        Assert.False(k.All[3].Standing);
        Assert.Equal((CrateKind.Ammo, new Vector2(600.5f, 712.25f)), (k.All[3].Kind, k.All[3].Position));   // what it was stays
        Assert.Equal((2, 0), (k.KnownCount, k.StandingKnown));
        Assert.Equal(version + 2, k.Version);

        k.Take(3);   // already down: nothing moves
        Assert.Equal((2, 0), (k.KnownCount, k.StandingKnown));
        Assert.Equal(version + 2, k.Version);

        Assert.Throws<ArgumentOutOfRangeException>(() => k.Take(64));
    }

    [Fact]
    public void A_pickup_message_gives_a_crate_never_seen_its_kind_and_position()
    {
        var k = new CrateKnowledge();
        int version = k.Version;

        k.Take(5, CrateKind.Ammo, new Vector2(10, 20));   // eaten before any sight pass caught it: the message says what it was

        CrateKnowledge.Entry e = k.All[5];
        Assert.True(e.Known && !e.Standing && !e.Visible);
        Assert.Equal((CrateKind.Ammo, new Vector2(10, 20)), (e.Kind, e.Position));
        Assert.Equal((1, 0), (k.KnownCount, k.StandingKnown));
        Assert.Equal(version + 1, k.Version);

        k.Take(5, CrateKind.Repair, Vector2.Zero);   // already down: nothing moves, not even what it was
        Assert.Equal((CrateKind.Ammo, new Vector2(10, 20)), (k.All[5].Kind, k.All[5].Position));
        Assert.Equal(version + 1, k.Version);

        Assert.Throws<ArgumentOutOfRangeException>(() => k.Take(64, CrateKind.Ammo, Vector2.Zero));
        Assert.Throws<ArgumentException>(() => k.Take(6, (CrateKind)CrateRules.Kinds, Vector2.Zero));
        Assert.False(k.All[6].Known);
    }

    [Fact]
    public void A_crate_the_start_gave_to_the_centimetre_doesnt_move_when_its_row_comes()
    {
        var k = new CrateKnowledge();
        var crate = new Crate(7, new Vector2(123.456f, 78.9012f), CrateKind.Reload);
        int xCm = BattleProtocol.ToCm(crate.Position.X), zCm = BattleProtocol.ToCm(crate.Position.Y);
        Crate onTheWire = crate with { Position = new Vector2(BattleProtocol.FromCm(xCm), BattleProtocol.FromCm(zCm)) };
        k.Reset(new[] { onTheWire });   // the server sends a start's positions to the centimetre, as the rows carry them
        int version = k.Version;

        k.Apply(Snapshot(Row(7, CrateKind.Reload, true, xCm, zCm)));

        Assert.Equal(version, k.Version);
        Assert.Equal(onTheWire.Position, k.All[7].Position);
        Assert.True(k.All[7] is { Known: true, Standing: true, Visible: true });

        k.Reset(new[] { crate });   // the table's raw floats instead: the row moves the crate under half a centimetre, and the version says so
        version = k.Version;
        k.Apply(Snapshot(Row(7, CrateKind.Reload, true, xCm, zCm)));
        Assert.Equal(version + 1, k.Version);
        Assert.Equal(onTheWire.Position, k.All[7].Position);
        Assert.True(Vector2.Distance(crate.Position, k.All[7].Position) < 0.0051f);
    }

    [Fact]
    public void A_match_start_replaces_the_lot_with_what_the_server_says_the_seat_knows()
    {
        var k = new CrateKnowledge();
        k.Apply(Snapshot(Row(3, CrateKind.Ammo, false, 60050, 71225), Row(40, CrateKind.Optics, true, 102400, 85200)));
        k.Take(9);
        int version = k.Version;

        k.Reset(new[] { C0, C3 });

        Assert.Equal((2, 2), (k.KnownCount, k.StandingKnown));
        Assert.True(version < k.Version);
        CrateKnowledge.Entry e0 = k.All[0], e3 = k.All[3];
        Assert.True(e0.Known && e0.Standing && !e0.Visible);
        Assert.Equal((CrateKind.Repair, new Vector2(152.5f, 398.25f)), (e0.Kind, e0.Position));
        Assert.True(e3.Known && e3.Standing);
        Assert.Equal((CrateKind.Ammo, new Vector2(600.5f, 712.25f)), (e3.Kind, e3.Position));
        Assert.False(k.All[40].Known);
        Assert.False(k.All[9].Known);

        k.Reset(null);   // a fresh start
        Assert.Equal((0, 0), (k.KnownCount, k.StandingKnown));
        foreach (CrateKnowledge.Entry e in k.All) Assert.False(e.Known);

        k.Reset(Array.Empty<Crate>());
        Assert.Equal((0, 0), (k.KnownCount, k.StandingKnown));
        Assert.Throws<ArgumentException>(() => k.Reset(new[] { new Crate(64, Vector2.One, CrateKind.Armor) }));
    }

    [Fact]
    public void Clearing_forgets_everything()
    {
        var k = new CrateKnowledge();
        k.Apply(Snapshot(Row(3, CrateKind.Ammo, true, 60050, 71225)));
        int version = k.Version;

        k.Clear();

        Assert.Equal((0, 0), (k.KnownCount, k.StandingKnown));
        Assert.False(k.All[3].Known || k.All[3].Visible || k.All[3].Standing);
        Assert.True(version < k.Version);
        int cleared = k.Version;
        k.Clear();   // nothing to forget: nothing changes
        Assert.Equal(cleared, k.Version);
    }

    [Fact]
    public void The_version_moves_only_when_something_drawn_changes()
    {
        var k = new CrateKnowledge();
        BattleSnapshot s = Snapshot(Row(3, CrateKind.Ammo, true, 60050, 71225), Row(40, CrateKind.Optics, true, 102400, 85200));
        k.Apply(s);
        int version = k.Version;

        k.Apply(s);
        Assert.Equal(version, k.Version);

        k.Apply(Snapshot(Row(3, CrateKind.Ammo, true, 60051, 71225)));   // a position moved (it never does; the version says so all the same)
        Assert.Equal(version + 1, k.Version);
        k.Apply(Snapshot(Row(3, CrateKind.Armor, true, 60051, 71225)));
        Assert.Equal(version + 2, k.Version);
        k.Apply(Snapshot(Row(3, CrateKind.Armor, true, 60051, 71225)));
        Assert.Equal(version + 2, k.Version);
    }

    [Fact]
    public void Applying_a_snapshot_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var k = new CrateKnowledge();
            BattleSnapshot a = Snapshot(Row(3, CrateKind.Ammo, true, 60050, 71225), Row(40, CrateKind.Optics, true, 102400, 85200), Row(63, CrateKind.Reload, false, 5, 5));
            BattleSnapshot b = Snapshot(Row(3, CrateKind.Ammo, true, 60050, 71225), Row(40, CrateKind.Optics, false, 102400, 85200));
            for (int i = 0; i < 100; i++)   // warm-up
            {
                k.Apply(a);
                k.Apply(b);
                k.Take((byte)(i % 64));
            }
            k.Reset(new[] { C0, C3 });

            Allocation.Window window = Allocation.Start();
            for (int i = 0; i < 1000; i++)
            {
                k.Apply(a);
                k.Apply(b);
                k.Take((byte)(i % 64));
            }
            Allocation.Measured measured = window.Stop();

            Assert.True(k.KnownCount >= 3);
            return measured;
        });
    }
}

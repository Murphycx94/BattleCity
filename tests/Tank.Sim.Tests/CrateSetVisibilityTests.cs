using System.Diagnostics;
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// The mask of crates a viewer sees (stage 6 design §4.4, <see cref="CrateSet.VisibleTo"/>): a bit per crate id, eaten ones
/// included (a client learns a crate is gone by seeing its empty spot), none for a wreck; without allocating.
/// </summary>
public sealed class CrateSetVisibilityTests
{
    /// <summary>The riverland's 48 crates for seed 1, placed once for these tests.</summary>
    internal static readonly Lazy<Crate[]> RiverlandCrates = new(() =>
        CratePlacer.Place(RiverlandFixture.Field, RiverlandFixture.Grid, RiverlandMap.Spawns.Solo, 1u ^ CratePlacer.SeedSalt));

    private static readonly Vector2 TankAt = new(100, 100);

    /// <summary>Three crates 60 m from the tank: north, east and south of it.</summary>
    private static CrateSet ThreeCrates() => new(new Crate[]
    {
        new(0, TankAt - new Vector2(0, 60), CrateKind.Repair),
        new(1, TankAt + new Vector2(60, 0), CrateKind.Ammo),
        new(2, TankAt + new Vector2(0, 60), CrateKind.Optics),
    });

    private static TankState Viewer(Vector2 at, TankSpec? spec = null) => new()
    {
        Id = 1, Spec = spec ?? TankCatalog.Medium, Position = at, LastFired = TankState.NeverFired,
    };

    [Fact]
    public void In_the_open_a_tank_sees_every_crate() =>
        Assert.Equal(0b111ul, ThreeCrates().VisibleTo(Viewer(TankAt), TestWorld.FlatField()));

    [Fact]
    public void An_eaten_crate_stays_in_the_mask()
    {
        CrateSet set = ThreeCrates();
        Assert.True(set.Take(1, tankId: 2));
        Assert.Equal(0b101ul, set.StandingMask);
        Assert.Equal(0b111ul, set.VisibleTo(Viewer(TankAt), TestWorld.FlatField()));   // the empty spot is seen
    }

    [Fact]
    public void A_crate_behind_a_wall_is_left_out()
    {
        // A rock slab 30 m north of the tank, across the line to crate 0.
        Prop slab = new(1, PropKind.Rock, new Obb2(TankAt - new Vector2(0, 30), new Vector2(6, 0.5f), 0), -1f, 7f);
        Assert.Equal(0b110ul, ThreeCrates().VisibleTo(Viewer(TankAt), TestWorld.FlatField(slab)));
    }

    [Fact]
    public void A_crate_beyond_the_view_range_is_left_out()
    {
        Vector2 at = new(200, 390);
        var set = new CrateSet(new Crate[]
        {
            new(0, at - new Vector2(0, 200), CrateKind.Armor),
            new(1, at - new Vector2(0, 271), CrateKind.Reload),   // a metre past 270
        });
        Assert.Equal(0b01ul, set.VisibleTo(Viewer(at), TestWorld.WideFlatField()));
    }

    [Fact]
    public void A_wreck_sees_no_crate()
    {
        TankState wreck = Viewer(TankAt);
        wreck.IsDestroyed = true;
        Assert.Equal(0ul, ThreeCrates().VisibleTo(wreck, TestWorld.FlatField()));
    }

    [Fact]
    public void An_empty_set_gives_an_empty_mask() =>
        Assert.Equal(0ul, new CrateSet(Array.Empty<Crate>()).VisibleTo(Viewer(TankAt), TestWorld.FlatField()));

    [Fact]
    public void Looking_over_the_riverlands_crates_does_not_allocate()
    {
        Battlefield field = RiverlandFixture.Field;
        Allocation.AssertNone(() =>
        {
            var set = new CrateSet(RiverlandCrates.Value);
            Assert.Equal(CratePlacer.Count, set.Count);
            TankState viewer = Viewer(RiverlandMap.Landmarks.Town);
            ulong mask = 0;
            for (int i = 0; i < 10; i++) mask = set.VisibleTo(viewer, field);   // warm-up
            Allocation.Window window = Allocation.Start();
            for (int i = 0; i < 1000; i++) mask |= set.VisibleTo(viewer, field);
            Allocation.Measured measured = window.Stop();
            float reach = CrateRules.ViewRange(viewer);
            for (int id = 0; id < set.Count; id++)
                if ((mask >> id & 1ul) != 0)
                    Assert.True(Vector2.Distance(viewer.Position, set[id].Position) <= reach, $"crate {id} is seen beyond the view range");
            return measured;
        });
    }
}

/// <summary>What one vision update's crate looks cost on the server: every seat's tank over the riverland's 48 crates.</summary>
[Collection(TimingCollection.Name)]
public sealed class CrateSetVisibilityTimingTests
{
    private const int Repeats = 5;

    private readonly ITestOutputHelper _output;

    public CrateSetVisibilityTimingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Sixteen_tanks_look_over_the_riverlands_crates_quickly()
    {
        Battlefield field = RiverlandFixture.Field;
        var set = new CrateSet(CrateSetVisibilityTests.RiverlandCrates.Value);
        IReadOnlyList<SpawnPoint> spawns = RiverlandMap.Spawns.Solo;
        var tanks = new TankState[spawns.Count];
        for (int i = 0; i < tanks.Length; i++)
            tanks[i] = new TankState { Id = i + 1, Spec = TankCatalog.Medium, Position = spawns[i].Position, Yaw = spawns[i].Yaw, LastFired = TankState.NeverFired };

        ulong seen = 0;
        for (int r = 0; r < 3; r++) seen |= Pass(set, tanks, field);   // warm-up
        double best = double.MaxValue;
        for (int r = 0; r < Repeats; r++)
        {
            long start = Stopwatch.GetTimestamp();
            seen |= Pass(set, tanks, field);
            best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        int lines = 0;   // the sight lines a pass casts: the crates within each tank's view range
        foreach (TankState tank in tanks)
            foreach (Crate crate in set.All)
                if (Vector2.Distance(tank.Position, crate.Position) <= CrateRules.ViewRange(tank)) lines++;
        _output.WriteLine($"CrateSet.VisibleTo × {tanks.Length} tanks over {set.Count} crates: {best:F3} ms a pass at best of {Repeats} " +
                          $"({lines} crates in range, so that many sight lines cast; {BitOperations.PopCount(seen)} crates seen from the spawns)");
        // Nearly all of a pass is the terrain march of each sight line (HeightField.Raycast samples every metre of a line
        // up to 270 m long); from the spawns a pass casts about 110 lines, under a vision update's tank-to-tank spotting.
#if DEBUG
        const double Limit = 10.0;
#else
        const double Limit = 2.0;
#endif
        Assert.True(best <= Limit, $"a pass took {best:F3} ms at best (limit {Limit} ms)");
    }

    private static ulong Pass(CrateSet set, TankState[] tanks, Battlefield field)
    {
        ulong seen = 0;
        for (int i = 0; i < tanks.Length; i++) seen |= set.VisibleTo(tanks[i], field);
        return seen;
    }
}

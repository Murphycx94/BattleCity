using System.Numerics;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Sim.Tests;

/// <summary>The zone's five-ring timetable, its damage, ring 0 and the check a match's rings pass (stage 6 design §3.1).</summary>
public sealed class ZoneScheduleTests
{
    private const int Tps = SimConstants.TickRate;

    [Fact]
    public void The_stages_follow_the_design_table()
    {
        ZoneStage[] expected =
        {
            new(90 * Tps, 90 * Tps, 420f, 1),
            new(60 * Tps, 60 * Tps, 250f, 2),
            new(50 * Tps, 45 * Tps, 140f, 4),
            new(40 * Tps, 35 * Tps, 60f, 6),
            new(30 * Tps, 40 * Tps, 0f, 10),
        };
        Assert.Equal(5, ZoneSchedule.Rings);
        Assert.Equal(expected, ZoneSchedule.Stages.ToArray());
        for (int k = 1; k <= ZoneSchedule.Rings; k++) Assert.Equal(expected[k - 1], ZoneSchedule.Stage(k));
    }

    [Theory]
    [InlineData(1, 0, 2700, 5400)]
    [InlineData(2, 5400, 7200, 9000)]
    [InlineData(3, 9000, 10500, 11850)]
    [InlineData(4, 11850, 13050, 14100)]
    [InlineData(5, 14100, 15000, 16200)]
    public void Each_ring_waits_shrinks_and_ends_on_time(int ring, int waitStart, int shrinkStart, int shrinkEnd)
    {
        Assert.Equal(waitStart, ZoneSchedule.WaitStart(ring));
        Assert.Equal(shrinkStart, ZoneSchedule.ShrinkStart(ring));
        Assert.Equal(shrinkEnd, ZoneSchedule.ShrinkEnd(ring));
    }

    [Fact]
    public void The_zone_closes_at_nine_minutes()
    {
        Assert.Equal(16200, ZoneSchedule.ClosedAt);
        Assert.Equal(540 * Tps, ZoneSchedule.ClosedAt);
    }

    [Theory]
    [InlineData(-30, 1)]
    [InlineData(0, 1)]
    [InlineData(5399, 1)]
    [InlineData(5400, 2)]
    [InlineData(9000, 3)]
    [InlineData(14099, 4)]
    [InlineData(14100, 5)]
    [InlineData(99999, 5)]
    public void Rings_are_announced_as_their_waiting_begins(int elapsed, int announced) =>
        Assert.Equal(announced, ZoneSchedule.AnnouncedBy(elapsed));

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void There_is_no_stage_outside_rings_one_to_five(int ring)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoneSchedule.Stage(ring));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoneSchedule.WaitStart(ring));
    }

    [Theory]
    [InlineData(1000, 1, 10)]
    [InlineData(820, 4, 33)]     // 32.8 rounds up
    [InlineData(1150, 6, 69)]
    [InlineData(1000, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(500, 10, 50)]
    public void Damage_is_a_share_of_max_health_rounded_up(int maxHealth, int percent, int damage) =>
        Assert.Equal(damage, ZoneSchedule.DamageOf(maxHealth, percent));

    [Fact]
    public void Damage_of_a_negative_health_or_share_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoneSchedule.DamageOf(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoneSchedule.DamageOf(100, -1));
    }

    [Fact]
    public void Ring_zero_on_the_riverland_is_the_playable_squares_circle()
    {
        ZoneRing outer = ZoneSchedule.Outer(RiverlandFixture.Field);
        Assert.Equal(new Vector2(600f, 600f), outer.Centre);
        Assert.Equal(806.10f, outer.Radius, 0.01f);
    }

    [Fact]
    public void Ring_zero_on_the_sandbox_holds_its_playable_rectangle()
    {
        Battlefield field = MapCatalog.Sandbox.NewBattlefield();
        float low = field.Border, highX = field.Terrain.SizeX - field.Border, highZ = field.Terrain.SizeZ - field.Border;
        ZoneRing outer = ZoneSchedule.Outer(field);
        Assert.Equal(new Vector2((low + highX) / 2f, (low + highZ) / 2f), outer.Centre);
        Assert.Equal(new Vector2(highX - low, highZ - low).Length() / 2f, outer.Radius, 0.001f);
        foreach (Vector2 corner in new[] { new Vector2(low, low), new Vector2(highX, low), new Vector2(low, highZ), new Vector2(highX, highZ) })
            Assert.Equal(outer.Radius, Vector2.Distance(corner, outer.Centre), 0.01f);
    }

    [Fact]
    public void A_ring_contains_what_lies_within_its_radius()
    {
        var ring = new ZoneRing(new Vector2(100f, 100f), 10f);
        Assert.True(ring.Contains(new Vector2(110f, 100f)));
        Assert.False(ring.Contains(new Vector2(110.01f, 100f)));
        Assert.False(new ZoneRing(new Vector2(100f, 100f), 0f).Contains(new Vector2(100f, 100f)));
    }

    private static ZoneRing[] Nested() => new ZoneRing[]
    {
        new(new Vector2(600, 600), 806),
        new(new Vector2(500, 600), 420),
        new(new Vector2(450, 600), 250),
        new(new Vector2(450, 640), 140),
        new(new Vector2(470, 640), 60),
        new(new Vector2(470, 650), 0),
    };

    [Fact]
    public void Six_nested_rings_pass_the_check_as_a_copy()
    {
        ZoneRing[] rings = Nested();
        IReadOnlyList<ZoneRing> checkedRings = ZoneSchedule.Check(rings, "zone");
        Assert.Equal(rings, checkedRings);
        rings[1] = default;
        Assert.NotEqual(rings[1], checkedRings[1]);
    }

    [Fact]
    public void A_ring_touching_its_outer_ring_from_inside_passes()
    {
        ZoneRing[] rings = Nested();
        rings[5] = new ZoneRing(new Vector2(470 + 60, 640), 0);   // ring 4's edge
        ZoneSchedule.Check(rings, "zone");
    }

    [Fact]
    public void Too_few_too_many_or_no_rings_throw()
    {
        ZoneRing[] rings = Nested();
        AssertBad(rings[..5]);
        AssertBad(rings.Append(rings[5]).ToArray());
        ArgumentException e = Assert.ThrowsAny<ArgumentException>(() => ZoneSchedule.Check(null, "zone"));
        Assert.Equal("zone", e.ParamName);
    }

    [Fact]
    public void A_ring_reaching_out_of_the_one_before_throws()
    {
        ZoneRing[] rings = Nested();
        rings[2] = new ZoneRing(new Vector2(500 - 420 + 250 - 0.5f, 600), 250);   // 0.5 m out of ring 1 to the west
        AssertBad(rings);
    }

    [Fact]
    public void A_negative_or_not_finite_radius_or_centre_throws()
    {
        foreach (ZoneRing bad in new ZoneRing[]
                 {
                     new(new Vector2(470, 650), -1f),
                     new(new Vector2(470, 650), float.NaN),
                     new(new Vector2(float.NaN, 650), 0f),
                     new(new Vector2(470, float.PositiveInfinity), 0f),
                 })
        {
            ZoneRing[] rings = Nested();
            rings[5] = bad;
            AssertBad(rings);
        }
        ZoneRing[] outer = Nested();
        outer[0] = new ZoneRing(outer[0].Centre, float.PositiveInfinity);
        AssertBad(outer);
    }

    [Fact]
    public void The_check_allows_a_centimetre_of_slack_and_no_more()
    {
        ZoneRing[] rings = Nested();
        rings[2] = new ZoneRing(rings[1].Centre, rings[1].Radius + 0.01f);   // concentric, a centimetre over ring 1
        ZoneRing[] bigger = Nested();
        bigger[2] = bigger[1] with { Radius = bigger[1].Radius + 0.02f };   // two centimetres over
        AssertBad(Shrunk(bigger));
        ZoneSchedule.Check(Shrunk(rings), "zone");

        ZoneRing[] offset = Nested();   // ring 2 10 m east of ring 1's centre: 10 + r2 against 420 + 0.01
        offset[2] = new ZoneRing(offset[1].Centre + new Vector2(10, 0), 410.009f);
        ZoneSchedule.Check(Shrunk(offset), "zone");
        offset[2] = offset[2] with { Radius = 410.011f };
        AssertBad(Shrunk(offset));
    }

    [Fact]
    public void A_ring_closed_before_ring_five_passes_if_the_rest_are_points_on_its_centre()
    {
        ZoneRing[] rings = Nested();
        rings[3] = new ZoneRing(new Vector2(450, 640), 0f);
        rings[4] = new ZoneRing(new Vector2(450, 640), 0f);
        rings[5] = new ZoneRing(new Vector2(450, 640.005f), 0f);   // within the slack
        ZoneSchedule.Check(rings, "zone");

        rings[4] = new ZoneRing(new Vector2(450, 640), 0.5f);   // grows out of a closed ring 3
        AssertBad(rings);
    }

    [Fact]
    public void The_check_returns_an_array_of_its_own()
    {
        ZoneRing[] rings = Nested();
        ZoneRing[] copy = ZoneSchedule.Check(rings, "zone");
        Assert.NotSame(rings, copy);
        Assert.Equal(rings, copy);
    }

    /// <summary><paramref name="rings"/> with rings 3–5 put on ring 2's centre at their scheduled radii, so only ring 2 against ring 1 is in question.</summary>
    private static ZoneRing[] Shrunk(ZoneRing[] rings)
    {
        Vector2 c = rings[2].Centre;
        rings[3] = new ZoneRing(c, 140);
        rings[4] = new ZoneRing(c, 60);
        rings[5] = new ZoneRing(c, 0);
        return rings;
    }

    private static void AssertBad(ZoneRing[] rings)
    {
        ArgumentException e = Assert.Throws<ArgumentException>(() => ZoneSchedule.Check(rings, "zone"));
        Assert.Equal("zone", e.ParamName);
    }
}

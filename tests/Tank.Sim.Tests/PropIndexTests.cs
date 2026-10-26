using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;

namespace Tank.Sim.Tests;

/// <summary>The uniform-grid prop index behind <see cref="Battlefield"/>'s queries (riverland design §4.1).</summary>
public sealed class PropIndexTests
{
    private static readonly PropSet[] Sets = Enum.GetValues<PropSet>();

    /// <summary>
    /// 300 m × 300 m of flat ground: 150 rocks, 250 bushes, 150 wall segments, 50 fence panels, 200 trees and 25 houses,
    /// some hanging over the edge.
    /// </summary>
    private static Battlefield Scattered() =>
        ScatteredField.Build(seed: 7, rocks: 150, bushes: 250, walls: 150, fences: 50, minSegments: 1, maxSegments: 1, trees: 200, houses: 25);

    [Fact]
    public void Near_returns_ascending_distinct_indices_covering_everything_in_reach()
    {
        Battlefield field = Scattered();
        var rng = new XorShift(13);
        var buffer = new int[512];
        long found = 0, everything = 0;
        for (int q = 0; q < 2000; q++)
        {
            var center = new Vector2(Between(ref rng, -50, ScatteredField.Size + 50), Between(ref rng, -50, ScatteredField.Size + 50));
            float radius = Between(ref rng, 0, 30);
            foreach (PropSet set in Sets)
            {
                int[] near = field.Near(set, center, radius, buffer).ToArray();
                int[] all = field.All(set).ToArray();
                AssertAscendingSubsetOf(all, near);
                foreach (int i in all)
                {
                    Prop p = field.Props[i];
                    if (Vector2.Distance(p.Footprint.Center, center) <= radius + Reach(set, p))
                        Assert.True(near.Contains(i), $"{set}: prop {p.Id} reaches the disc at {center} r {radius} but was not returned");
                }
                found += near.Length;
                everything += all.Length;
            }
        }
        Assert.True(found * 10 < everything, $"the index kept {found} of {everything} candidates: it hardly narrows anything down");
    }

    [Fact]
    public void Along_covers_every_prop_whose_circle_reaches_the_widened_segment()
    {
        Battlefield field = Scattered();
        var rng = new XorShift(17);
        var buffer = new int[512];
        long found = 0, everything = 0;
        for (int q = 0; q < 2000; q++)
        {
            (Vector2 start, Vector2 end) = Segment(ref rng, q);
            float margin = Between(ref rng, 0, 3);
            foreach (PropSet set in Sets)
            {
                int[] along = field.Along(set, start, end, margin, buffer).ToArray();
                int[] all = field.All(set).ToArray();
                AssertAscendingSubsetOf(all, along);
                foreach (int i in all)
                {
                    Prop p = field.Props[i];
                    if (DistanceToSegment(p.Footprint.Center, start, end) <= margin + Reach(set, p))
                        Assert.True(along.Contains(i), $"{set}: prop {p.Id} reaches {start}→{end} (margin {margin}) but was not returned");
                }
                found += along.Length;
                everything += all.Length;
            }
        }
        Assert.True(found * 3 < everything, $"the index kept {found} of {everything} candidates: it hardly narrows anything down");
    }

    [Fact]
    public void Every_prop_is_found_from_the_rim_of_its_registered_circle()
    {
        // A point just inside the circle, straight across or along from its centre, lies at the edge of the square the
        // prop is registered over: a circle registered a little too small (a house's rubble spread one way only, say)
        // misses the cells there whenever a cell line runs between.
        Battlefield field = Scattered();
        var buffer = new int[512];
        var directions = new[] { Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY };
        foreach (PropSet set in Sets)
        foreach (int i in field.All(set).ToArray())
        {
            Prop p = field.Props[i];
            foreach (Vector2 direction in directions)
            {
                Vector2 rim = p.Footprint.Center + direction * (Reach(set, p) * 0.999f);
                Assert.True(field.Near(set, rim, 0f, buffer).Contains(i), $"{set}: {p.Kind} {p.Id} not found from {rim}");
                Assert.True(field.Along(set, rim, rim, 0f, buffer).Contains(i), $"{set}: {p.Kind} {p.Id} not found along {rim}");
            }
        }
    }

    [Fact]
    public void A_buffer_too_small_falls_back_to_the_whole_set()
    {
        Battlefield field = Scattered();
        Span<int> one = stackalloc int[1], enough = stackalloc int[512];
        var middle = new Vector2(150, 150);
        foreach (PropSet set in Sets)
        {
            Assert.True(field.Near(set, middle, 40f, enough).Length >= 2, $"{set}: the test wants at least two candidates");
            Assert.Equal(field.All(set).ToArray(), field.Near(set, middle, 40f, one).ToArray());
            Assert.Equal(field.All(set).ToArray(), field.Along(set, middle, middle + new Vector2(80, 30), 2f, one).ToArray());
        }
    }

    [Fact]
    public void With_the_index_off_queries_return_the_whole_set()
    {
        Battlefield field = Scattered();
        Assert.True(field.UseIndex);
        field.UseIndex = false;
        var corner = new Vector2(5, 5);
        Span<int> buffer = stackalloc int[512];
        foreach (PropSet set in Sets)
        {
            Assert.Equal(field.All(set).ToArray(), field.Near(set, corner, 1f, buffer).ToArray());
            Assert.Equal(field.All(set).ToArray(), field.Along(set, corner, corner + Vector2.UnitX, 0f, buffer).ToArray());
        }
    }

    [Fact]
    public void Each_set_holds_the_props_its_kind_says()
    {
        Battlefield field = Scattered();
        Prop[] houses = field.Props.Where(p => p.Kind == PropKind.House).ToArray();
        Assert.Equal(25, houses.Length);
        Assert.Contains(houses, h => !Inside(h.Footprint));   // some hang over the edge, into the edge cells
        foreach (PropSet set in Sets)
        {
            int[] expected = Enumerable.Range(0, field.Props.Count).Where(i => Holds(set, field.Props[i])).ToArray();
            Assert.NotEmpty(expected);
            Assert.Equal(expected, field.All(set).ToArray());
        }
    }

    [Fact]
    public void Raycast_agrees_with_and_without_the_index_on_the_sandbox()
    {
        Battlefield field = SandboxMap.Create();
        var rng = new XorShift(11);
        int propHits = 0;
        for (int q = 0; q < 5000; q++)
        {
            var from = new Vector3(Between(ref rng, 0, field.Terrain.SizeX), 0, Between(ref rng, 0, field.Terrain.SizeZ));
            from.Y = field.Terrain.HeightAt(from.X, from.Z) + Between(ref rng, 0, 20);
            float angle = Between(ref rng, 0, Angles.TwoPi), length = Between(ref rng, 0, 300);
            var to = from + new Vector3(MathF.Cos(angle) * length, 0, MathF.Sin(angle) * length);
            to.Y = field.Terrain.HeightAt(to.X, to.Z) + Between(ref rng, 0, 20);

            field.UseIndex = true;
            bool indexed = field.Raycast(from, to, out RayHit a);
            field.UseIndex = false;
            bool scanned = field.Raycast(from, to, out RayHit b);

            Assert.Equal((scanned, BitConverter.SingleToUInt32Bits(b.T), b.PropId), (indexed, BitConverter.SingleToUInt32Bits(a.T), a.PropId));
            if (indexed && a.PropId != RayHit.TerrainId) propHits++;
        }
        Assert.True(propHits > 100, $"only {propHits} rays hit a prop");
    }

    [Fact]
    public void Queries_allocate_nothing()
    {
        Allocation.AssertNone(() =>
        {
            Battlefield field = Scattered();
            Span<int> buffer = stackalloc int[512];
            var rng = new XorShift(23);
            int sum = Query(field, ref rng, buffer, 20);   // warm up
            Allocation.Window window = Allocation.Start();
            sum += Query(field, ref rng, buffer, 1000);
            Allocation.Measured measured = window.Stop();
            Assert.True(sum > 0);
            return measured;
        });
    }

    private static int Query(Battlefield field, ref XorShift rng, Span<int> buffer, int count)
    {
        int sum = 0;
        for (int q = 0; q < count; q++)
        {
            var a = new Vector2(Between(ref rng, 0, ScatteredField.Size), Between(ref rng, 0, ScatteredField.Size));
            var b = new Vector2(Between(ref rng, 0, ScatteredField.Size), Between(ref rng, 0, ScatteredField.Size));
            sum += field.Near(PropSet.Blockers, a, 10f, buffer).Length;
            sum += field.Along(PropSet.ShellStoppers, a, b, 0.5f, buffer).Length;
            if (field.Raycast(new Vector3(a.X, 1.5f, a.Y), new Vector3(b.X, 1.5f, b.Y), out _)) sum++;
        }
        return sum;
    }

    /// <summary>
    /// Random segments over the map and 50 m beyond it: every tenth of zero length, every tenth level on a grid line
    /// (z = 8k), every tenth upright on one (x = 8k), the rest anywhere, up to 400 m long.
    /// </summary>
    private static (Vector2 Start, Vector2 End) Segment(ref XorShift rng, int q)
    {
        var start = new Vector2(Between(ref rng, -50, ScatteredField.Size + 50), Between(ref rng, -50, ScatteredField.Size + 50));
        float length = Between(ref rng, 0, 400), angle = Between(ref rng, 0, Angles.TwoPi);
        float line = Battlefield.IndexCellSize * (int)Between(ref rng, 0, ScatteredField.Size / Battlefield.IndexCellSize + 1);
        return (q % 10) switch
        {
            0 => (start, start),
            1 => (start with { Y = line }, new Vector2(start.X + length - 200, line)),
            2 => (start with { X = line }, new Vector2(line, start.Y + length - 200)),
            _ => (start, start + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length),
        };
    }

    private static void AssertAscendingSubsetOf(int[] all, int[] found)
    {
        for (int k = 1; k < found.Length; k++) Assert.True(found[k - 1] < found[k], $"not strictly ascending at {k}: {found[k - 1]}, {found[k]}");
        var members = new HashSet<int>(all);
        Assert.All(found, i => Assert.Contains(i, members));
    }

    /// <summary>
    /// The radius each set registers a prop by, around its footprint's centre (riverland design §4.1): for walls the
    /// rubble's circumcircle, the rubble spreading across a wall and both ways round a house.
    /// </summary>
    private static float Reach(PropSet set, in Prop p) => set switch
    {
        PropSet.Walls => (p.Footprint.HalfExtents + new Vector2(p.Kind == PropKind.House ? Prop.RubbleSpread : 0f, Prop.RubbleSpread)).Length(),
        PropSet.Foliage => p.FoliageRadius,
        _ => p.Footprint.HalfExtents.Length(),
    };

    private static bool Holds(PropSet set, in Prop p) => set switch
    {
        PropSet.Blockers => p.IsSolid,
        PropSet.ShellStoppers => p.StopsShells,
        PropSet.Flimsy => p.IsFlimsy,
        PropSet.Walls => p.IsWall,
        PropSet.Foliage => p.Kind is PropKind.Bush or PropKind.Tree,
        _ => throw new ArgumentOutOfRangeException(nameof(set)),
    };

    /// <summary>Whether all of <paramref name="footprint"/>'s circumcircle lies on the map.</summary>
    private static bool Inside(in Obb2 footprint)
    {
        float r = footprint.HalfExtents.Length();
        Vector2 c = footprint.Center;
        return c.X - r >= 0 && c.Y - r >= 0 && c.X + r <= ScatteredField.Size && c.Y + r <= ScatteredField.Size;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    private static float Between(ref XorShift rng, float min, float max) => min + rng.NextFloat() * (max - min);
}

/// <summary>Flat 300 m × 300 m ground with props scattered at random over it (and a little beyond its edges).</summary>
internal static class ScatteredField
{
    public const int Samples = 151;
    public const float Cell = 2f;
    public const float Size = (Samples - 1) * Cell;
    private const float Overhang = 10f;

    /// <summary>
    /// <paramref name="rocks"/> rocks (half extents 1–5 m, any heading), <paramref name="bushes"/> bushes (radius 2–4.6 m),
    /// <paramref name="walls"/> brick or stone wall lines and <paramref name="fences"/> fence lines of
    /// <paramref name="minSegments"/>–<paramref name="maxSegments"/> 2 m segments, <paramref name="trees"/> trees and
    /// <paramref name="houses"/> houses, numbered from 1 in that order.
    /// </summary>
    public static Battlefield Build(uint seed, int rocks, int bushes, int walls, int fences, int minSegments, int maxSegments, int trees = 0, int houses = 0)
    {
        var terrain = new HeightField(Samples, Samples, Cell, new float[Samples * Samples]);
        var rng = new XorShift(seed);
        var props = new List<Prop>();
        for (int k = 0; k < rocks; k++)
        {
            var half = new Vector2(Between(ref rng, 1, 5), Between(ref rng, 1, 5));
            props.Add(new Prop(props.Count + 1, PropKind.Rock, new Obb2(Anywhere(ref rng), half, Between(ref rng, 0, Angles.TwoPi)), -0.3f, Between(ref rng, 2, 4)));
        }
        for (int k = 0; k < bushes; k++)
        {
            float radius = Between(ref rng, 2, 4.6f);
            props.Add(new Prop(props.Count + 1, PropKind.Bush, new Obb2(Anywhere(ref rng), new Vector2(radius, radius), 0f), -0.3f, 1.8f));
        }
        for (int k = 0; k < walls + fences; k++)
        {
            PropKind kind = k >= walls ? PropKind.Fence : rng.NextUInt() % 2 == 0 ? PropKind.BrickWall : PropKind.StoneWall;
            int segments = minSegments + (int)(rng.NextUInt() % (uint)(maxSegments - minSegments + 1));
            Vector2 start = Anywhere(ref rng);
            float angle = Between(ref rng, 0, Angles.TwoPi);
            var line = new WallLine(kind, start, start + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (segments * WallLine.SegmentLength));
            props.AddRange(line.Segments(terrain, props.Count + 1));
        }
        for (int k = 0; k < trees; k++) props.Add(Tree(props.Count + 1, Anywhere(ref rng), ref rng));
        for (int k = 0; k < houses; k++) props.Add(House(props.Count + 1, Anywhere(ref rng), ref rng));
        return new Battlefield(terrain, props, border: 10f);
    }

    /// <summary>
    /// A forest over the whole field: <paramref name="trees"/> trees on a jittered square grid (as many rows as columns,
    /// just enough of them), then <paramref name="bushes"/> bushes and <paramref name="rocks"/> rocks anywhere and
    /// <paramref name="houses"/> houses within <see cref="Hamlet"/> of the middle, numbered from 1 in that order.
    /// </summary>
    public static Battlefield Forest(uint seed, int trees, int bushes, int rocks, int houses = 0)
    {
        var terrain = new HeightField(Samples, Samples, Cell, new float[Samples * Samples]);
        var rng = new XorShift(seed);
        var props = new List<Prop>();
        int columns = (int)MathF.Ceiling(MathF.Sqrt(trees));
        float pitch = Size / columns, jitter = pitch / 4;
        for (int k = 0; k < trees; k++)
        {
            var cell = new Vector2((k % columns + 0.5f) * pitch, (k / columns + 0.5f) * pitch);
            var at = cell + new Vector2(Between(ref rng, -jitter, jitter), Between(ref rng, -jitter, jitter));
            props.Add(Tree(props.Count + 1, at, ref rng));
        }
        for (int k = 0; k < bushes; k++)
        {
            float radius = Between(ref rng, 2, 4.6f);
            props.Add(new Prop(props.Count + 1, PropKind.Bush, new Obb2(Anywhere(ref rng), new Vector2(radius, radius), 0f), -0.3f, 1.8f));
        }
        for (int k = 0; k < rocks; k++)
        {
            var half = new Vector2(Between(ref rng, 1, 5), Between(ref rng, 1, 5));
            props.Add(new Prop(props.Count + 1, PropKind.Rock, new Obb2(Anywhere(ref rng), half, Between(ref rng, 0, Angles.TwoPi)), -0.3f, Between(ref rng, 2, 4)));
        }
        for (int k = 0; k < houses; k++)
        {
            var at = new Vector2(Size / 2 + Between(ref rng, -Hamlet, Hamlet), Size / 2 + Between(ref rng, -Hamlet, Hamlet));
            props.Add(House(props.Count + 1, at, ref rng));
        }
        return new Battlefield(terrain, props, border: 10f);
    }

    /// <summary>How far, along either axis, a <see cref="Forest"/>'s houses stand from the middle, m.</summary>
    public const float Hamlet = 50f;

    /// <summary>A tree at <paramref name="at"/>: trunk 0.35–0.6 m, 9–15 m tall.</summary>
    private static Prop Tree(int id, Vector2 at, ref XorShift rng)
    {
        float trunk = Between(ref rng, 0.35f, 0.6f);
        return new Prop(id, PropKind.Tree, new Obb2(at, new Vector2(trunk, trunk), 0f), -0.3f, Between(ref rng, 9, 15) + 0.3f);
    }

    /// <summary>A house at <paramref name="at"/>: 8–18 m along its ridge, 6–12 m across, any heading, 6–9 m to the ridge.</summary>
    private static Prop House(int id, Vector2 at, ref XorShift rng)
    {
        var half = new Vector2(Between(ref rng, 4, 9), Between(ref rng, 3, 6));
        float yaw = Between(ref rng, 0, Angles.TwoPi);
        return new Prop(id, PropKind.House, new Obb2(at, half, yaw), -WallLine.Sink, Between(ref rng, 6, 9) + WallLine.Sink);
    }

    private static Vector2 Anywhere(ref XorShift rng) =>
        new(Between(ref rng, -Overhang, Size + Overhang), Between(ref rng, -Overhang, Size + Overhang));

    private static float Between(ref XorShift rng, float min, float max) => min + rng.NextFloat() * (max - min);
}

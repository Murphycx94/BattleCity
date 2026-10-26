using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Maps.Riverland;
using Xunit.Abstractions;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Tests;

/// <summary>The riverland's rocks, bush patches and trees, and the order props are numbered in (riverland design §3.7–3.9, §3.12).</summary>
public sealed class RiverlandScatterTests
{
    /// <summary>BattleMessages.MaxMapProps (Tank.Net): the most props a map may have on the wire.</summary>
    private const int MaxMapProps = 16384;
    private const float PlotMargin = 3f, Sink = 0.3f;
    /// <summary>A patch's bushes lie within 9 m of its first; patches are at least 40 m apart, so 20 m tells them apart.</summary>
    private const float SamePatch = 20f;

    private readonly ITestOutputHelper _output;

    public RiverlandScatterTests(ITestOutputHelper output) => _output = output;

    private static Battlefield Field => RiverlandFixture.Field;

    private static List<Prop> Of(PropKind kind) => Field.Props.Where(p => p.Kind == kind).ToList();

    [Fact]
    public void Prop_ids_run_in_kind_order()
    {
        IReadOnlyList<Prop> props = Field.Props;
        int previous = 0;
        for (int k = 0; k < props.Count; k++)
        {
            Assert.Equal(k + 1, props[k].Id);
            int rank = Rank(props[k].Kind);
            Assert.True(rank >= previous, $"prop {props[k].Id} is a {props[k].Kind} after a later kind");
            previous = rank;
        }
        Assert.Equal(4, previous);

        static int Rank(PropKind kind) => kind switch
        {
            PropKind.Rock => 0,
            PropKind.Bush => 1,
            PropKind.Tree => 2,
            PropKind.House => 3,
            _ => 4,
        };
    }

    [Fact]
    public void Counts_are_in_the_designed_ranges()
    {
        int rocks = Of(PropKind.Rock).Count, bushes = Of(PropKind.Bush).Count, trees = Of(PropKind.Tree).Count, houses = Of(PropKind.House).Count;
        _output.WriteLine($"{rocks} rocks, {bushes} bushes, {trees} trees, {houses} houses, {Field.Props.Count} props in all");
        Assert.InRange(rocks, 500, 700);
        Assert.InRange(bushes, 800, 1300);
        // The design's "about 5000 trees" assumed 49 m² a tree (7 m squared); Bridson's Poisson disc at its 6 m minimum
        // spacing fills about 60 m² a tree, and the forest edges thin out to 12 m: about 3500 is what that spacing gives.
        Assert.InRange(trees, 3000, 4500);
        Assert.InRange(houses, 55, 60);
        Assert.True(Field.Props.Count <= MaxMapProps, $"{Field.Props.Count} props");
    }

    [Fact]
    public void Trees_have_designed_sizes()
    {
        foreach (Prop tree in Of(PropKind.Tree))
        {
            Obb2 f = tree.Footprint;
            Assert.True(f.HalfExtents.X == f.HalfExtents.Y, $"tree {tree.Id}'s footprint is {f.HalfExtents}");
            Assert.InRange(f.HalfExtents.X, 0.35f, 0.6f);
            Assert.InRange(tree.Height - Sink, 9f, 15f);
            float ground = Field.Terrain.HeightAt(f.Center.X, f.Center.Y);
            Assert.True(MathF.Abs(tree.Bottom + Sink - ground) < 0.01f, $"tree {tree.Id} stands at {tree.Bottom + Sink:F2} m on ground at {ground:F2} m");
        }
    }

    [Fact]
    public void Forest_trees_are_at_least_6_metres_apart()
    {
        List<Prop> trees = Of(PropKind.Tree);
        List<Vector2> inRects = trees.Select(t => t.Footprint.Center).Where(p => L.Forests.Any(f => f.Contains(p))).OrderBy(p => p.X).ToList();
        for (int a = 0; a < inRects.Count; a++)
        for (int b = a + 1; b < inRects.Count && inRects[b].X - inRects[a].X < 6; b++)
        {
            float d = Vector2.Distance(inRects[a], inRects[b]);
            Assert.True(d >= 6 - 1e-3f, $"trees at {inRects[a]} and {inRects[b]} are {d:F2} m apart");
        }

        int inForests = trees.Count(t => L.InForest(t.Footprint.Center) || L.Forests.Any(f => f.Contains(t.Footprint.Center)));
        _output.WriteLine($"{inForests} of {trees.Count} trees in the forests, {inRects.Count} inside the rectangles");
        Assert.True(inForests >= 0.8f * trees.Count, $"only {inForests} of {trees.Count} trees are in the forests");
    }

    [Fact]
    public void Nothing_grows_on_roads_in_water_or_on_plots()
    {
        List<Obb2> plots = Of(PropKind.House).Select(h => h.Footprint with { HalfExtents = h.Footprint.HalfExtents + new Vector2(PlotMargin) }).ToList();
        foreach (Prop p in Field.Props.Where(p => p.Kind is PropKind.Rock or PropKind.Bush or PropKind.Tree))
        {
            Vector2 c = p.Footprint.Center;
            Assert.True(Field.WaterDepthAt(c) <= 0, $"{p.Kind} {p.Id} at {c} stands in water");
            foreach (Obb2 plot in plots)
            {
                Assert.False(plot.Contains(c), $"{p.Kind} {p.Id} at {c} stands on the plot at {plot.Center}");
                if (p.Kind == PropKind.Rock) Assert.False(Obb2.TryGetPenetration(p.Footprint, plot, out _), $"rock {p.Id} at {c} reaches onto the plot at {plot.Center}");
            }
            switch (p.Kind)
            {
                case PropKind.Bush:
                    float centre = L.AllRoads.Min(r => r.Line.DistanceTo(c, out _));
                    Assert.True(centre >= 6, $"bush {p.Id} at {c} is {centre:F2} m from a road's centre line");
                    break;
                case PropKind.Tree:
                    float trunk = RoadEdgeDistance(c) - p.Footprint.HalfExtents.X;
                    Assert.True(trunk >= 3, $"tree {p.Id} at {c} is {trunk:F2} m from a road's edge");
                    break;
                default:
                    float rock = RoadEdgeDistance(p.Footprint);
                    Assert.True(rock >= 3, $"rock {p.Id} at {c} is {rock:F2} m from a road's edge");
                    break;
            }
        }
    }

    [Fact]
    public void Trees_avoid_steep_ground_and_rocks()
    {
        List<Prop> rocks = Of(PropKind.Rock);
        foreach (Prop tree in Of(PropKind.Tree))
        {
            Vector2 c = tree.Footprint.Center;
            float r = tree.Footprint.HalfExtents.X;
            float slope = MathF.Acos(Field.Terrain.NormalAt(c.X, c.Y).Y) * 180 / MathF.PI;
            Assert.True(slope <= 30, $"tree {tree.Id} at {c} stands on a {slope:F1}° slope");
            foreach (Prop rock in rocks)
            {
                if (Vector2.Distance(c, rock.Footprint.Center) > r + rock.Footprint.HalfExtents.Length()) continue;
                Assert.True(rock.Footprint.DistanceToPoint(c) > r, $"tree {tree.Id} at {c} grows into rock {rock.Id}");
            }
        }
    }

    [Fact]
    public void Bush_patches_are_40_metres_apart_on_flat_ground()
    {
        List<List<Prop>> patches = Patches();
        _output.WriteLine($"{patches.Count} patches of {patches.Min(p => p.Count)}–{patches.Max(p => p.Count)} bushes");
        Assert.InRange(patches.Count, 60, 80);
        var centres = patches.Select(p => p[0].Footprint.Center).ToList();
        for (int a = 0; a < centres.Count; a++)
        for (int b = a + 1; b < centres.Count; b++)
            Assert.True(Vector2.Distance(centres[a], centres[b]) >= 40, $"patches at {centres[a]} and {centres[b]} are too close");

        foreach (List<Prop> patch in patches)
        {
            Vector2 centre = patch[0].Footprint.Center;
            Assert.InRange(patch.Count, 12, 18);
            Assert.True(patch[0].Footprint.HalfExtents.X >= 3.8f, $"patch {centre}: its centre bush is only {patch[0].Footprint.HalfExtents.X:F2} m");
            foreach (Prop bush in patch)
            {
                Assert.InRange(bush.Footprint.HalfExtents.X, 3.2f, 4.6f);
                Assert.InRange(bush.Height - Sink, 4.2f, 5.2f);
                Assert.True(Vector2.Distance(bush.Footprint.Center, centre) <= 9 + 1e-3f, $"bush {bush.Id} strays from its patch at {centre}");
            }
            for (float dx = -13; dx <= 13; dx += 1f)
            for (float dz = -13; dz <= 13; dz += 1f)
            {
                if (dx * dx + dz * dz > 13 * 13) continue;
                float slope = MathF.Acos(Field.Terrain.NormalAt(centre.X + dx, centre.Y + dz).Y) * 180f / MathF.PI;
                Assert.True(slope < 10f, $"patch {centre}: {slope:F1}° at offset ({dx}, {dz})");
            }
        }
    }

    [Fact]
    public void Rocks_pile_where_the_layout_says()
    {
        List<Prop> rocks = Of(PropKind.Rock);
        int cliff = rocks.Count(r => L.PlateauEdgeDistance(r.Footprint.Center) is >= -L.CliffWidth - 30 and <= 30);
        int dunes = rocks.Count(r => L.Dunes.Contains(r.Footprint.Center));
        int hills = rocks.Count(r => OnAHill(r.Footprint.Center));
        int plain = rocks.Count(r => OnThePlain(r.Footprint.Center));
        _output.WriteLine($"{rocks.Count} rocks: {cliff} by the plateau's cliff, {dunes} in the dunes, {hills} on the hills, {plain} on the plain");
        Assert.True(cliff >= 150, $"only {cliff} rocks within 30 m of the plateau's cliff");
        Assert.True(dunes >= 60, $"only {dunes} rocks in the dunes");
        Assert.True(hills >= 70, $"only {hills} rocks on the hills and knolls");
        Assert.True(plain >= 150, $"only {plain} rocks in piles on the plain");
    }

    [Fact]
    public void Lone_trees_number_300_to_500()
    {
        // Forest trees grow at most ForestJitter (25 m) outside their rectangle, lone ones at least 6 m farther out.
        int lone = Of(PropKind.Tree).Count(t => L.Forests.All(f => f.SignedDistance(t.Footprint.Center) > L.ForestJitter + 6));
        _output.WriteLine($"{lone} lone trees");
        Assert.InRange(lone, 300, 500);
    }

    [Fact]
    public void Bush_patches_lie_at_most_20_metres_into_a_forest()
    {
        foreach (List<Prop> patch in Patches())
        {
            Vector2 centre = patch[0].Footprint.Center;
            Assert.True(L.ForestDepth(centre) <= 20, $"the patch at {centre} is {L.ForestDepth(centre):F1} m into a forest");
        }
    }

    [Fact]
    public void Riverland_battle_is_identical_with_and_without_the_index()
    {
        Vector2 ford = L.Fords[0].Centre;
        L.River.DistanceTo(ford, out float along);
        Vector2 downstream = L.River.TangentAt(along), across = new(-downstream.Y, downstream.X);
        var anchors = new (Vector2 At, Vector2 Facing)[]
        {
            (L.Town - new Vector2(L.TownRadius + 10, 0), L.Town),                 // the town's west end
            (new Vector2(L.Forests[0].Min.X - 20, 620), L.Town),                  // the east forest's edge
            (ford - across * 45, ford), (ford + across * 45, ford),              // both banks at the ford
        };
        var spawns = new List<(Vector2 Position, float Yaw)>();
        foreach ((Vector2 at, Vector2 facing) in anchors)
        {
            Vector2 first = OpenSpot(at, Array.Empty<Vector2>());
            Vector2 second = OpenSpot(at, new[] { first });
            spawns.Add((first, Angles.YawOf(facing - first)));
            spawns.Add((second, Angles.YawOf(facing - second)));
        }
        _output.WriteLine(string.Join(", ", spawns.Select(s => s.Position)));

        var (shots, propsDown, _, _, _) = PropIndexEquivalenceTests.RunTwice(() => Field.Fresh(), spawns, ticks: 900);
        _output.WriteLine($"{shots} shots, {propsDown} props down");
        Assert.True(shots > 30 && propsDown > 5, $"a quiet battle: {shots} shots, {propsDown} props down");
    }

    // ---- helpers ----

    /// <summary>Within a hill's or knoll's radius.</summary>
    private static bool OnAHill(Vector2 p) => L.Hills.Any(h => Vector2.Distance(p, new Vector2(h.X, h.Z)) <= h.Radius);

    /// <summary>
    /// Where only the piles put rocks: more than 30 m out from the plateau's cliff, away from the ramps' sides (their rocks
    /// lie up to 25 m from the centre line), off the hills and knolls, outside the dunes.
    /// </summary>
    private static bool OnThePlain(Vector2 p) =>
        L.PlateauEdgeDistance(p) > 30 && !OnAHill(p) && !L.Dunes.Contains(p) && !L.Ramps.Any(ramp =>
        {
            (float along, float across) = ramp.Locate(p);
            return across <= 26 && along >= -1 && along <= ramp.Length + 1;
        });

    /// <summary>The bushes grouped by patch, in id order: a bush far from the current patch's first starts the next.</summary>
    private static List<List<Prop>> Patches()
    {
        var patches = new List<List<Prop>>();
        foreach (Prop bush in Of(PropKind.Bush))
        {
            if (patches.Count == 0 || Vector2.Distance(patches[^1][0].Footprint.Center, bush.Footprint.Center) > SamePatch) patches.Add(new List<Prop>());
            patches[^1].Add(bush);
        }
        return patches;
    }

    /// <summary>
    /// The nearest point to <paramref name="at"/> (searched outwards every 4 m) on dry, gentle ground 10 m clear of every
    /// prop's footprint and 12 m from <paramref name="taken"/>.
    /// </summary>
    private static Vector2 OpenSpot(Vector2 at, Vector2[] taken)
    {
        for (float ring = 0; ring <= 120; ring += 4)
        for (float dx = -ring; dx <= ring; dx += 4)
        for (float dz = -ring; dz <= ring; dz += 4)
        {
            if (MathF.Max(MathF.Abs(dx), MathF.Abs(dz)) < ring) continue;
            var p = at + new Vector2(dx, dz);
            if (Field.WaterDepthAt(p) > 0 || Field.Terrain.NormalAt(p.X, p.Y).Y < MathF.Cos(15 * MathF.PI / 180)) continue;
            if (taken.Any(t => Vector2.Distance(t, p) < 12)) continue;
            if (Field.Props.Any(q => Vector2.Distance(q.Footprint.Center, p) < 10 + q.Footprint.HalfExtents.Length() && q.Footprint.DistanceToPoint(p) < 10)) continue;
            return p;
        }
        throw new InvalidOperationException($"no open spot near {at}");
    }

    /// <summary>How far <paramref name="p"/> is from the edge of the nearest road (4 m half width) or track (3 m).</summary>
    private static float RoadEdgeDistance(Vector2 p) =>
        L.AllRoads.Min(road => road.Line.DistanceTo(p, out _) - (road.IsTrack ? L.TrackHalfWidth : L.RoadHalfWidth));

    /// <summary>How far the box comes to the edge of the nearest road or track, from its outline every 0.5 m.</summary>
    private static float RoadEdgeDistance(Obb2 box)
    {
        float centre = RoadEdgeDistance(box.Center), reach = box.HalfExtents.Length();
        if (centre - reach >= 3) return centre - reach;
        Vector2 r = box.AxisRight * box.HalfExtents.X, f = box.AxisForward * box.HalfExtents.Y;
        Vector2[] corners = { box.Center - r - f, box.Center + r - f, box.Center + r + f, box.Center - r + f };
        float best = float.PositiveInfinity;
        for (int k = 0; k < 4; k++)
        {
            Vector2 a = corners[k], b = corners[(k + 1) % 4];
            int steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / 0.5f));
            for (int s = 0; s < steps; s++) best = MathF.Min(best, RoadEdgeDistance(Vector2.Lerp(a, b, s / (float)steps)));
        }
        return best;
    }
}

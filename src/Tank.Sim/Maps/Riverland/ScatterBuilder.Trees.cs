using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps.Building;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Maps.Riverland;

/// <summary>The forests and the lone trees (riverland design §2.4, §3.7).</summary>
internal sealed partial class ScatterBuilder
{
    // ---- trees (§2.4, §3.7) ----
    private const float MinTrunk = 0.35f, MaxTrunk = 0.6f, MinTreeHeight = 9f, MaxTreeHeight = 15f, TreeSink = WallLine.Sink;
    private static readonly float TreeFlat = MathF.Cos(29.9f * MathF.PI / 180f);   // 30° at most
    /// <summary>
    /// In a forest trunks are at least ForestSpacing apart, thinning to EdgeSpacing over the outermost EdgeBand m (eased:
    /// the spacing grows with the square of the way out through the band, so the forest stays thick until near its edge).
    /// </summary>
    private const float ForestSpacing = 6f, EdgeSpacing = 12f, EdgeBand = 20f;
    /// <summary>About 400 lone trees: one, or a clump of 3–5 at least ClumpGap apart within ClumpRadius of the first.</summary>
    private const int LoneTreeTarget = 400, MaxLoneAttempts = 40000, MinClump = 3, MaxClump = 5;
    private const float SingleChance = 0.4f, ClumpGap = 3f, ClumpRadius = 7f, ClumpSpacing = 12f;
    /// <summary>A clump gets ClumpAttemptsPerTree draws for each tree it wants.</summary>
    private const int ClumpAttemptsPerTree = 8;
    /// <summary>Lone trees grow BankNear–BankFar m from the river's centre line, RoadNear–RoadFar m from a road's edge, or round the villages.</summary>
    private const float BankNear = 30f, BankFar = 60f, RoadNear = 4f, RoadFar = 15f, VillageFringe = 30f, FarmFringe = 25f;
    /// <summary>Lone trees keep this far outside the forests' rectangles grown by their jitter, clear of the forest trees.</summary>
    private const float ForestKeepOff = 6f;

    // ================= trees =================

    /// <summary>The five forests' trees, forest by forest, then about 400 lone trees by the river, the roads and the villages.</summary>
    public List<Prop> Trees()
    {
        var trees = new List<Prop>();
        var rng = new XorShift(L.ForestSeed);
        foreach (GroundRect forest in L.Forests)
        {
            GroundRect area = forest;
            Vector2 min = Vector2.Max(forest.Min - new Vector2(L.ForestJitter), new Vector2(Edge + 1));
            Vector2 max = Vector2.Min(forest.Max + new Vector2(L.ForestJitter), new Vector2(Size - Edge - 1));
            List<Vector2> points = PoissonDisc.Sample(ref rng, min, max,
                p => ForestSpacingAt(area, p),
                p => L.ForestDepth(area, p) > 0 && CanGrowTree(p, MaxTrunk));
            foreach (Vector2 p in points) trees.Add(Tree(ref rng, p));
        }
        LoneTrees(trees);
        return trees;
    }

    /// <summary>The trunks' least spacing at <paramref name="p"/> in <paramref name="forest"/>: 6 m deep inside, easing up to 12 m at the edge.</summary>
    private static float ForestSpacingAt(in GroundRect forest, Vector2 p)
    {
        float outwards = 1 - Math.Clamp(L.ForestDepth(forest, p) / EdgeBand, 0f, 1f);
        return ForestSpacing + (EdgeSpacing - ForestSpacing) * outwards * outwards;
    }

    /// <summary>Single trees and clumps of 3–5, each clump 12 m from the others, until about 400 stand.</summary>
    private void LoneTrees(List<Prop> trees)
    {
        var rng = new XorShift(L.LoneTreeSeed);
        int placed = 0;
        Vector2 min = new(Edge + 2), max = new(Size - Edge - 2);
        for (int attempt = 0; attempt < MaxLoneAttempts && placed < LoneTreeTarget; attempt++)
        {
            int region = (int)(rng.NextUInt() % 3);
            Vector2 first = Lerp(min, max, ref rng);
            if (!InLoneRegion(region, first) || NearForest(first) || _loneTrees.AnyWithin(first, 0, ClumpSpacing)) continue;
            int wanted = rng.NextFloat() < SingleChance ? 1 : MinClump + (int)(rng.NextUInt() % (MaxClump - MinClump + 1));
            int inClump = 0;
            for (int k = 0; k < wanted * ClumpAttemptsPerTree && inClump < wanted; k++)
            {
                Vector2 p = inClump == 0 ? first : first + InDisc(ref rng) * ClumpRadius;
                if (_loneTrees.AnyWithin(p, 0, ClumpGap) || NearForest(p) || !CanGrowTree(p, MaxTrunk))
                {
                    if (inClump == 0) break;
                    continue;
                }
                trees.Add(Tree(ref rng, p));
                _loneTrees.Add(p, 0);
                inClump++;
            }
            placed += inClump;
        }
    }

    /// <summary>Region 0: the river's banks; 1: the roads' and tracks' sides; 2: in and round the villages and farms.</summary>
    private bool InLoneRegion(int region, Vector2 p)
    {
        switch (region)
        {
            case 0:
                float river = _shape.River.DistanceAt(p.X, p.Y);
                return river is >= BankNear and <= BankFar;
            case 1:
                float road = RoadEdge(p);
                return road is >= RoadNear and <= RoadFar;
            default:
                foreach (Village village in L.Villages)
                    if (Vector2.Distance(p, village.Centre) <= village.Radius + VillageFringe) return true;
                foreach (Vector2 farm in L.Farms)
                    if (Vector2.Distance(p, farm) <= L.FarmRadius + FarmFringe) return true;
                return false;
        }
    }

    /// <summary>Within reach of a forest's trees: its rectangle grown by its jitter and ForestKeepOff.</summary>
    private static bool NearForest(Vector2 p)
    {
        foreach (GroundRect forest in L.Forests)
            if (forest.SignedDistance(p) < L.ForestJitter + ForestKeepOff) return true;
        return false;
    }

    /// <summary>A trunk of <paramref name="r"/> fits at <paramref name="p"/>: off roads, water, plots, rocks and bushes, on ground of 30° at most.</summary>
    private bool CanGrowTree(Vector2 p, float r)
    {
        if (!Inside(p, r + 1) || RoadEdge(p) - r < RoadClearance + GridSlack) return false;
        if (_terrain.NormalAt(p.X, p.Y).Y < TreeFlat) return false;
        if (_occupied.AnyWithin(p, r, PlotClearance) || _rocks.AnyWithin(p, r, 0.2f) || _bushes.AnyWithin(p, r, 0)) return false;
        return IsDry(p, new Vector2(r));
    }

    /// <summary>A tree at <paramref name="p"/>: trunk 0.35–0.6 m, 9–15 m tall, its base sunk 0.3 m (riverland design §2.4).</summary>
    private Prop Tree(ref XorShift rng, Vector2 p)
    {
        float trunk = MinTrunk + (MaxTrunk - MinTrunk) * rng.NextFloat();
        float height = MinTreeHeight + (MaxTreeHeight - MinTreeHeight) * rng.NextFloat();
        float ground = _terrain.HeightAt(p.X, p.Y);
        return new Prop(0, PropKind.Tree, new Obb2(p, new Vector2(trunk), 0f), ground - TreeSink, height + TreeSink);
    }
}

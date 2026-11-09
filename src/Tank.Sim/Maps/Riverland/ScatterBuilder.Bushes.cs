using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;
using S = Tank.Sim.Maps.SandboxMap;

namespace Tank.Sim.Maps.Riverland;

/// <summary>The bush patches (riverland design §3.8).</summary>
internal sealed partial class ScatterBuilder
{
    // ---- bushes (§3.8): bush sizes as the sandbox's ----
    private const int Patches = 70, MinBushes = 12, MaxBushes = 18;
    private const float PatchSpacing = 40f, PatchStep = 10f, PatchFlatRadius = 13f;
    private static readonly float PatchFlat = MathF.Cos(9.9f * MathF.PI / 180f);   // under 10° (the test's NormalAt, its 1 m grid)
    private static readonly float BushFlat = MathF.Cos(S.BushMaxSlope);
    /// <summary>A bush's centre stays this far from a road's or track's centre line.</summary>
    private const float BushRoadCentre = 6f;
    /// <summary>No bush deeper than this into a forest.</summary>
    private const float BushForestDepth = 20f;
    /// <summary>A patch centre stays this far from a road's or track's centre line, so the patch keeps off it.</summary>
    private const float PatchRoadCentre = 2 * BushRoadCentre;
    /// <summary>
    /// A patch centre's weight: 1, plus PatchBankWeight PatchBankNear–PatchBankFar m from the river's centre line,
    /// PatchLakeWeight PatchLakeNear–PatchLakeFar m outside the lake shore, PatchForestWeight inside or up to
    /// PatchForestReach m outside a forest's outline, PatchVillageWeight within PatchVillageFringe m outside a village,
    /// PatchFarmWeight within PatchFarmFringe m outside a farm, PatchHillWeight for each hill whose radius × PatchHillInner–
    /// PatchHillOuter it lies at (the slope's foot), PatchPlateauWeight PatchPlateauNear–PatchPlateauFar m outside the
    /// plateau's rim; × PatchDuneFactor in the dunes.
    /// </summary>
    private const float PatchBankNear = 30f, PatchBankFar = 80f, PatchBankWeight = 4f;
    private const float PatchLakeNear = 12f, PatchLakeFar = 45f, PatchLakeWeight = 3f;
    private const float PatchForestReach = 25f, PatchForestWeight = 4f;
    private const float PatchVillageFringe = 60f, PatchVillageWeight = 3f;
    private const float PatchFarmFringe = 40f, PatchFarmWeight = 2f;
    private const float PatchHillInner = 0.75f, PatchHillOuter = 1.25f, PatchHillWeight = 3f;
    private const float PatchPlateauNear = 10f, PatchPlateauFar = 60f, PatchPlateauWeight = 3f;
    private const float PatchDuneFactor = 0.3f;

    // ================= bushes =================

    /// <summary>
    /// About 70 patches of 12–18 bushes, their centres 40 m apart on flat ground, drawn by weight from a 10 m grid of
    /// candidates: banks, forest edges, village outskirts and the feet of slopes weigh most.
    /// </summary>
    public List<Prop> Bushes()
    {
        var rng = new XorShift(L.BushSeed);
        var candidates = new List<Vector2>();
        var weights = new List<float>();
        for (float z = Edge + 20; z <= Size - Edge - 20; z += PatchStep)
        for (float x = Edge + 20; x <= Size - Edge - 20; x += PatchStep)
        {
            var p = new Vector2(x, z) + (new Vector2(rng.NextFloat(), rng.NextFloat()) - new Vector2(0.5f)) * (PatchStep * 0.6f);
            float w = PatchWeight(p);
            if (w <= 0) continue;
            candidates.Add(p);
            weights.Add(w);
        }

        var bushes = new List<Prop>();
        var centres = new List<Vector2>();
        float total = weights.Sum();
        while (centres.Count < Patches && total > 0)
        {
            int pick = WeightedPick(weights, total, ref rng);
            if (pick < 0) break;   // rounding left a total with every weight spent
            Vector2 centre = candidates[pick];
            total -= weights[pick];
            weights[pick] = 0;
            if (centres.Any(c => Vector2.DistanceSquared(c, centre) < PatchSpacing * PatchSpacing) || !IsFlat(centre)) continue;
            List<Prop>? patch = PlantPatch(ref rng, centre);
            if (patch is null) continue;
            bushes.AddRange(patch);
            centres.Add(centre);
            foreach (Prop bush in patch) _bushes.Add(bush.Footprint.Center, bush.Footprint.HalfExtents.X);
            for (int k = 0; k < candidates.Count; k++)
                if (weights[k] > 0 && Vector2.DistanceSquared(candidates[k], centre) < PatchSpacing * PatchSpacing)
                {
                    total -= weights[k];
                    weights[k] = 0;
                }
        }
        return bushes;
    }

    /// <summary>How much a patch centre at <paramref name="p"/> is wanted: 0 where it can't go, more by banks, forest edges, villages and slopes.</summary>
    private float PatchWeight(Vector2 p)
    {
        if (RoadCentre(p) < PatchRoadCentre || _terrain.HeightAt(p.X, p.Y) < DryHeight) return 0;
        float forest = L.ForestDepth(p);
        if (forest > BushForestDepth - S.BushSpread) return 0;
        float w = 1;
        float river = _shape.River.DistanceAt(p.X, p.Y);
        if (river is >= PatchBankNear and <= PatchBankFar) w += PatchBankWeight;
        float lake = L.LakeShoreDistance(p);
        if (lake is >= PatchLakeNear and <= PatchLakeFar) w += PatchLakeWeight;
        if (forest >= -PatchForestReach) w += PatchForestWeight;
        foreach (Village village in L.Villages)
        {
            float d = Vector2.Distance(p, village.Centre);
            if (d >= village.Radius && d <= village.Radius + PatchVillageFringe) w += PatchVillageWeight;
        }
        foreach (Vector2 farm in L.Farms)
        {
            float d = Vector2.Distance(p, farm);
            if (d >= L.FarmRadius && d <= L.FarmRadius + PatchFarmFringe) w += PatchFarmWeight;
        }
        foreach (TerrainGenerator.Hill hill in L.Hills)
        {
            float d = Vector2.Distance(p, new Vector2(hill.X, hill.Z));
            if (d >= PatchHillInner * hill.Radius && d <= PatchHillOuter * hill.Radius) w += PatchHillWeight;
        }
        float plateau = L.PlateauEdgeDistance(p);
        if (plateau is >= PatchPlateauNear and <= PatchPlateauFar) w += PatchPlateauWeight;
        if (L.Dunes.Contains(p)) w *= PatchDuneFactor;
        return w;
    }

    /// <summary>An index drawn with probability proportional to its weight (<paramref name="total"/> their sum).</summary>
    private static int WeightedPick(List<float> weights, float total, ref XorShift rng)
    {
        float target = rng.NextFloat() * total;
        int last = -1;
        for (int k = 0; k < weights.Count; k++)
        {
            if (weights[k] <= 0) continue;
            last = k;
            target -= weights[k];
            if (target < 0) return k;
        }
        return last;   // float rounding left a sliver: the last one with weight (−1 when none has any)
    }

    /// <summary>Every slope within 13 m (on a 1 m grid) under 10°.</summary>
    private bool IsFlat(Vector2 centre)
    {
        for (float dx = -PatchFlatRadius; dx <= PatchFlatRadius; dx += 1f)
        for (float dz = -PatchFlatRadius; dz <= PatchFlatRadius; dz += 1f)
        {
            if (dx * dx + dz * dz > PatchFlatRadius * PatchFlatRadius) continue;
            if (_terrain.NormalAt(centre.X + dx, centre.Y + dz).Y <= PatchFlat) return false;
        }
        return true;
    }

    /// <summary>
    /// 12–18 bushes as the sandbox plants them: the first on the centre (at least 3.8 m), the rest within 9 m, overlapping
    /// a little; null when the centre bush or 12 bushes don't fit.
    /// </summary>
    private List<Prop>? PlantPatch(ref XorShift rng, Vector2 centre)
    {
        int wanted = MinBushes + (int)(rng.NextUInt() % (MaxBushes - MinBushes + 1));
        var patch = new List<Prop>();
        for (int attempt = 0; attempt < S.BushAttemptsPerPatch && patch.Count < wanted; attempt++)
        {
            Vector2 at = centre + InDisc(ref rng) * S.BushSpread;
            float radius = S.MinBushRadius + rng.NextFloat() * (S.MaxBushRadius - S.MinBushRadius);
            float height = S.MinBushHeight + rng.NextFloat() * (S.MaxBushHeight - S.MinBushHeight);
            if (patch.Count == 0)
            {
                at = centre;
                radius = MathF.Max(radius, S.MinCentreBushRadius);
                if (!CanGrowBush(at, radius, patch)) radius = S.MinCentreBushRadius;
                if (!CanGrowBush(at, radius, patch)) return null;
            }
            else if (!CanGrowBush(at, radius, patch)) continue;
            float ground = _terrain.HeightAt(at.X, at.Y);
            patch.Add(new Prop(0, PropKind.Bush, new Obb2(at, new Vector2(radius), 0f), ground - S.BushSink, height + S.BushSink));
        }
        return patch.Count >= MinBushes ? patch : null;
    }

    private bool CanGrowBush(Vector2 c, float r, List<Prop> patch)
    {
        if (_terrain.NormalAt(c.X, c.Y).Y < BushFlat || !Inside(c, r + 2)) return false;
        if (RoadCentre(c) < BushRoadCentre + GridSlack || L.ForestDepth(c) > BushForestDepth) return false;
        if (_occupied.AnyWithin(c, r, PlotClearance) || _rocks.AnyWithin(c, r, 0) || !IsDry(c, new Vector2(r))) return false;
        foreach (Prop bush in patch)
            if (Vector2.Distance(c, bush.Footprint.Center) < S.BushOverlap * (r + bush.Footprint.HalfExtents.X)) return false;
        return true;
    }
}

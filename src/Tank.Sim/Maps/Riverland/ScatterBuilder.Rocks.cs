using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;
using S = Tank.Sim.Maps.SandboxMap;

namespace Tank.Sim.Maps.Riverland;

/// <summary>The rocks (riverland design §3.9): at the cliff's foot, beside the ramps, on the hills, in the dunes and in piles on the plain.</summary>
internal sealed partial class ScatterBuilder
{
    // ---- rocks (§3.9): sizes and sink as the sandbox's ----
    private const int CliffRocks = 170, RampRocks = 30, HillRocks = 100, DuneRocks = 80, PiledRocks = 220;
    private const int AttemptsPerRock = 60;
    /// <summary>Rock centres at the cliff's foot lie this far outside the plateau's (noisy) rim; the cliff itself is the CliffWidth inside it.</summary>
    private const float CliffFootMin = 1f, CliffFootMax = 10f;
    /// <summary>Beside a ramp: rock centres this far from its centre line; its lane keeps RampClear m clear past its half width.</summary>
    private const float RampSideMin = 15f, RampSideMax = 25f, RampClear = 2f;
    /// <summary>On a hill: rock centres within this fraction of its radius.</summary>
    private const float HillReach = 0.85f;
    /// <summary>A pile: 3–8 rocks within PileRadius m of its centre; pile centres PileSpacing m apart.</summary>
    private const int MinPile = 3, MaxPile = 8;
    private const float PileRadius = 9f, PileSpacing = 50f;
    /// <summary>A pile gets PileAttemptsPerRock draws for each rock it wants.</summary>
    private const int PileAttemptsPerRock = 6;
    /// <summary>Rock centres at least this × the sum of their bounding radii apart: piles, not stacks (as the sandbox's).</summary>
    private const float RockSpacing = 0.5f;
    /// <summary>
    /// A pile's centre lies on the open plain: PlainPlateauClear m outside the plateau's rim, PlainDunesClear outside the
    /// dunes, PlainForestClear outside every forest's outline, PlainRiverClear from the river's centre line,
    /// PlainLakeClear outside the lake shore, beyond PlainBigHillReach of a big hill's radius, outside the villages and
    /// PlainFarmClear beyond a farm's radius.
    /// </summary>
    private const float PlainPlateauClear = 30f, PlainDunesClear = 10f, PlainForestClear = 15f, PlainRiverClear = 50f,
                        PlainLakeClear = 25f, PlainBigHillReach = 0.9f, PlainFarmClear = 10f;
    /// <summary>A hill rock goes on the south-west hill below SouthWestHillChance, on the east hill below BigHillsChance, else on a knoll.</summary>
    private const float SouthWestHillChance = 0.35f, BigHillsChance = 0.65f;
    private static readonly TerrainGenerator.Hill[] BigHills = { L.SouthWestHill, L.EastHill };
    private static readonly TerrainGenerator.Hill[] SmallHills = L.Hills.Where(h => !BigHills.Contains(h)).ToArray();

    // ================= rocks =================

    /// <summary>About 600 rocks: at the plateau's cliff foot and beside its ramps, on the hills, in the dunes, in piles on the plain.</summary>
    public List<Prop> Rocks()
    {
        var rng = new XorShift(L.RockSeed);
        var rocks = new List<Prop>();
        float reach = L.PlateauJitter + CliffFootMax + 1;
        Vector2 cliffMin = L.Plateau.Min - new Vector2(reach), cliffMax = L.Plateau.Max + new Vector2(reach);
        ScatterRocks(ref rng, rocks, CliffRocks, (ref XorShift r) =>
        {
            Vector2 p = Lerp(cliffMin, cliffMax, ref r);
            float d = L.PlateauEdgeDistance(p);
            return d is >= CliffFootMin and <= CliffFootMax ? p : null;
        });
        ScatterRocks(ref rng, rocks, RampRocks, (ref XorShift r) =>
        {
            Ramp ramp = L.Ramps[(int)(r.NextUInt() % (uint)L.Ramps.Length)];
            float along = ramp.Length * r.NextFloat(), across = RampSideMin + (RampSideMax - RampSideMin) * r.NextFloat();
            Vector2 side = new Vector2(-ramp.Down.Y, ramp.Down.X) * (r.NextUInt() % 2 == 0 ? 1 : -1);
            return ramp.Top + ramp.Down * along + side * across;
        });
        ScatterRocks(ref rng, rocks, HillRocks, (ref XorShift r) =>
        {
            TerrainGenerator.Hill hill = PickHill(ref r);
            return new Vector2(hill.X, hill.Z) + InDisc(ref r) * (hill.Radius * HillReach);
        });
        Vector2 inset = new(4);
        ScatterRocks(ref rng, rocks, DuneRocks, (ref XorShift r) => Lerp(L.Dunes.Min + inset, L.Dunes.Max - inset, ref r));
        PileRocks(ref rng, rocks);
        return rocks;
    }

    private delegate Vector2? Spot(ref XorShift rng);

    /// <summary>Draws spots until <paramref name="target"/> more rocks stand, or AttemptsPerRock × target draws are spent.</summary>
    private void ScatterRocks(ref XorShift rng, List<Prop> rocks, int target, Spot spot)
    {
        int placed = 0;
        for (int attempt = 0; attempt < target * AttemptsPerRock && placed < target; attempt++)
            if (spot(ref rng) is Vector2 p && TryRock(ref rng, p, rocks)) placed++;
    }

    /// <summary>Piles of 3–8 rocks on the open plain (off the plateau, the big hills, the dunes, the forests, the banks and the villages).</summary>
    private void PileRocks(ref XorShift rng, List<Prop> rocks)
    {
        var piles = new List<Vector2>();
        int placed = 0;
        for (int attempt = 0; attempt < PiledRocks * AttemptsPerRock && placed < PiledRocks; attempt++)
        {
            Vector2 centre = Lerp(new Vector2(Edge + PileRadius), new Vector2(Size - Edge - PileRadius), ref rng);
            if (!IsPlain(centre) || piles.Any(p => Vector2.DistanceSquared(p, centre) < PileSpacing * PileSpacing)) continue;
            int wanted = MinPile + (int)(rng.NextUInt() % (MaxPile - MinPile + 1)), inPile = 0;
            for (int k = 0; k < wanted * PileAttemptsPerRock && inPile < wanted; k++)
                if (TryRock(ref rng, centre + InDisc(ref rng) * PileRadius, rocks)) inPile++;
            if (inPile > 0) piles.Add(centre);
            placed += inPile;
        }
    }

    private bool IsPlain(Vector2 p)
    {
        if (L.PlateauEdgeDistance(p) < PlainPlateauClear || L.Dunes.SignedDistance(p) < PlainDunesClear || L.ForestDepth(p) > -PlainForestClear) return false;
        if (_shape.River.DistanceAt(p.X, p.Y) < PlainRiverClear || L.LakeShoreDistance(p) < PlainLakeClear) return false;
        foreach (TerrainGenerator.Hill hill in BigHills)
            if (Vector2.Distance(p, new Vector2(hill.X, hill.Z)) < hill.Radius * PlainBigHillReach) return false;
        foreach (Village village in L.Villages)
            if (Vector2.Distance(p, village.Centre) < village.Radius) return false;
        foreach (Vector2 farm in L.Farms)
            if (Vector2.Distance(p, farm) < L.FarmRadius + PlainFarmClear) return false;
        return true;
    }

    /// <summary>The two big hills a third of the time each, the small ones the rest.</summary>
    private static TerrainGenerator.Hill PickHill(ref XorShift rng)
    {
        float u = rng.NextFloat();
        if (u < SouthWestHillChance) return L.SouthWestHill;
        if (u < BigHillsChance) return L.EastHill;
        return SmallHills[(int)(rng.NextUInt() % (uint)SmallHills.Length)];
    }

    /// <summary>
    /// A rock of the sandbox's sizes at <paramref name="centre"/>, if it fits: added to <paramref name="rocks"/> (id 0 for
    /// now). Rocks intentionally have no slope limit: those at the cliff's foot and on the hills may bed into slopes.
    /// </summary>
    private bool TryRock(ref XorShift rng, Vector2 centre, List<Prop> rocks)
    {
        var half = new Vector2(S.MinRockHalfX + rng.NextFloat() * (S.MaxRockHalfX - S.MinRockHalfX),
                               S.MinRockHalfZ + rng.NextFloat() * (S.MaxRockHalfZ - S.MinRockHalfZ));
        float height = S.MinRockHeight + rng.NextFloat() * (S.MaxRockHeight - S.MinRockHeight);
        float yaw = rng.NextFloat() * Angles.TwoPi;
        float r = half.Length();
        if (!Inside(centre, r + 2) || RoadEdge(centre) - r < RoadClearance + GridSlack || OnRamp(centre, r)) return false;
        if (_occupied.AnyWithin(centre, r, PlotClearance) || _rockSpacing.AnyWithin(centre, RockSpacing * r, 0)) return false;
        if (!IsDry(centre, new Vector2(r))) return false;

        var footprint = new Obb2(centre, half, yaw);
        // Sink the base below the lowest corner, so the rock never floats on a slope; its top stays above the centre.
        float ground = _terrain.HeightAt(centre.X, centre.Y), lowest = ground;
        Vector2 right = footprint.AxisRight * half.X, forward = footprint.AxisForward * half.Y;
        foreach (Vector2 corner in new[] { centre - right - forward, centre + right - forward, centre + right + forward, centre - right + forward })
            lowest = MathF.Min(lowest, _terrain.HeightAt(corner.X, corner.Y));
        float bottom = lowest - S.RockSink;
        rocks.Add(new Prop(0, PropKind.Rock, footprint, bottom, ground + height - bottom));
        _rocks.Add(centre, r);
        _rockSpacing.Add(centre, RockSpacing * r);
        return true;
    }

    /// <summary>A circle of <paramref name="radius"/> at <paramref name="p"/> reaches a ramp's lane (or the ground just past its ends).</summary>
    private static bool OnRamp(Vector2 p, float radius)
    {
        foreach (Ramp ramp in L.Ramps)
        {
            (float along, float across) = ramp.Locate(p);
            if (along >= -L.RampLanding - radius && along <= ramp.Length + L.RampFoot + radius && across - radius < L.RampHalfWidth + RampClear) return true;
        }
        return false;
    }
}

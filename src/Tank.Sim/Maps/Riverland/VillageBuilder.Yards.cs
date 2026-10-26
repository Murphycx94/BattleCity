using System.Numerics;
using Tank.Sim.Geometry;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;
using static Tank.Sim.Maps.Riverland.VillageGeometry;

namespace Tank.Sim.Maps.Riverland;

/// <summary>The town's yards, and the walls and fences of the yards and the farms (riverland design §3.6).</summary>
internal sealed partial class VillageBuilder
{
    /// <summary>Six yards in the town, their back walls 5.8–8 m behind the house.</summary>
    private const int TownYards = 6;
    private const float MinYardDepth = 5.8f, MaxYardDepth = 8f;
    /// <summary>A yard's back wall is MinYardWall + YardWallStep × (0 to YardWallSteps − 1) long: 10, 12, 14 or 16 m, all equally likely.</summary>
    private const float MinYardWall = 10f, YardWallStep = 2f;
    private const int YardWallSteps = 4;
    /// <summary>A yard's back wall is brick this often, else stone.</summary>
    private const float BrickChance = 0.5f;
    /// <summary>Fences are laid FenceClear from the plots they run along; every wall's and fence's face stays PlotClearance clear of every plot.</summary>
    private const float FenceClear = 0.6f, PlotClearance = 0.4f;
    /// <summary>Walls and fences: this far from a road's edge, on ground this high (dry), dropping at most this much from a segment's middle to its ends.</summary>
    private const float WallRoadClearance = 2f, WallDryHeight = 0.5f, WallMaxDrop = 0.6f;

    // ---- the town's yards ----

    /// <summary>
    /// Behind six of the town's houses (in a drawn order, skipping those with no room) a brick or stone wall 10–16 m
    /// long, 5.8–8 m behind the house, with a fence from each end towards the house: beside the plot as far as the
    /// house's middle when the wall is long enough to clear it, else up to just short of the plot.
    /// </summary>
    private void BuildYards()
    {
        VillageHouse[] town = _houses.Where(h => Vector2.Distance(h.Footprint.Center, L.Town) <= L.TownRadius).ToArray();
        for (int k = town.Length - 1; k > 0; k--)
        {
            int swap = (int)(_rng.NextFloat() * (k + 1));
            (town[k], town[swap]) = (town[swap], town[k]);
        }
        int yards = 0;
        foreach (VillageHouse house in town)
        {
            if (yards == TownYards) break;
            if (TryYard(house)) yards++;
        }
    }

    private bool TryYard(VillageHouse house)
    {
        float depth = Draw(MinYardDepth, MaxYardDepth);
        float length = MinYardWall + YardWallStep * MathF.Min(YardWallSteps - 1, MathF.Floor(_rng.NextFloat() * YardWallSteps));
        PropKind kind = _rng.NextFloat() < BrickChance ? PropKind.BrickWall : PropKind.StoneWall;
        float thickness = WallLine.Thickness(kind);

        Obb2 footprint = house.Footprint;
        Vector2 forward = footprint.AxisForward, right = footprint.AxisRight;
        Vector2 middle = footprint.Center - forward * (house.Type.Width / 2 + depth);
        Vector2 left = middle - right * (length / 2), rightEnd = middle + right * (length / 2);
        bool beside = length / 2 >= house.Type.Length / 2 + PlotMargin + FenceClear;
        float reach = beside ? depth - thickness / 2 + house.Type.Width / 2 : depth - thickness / 2 - PlotMargin - FenceClear;
        float fence = 2 * MathF.Floor(reach / 2);
        if (fence < 2) return false;
        Vector2 inner = forward * (thickness / 2);
        WallLine[] lines =
        {
            new(kind, left, rightEnd),
            new(PropKind.Fence, left + inner, left + inner + forward * fence),
            new(PropKind.Fence, rightEnd + inner, rightEnd + inner + forward * fence),
        };
        if (!LinesFit(lines, ++_groups, Array.Empty<VillageHouse>())) return false;
        AddLines(lines, _groups);
        return true;
    }

    // ---- walls and fences ----

    /// <summary>
    /// Every segment of the lines inside the playable area, WallRoadClearance from every road's edge, on dry, gentle
    /// ground out of the forests and off the ramps, PlotClearance from every plot (those already placed and
    /// <paramref name="alsoPlots"/>), and clear of other groups' segments.
    /// </summary>
    private bool LinesFit(WallLine[] lines, int group, IReadOnlyList<VillageHouse> alsoPlots)
    {
        foreach (WallLine line in lines)
        foreach (Obb2 box in line.Footprints())
        {
            Vector2 c = box.Center, half = box.AxisRight * box.HalfExtents.X;
            if (c.X < EdgeMargin || c.Y < EdgeMargin || c.X > L.Size - EdgeMargin || c.Y > L.Size - EdgeMargin) return false;
            if (RoadEdgeDistance(box) < WallRoadClearance || L.InForest(c) || NearRamp(c)) return false;
            float h = HeightAt(c), h0 = HeightAt(c - half), h1 = HeightAt(c + half);
            if (MathF.Min(h, MathF.Min(h0, h1)) < WallDryHeight || MathF.Abs(h0 - h) > WallMaxDrop || MathF.Abs(h1 - h) > WallMaxDrop) return false;
            Obb2 clear = box with { HalfExtents = box.HalfExtents + new Vector2(0, PlotClearance) };
            foreach (VillageHouse house in _houses.Concat(alsoPlots))
                if (Obb2.TryGetPenetration(clear, house.Plot, out _)) return false;
            foreach ((Obb2 other, int otherGroup) in _segments)
                if (otherGroup != group && Obb2.TryGetPenetration(box, other, out _)) return false;
        }
        return true;
    }

    private void AddLines(WallLine[] lines, int group)
    {
        foreach (WallLine line in lines)
        {
            _walls.Add(line);
            foreach (Obb2 box in line.Footprints()) _segments.Add((box, group));
        }
    }
}

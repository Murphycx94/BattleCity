using System.Numerics;
using Tank.Sim.Geometry;
using static Tank.Sim.Maps.Riverland.VillageGeometry;

namespace Tank.Sim.Maps.Riverland;

/// <summary>The five farms: a house or two in a fence ring (riverland design §3.6).</summary>
internal sealed partial class VillageBuilder
{
    /// <summary>A farm's fence ring: FarmDepth deep, FarmWidth wide (two houses) or FarmNarrow (one), a FarmGap gap in the front.</summary>
    private const float FarmDepth = 24f, FarmWidth = 36f, FarmNarrow = 28f, FarmGap = 4f;
    /// <summary>A farm's main house is a farmhouse this often, else a barn; a cottage stands beside it this often.</summary>
    private const float FarmhouseChance = 0.5f, FarmCottageChance = 0.5f;
    /// <summary>Where a farm's ring may go: its site, then 6 and 12 m around it.</summary>
    private static readonly Vector2[] FarmOffsets = BuildFarmOffsets();

    // ---- farms ----

    /// <summary>A farmhouse or a barn, half the time a cottage beside it, in a fence ring whose gap faces the nearest road.</summary>
    private void BuildFarm(Vector2 site)
    {
        HouseType main = _rng.NextFloat() < FarmhouseChance ? HouseType.Farmhouse : HouseType.Barn;
        bool cottage = _rng.NextFloat() < FarmCottageChance;
        foreach (bool withCottage in cottage ? new[] { true, false } : new[] { false })
            foreach (Vector2 offset in FarmOffsets)
                if (TryFarm(site + offset, main, withCottage)) return;
        throw new InvalidOperationException($"No room for the farm at {site}.");
    }

    private bool TryFarm(Vector2 centre, HouseType main, bool withCottage)
    {
        (RoadLine road, float along) = NearestRoad(centre);
        Vector2 forward = Vector2.Normalize(road.Line.PointAt(along) - centre);
        float yaw = Angles.YawOf(forward);
        Vector2 right = Angles.Right(yaw);
        Vector2 At(float a, float b) => centre + right * a + forward * b;

        float width = withCottage ? FarmWidth : FarmNarrow, depth = FarmDepth;
        float back = -depth / 2 + FenceClear + PlotMargin;   // the houses' back walls
        var houses = new List<VillageHouse>();
        float mainAt = withCottage ? -width / 2 + FenceClear + PlotMargin + main.Length / 2 : 0;
        if (!TryHouse(main, At(mainAt, back + main.Width / 2), yaw, houses)) return false;
        HouseType small = HouseType.Cottage;
        if (withCottage && !TryHouse(small, At(width / 2 - FenceClear - PlotMargin - small.Length / 2, back + small.Width / 2), yaw, houses)) return false;

        float w = width / 2, d = depth / 2, g = FarmGap / 2;
        WallLine[] ring =
        {
            new(PropKind.Fence, At(-w, -d), At(w, -d)),
            new(PropKind.Fence, At(w, -d), At(w, d)),
            new(PropKind.Fence, At(w, d), At(g, d)),
            new(PropKind.Fence, At(-g, d), At(-w, d)),
            new(PropKind.Fence, At(-w, d), At(-w, -d)),
        };
        if (!LinesFit(ring, ++_groups, houses)) return false;
        _houses.AddRange(houses);
        AddLines(ring, _groups);
        return true;
    }

    private bool TryHouse(HouseType type, Vector2 centre, float yaw, List<VillageHouse> into)
    {
        var footprint = new Obb2(centre, new Vector2(type.Length / 2, type.Width / 2), yaw);
        if (!PlotFits(footprint, out float ground)) return false;
        into.Add(new VillageHouse(type, footprint, ground));
        return true;
    }

    private static Vector2[] BuildFarmOffsets()
    {
        var offsets = new List<Vector2> { Vector2.Zero };
        foreach (float radius in new[] { 6f, 12f })
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.PI / 4;
                offsets.Add(radius * new Vector2(MathF.Cos(a), MathF.Sin(a)));
            }
        return offsets.ToArray();
    }
}

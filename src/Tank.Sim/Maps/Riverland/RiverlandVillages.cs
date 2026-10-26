using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps.Building;
using Tank.Sim.Terrain;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;
using static Tank.Sim.Maps.Riverland.VillageGeometry;

namespace Tank.Sim.Maps.Riverland;

/// <summary>The four kinds of house (riverland design §3.6): length along the ridge, width across it, height to the ridge, metres.</summary>
internal readonly record struct HouseType(string Name, float Length, float Width, float Height)
{
    public static readonly HouseType Cottage = new("cottage", 8, 6, 6.5f);
    public static readonly HouseType Farmhouse = new("farmhouse", 12, 7, 7.5f);
    public static readonly HouseType TwoStorey = new("two-storey house", 9, 7, 9);
    public static readonly HouseType Barn = new("barn", 14, 9, 8);
}

/// <summary>A house: its footprint (ridge along <see cref="Obb2.AxisRight"/>, door facing <see cref="Obb2.AxisForward"/>) and the height its plot is levelled to.</summary>
internal sealed record VillageHouse(HouseType Type, Obb2 Footprint, float Ground)
{
    /// <summary>A house's plot is its footprint grown by this much on every side; it is levelled and its ground is dirt.</summary>
    public const float PlotMargin = 3f;

    public Obb2 Plot => Footprint with { HalfExtents = Footprint.HalfExtents + new Vector2(PlotMargin) };

    /// <summary>The house as a prop: from <see cref="WallLine.Sink"/> under its plot up to its ridge.</summary>
    public Prop ToProp(int id) => new(id, PropKind.House, Footprint, Ground - WallLine.Sink, Type.Height + WallLine.Sink);
}

/// <summary>
/// The riverland's villages (riverland design §3.6): the town on both banks, the north, west and south villages and five
/// farms; their houses on levelled plots, the town's yard walls and the farms' fence rings. <see cref="Lay"/> chooses
/// them on the ground as it is once the roads are paved, levels the plots and keeps the rest; the props are made from
/// the finished height field.
/// </summary>
internal sealed class RiverlandVillages
{
    private const int Cells = RiverlandMap.Samples - 1;
    private readonly bool[] _yards;

    internal RiverlandVillages(List<VillageHouse> houses, List<WallLine> walls, bool[] yards, PlacementGrid occupied)
    {
        Houses = houses;
        Walls = walls;
        _yards = yards;
        Occupied = occupied;
    }

    /// <summary>Every house, the town's first, then the villages', then the farms'.</summary>
    public IReadOnlyList<VillageHouse> Houses { get; }

    /// <summary>The town's yard walls with their side fences, then the farms' fence rings; each a whole number of 2 m segments.</summary>
    public IReadOnlyList<WallLine> Walls { get; }

    /// <summary>The plots (by their outer circles) and the wall and fence segments, for whatever is scattered later to keep off.</summary>
    public PlacementGrid Occupied { get; }

    /// <summary>Whether ground cell (i, j)'s centre lies on a house's plot.</summary>
    public bool IsYard(int i, int j) => _yards[j * Cells + i];

    /// <summary>The houses as props numbered <paramref name="firstId"/>, <paramref name="firstId"/> + 1, … in <see cref="Houses"/>' order.</summary>
    public IEnumerable<Prop> HouseProps(int firstId) => Houses.Select((house, k) => house.ToProp(firstId + k));

    /// <summary>The walls' and fences' segments on the finished <paramref name="terrain"/>, numbered from <paramref name="firstId"/> in <see cref="Walls"/>' order.</summary>
    public List<Prop> WallProps(HeightField terrain, int firstId)
    {
        var props = new List<Prop>();
        foreach (WallLine wall in Walls) props.AddRange(wall.Segments(terrain, firstId + props.Count));
        return props;
    }

    /// <summary>
    /// Chooses the houses on <paramref name="heights"/> (the ground with the roads paved), levels their plots in place
    /// (leaving the roads' surfaces and the water as they are) and chooses the walls and fences.
    /// </summary>
    public static RiverlandVillages Lay(float[] heights, DistanceGrid river, DistanceGrid roads, DistanceGrid tracks) =>
        new VillageBuilder(heights, river, roads, tracks).Build();
}

/// <summary>
/// Lays out <see cref="RiverlandVillages"/>: one pass, one <see cref="XorShift"/>, a fixed order. The villages and the
/// plots are here; the farms and the town's yards (with the walls and fences) in the other parts.
/// </summary>
internal sealed partial class VillageBuilder
{
    private const int N = RiverlandMap.Samples, Cells = N - 1;
    private const float Cell = RiverlandMap.CellSize;
    private const float PlotMargin = VillageHouse.PlotMargin;

    /// <summary>Walking a road, a house that doesn't fit is tried again this much farther along.</summary>
    private const float Step = 2f;
    /// <summary>A house's front is drawn this far from its road's edge, and must end up FrontMin–FrontMax from every road's.</summary>
    /// <remarks>
    /// At least 5.2 m (more than the design's 3) so the plot stays 2 m clear of the road's edge: the roads' surfaces and
    /// the samples next to them, which interpolating the road's surface reads, are left as the roads have them.
    /// </remarks>
    private const float MinSetback = 5.5f, MaxSetback = 9.5f, FrontMin = 5.2f, FrontMax = 9.8f;
    /// <summary>Neighbours in a row are drawn MinGap–MaxGap apart; two houses up to RowGap apart are a row, RowBreak or more apart separate rows, nothing between.</summary>
    private const float MinGap = 6.5f, MaxGap = 11.5f, RowGap = 12f, RowBreak = 20f;
    /// <summary>Samples this close to a road's or track's edge keep the height the road gave them.</summary>
    private const float RoadGuard = 2f;
    /// <summary>A plot: every sample at least DryHeight high, rising at most MaxRise across it, DryRiverDistance from the river's centre line.</summary>
    private const float DryHeight = 0.6f, MaxRise = 3f, DryRiverDistance = 30f;
    /// <summary>A plot is levelled to its mean height, easing back to the ground around over this many metres.</summary>
    private const float Blend = 3f;
    /// <summary>Everything stays this far inside the playable area (the border band rises).</summary>
    private const float EdgeMargin = RiverlandMap.Border + 4f;
    private static readonly float MinFacing = MathF.Cos(20f * MathF.PI / 180f);
    /// <summary>The town's houses: TownTwoStorey of them two-storey, up to TownFarmhouse farmhouses, the rest cottages (cumulative chances).</summary>
    private const float TownTwoStorey = 0.45f, TownFarmhouse = 0.8f;
    /// <summary>A village's houses: VillageCottage of them cottages, up to VillageFarmhouse farmhouses, the rest two-storey (cumulative chances).</summary>
    private const float VillageCottage = 0.5f, VillageFarmhouse = 0.9f;
    /// <summary>The cell size of the grid of what is occupied, handed on to the scattering: about a plot's size.</summary>
    private const float OccupiedCell = 16f;

    private readonly float[] _h, _ground;
    private readonly DistanceGrid _river, _roads, _tracks;
    private readonly List<VillageHouse> _houses = new();
    private readonly List<WallLine> _walls = new();
    private readonly List<(Obb2 Box, int Group)> _segments = new();
    private XorShift _rng = new(L.VillageSeed);
    private int _groups;

    public VillageBuilder(float[] heights, DistanceGrid river, DistanceGrid roads, DistanceGrid tracks)
    {
        _h = heights;
        _ground = (float[])heights.Clone();
        (_river, _roads, _tracks) = (river, roads, tracks);
    }

    public RiverlandVillages Build()
    {
        foreach (Village village in L.Villages) BuildVillage(village);
        foreach (Vector2 farm in L.Farms) BuildFarm(farm);
        LevelPlots();
        BuildYards();
        return new RiverlandVillages(_houses, _walls, Yards(), Occupied());
    }

    // ---- villages ----

    private void BuildVillage(Village village)
    {
        var streets = new List<List<VillageHouse>>();
        foreach (RoadLine road in village.Streets)
        {
            streets.Add(WalkStreet(village, road, 1));
            streets.Add(WalkStreet(village, road, -1));
        }
        Trim(streets, village);
    }

    /// <summary>
    /// Walks one side of the road from its start, putting a house wherever one fits within the village's radius: long
    /// side along the road, front to it, a drawn setback from its edge, a drawn gap after the last one; where it doesn't
    /// fit, 2 m farther along.
    /// </summary>
    private List<VillageHouse> WalkStreet(Village village, RoadLine road, int side)
    {
        var street = new List<VillageHouse>();
        Polyline line = road.Line;
        float halfWidth = HalfWidth(road), reach = village.Radius + halfWidth + MaxSetback + HouseType.Barn.Width;
        HouseType type = PickType(village.IsTown);
        float setback = Draw(MinSetback, MaxSetback);
        for (float s = 0; s + type.Length <= line.Length;)
        {
            float mid = s + type.Length / 2;
            Vector2 at = line.PointAt(mid);
            if (Vector2.DistanceSquared(at, village.Centre) > reach * reach)
            {
                s += Step;
                continue;
            }
            Vector2 tangent = line.TangentAt(mid), away = side * new Vector2(-tangent.Y, tangent.X);
            Vector2 centre = at + away * (halfWidth + setback + type.Width / 2);
            var footprint = new Obb2(centre, new Vector2(type.Length / 2, type.Width / 2), Angles.YawOf(-away));
            if (Vector2.Distance(centre, village.Centre) <= village.Radius && FitsStreet(footprint, road, street, out float ground))
            {
                var house = new VillageHouse(type, footprint, ground);
                street.Add(house);
                _houses.Add(house);
                s += type.Length + Draw(MinGap, MaxGap);
                type = PickType(village.IsTown);
                setback = Draw(MinSetback, MaxSetback);
            }
            else s += Step;
        }
        return street;
    }

    /// <summary>On a plot that fits, in the row (or clearly past it), facing its own road from FrontMin–FrontMax away.</summary>
    private bool FitsStreet(in Obb2 footprint, RoadLine road, List<VillageHouse> street, out float ground)
    {
        ground = 0;
        if (street.Count > 0)
        {
            float gap = Distance(street[^1].Footprint, footprint);
            if (gap > RowGap && gap < RowBreak) return false;
        }
        if (!PlotFits(footprint, out ground) || RoadEdgeDistance(footprint) > FrontMax) return false;
        (RoadLine nearest, float along) = NearestRoad(footprint.Center);
        if (nearest != road) return false;
        Vector2 toRoad = Vector2.Normalize(road.Line.PointAt(along) - footprint.Center);
        return Vector2.Dot(toRoad, footprint.AxisForward) >= MinFacing;
    }

    /// <summary>Down to the village's number of houses, each time taking the end of a row farthest from the centre.</summary>
    private void Trim(List<List<VillageHouse>> streets, Village village)
    {
        while (streets.Sum(s => s.Count) > village.Houses)
        {
            (List<VillageHouse>? street, int index, float farthest) = (null, -1, -1f);
            foreach (List<VillageHouse> candidate in streets)
                for (int k = 0; k < candidate.Count; k++)
                {
                    bool rowEnd = k == 0 || k == candidate.Count - 1
                                  || Distance(candidate[k - 1].Footprint, candidate[k].Footprint) > RowGap
                                  || Distance(candidate[k].Footprint, candidate[k + 1].Footprint) > RowGap;
                    float d = Vector2.Distance(candidate[k].Footprint.Center, village.Centre);
                    if (rowEnd && d > farthest) (street, index, farthest) = (candidate, k, d);
                }
            _houses.Remove(street![index]);
            street.RemoveAt(index);
        }
    }

    /// <summary>The town mostly two-storey houses and farmhouses; a village mostly cottages and farmhouses.</summary>
    private HouseType PickType(bool town)
    {
        float r = _rng.NextFloat();
        if (town) return r < TownTwoStorey ? HouseType.TwoStorey : r < TownFarmhouse ? HouseType.Farmhouse : HouseType.Cottage;
        return r < VillageCottage ? HouseType.Cottage : r < VillageFarmhouse ? HouseType.Farmhouse : HouseType.TwoStorey;
    }

    // ---- the plots ----

    /// <summary>
    /// A plot that fits (riverland design §3.6): inside the playable area, the house FrontMin from every road's edge,
    /// clear of other plots and of walls, every sample dry (≥ 0.6 m), out of the forests, off the ramps, 30 m from the
    /// river's centre line, rising at most 3 m across. <paramref name="ground"/> is its mean height.
    /// </summary>
    private bool PlotFits(in Obb2 footprint, out float ground)
    {
        ground = 0;
        Obb2 plot = footprint with { HalfExtents = footprint.HalfExtents + new Vector2(PlotMargin) };
        foreach (Vector2 corner in Corners(plot))
            if (corner.X < EdgeMargin || corner.Y < EdgeMargin || corner.X > L.Size - EdgeMargin || corner.Y > L.Size - EdgeMargin) return false;
        foreach (VillageHouse other in _houses)
            if (Obb2.TryGetPenetration(plot, other.Plot, out _)) return false;
        foreach ((Obb2 box, _) in _segments)
            if (Obb2.TryGetPenetration(plot, box, out _)) return false;
        if (RoadEdgeDistance(footprint) < FrontMin) return false;

        float lo = float.PositiveInfinity, hi = float.NegativeInfinity, sum = 0;
        int count = 0;
        foreach (int at in SamplesIn(plot))
        {
            float h = _ground[at];
            var p = new Vector2(at % N * Cell, at / N * Cell);
            if (h < DryHeight || _river.Distance[at] < DryRiverDistance || L.InForest(p) || NearRamp(p)) return false;
            lo = MathF.Min(lo, h);
            hi = MathF.Max(hi, h);
            sum += h;
            count++;
        }
        if (count == 0 || hi - lo > MaxRise) return false;
        ground = sum / count;
        return true;
    }

    /// <summary>
    /// Each plot to its height, easing back to the ground around over 3 m; then each plot once more without easing, as a
    /// later plot's easing may reach an earlier one; then the roads' surfaces (with the samples right next to them) and
    /// whatever lay under water put back as they were.
    /// </summary>
    private void LevelPlots()
    {
        foreach (VillageHouse house in _houses) HeightSculpt.Flatten(_h, N, Cell, house.Plot, house.Ground, Blend);
        foreach (VillageHouse house in _houses) HeightSculpt.Flatten(_h, N, Cell, house.Plot, house.Ground, 0);
        for (int at = 0; at < _h.Length; at++)
        {
            if (_h[at] == _ground[at]) continue;
            if (_roads.Distance[at] < L.RoadHalfWidth + RoadGuard || _tracks.Distance[at] < L.TrackHalfWidth + RoadGuard
                || _ground[at] <= RiverlandMap.WaterLevel)
                _h[at] = _ground[at];
        }
    }

    // ---- results ----

    /// <summary>The ground cells whose centres lie on a plot.</summary>
    private bool[] Yards()
    {
        var yards = new bool[Cells * Cells];
        foreach (VillageHouse house in _houses)
        {
            Obb2 plot = house.Plot;
            float rx = plot.Radius(Vector2.UnitX), rz = plot.Radius(Vector2.UnitY);
            int i0 = Math.Max(0, (int)MathF.Floor((plot.Center.X - rx) / Cell)), i1 = Math.Min(Cells - 1, (int)MathF.Ceiling((plot.Center.X + rx) / Cell));
            int j0 = Math.Max(0, (int)MathF.Floor((plot.Center.Y - rz) / Cell)), j1 = Math.Min(Cells - 1, (int)MathF.Ceiling((plot.Center.Y + rz) / Cell));
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
                if (plot.Contains(new Vector2((i + 0.5f) * Cell, (j + 0.5f) * Cell))) yards[j * Cells + i] = true;
        }
        return yards;
    }

    private PlacementGrid Occupied()
    {
        var grid = new PlacementGrid(L.Size, L.Size, OccupiedCell);
        foreach (VillageHouse house in _houses) grid.Add(house.Plot.Center, house.Plot.HalfExtents.Length());
        foreach ((Obb2 box, _) in _segments) grid.Add(box.Center, box.HalfExtents.Length());
        return grid;
    }

    // ---- helpers ----

    private float Draw(float min, float max) => min + (max - min) * _rng.NextFloat();

    private float HeightAt(Vector2 p) => RiverlandTerrain.HeightAt(_h, p);

    /// <summary>How near the box's outline comes to a road's or track's edge (<see cref="VillageGeometry.RoadEdgeDistance"/>).</summary>
    private float RoadEdgeDistance(in Obb2 box) => VillageGeometry.RoadEdgeDistance(box, _roads, _tracks);
}

using Tank.Sim.Geometry;
using Tank.Sim.Maps.Building;
using Tank.Sim.Terrain;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Maps.Riverland;

/// <summary>
/// The riverland's rocks, bush patches and trees (riverland design §3.7–3.9), scattered on the finished ground after the
/// houses: rocks first, then bushes, then trees, each keeping clear of the roads, the water, the house plots and walls,
/// and of what was scattered before it.
/// </summary>
internal static class RiverlandScatter
{
    /// <summary>The rocks, then the bushes (patch by patch), then the trees (forest by forest, then the lone ones), numbered from <paramref name="firstId"/>.</summary>
    public static List<Prop> Place(RiverlandShape shape, HeightField terrain, int firstId)
    {
        var builder = new ScatterBuilder(shape, terrain);
        List<Prop> props = builder.Rocks();
        props.AddRange(builder.Bushes());
        props.AddRange(builder.Trees());
        for (int k = 0; k < props.Count; k++) props[k] = props[k] with { Id = firstId + k };
        return props;
    }
}

/// <summary>
/// Scatters rocks, bushes and trees in a fixed order, each kind from its own <see cref="XorShift"/>: the shared state is
/// here, the rocks, bushes, trees and shared helpers in the other parts.
/// </summary>
internal sealed partial class ScatterBuilder
{
    private const float Size = L.Size, Edge = RiverlandMap.Border, Cell = RiverlandMap.CellSize;
    private const int N = RiverlandMap.Samples;
    /// <summary>The distance grids are bilinear between samples: distances read from them are trusted to within this.</summary>
    private const float GridSlack = 0.5f;
    /// <summary>Every height sample under a prop's bounding box lies this far above the water.</summary>
    private const float DryHeight = 0.05f;
    /// <summary>Rocks and trees keep this far from a road's or track's edge (riverland design §3.7, §3.9).</summary>
    private const float RoadClearance = 3f;
    /// <summary>Rocks, bushes and trees keep this far from the house plots and the walls.</summary>
    private const float PlotClearance = 0.5f;
    /// <summary>
    /// The neighbour grids' cells: CircleGridCell for the rocks' and bushes' circles (a few times the biggest one, so a
    /// check reads a handful of cells), LoneTreeGridCell for the lone trees' centres (checked within 3–12 m).
    /// </summary>
    private const float CircleGridCell = 16f, LoneTreeGridCell = 8f;

    private readonly RiverlandShape _shape;
    private readonly HeightField _terrain;
    private readonly PlacementGrid _occupied;
    private readonly PlacementGrid _rocks = new(Size, Size, CircleGridCell);         // bounding circles
    private readonly PlacementGrid _rockSpacing = new(Size, Size, CircleGridCell);   // RockSpacing × bounding circles
    private readonly PlacementGrid _bushes = new(Size, Size, CircleGridCell);
    private readonly PlacementGrid _loneTrees = new(Size, Size, LoneTreeGridCell);   // centres

    public ScatterBuilder(RiverlandShape shape, HeightField terrain)
    {
        _shape = shape;
        _terrain = terrain;
        _occupied = shape.Villages.Occupied;
    }
}

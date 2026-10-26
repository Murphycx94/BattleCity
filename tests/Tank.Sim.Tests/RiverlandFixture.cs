using Tank.Sim.Maps;
using Tank.Sim.Maps.Building;
using Tank.Sim.Maps.Riverland;

namespace Tank.Sim.Tests;

/// <summary>
/// The riverland from the map catalog, generated once for the whole test run and only read (a test that changes prop
/// state uses <c>Field.Fresh()</c>), plus distance grids stamped from the layout's centre lines for the tests' own lookups.
/// </summary>
internal static class RiverlandFixture
{
    /// <summary>The test grids reach this far from their lines; farther is +∞.</summary>
    public const float GridReach = 160f;

    private static readonly Lazy<Battlefield> LazyField = new(MapCatalog.Riverland.NewBattlefield);
    private static readonly Lazy<DistanceGrid> LazyRiver = new(() => Stamp(new[] { RiverlandLayout.River }));
    private static readonly Lazy<DistanceGrid> LazyRoads = new(() => Stamp(RiverlandLayout.Roads.Select(r => r.Line).ToArray()));
    private static readonly Lazy<DistanceGrid> LazyTracks = new(() => Stamp(RiverlandLayout.Tracks.Select(r => r.Line).ToArray()));

    public static Battlefield Field => LazyField.Value;

    /// <summary>Distance to the river's centre line, per height-field sample.</summary>
    public static DistanceGrid River => LazyRiver.Value;

    /// <summary>Distance to the nearest road's centre line, per height-field sample.</summary>
    public static DistanceGrid Roads => LazyRoads.Value;

    /// <summary>Distance to the nearest dirt track's centre line, per height-field sample.</summary>
    public static DistanceGrid Tracks => LazyTracks.Value;

    private static DistanceGrid Stamp(Polyline[] lines)
    {
        var grid = new DistanceGrid(RiverlandMap.Samples, RiverlandMap.Samples, RiverlandMap.CellSize);
        for (int k = 0; k < lines.Length; k++) lines[k].Stamp(grid, GridReach, k);
        return grid;
    }
}

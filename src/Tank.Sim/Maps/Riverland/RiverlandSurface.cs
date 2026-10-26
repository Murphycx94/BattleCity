using System.Numerics;
using Tank.Sim.Maps.Building;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Maps.Riverland;

/// <summary>
/// The riverland's ground kinds (riverland design §2.1, §3.3–3.7), one per 2 m cell, each judged at the cell's centre.
/// First that applies: road; dirt track; a ford's bed (dirt); a house's plot (dirt); sand (river bank and bed, lake beach
/// and bed, dunes); forest floor (dirt); grass.
/// </summary>
internal static class RiverlandSurface
{
    private const int Samples = RiverlandMap.Samples, Cells = Samples - 1;
    private const float Cell = RiverlandMap.CellSize;

    public static SurfaceKind[] Build(RiverlandShape shape)
    {
        var cells = new SurfaceKind[Cells * Cells];
        float[] h = shape.Heights;
        float bankReach = L.RiverHalfWidth + L.BankWidth + L.BankJitter;
        float beachReach = L.LakeRadius + L.LakeJitter + L.BeachWidth;
        for (int j = 0; j < Cells; j++)
        for (int i = 0; i < Cells; i++)
        {
            var p = new Vector2((i + 0.5f) * Cell, (j + 0.5f) * Cell);
            int corner = j * Samples + i;
            float road = shape.Roads.DistanceAt(p.X, p.Y), track = shape.Tracks.DistanceAt(p.X, p.Y);
            float river = shape.River.DistanceAt(p.X, p.Y);
            float depth = RiverlandMap.WaterLevel - (h[corner] + h[corner + 1] + h[corner + Samples] + h[corner + Samples + 1]) / 4;
            SurfaceKind kind;
            if (road <= L.RoadHalfWidth) kind = SurfaceKind.Road;
            else if (track <= L.TrackHalfWidth) kind = SurfaceKind.Dirt;
            else if (depth > 0 && river < L.FordRiverReach && OnFord(shape, corner, road, track)) kind = SurfaceKind.Dirt;
            else if (shape.Villages.IsYard(i, j)) kind = SurfaceKind.Dirt;
            else if ((river < bankReach && river < L.BankOuter(p))
                     || (Vector2.DistanceSquared(p, L.LakeCentre) < beachReach * beachReach && L.LakeShoreDistance(p) < L.BeachWidth)
                     || L.Dunes.Contains(p))
                kind = SurfaceKind.Sand;
            else if (L.InForest(p)) kind = SurfaceKind.Dirt;
            else kind = SurfaceKind.Grass;
            cells[j * Cells + i] = kind;
        }
        return cells;
    }

    /// <summary>Within a ford's half width of the centre line of a road that fords the river.</summary>
    private static bool OnFord(RiverlandShape shape, int corner, float road, float track)
    {
        foreach (Ford ford in L.Fords)
        {
            DistanceGrid grid = ford.Road.IsTrack ? shape.Tracks : shape.Roads;
            float distance = ford.Road.IsTrack ? track : road;
            if (distance <= L.FordHalfWidth && grid.Line[corner] == ford.Road.Id) return true;
        }
        return false;
    }
}

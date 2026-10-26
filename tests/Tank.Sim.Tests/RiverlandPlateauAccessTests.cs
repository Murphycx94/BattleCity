using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Maps.Building;
using Tank.Sim.Maps.Riverland;
using L = Tank.Sim.Maps.Riverland.RiverlandLayout;

namespace Tank.Sim.Tests;

/// <summary>Only two ramps lead up the north-west plateau (riverland design §3.3): a tank gets up or down by them alone.</summary>
public sealed class RiverlandPlateauAccessTests
{
    /// <summary>A corridor blocked over a ramp reaches this far past its lane's sides, and past its landing and its foot.</summary>
    private const float CorridorSideMargin = 4f, CorridorEndMargin = 5f;
    /// <summary>The plateau's rim wanders 10 m and its cliff is 7 m wide: the top's flood stays within its rectangle grown by this.</summary>
    private const float PlateauSlack = 25f;
    private static readonly Vector2 PlateauTop = new(240, 200);

    private static Battlefield Field => RiverlandFixture.Field;

    [Fact]
    public void The_plateau_top_is_reached_only_by_its_two_ramps()
    {
        var passable = new Passability(Field, Angles.Deg(32), 1.6f);
        Assert.True(passable.IsPassableAt(PlateauTop), "the plateau top is not passable where the test starts");

        bool[] reached = Flood(passable, PlateauTop, L.Ramps);
        var grown = new GroundRect(L.Plateau.Min - new Vector2(PlateauSlack), L.Plateau.Max + new Vector2(PlateauSlack));
        int count = 0;
        var escapes = new List<Vector2>();
        for (int k = 0; k < reached.Length; k++)
        {
            if (!reached[k]) continue;
            count++;
            Vector2 p = CellCentre(k);
            if (!grown.Contains(p) && escapes.Count < 10) escapes.Add(p);
        }
        Assert.True(escapes.Count == 0, $"with both ramps blocked the plateau top reaches {string.Join(", ", escapes)}");
        float topArea = (L.Plateau.Max.X - L.Plateau.Min.X) * (L.Plateau.Max.Y - L.Plateau.Min.Y);
        Assert.True(count * RiverlandMap.CellSize * RiverlandMap.CellSize > topArea / 2, $"only {count} cells of the plateau top are reached");

        Vector2 spawn = RiverlandMap.Spawns.Team.First(s => s.Team == 0).Position;
        foreach (Ramp open in L.Ramps)
        {
            Ramp[] blocked = L.Ramps.Where(r => r != open).ToArray();
            bool[] down = Flood(passable, PlateauTop, blocked);
            Assert.True(down[CellOf(spawn)], $"with only the ramp at {open.Top} open, team 0's spawn {spawn} can't be reached from the plateau top");
        }
    }

    /// <summary>
    /// The cells reached from <paramref name="start"/> over passable cells (four neighbours), with a corridor over each of
    /// <paramref name="blocked"/> closed: its lane and sides plus CorridorSideMargin across, from CorridorEndMargin behind
    /// its landing to CorridorEndMargin past its foot.
    /// </summary>
    private static bool[] Flood(Passability passable, Vector2 start, IReadOnlyList<Ramp> blocked)
    {
        const int cells = RiverlandMap.Samples - 1;
        var reached = new bool[cells * cells];
        var queue = new Queue<int>();
        int first = CellOf(start);
        reached[first] = true;
        queue.Enqueue(first);
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            int i = at % cells, j = at / cells;
            foreach ((int ni, int nj) in new[] { (i - 1, j), (i + 1, j), (i, j - 1), (i, j + 1) })
            {
                if (!passable.IsPassable(ni, nj)) continue;
                int next = nj * cells + ni;
                if (reached[next] || InCorridor(CellCentre(next), blocked)) continue;
                reached[next] = true;
                queue.Enqueue(next);
            }
        }
        return reached;
    }

    private static bool InCorridor(Vector2 p, IReadOnlyList<Ramp> ramps)
    {
        foreach (Ramp ramp in ramps)
        {
            (float along, float across) = ramp.Locate(p);
            if (across <= L.RampHalfWidth + L.RampSide + CorridorSideMargin
                && along >= -L.RampLanding - CorridorEndMargin && along <= ramp.Length + L.RampFoot + CorridorEndMargin)
                return true;
        }
        return false;
    }

    private static int CellOf(Vector2 p) =>
        (int)(p.Y / RiverlandMap.CellSize) * (RiverlandMap.Samples - 1) + (int)(p.X / RiverlandMap.CellSize);

    private static Vector2 CellCentre(int k)
    {
        const int cells = RiverlandMap.Samples - 1;
        return new Vector2((k % cells + 0.5f) * RiverlandMap.CellSize, (k / cells + 0.5f) * RiverlandMap.CellSize);
    }
}

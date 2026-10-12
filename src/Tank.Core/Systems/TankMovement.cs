using Tank.Core.Math;
using Tank.Core.Rules;
using Tank.Core.World;

namespace Tank.Core.Systems;

/// <summary>
/// Pure tank movement, shared verbatim by the server simulation and client-side prediction.
/// NES rules: four directions, snapping to the half-tile grid when turning 90 degrees,
/// stopping flush against terrain and other tanks, sliding on ice.
/// </summary>
public static class TankMovement
{
    public static void Step(ref TankState tank, in InputCmd input, bool frozen, GameMap map, ReadOnlySpan<Box> obstacles)
    {
        tank.Blocked = false;
        if (tank.SpawnTicks > 0 || frozen)
        {
            tank.Moving = false;
            tank.SlideTicks = 0;
            return;
        }

        bool wantMove;
        if (input.HasMove)
        {
            Dir d = input.MoveDir;
            if (d != tank.Dir) Turn(ref tank, d, map, obstacles);
            wantMove = true;
            tank.SlideTicks = IsOnIce(map, tank) ? (byte)TankStats.IceSlideTicks : (byte)0;
        }
        else if (tank.SlideTicks > 0)
        {
            tank.SlideTicks--;
            wantMove = true;
        }
        else
        {
            wantMove = false;
        }

        tank.Moving = wantMove;
        if (!wantMove) return;

        int speed = TankStats.Speed(tank);
        int moved = Advance(ref tank, speed, map, obstacles);
        tank.Blocked = moved < speed;
        if (tank.Blocked && !input.HasMove) tank.SlideTicks = 0;
    }

    public static bool IsOnIce(GameMap map, in TankState t) => map.CellAt(t.CenterX, t.CenterZ) == CellType.Ice;

    private static void Turn(ref TankState tank, Dir newDir, GameMap map, ReadOnlySpan<Box> obstacles)
    {
        if (newDir.IsPerpendicularTo(tank.Dir))
        {
            Box before = tank.Box;
            int sx = newDir.IsVertical() ? IntMath.RoundTo(tank.X, Units.Half) : tank.X;
            int sz = newDir.IsVertical() ? tank.Z : IntMath.RoundTo(tank.Z, Units.Half);
            var snapped = new Box(sx, sz, Units.TankSize, Units.TankSize);
            if (!map.BlocksTank(snapped) && !HitsNewObstacle(before, snapped, obstacles))
            {
                tank.X = sx;
                tank.Z = sz;
            }
        }
        tank.Dir = newDir;
    }

    /// <summary>Moves up to <paramref name="speed"/> along the facing direction; returns distance moved.</summary>
    private static int Advance(ref TankState tank, int speed, GameMap map, ReadOnlySpan<Box> obstacles)
    {
        Dir d = tank.Dir;
        Box cur = tank.Box;
        int limit = speed;

        if (map.BlocksTank(Shift(cur, d, limit))) limit = System.Math.Min(limit, DistanceToCellBoundary(cur, d));

        foreach (ref readonly Box o in obstacles)
        {
            if (o.Overlaps(cur)) continue; // already intersecting (e.g. spawn overlap): let them separate
            if (!Shift(cur, d, limit).Overlaps(o)) continue;
            int gap = d switch
            {
                Dir.Right => o.X - cur.MaxX,
                Dir.Left => cur.X - o.MaxX,
                Dir.Down => o.Z - cur.MaxZ,
                _ => cur.Z - o.MaxZ,
            };
            limit = System.Math.Max(0, System.Math.Min(limit, gap));
        }

        tank.X += d.Dx() * limit;
        tank.Z += d.Dz() * limit;
        return limit;
    }

    private static Box Shift(in Box b, Dir d, int dist) => b with { X = b.X + d.Dx() * dist, Z = b.Z + d.Dz() * dist };

    /// <summary>
    /// Distance from the leading edge to the next cell boundary. Moving up to that boundary keeps the box
    /// within the cell columns/rows it already occupies, so it can never newly collide with terrain.
    /// </summary>
    private static int DistanceToCellBoundary(in Box b, Dir d) => d switch
    {
        Dir.Right => IntMath.CeilDiv(b.MaxX, Units.Cell) * Units.Cell - b.MaxX,
        Dir.Left => b.X - IntMath.FloorDiv(b.X, Units.Cell) * Units.Cell,
        Dir.Down => IntMath.CeilDiv(b.MaxZ, Units.Cell) * Units.Cell - b.MaxZ,
        _ => b.Z - IntMath.FloorDiv(b.Z, Units.Cell) * Units.Cell,
    };

    private static bool HitsNewObstacle(in Box before, in Box after, ReadOnlySpan<Box> obstacles)
    {
        foreach (ref readonly Box o in obstacles)
            if (after.Overlaps(o) && !before.Overlaps(o)) return true;
        return false;
    }
}

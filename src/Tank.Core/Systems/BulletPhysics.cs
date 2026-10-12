using Tank.Core.Math;
using Tank.Core.World;

namespace Tank.Core.Systems;

/// <summary>Bullet geometry helpers shared by the server and client-side bullet prediction.</summary>
public static class BulletPhysics
{
    public static (int X, int Z) MuzzlePosition(in TankState t)
    {
        int offset = Units.TankSize / 2 - Units.BulletSize / 2;
        return (t.CenterX + t.Dir.Dx() * offset, t.CenterZ + t.Dir.Dz() * offset);
    }

    public static bool OutOfBounds(GameMap map, in Box b) =>
        b.X < 0 || b.Z < 0 || b.MaxX > map.WidthSub || b.MaxZ > map.HeightSub;

    /// <summary>True when the bullet box overlaps a cell that stops bullets.</summary>
    public static bool HitsTerrain(GameMap map, in Box b)
    {
        int cx0 = IntMath.FloorDiv(b.X, Units.Cell), cx1 = IntMath.FloorDiv(b.MaxX - 1, Units.Cell);
        int cz0 = IntMath.FloorDiv(b.Z, Units.Cell), cz1 = IntMath.FloorDiv(b.MaxZ - 1, Units.Cell);
        for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
                if (map.Get(cx, cz).BlocksBullet()) return true;
        return false;
    }

    /// <summary>
    /// Destroys a strip of cells where the bullet hit: 4 cells across the path, 1 cell deep
    /// (2 deep and steel too for power bullets). Returns true if anything was destroyed.
    /// </summary>
    public static bool DamageTerrain(GameMap map, in BulletState bullet)
    {
        Box b = bullet.Box;
        int cx0 = IntMath.FloorDiv(b.X, Units.Cell), cx1 = IntMath.FloorDiv(b.MaxX - 1, Units.Cell);
        int cz0 = IntMath.FloorDiv(b.Z, Units.Cell), cz1 = IntMath.FloorDiv(b.MaxZ - 1, Units.Cell);
        int depth = bullet.Power ? 2 : 1;
        bool vertical = bullet.Dir.IsVertical();

        // The first blocking row/column met along the path is the one nearest to where the bullet came from.
        int hitLine = int.MinValue;
        for (int cz = cz0; cz <= cz1; cz++)
        {
            for (int cx = cx0; cx <= cx1; cx++)
            {
                if (!map.Get(cx, cz).BlocksBullet()) continue;
                int line = vertical ? cz : cx;
                bool nearer = bullet.Dir switch
                {
                    Dir.Up or Dir.Left => line > hitLine,
                    _ => hitLine == int.MinValue || line < hitLine,
                };
                if (nearer) hitLine = line;
            }
        }
        if (hitLine == int.MinValue) return false;

        int center = vertical ? bullet.X : bullet.Z;
        int a0 = IntMath.FloorDiv(center - Units.BrickStripHalfWidth, Units.Cell);
        int a1 = IntMath.FloorDiv(center + Units.BrickStripHalfWidth - 1, Units.Cell);
        int step = bullet.Dir is Dir.Up or Dir.Left ? -1 : 1;
        bool destroyed = false;
        for (int k = 0; k < depth; k++)
        {
            int line = hitLine + k * step;
            for (int a = a0; a <= a1; a++)
            {
                int cx = vertical ? a : line, cz = vertical ? line : a;
                CellType c = map.Get(cx, cz);
                if (c == CellType.Brick || (c == CellType.Steel && bullet.Power && map.InBounds(cx, cz)))
                {
                    map.Set(cx, cz, CellType.Empty);
                    destroyed = true;
                }
            }
        }
        return destroyed;
    }
}

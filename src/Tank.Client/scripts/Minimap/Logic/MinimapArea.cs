using System.Numerics;
using Tank.Sim.Maps;

namespace Tank.Client.Minimap;

/// <summary>
/// The square of ground the map shows: the playable rectangle (Border in from each edge of the terrain) grown to a square
/// about its centre. Unit coordinates run 0..1 across it: u = +x to the right, v = +z downwards (image row 0 is the
/// smallest z), as the "map" camera preset sees the ground.
/// </summary>
public readonly record struct MinimapArea(float MinX, float MinZ, float Size)
{
    /// <summary>
    /// <paramref name="field"/>'s playable rectangle, its longer side kept and its shorter one grown equally at both ends,
    /// so the square shares the rectangle's centre (both catalog maps are square already).
    /// </summary>
    public static MinimapArea Of(Battlefield field)
    {
        float border = field.Border;
        float width = field.Terrain.SizeX - 2 * border, depth = field.Terrain.SizeZ - 2 * border;
        float size = MathF.Max(width, depth);
        return new MinimapArea(border + (width - size) / 2, border + (depth - size) / 2, size);
    }

    /// <summary>Ground (x, z) to unit (u, v); ground outside the square gives values outside 0..1.</summary>
    public Vector2 ToUnit(Vector2 ground) => new((ground.X - MinX) / Size, (ground.Y - MinZ) / Size);

    /// <summary>Unit (u, v) back to ground (x, z).</summary>
    public Vector2 ToGround(Vector2 unit) => new(MinX + unit.X * Size, MinZ + unit.Y * Size);

    /// <summary>The ground one pixel covers, m, when the square is drawn <paramref name="pixels"/> wide.</summary>
    public float MetresPerPixel(int pixels) => Size / pixels;
}

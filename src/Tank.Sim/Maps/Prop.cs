using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Maps;

public enum PropKind : byte
{
    Rock = 0,
    /// <summary>Tall grass a tank can hide in: no collision, shells and aim rays pass through. Round footprint.</summary>
    Bush = 1,
    /// <summary>A 2 m segment of brick wall: stops tanks and shells until it is shot (or a fast heavy tank rams it) to rubble.</summary>
    BrickWall = 2,
    /// <summary>A 2 m segment of stone wall: like brick, but three times as tough and no tank can ram it down.</summary>
    StoneWall = 3,
    /// <summary>A 2 m wooden fence panel: stops tanks until one touches it; shells fly through and knock it down.</summary>
    Fence = 4,
}

/// <summary>
/// A static object: a ground footprint extruded from <see cref="Bottom"/> up by <see cref="Height"/>. Walls and fences run
/// along the footprint's <see cref="Obb2.AxisRight"/> and are thin along <see cref="Obb2.AxisForward"/> (see <see cref="WallLine"/>).
/// Whether it still stands, and its hit points, live in <see cref="Battlefield"/>.
/// </summary>
public readonly record struct Prop(int Id, PropKind Kind, Obb2 Footprint, float Bottom, float Height)
{
    /// <summary>Rubble spreads this far beyond each face of the wall it was.</summary>
    public const float RubbleSpread = 0.5f;

    public float Top => Bottom + Height;

    /// <summary>Blocks tanks while standing: rocks, walls and fences. Bushes don't; they only hide what is inside them.</summary>
    public bool IsSolid => Kind != PropKind.Bush;

    public bool IsWall => Kind is PropKind.BrickWall or PropKind.StoneWall;

    /// <summary>Stops shells and rays while standing: rocks and walls. Shells fly through fences and bushes, knocking them down.</summary>
    public bool StopsShells => Kind == PropKind.Rock || IsWall;

    /// <summary>Goes down to whatever reaches it, a shell flying on through: fences and bushes (destructible, and not stopping shells).</summary>
    public bool IsFlimsy => IsDestructible && !StopsShells;

    /// <summary>
    /// Hit points when intact: a brick wall 300, a stone wall 900; fences and bushes 1, since anything that reaches them
    /// knocks them down; rocks 0: they can't be destroyed.
    /// </summary>
    public int MaxDurability => Kind switch
    {
        PropKind.BrickWall => 300,
        PropKind.StoneWall => 900,
        PropKind.Fence or PropKind.Bush => 1,
        _ => 0,
    };

    public bool IsDestructible => MaxDurability > 0;

    /// <summary>Distance from <paramref name="point"/> to the prop's solid (a bush's is an upright cylinder); 0 inside.</summary>
    public float DistanceTo(Vector3 point)
    {
        var ground = new Vector2(point.X, point.Z);
        float across = Kind == PropKind.Bush
            ? MathF.Max(Vector2.Distance(ground, Footprint.Center) - Footprint.HalfExtents.X, 0f)
            : Footprint.DistanceToPoint(ground);
        float up = point.Y > Top ? point.Y - Top : point.Y < Bottom ? Bottom - point.Y : 0f;
        return MathF.Sqrt(across * across + up * up);
    }

    /// <summary>Where a wall's rubble lies once it is down: its footprint widened by <see cref="RubbleSpread"/> on both faces.</summary>
    public Obb2 RubbleFootprint => Footprint with { HalfExtents = Footprint.HalfExtents + new Vector2(0, RubbleSpread) };
}

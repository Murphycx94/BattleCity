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
    /// <summary>
    /// A house (riverland design §2.5): masonry like the walls (<see cref="Prop.IsWall"/>), 500 hit points; a medium or
    /// heavy tank faster than its <see cref="Vehicles.TankSpec.HouseRamSpeed"/> knocks it down whole. Its footprint runs
    /// along the ridge on <see cref="Obb2.AxisRight"/>, its front (the door) faces <see cref="Obb2.AxisForward"/>.
    /// </summary>
    House = 5,
    /// <summary>
    /// A tree (riverland design §2.4): its footprint is the trunk, a circle; a tank touching it or a shell through it knocks
    /// it down, neither held up. Its crown, <see cref="Prop.CrownPerTrunk"/> times as wide, hides tanks like a bush.
    /// </summary>
    Tree = 6,
}

/// <summary>
/// A static object: a ground footprint extruded from <see cref="Bottom"/> up by <see cref="Height"/>. Walls and fences run
/// along the footprint's <see cref="Obb2.AxisRight"/> and are thin along <see cref="Obb2.AxisForward"/> (see <see cref="WallLine"/>);
/// a house's ridge runs along it too.
/// Whether it still stands, and its hit points, live in <see cref="Battlefield"/>.
/// </summary>
public readonly record struct Prop(int Id, PropKind Kind, Obb2 Footprint, float Bottom, float Height)
{
    /// <summary>The last kind there is: a kind above it is unknown (the network checks it).</summary>
    public const PropKind LastKind = PropKind.Tree;

    /// <summary>Rubble spreads this far beyond each face of the wall it was (and each side of a house).</summary>
    public const float RubbleSpread = 0.5f;

    /// <summary>A tree's crown is this many times as wide as its trunk (riverland design §2.4).</summary>
    public const float CrownPerTrunk = 7f;

    public float Top => Bottom + Height;

    /// <summary>
    /// Stands in tanks' way while standing: rocks, walls, houses, fences and trees. Bushes don't; they only hide what is
    /// inside them. A fence or tree gives way to any tank that touches it (<see cref="Vehicles.TankMotion"/>), so it never holds one up.
    /// </summary>
    public bool IsSolid => Kind != PropKind.Bush;

    /// <summary>
    /// Masonry (riverland design §2.5): brick and stone walls, and houses. Shells strike it (direct and splash damage), it
    /// falls to rubble that slows tanks, and a barrel pushed into it fires point-blank into its face.
    /// </summary>
    public bool IsWall => Kind is PropKind.BrickWall or PropKind.StoneWall or PropKind.House;

    /// <summary>
    /// Stops shells and rays while standing: rocks and masonry (walls and houses). Shells fly through fences, bushes and
    /// trees, knocking them down.
    /// </summary>
    public bool StopsShells => Kind == PropKind.Rock || IsWall;

    /// <summary>Goes down to whatever reaches it, a shell flying on through: fences, bushes and trees (destructible, and not stopping shells).</summary>
    public bool IsFlimsy => IsDestructible && !StopsShells;

    /// <summary>Bushes and trees: the footprint is a circle of radius <c>Footprint.HalfExtents.X</c> (a tree's is its trunk).</summary>
    public bool IsRound => Kind is PropKind.Bush or PropKind.Tree;

    /// <summary>Hides tanks while standing (riverland design §2.4): bushes, and trees by their crowns (<see cref="FoliageRadius"/>).</summary>
    public bool IsFoliage => Kind is PropKind.Bush or PropKind.Tree;

    /// <summary>How far round its centre the foliage hides tanks: a tree's crown (<see cref="CrownPerTrunk"/> × its trunk), a bush's own radius.</summary>
    public float FoliageRadius => Kind == PropKind.Tree ? Footprint.HalfExtents.X * CrownPerTrunk : Footprint.HalfExtents.X;

    /// <summary>
    /// Hit points when intact: a brick wall 300, a stone wall 900, a house 500; fences, bushes and trees 1, since anything
    /// that reaches them knocks them down; rocks 0: they can't be destroyed.
    /// </summary>
    public int MaxDurability => Kind switch
    {
        PropKind.BrickWall => 300,
        PropKind.StoneWall => 900,
        PropKind.House => 500,
        PropKind.Fence or PropKind.Bush or PropKind.Tree => 1,
        _ => 0,
    };

    public bool IsDestructible => MaxDurability > 0;

    /// <summary>
    /// Distance from <paramref name="point"/> to the prop's solid (a round one's is an upright cylinder: a tree's is its
    /// trunk); 0 inside.
    /// </summary>
    public float DistanceTo(Vector3 point)
    {
        var ground = new Vector2(point.X, point.Z);
        float across = IsRound
            ? MathF.Max(Vector2.Distance(ground, Footprint.Center) - Footprint.HalfExtents.X, 0f)
            : Footprint.DistanceToPoint(ground);
        float up = point.Y > Top ? point.Y - Top : point.Y < Bottom ? Bottom - point.Y : 0f;
        return MathF.Sqrt(across * across + up * up);
    }

    /// <summary>
    /// Where masonry's rubble lies once it is down: a wall's footprint widened by <see cref="RubbleSpread"/> on both faces
    /// (its ends meet the next segment's), a house's on all four sides.
    /// </summary>
    public Obb2 RubbleFootprint => Footprint with
    {
        HalfExtents = Footprint.HalfExtents + (Kind == PropKind.House ? new Vector2(RubbleSpread, RubbleSpread) : new Vector2(0, RubbleSpread)),
    };
}

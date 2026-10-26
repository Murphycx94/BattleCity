using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Maps;
using SN = System.Numerics;

namespace Tank.Client.Combat;

/// <summary>
/// Effects for props (stage 2C design §4.2): a shell striking a wall directly (not a splash, <see cref="IsDirectHit"/>)
/// throws a few chips of it back towards the shooter; a wall coming down bursts into a big cloud of dust and chunks over
/// where it stood; a fence knocked down sheds wood chips; a bush shot away bursts into leaves from near its top; a tree
/// felled sheds bark chips in a cloud of its needles or leaves from its crown (riverland design §6.4); a house coming
/// down bursts into a big cloud of plaster dust and three waves of chunks, of its walls low down, off its roof and of its
/// walls from the ridge, and a shell striking one chips its walls (riverland design §6.5), each in that house's colours. The shell's own burst
/// (<see cref="GameEventType.ShellExploded"/>) plays as before.
/// </summary>
public static class PropFx
{
    private const float ChipSize = 1f;
    // Chips start this far off the struck face, back along the shell's flight: a little more than the debris emission
    // radius at ChipSize (0.3 m, BattleFx), so they fly out in front of the wall instead of from inside it.
    private const float ChipStandOff = 0.35f;
    private const float CollapseSize = 2f;
    private const float FenceSize = 0.9f;
    private const float LeafSize = 2.4f;
    private const float CollapseHeight = 1.2f;   // the cloud rises from about this high on the wall
    private const float FenceChipHeight = 0.6f;   // wood chips fly from about half-way up the panel
    private const float LeafBelowTop = 1f;
    private const float TreeSize = 2f;
    private const float CrownBelowTop = 3f;   // a felled tree's leaves burst from about this far below its top
    private const float HouseDustSize = 3.5f;
    private const float HouseDustHeight = 2f;
    private const float HouseChunkSize = 2.5f;
    private const float HouseWallChunks = 1f;   // the first wave of plaster flies from this high
    private const float HouseRoofChunks = 4f;   // the tiles from this high, the last wave of plaster from the ridge

    /// <summary>
    /// True for a <see cref="GameEventType.PropDamaged"/> from a shell striking the wall itself; false for a splash (an HE
    /// burst nearby), which carries no velocity, and for any other event.
    /// </summary>
    public static bool IsDirectHit(this in GameEvent e) => e.Type == GameEventType.PropDamaged && e.Velocity != SN.Vector3.Zero;

    /// <param name="forests">The map's forests (<c>BattleWorld.Forests</c>): a felled tree's leaves are its kind's.</param>
    public static void OnEvent(in GameEvent e, Battlefield field, BattleFx fx, IReadOnlyList<TreeFit.ConiferRegion> forests)
    {
        if (e.Type == GameEventType.PropDamaged)
        {
            if (e.IsDirectHit()) Chips(e, field.PropById(e.PropId), fx);
            return;
        }
        if (e.Type != GameEventType.PropDestroyed) return;
        Prop prop = field.PropById(e.PropId);
        Vector3 push = e.Velocity.ToGodot();
        switch (prop.Kind)
        {
            case PropKind.BrickWall or PropKind.StoneWall:
                fx.Debris(GroundUnder(prop) + Vector3.Up * CollapseHeight, push, Chunks(prop), Dust(prop.Kind), CollapseSize);
                break;
            case PropKind.Fence:
                fx.Debris(GroundUnder(prop) + Vector3.Up * FenceChipHeight, push, Palette.Wood.ToColor(), null, FenceSize);
                break;
            case PropKind.Bush:   // from near the top, so the burst shows over the bushes around it
                fx.Debris(prop.Footprint.Center.Ground(prop.Top - LeafBelowTop), push, Palette.BushLight.ToColor(), Palette.BushDark.ToColor(), LeafSize);
                break;
            case PropKind.House:
                House(prop, push, fx);
                break;
            case PropKind.Tree:   // bark chips in a cloud of the crown's needles or leaves
                SN.Vector3 crown = TreeFit.IsConifer(prop, forests) ? Palette.Needles : Palette.Leaves;
                fx.Debris(prop.Footprint.Center.Ground(prop.Top - CrownBelowTop), push, Palette.Bark.ToColor(), crown.ToColor(), TreeSize);
                break;
        }
    }

    /// <summary>
    /// Chips thrown back the way the shell came (its level flight reversed), from <see cref="ChipStandOff"/> in front of
    /// where it struck; straight up off the wall for a shell coming straight down.
    /// </summary>
    private static void Chips(in GameEvent hit, in Prop prop, BattleFx fx)
    {
        var back = new Vector3(-hit.Velocity.X, 0f, -hit.Velocity.Z);
        back = back.LengthSquared() > 1e-6f ? back.Normalized() : Vector3.Zero;
        fx.Debris(hit.Position.ToGodot() + back * ChipStandOff, back, Chunks(prop), null, ChipSize);
    }

    /// <summary>
    /// A house coming down: the dust cloud (with chunks of its walls in it), then the three waves of chunks, of its walls
    /// low down, of its roof and of its walls from the ridge, in its own colours (<see cref="HouseShapes.ColoursOf"/>).
    /// </summary>
    private static void House(in Prop prop, Vector3 push, BattleFx fx)
    {
        Vector3 ground = GroundUnder(prop);
        var (wallColour, roofColour) = HouseShapes.ColoursOf(HouseShapes.SeedFor(prop));
        Color wall = wallColour.ToColor();
        fx.Debris(ground + Vector3.Up * HouseDustHeight, push, wall, Palette.PlasterDust.ToColor(), HouseDustSize);
        fx.Debris(ground + Vector3.Up * HouseWallChunks, push, wall, null, HouseChunkSize);
        fx.Debris(ground + Vector3.Up * HouseRoofChunks, push, roofColour.ToColor(), null, HouseChunkSize);
        fx.Debris(prop.Footprint.Center.Ground(prop.Top), push, wall, null, HouseChunkSize);
    }

    /// <summary>The ground under a wall's, fence's or house's middle (its base, less the sink).</summary>
    private static Vector3 GroundUnder(in Prop prop) => prop.Footprint.Center.Ground(prop.Bottom + WallLine.Sink);

    /// <summary>The colour of a wall's or house's chips and chunks: its stone, brick, or a house's own walls.</summary>
    private static Color Chunks(in Prop prop) => (prop.Kind switch
    {
        PropKind.StoneWall => Palette.Stone,
        PropKind.House => HouseShapes.ColoursOf(HouseShapes.SeedFor(prop)).Wall,
        _ => Palette.Brick,
    }).ToColor();

    private static Color Dust(PropKind kind) => (kind == PropKind.StoneWall ? Palette.RockDust : Palette.BrickDust).ToColor();
}

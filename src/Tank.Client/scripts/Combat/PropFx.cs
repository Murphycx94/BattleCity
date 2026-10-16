using Godot;
using Tank.Client.Art;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Maps;
using SN = System.Numerics;

namespace Tank.Client.Combat;

/// <summary>
/// Effects for props (stage 2C design §4.2): a shell striking a wall directly (not a splash, <see cref="IsDirectHit"/>)
/// throws a few chips of it back towards the shooter; a wall coming down bursts into a big cloud of dust and chunks over
/// where it stood; a fence knocked down sheds wood chips; a bush shot away bursts into leaves from near its top. The
/// shell's own burst (<see cref="GameEventType.ShellExploded"/>) plays as before.
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

    /// <summary>
    /// True for a <see cref="GameEventType.PropDamaged"/> from a shell striking the wall itself; false for a splash (an HE
    /// burst nearby), which carries no velocity, and for any other event.
    /// </summary>
    public static bool IsDirectHit(this in GameEvent e) => e.Type == GameEventType.PropDamaged && e.Velocity != SN.Vector3.Zero;

    public static void OnEvent(in GameEvent e, Battlefield field, BattleFx fx)
    {
        if (e.Type == GameEventType.PropDamaged)
        {
            if (e.IsDirectHit()) Chips(e, field.PropById(e.PropId).Kind, fx);
            return;
        }
        if (e.Type != GameEventType.PropDestroyed) return;
        Prop prop = field.PropById(e.PropId);
        Vector3 push = e.Velocity.ToGodot();
        switch (prop.Kind)
        {
            case PropKind.BrickWall or PropKind.StoneWall:
                fx.Debris(GroundUnder(prop) + Vector3.Up * CollapseHeight, push, Chunks(prop.Kind), Dust(prop.Kind), CollapseSize);
                break;
            case PropKind.Fence:
                fx.Debris(GroundUnder(prop) + Vector3.Up * FenceChipHeight, push, Palette.Wood.ToColor(), null, FenceSize);
                break;
            case PropKind.Bush:   // from near the top, so the burst shows over the bushes around it
                fx.Debris(prop.Footprint.Center.Ground(prop.Top - LeafBelowTop), push, Palette.BushLight.ToColor(), Palette.BushDark.ToColor(), LeafSize);
                break;
        }
    }

    /// <summary>
    /// Chips thrown back the way the shell came (its level flight reversed), from <see cref="ChipStandOff"/> in front of
    /// where it struck; straight up off the wall for a shell coming straight down.
    /// </summary>
    private static void Chips(in GameEvent hit, PropKind kind, BattleFx fx)
    {
        var back = new Vector3(-hit.Velocity.X, 0f, -hit.Velocity.Z);
        back = back.LengthSquared() > 1e-6f ? back.Normalized() : Vector3.Zero;
        fx.Debris(hit.Position.ToGodot() + back * ChipStandOff, back, Chunks(kind), null, ChipSize);
    }

    /// <summary>The ground under a wall's or fence's middle (its base, less the sink).</summary>
    private static Vector3 GroundUnder(in Prop prop) => prop.Footprint.Center.Ground(prop.Bottom + WallLine.Sink);

    private static Color Chunks(PropKind kind) => (kind == PropKind.StoneWall ? Palette.Stone : Palette.Brick).ToColor();

    private static Color Dust(PropKind kind) => (kind == PropKind.StoneWall ? Palette.RockDust : Palette.BrickDust).ToColor();
}

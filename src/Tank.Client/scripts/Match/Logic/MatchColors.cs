using System.Numerics;
using Tank.Client.Art;
using Tank.Client.Vehicles;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>Whose a tank is, seen from the player's seat.</summary>
public enum Side : byte { Own, Ally, Enemy }

/// <summary>
/// How a capture bar is filled (stage 5 design §3.4): the own base's in danger red, at full strength or in the dark half
/// of its blink; the enemy base's in the ally blue-grey (it is us taking it), which never blinks.
/// </summary>
public enum CaptureFill : byte { Danger, DangerDim, Ally }

/// <summary>
/// Relative colours in a match (blueprint §4.1): the player's own tank is toy orange, teammates blue-grey like the
/// practice targets, everyone else red. Which side a tank is on depends on the mode: in a free-for-all everyone else is
/// an enemy even if the server's team numbers happen to coincide.
/// </summary>
public static class MatchColors
{
    /// <param name="ownTeam">The player's team as the scoreboard has it.</param>
    /// <param name="team">The other tank's team.</param>
    /// <param name="isOwn">Whether this is the player's own tank.</param>
    public static Side SideOf(BattleMode mode, byte ownTeam, byte team, bool isOwn)
    {
        if (isOwn) return Side.Own;
        return ModeRules.HasTeams(mode) && team == ownTeam ? Side.Ally : Side.Enemy;
    }

    /// <summary>The hull, turret and gun colour of a tank on <paramref name="side"/>.</summary>
    public static Vector3 Body(Side side) => side switch
    {
        Side.Own => Palette.TankBody,
        Side.Ally => Palette.AllyBody,
        _ => Palette.EnemyBody,
    };

    /// <summary>The colour of a tank's name and other HUD text about it.</summary>
    public static Vector3 Ui(Side side) => side switch
    {
        Side.Own => Palette.UiAccent,
        Side.Ally => Palette.AllyBody,
        _ => Palette.UiDanger,
    };

    /// <summary>
    /// The ring on the ground round base <paramref name="baseTeam"/> for a player on <paramref name="ownTeam"/> (a
    /// spectator's too): the own team's in the ally blue-grey, the other's in the enemy red, as their tanks are.
    /// </summary>
    public static Vector3 BaseRing(byte ownTeam, byte baseTeam) => Body(baseTeam == ownTeam ? Side.Ally : Side.Enemy);

    /// <summary>
    /// The fill of <paramref name="bar"/>: the own base's is <see cref="CaptureFill.Danger"/>, or
    /// <see cref="CaptureFill.DangerDim"/> while <paramref name="blinkDark"/> (the alarm is on and its blink in the dark
    /// half); the enemy base's is always <see cref="CaptureFill.Ally"/>.
    /// </summary>
    public static CaptureFill FillOf(in CaptureBar bar, bool blinkDark) =>
        !bar.OwnBase ? CaptureFill.Ally : blinkDark ? CaptureFill.DangerDim : CaptureFill.Danger;

    /// <summary>The colour of a capture bar's fill (the dim one is the same red, drawn fainter).</summary>
    public static Vector3 FillColor(CaptureFill fill) => fill == CaptureFill.Ally ? Ui(Side.Ally) : Palette.UiDanger;

    /// <summary>The paint a <c>TankModel</c> is built with: the side's body colour over the common tracks and hubs.</summary>
    public static TankPaint Paint(Side side) => new(Body(side), Palette.Track, Palette.TankHub);
}

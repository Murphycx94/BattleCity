using System.Numerics;
using Tank.Sim.Modes;
using Tank.Sim.Navigation;

namespace Tank.Sim.Ai;

/// <summary>
/// The zone in a royale (stage 6 design §3.4; stage 6A plan, task 6): the director reads the match's zone once a tick
/// (<see cref="Zone"/>) for its bots to run into the ring they must be in (<see cref="ZoneState.Target"/>: the next ring
/// while waiting, the ring being shrunk to while shrinking) and to wander inside it (see Bot.Zone.cs). The points a bot
/// runs for lie round the ring's centre, spread by tank id (<see cref="ZonePoint"/>).
/// </summary>
public sealed partial class BotDirector
{
    /// <summary>A bot runs for a point this share of the target ring's radius from its centre …</summary>
    public const float ZoneRunShare = 0.5f;

    /// <summary>… and counts as in once within this share of it (20 % clear of the edge); wander waypoints too.</summary>
    public const float ZoneInsideShare = 0.8f;

    /// <summary>The point moves to the nearest open cell within this, m.</summary>
    public const float ZoneSnap = 12f;

    /// <summary>
    /// A small ring counts as reached within this of its centre, m: "in" is within max(radius, this), "well inside" within
    /// max(<see cref="ZoneInsideShare"/> × radius, this). Of the schedule's rings only the last (radius 0) is that small:
    /// fifteen tanks don't fit within a few metres of its centre, and those standing there close the way for the rest
    /// (batch C review follow-up).
    /// </summary>
    public const float ZoneArriveRadius = 20f;

    /// <summary>In a ring smaller than this a bot holds once there instead of wandering, m.</summary>
    public const float ZoneWanderRadius = 40f;

    /// <summary>The straight distance is multiplied by this when no whole path to the point is held.</summary>
    public const float ZoneDetour = 1.3f;

    /// <summary>The share of <c>MaxForwardSpeed</c> the time to get there is worked out at.</summary>
    public const float ZoneSpeedShare = 0.6f;

    /// <summary>The time to get there plus this must leave the ring's end ahead, ticks; else the bot must run.</summary>
    public const int ZoneMarginTicks = 15 * SimConstants.TickRate;

    /// <summary>Bot tank id × this: the angle its point lies at, radians (consecutive ids land far apart round the ring).</summary>
    public const float GoldenAngle = 2.39996323f;

    /// <summary>A wander point inside a ring lies at most this share of its radius from the centre.</summary>
    public const float ZoneWanderShare = 0.6f;

    /// <summary>A bot tries at most this many points for one ring (running) or waypoints (wandering) before it settles.</summary>
    public const int ZoneTries = 8;

    /// <summary>
    /// A point in the ring (or a waypoint in it) a search found no way to is given up at once; the next is asked for after
    /// this, ticks. A run that has tried <see cref="ZoneTries"/> points holds this long, then starts over from the first.
    /// </summary>
    public const int ZoneRetryTicks = SimConstants.TickRate;

    private bool _royale;

    /// <summary>The match's zone as of this tick's <see cref="Update"/> (Phase None outside a royale or without rings).</summary>
    internal ZoneState Zone { get; private set; }

    /// <summary>The bots play the zone: a royale with its rings.</summary>
    internal bool ZoneOn => Zone.Phase != ZonePhase.None;

    /// <summary>Once in the constructor: whether the mode has a zone.</summary>
    private void SetUpZone() => _royale = ModeRules.HasZone(Rules.Mode);

    /// <summary>Once a tick, in <see cref="Update"/>: the rules' zone (no allocation).</summary>
    private void ReadZone() => Zone = _royale ? Rules.Zone : default;

    /// <summary>
    /// Whether a bot with <paramref name="distance"/> m to go at <paramref name="topSpeed"/> m/s must drop fighting to get
    /// into its ring: outside the safe circle already, or ceil(distance / (topSpeed × <see cref="ZoneSpeedShare"/>) × tick
    /// rate) plus <see cref="ZoneMarginTicks"/> reaches <paramref name="ticksToTarget"/> (<see cref="ZoneState.TicksToTarget"/>).
    /// A tank that can't move must always run.
    /// </summary>
    internal static bool MustRun(float distance, float topSpeed, int ticksToTarget, bool outsideCurrent)
    {
        if (outsideCurrent) return true;
        float speed = topSpeed * ZoneSpeedShare;
        if (!(speed > 0f)) return true;
        float ticks = MathF.Max(0f, distance) / speed * SimConstants.TickRate;
        if (!(ticks < int.MaxValue / 4)) return true;   // NaN or far beyond any match
        return (int)MathF.Ceiling(ticks) + ZoneMarginTicks >= ticksToTarget;
    }

    /// <summary>
    /// Whether <paramref name="p"/> counts as in <paramref name="ring"/> for a bot, where a run stops: within
    /// <see cref="InsideLimit"/> of its centre.
    /// </summary>
    internal static bool WellInside(Vector2 p, in ZoneRing ring)
    {
        float limit = InsideLimit(ring);
        return Vector2.DistanceSquared(p, ring.Centre) <= limit * limit;
    }

    /// <summary>Whether <paramref name="p"/> is in <paramref name="ring"/> at all: within max(radius, <see cref="ZoneArriveRadius"/>) of its centre.</summary>
    internal static bool InRing(Vector2 p, in ZoneRing ring)
    {
        float limit = MathF.Max(ring.Radius, ZoneArriveRadius);
        return Vector2.DistanceSquared(p, ring.Centre) <= limit * limit;
    }

    /// <summary>How near <paramref name="ring"/>'s centre a run stops: max(<see cref="ZoneInsideShare"/> × radius, <see cref="ZoneArriveRadius"/>), m.</summary>
    internal static float InsideLimit(in ZoneRing ring) => MathF.Max(ZoneInsideShare * ring.Radius, ZoneArriveRadius);

    /// <summary>
    /// How far along the segment from <paramref name="a"/> to <paramref name="b"/> it first comes within
    /// <paramref name="radius"/> of <paramref name="centre"/>: 0 when <paramref name="a"/> is, the whole length when it never does.
    /// </summary>
    internal static float ToCircle(Vector2 a, Vector2 b, Vector2 centre, float radius)
    {
        Vector2 d = b - a, f = a - centre;
        float c = f.LengthSquared() - radius * radius;
        if (c <= 0f) return 0f;
        float length = d.Length();
        if (length < 1e-6f) return length;
        Vector2 u = d / length;
        float along = -Vector2.Dot(f, u), disc = along * along - c;   // |f + t·u|² = r²: t² + 2t(f·u) + c = 0
        if (disc < 0f) return length;
        float t = along - MathF.Sqrt(disc);
        return t >= 0f && t <= length ? t : length;
    }

    /// <summary>
    /// The point in <paramref name="target"/> for <paramref name="bot"/> to run for (try <paramref name="attempt"/> 0, 1, …
    /// turns it by another <see cref="GoldenAngle"/>): at angle (tank id + attempt) × <see cref="GoldenAngle"/>,
    /// <see cref="ZoneRunShare"/> of the radius (of <see cref="ZoneArriveRadius"/> for a smaller ring) from the centre, moved
    /// to the nearest open cell within <see cref="ZoneSnap"/> that is still well inside (<see cref="WellInside"/>); else the
    /// centre's nearest open cell, if well inside; else the point itself.
    /// </summary>
    internal Vector2 ZonePoint(Bot bot, in ZoneRing target, int attempt)
    {
        float angle = (bot.TankId + attempt) * GoldenAngle;
        (float sin, float cos) = MathF.SinCos(angle);
        Vector2 p = target.Centre + new Vector2(cos, sin) * (ZoneRunShare * MathF.Max(target.Radius, ZoneArriveRadius));
        return SnapInside(p, target);
    }

    /// <summary>A random point for <paramref name="bot"/> to wander to in <paramref name="target"/> (its own random numbers), snapped as <see cref="ZonePoint"/>.</summary>
    internal Vector2 ZoneWanderPoint(Bot bot, in ZoneRing target)
    {
        float angle = bot.NextFloat() * MathF.Tau, share = bot.NextFloat() * ZoneWanderShare;
        (float sin, float cos) = MathF.SinCos(angle);
        return SnapInside(target.Centre + new Vector2(cos, sin) * (share * target.Radius), target);
    }

    /// <summary><paramref name="p"/>'s nearest open cell within <see cref="ZoneSnap"/> when well inside <paramref name="ring"/>, else the centre's, else <paramref name="p"/>.</summary>
    private Vector2 SnapInside(Vector2 p, in ZoneRing ring)
    {
        if (Grid.TryNearestOpen(p, ZoneSnap, out int i, out int j) && WellInside(NavGrid.CentreOf(i, j), ring)) return NavGrid.CentreOf(i, j);
        if (Grid.TryNearestOpen(ring.Centre, ZoneSnap, out i, out j) && WellInside(NavGrid.CentreOf(i, j), ring)) return NavGrid.CentreOf(i, j);
        return p;
    }

    /// <summary>"12 zone runs; " in a royale, else nothing (for <see cref="Summary"/>).</summary>
    private string ZoneRunsText()
    {
        if (!_royale) return "";
        int runs = 0;
        foreach (Bot bot in _bots) runs += bot.ZoneRuns;
        return $"{runs} zone runs; ";
    }
}

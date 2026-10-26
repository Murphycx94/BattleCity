using System.Globalization;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>One progress bar under the plate: whose base, and its points (1–100).</summary>
public readonly record struct CaptureBar(bool OwnBase, byte Points);

/// <summary>The bars to show, own base first: none outside a standard match, and only bases with points.</summary>
public readonly record struct CaptureBars(int Count, CaptureBar First, CaptureBar Second)
{
    public static readonly CaptureBars None = default;

    /// <summary>0 → <see cref="First"/>, 1 → <see cref="Second"/>; <see cref="ArgumentOutOfRangeException"/> at or beyond <see cref="Count"/>.</summary>
    public CaptureBar this[int index] => (uint)index < (uint)Count
        ? index == 0 ? First : Second
        : throw new ArgumentOutOfRangeException(nameof(index), index, "There is no such bar.");
}

/// <summary>The capture bars' contents and words (stage 5 design §3.4), none of it touching the engine.</summary>
public static class CaptureText
{
    private const string OwnBase = "我方基地", EnemyBase = "敌方基地";

    /// <summary>
    /// The bars for <paramref name="match"/>: only in a standard match; the own base is <paramref name="ownTeam"/>'s
    /// (<c>BaseProgress0</c> or <c>BaseProgress1</c>), listed before the enemy's; a base at 0 is left out. Allocates nothing.
    /// </summary>
    public static CaptureBars Bars(BattleMode mode, byte ownTeam, in MatchBlock match)
    {
        if (mode != BattleMode.Standard) return CaptureBars.None;
        byte own = ownTeam == 0 ? match.BaseProgress0 : match.BaseProgress1;
        byte enemy = ownTeam == 0 ? match.BaseProgress1 : match.BaseProgress0;
        var ownBar = new CaptureBar(true, own);
        var enemyBar = new CaptureBar(false, enemy);
        if (own > 0) return enemy > 0 ? new CaptureBars(2, ownBar, enemyBar) : new CaptureBars(1, ownBar, default);
        return enemy > 0 ? new CaptureBars(1, enemyBar, default) : CaptureBars.None;
    }

    /// <summary>"我方基地 37" / "敌方基地 62" (allocates: call it when the bar changed).</summary>
    public static string Label(in CaptureBar bar) =>
        string.Create(CultureInfo.InvariantCulture, $"{(bar.OwnBase ? OwnBase : EnemyBase)} {bar.Points}");
}

/// <summary>
/// Whether the own base is being taken right now: its points rose within <see cref="HoldSeconds"/>. The first reading
/// only sets the baseline, so a player who joins or reconnects while the own base already holds points sees no blink
/// until they rise again.
/// </summary>
public sealed class CaptureAlarm
{
    /// <summary>
    /// How long a rise keeps the alarm on. A snapshot raises the points by at most a few a second, so 1.5 s bridges two
    /// rises: the bar blinks for as long as the base is being taken and stops within 1.5 s after.
    /// </summary>
    public const double HoldSeconds = 1.5;

    /// <summary>The blink's rate: a square wave between full and dim, this many periods a second.</summary>
    public const double BlinkHz = 2.0;

    private bool _primed;
    private byte _last;
    private double _risenAt = double.NegativeInfinity;

    /// <summary>
    /// Feeds the own base's points at <paramref name="now"/> (seconds on any steady clock) and returns whether to blink:
    /// a rise starts the hold, a drop to a lower value or to 0 ends it at once. The first call only records the points.
    /// </summary>
    public bool Update(byte ownBasePoints, double now)
    {
        if (!_primed)
        {
            _primed = true;
            _last = ownBasePoints;
            return false;
        }
        if (ownBasePoints > _last) _risenAt = now;
        else if (ownBasePoints < _last || ownBasePoints == 0) _risenAt = double.NegativeInfinity;
        _last = ownBasePoints;
        return now - _risenAt < HoldSeconds;
    }

    /// <summary>
    /// <see cref="Update(byte, double)"/> with the own base's points taken from <paramref name="bars"/> (its first bar when
    /// that is the own base's, else 0: the own base has no points). The enemy base's points never count.
    /// </summary>
    public bool Update(in CaptureBars bars, double now) =>
        Update(bars.Count > 0 && bars.First.OwnBase ? bars.First.Points : (byte)0, now);

    /// <summary>The second half of each blink period; at 0 (the gallery's frozen clock) the bar is in its bright half.</summary>
    public static bool IsDarkHalf(double now) => ((long)Math.Floor(now * BlinkHz * 2) & 1) == 1;
}

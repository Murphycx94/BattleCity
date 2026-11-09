using System.Globalization;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// One frame's zone for the HUD (stage 6 design §3.3): off outside a royale (<see cref="On"/> false, everything else 0);
/// <see cref="Ring"/> and <see cref="Phase"/> as <see cref="ZoneState"/> has them, <see cref="SecondsLeft"/> the waiting or
/// shrinking left rounded up like the match clock, <see cref="Outside"/> when the own live tank is out of the safe circle
/// (the scene decides that), <see cref="Percent"/> what being outside costs a second now. A plain value: a view redraws
/// its text only when the frame's differs from the last.
/// </summary>
public readonly record struct ZoneHud(bool On, int Ring, ZonePhase Phase, int SecondsLeft, bool Outside, int Percent)
{
    /// <summary><paramref name="state"/> as the HUD shows it: SecondsLeft = <see cref="HudText.CeilSeconds"/>(TicksLeft), Percent = DamagePercent; off for Phase None.</summary>
    public static ZoneHud From(in ZoneState state, bool outside) =>
        state.Phase == ZonePhase.None ? default : new ZoneHud(true, state.Ring, state.Phase, HudText.CeilSeconds(state.TicksLeft), outside, state.DamagePercent);
}

/// <summary>The zone line's and the outside warning's words. Every method allocates: call them when the values changed.</summary>
public static class ZoneText
{
    public const string ClosedLine = "毒圈已闭合";

    /// <summary>U+2212, the minus sign: a hyphen reads as a dash in the warning's font.</summary>
    public const char Minus = '−';

    /// <summary>"毒圈 2/5 · 0:45 后缩小" (waiting), "毒圈缩小中 0:30" (shrinking), <see cref="ClosedLine"/>, "" when off.</summary>
    public static string Line(in ZoneHud zone)
    {
        if (!zone.On) return string.Empty;
        return zone.Phase switch
        {
            ZonePhase.Waiting => string.Create(CultureInfo.InvariantCulture, $"毒圈 {zone.Ring}/{ZoneSchedule.Rings} · {HudText.Clock(zone.SecondsLeft)} 后缩小"),
            ZonePhase.Shrinking => $"毒圈缩小中 {HudText.Clock(zone.SecondsLeft)}",
            _ => ClosedLine,
        };
    }

    /// <summary>"在安全区外 −2%/秒" (with <see cref="Minus"/>).</summary>
    public static string Outside(int percent) => string.Create(CultureInfo.InvariantCulture, $"在安全区外 {Minus}{percent}%/秒");
}

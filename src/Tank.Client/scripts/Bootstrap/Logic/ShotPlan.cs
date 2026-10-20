using System.Globalization;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Client.Bootstrap;

/// <summary>A moment a timed capture step can wait for, each noted the first time it happens.</summary>
public enum ShotMoment
{
    /// <summary>The run began (always already seen, at time 0).</summary>
    Start,
    /// <summary>The player fired.</summary>
    Fire,
    /// <summary>The player's shell struck a tank.</summary>
    Hit,
    /// <summary>The player destroyed a tank.</summary>
    Kill,
    /// <summary>The player's shell hurt a wall.</summary>
    Prop,
    /// <summary>The player destroyed a prop (a wall, a fence, a bush).</summary>
    Down,
    /// <summary>The player's own tank was destroyed (a match only).</summary>
    Death,
    /// <summary>The results came up (a match only).</summary>
    Ended,
}

/// <summary>
/// The timetable of a scripted run (blueprint §5 T9), pure logic shared by the sandbox and the match; <c>CaptureRun</c>
/// executes it. The screenshot is due a delay after one moment: the one <c>--shot-after EVENT:SECONDS</c> names, the
/// player's first shot with <c>--fx-shot SECONDS</c> (where nothing before a <see cref="FxShotWarmup"/> counts), or the
/// run's start (<c>--screenshot-at</c>, the default). The quit is due at <c>--quit-after</c>. Moments come from the
/// game events (<see cref="OnEvent"/>) or straight from the caller (<see cref="OnMoment"/>: the results screen is no
/// event, and the own shot in a match is seen in the predicted shot count). A <c>--shot-after</c> this plan can't map,
/// or one its run can never reach (<c>death</c> in the sandbox, say, or <c>ended</c> later than the results stay up), is
/// a line in <see cref="Warnings"/>, never silently swallowed; an unmappable one falls back to the start schedule.
/// A run takes one screenshot: a plan told it was already taken elsewhere (<see cref="MarkScreenshotTaken"/>) takes none.
/// </summary>
public sealed class ShotPlan
{
    /// <summary>--fx-shot: moments before this many seconds don't count, so the first shot caught is a warmed-up one.</summary>
    public const double FxShotWarmup = 3;

    /// <summary>How long the results stay up before the server sends everyone back to the room (and the match scene goes).</summary>
    public const double ResultsStaySeconds = ModeRules.ResultsTicks / (double)SimConstants.TickRate;

    private static readonly ShotMoment[] SandboxMoments = { ShotMoment.Fire, ShotMoment.Hit, ShotMoment.Kill, ShotMoment.Prop, ShotMoment.Down };
    private static readonly ShotMoment[] MatchMoments = { ShotMoment.Fire, ShotMoment.Hit, ShotMoment.Kill, ShotMoment.Death, ShotMoment.Ended };

    private readonly double[] _firstSeen = NotSeenYet();   // per ShotMoment: when it first happened, s; -1 not yet
    private readonly double _countFrom;
    private readonly double _quitAfter;
    private readonly ShotMoment _shotAfter;
    private readonly double _shotDelay;
    private bool _screenshotTaken;

    private ShotPlan(LaunchOptions options, string where, ShotMoment[] supported)
    {
        var warnings = new List<string>();
        _countFrom = options.FxShotDelay is null ? 0 : FxShotWarmup;
        _quitAfter = options.QuitAfter;
        (_shotAfter, _shotDelay) = PlanScreenshot(options, where, supported, warnings);
        Warnings = warnings;
    }

    /// <summary>A sandbox run's plan: the player can't die and nothing ends, so <c>death</c> and <c>ended</c> warn.</summary>
    public static ShotPlan ForSandbox(LaunchOptions options) => new(options, "the sandbox", SandboxMoments);

    /// <summary>A match's plan: walls and props aren't the point there, so <c>prop</c> and <c>down</c> warn.</summary>
    public static ShotPlan ForMatch(LaunchOptions options) => new(options, "a match", MatchMoments);

    /// <summary>One line per capture option that can't work as written, for the caller to report.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Whether the screenshot is a plain <c>--screenshot-at</c> (no <c>--shot-after</c>, no <c>--fx-shot</c>): the only
    /// kind <c>Main</c> takes itself, on the process clock, in the menu, the room or the HUD gallery before any match.
    /// </summary>
    public static bool StartScheduleOnly(LaunchOptions options) => options.ShotAfterEvent is null && options.FxShotDelay is null;

    /// <summary>Whether this plan's screenshot is spent: taken here, or marked taken elsewhere.</summary>
    public bool ScreenshotTaken => _screenshotTaken;

    /// <summary>The run's one screenshot was taken by someone else (Main in the room, an earlier match): this plan takes none.</summary>
    public void MarkScreenshotTaken() => _screenshotTaken = true;

    /// <summary>Whether <paramref name="moment"/> has already happened (the range demo fires once, then stops).</summary>
    public bool Seen(ShotMoment moment) => _firstSeen[(int)moment] >= 0;

    /// <summary>
    /// Notes the first time the player (<paramref name="ownTankId"/>) fired, hit or destroyed a tank, hurt a wall or
    /// destroyed a prop — and the first time the player's own tank was destroyed, whoever did it.
    /// </summary>
    public void OnEvent(in GameEvent e, int ownTankId, double time)
    {
        if (e.Type == GameEventType.TankDestroyed && e.TargetTankId == ownTankId) OnMoment(ShotMoment.Death, time);
        if (e.TankId != ownTankId) return;
        ShotMoment? moment = e.Type switch
        {
            GameEventType.ShellFired => ShotMoment.Fire,
            GameEventType.TankHit => ShotMoment.Hit,
            GameEventType.TankDestroyed => ShotMoment.Kill,
            GameEventType.PropDamaged => ShotMoment.Prop,
            GameEventType.PropDestroyed => ShotMoment.Down,
            _ => null,
        };
        if (moment is { } m) OnMoment(m, time);
    }

    /// <summary>Notes a moment the caller saw itself (the results screen, a predicted own shot); only the first time counts.</summary>
    public void OnMoment(ShotMoment moment, double time)
    {
        if (time >= _countFrom && _firstSeen[(int)moment] < 0) _firstSeen[(int)moment] = time;
    }

    /// <summary>True exactly once, the frame the screenshot falls due.</summary>
    public bool TakeScreenshot(double time)
    {
        if (_screenshotTaken || !ScreenshotDue(time)) return false;
        _screenshotTaken = true;
        return true;
    }

    /// <summary>Whether <c>--quit-after</c> has run out.</summary>
    public bool QuitDue(double time) => _quitAfter > 0 && time >= _quitAfter;

    /// <summary>--shot-after waits for its moment, --fx-shot for the player's first shot, otherwise --screenshot-at counts from the start.</summary>
    private static (ShotMoment After, double Delay) PlanScreenshot(LaunchOptions options, string where, ShotMoment[] supported, List<string> warnings)
    {
        if (options.ShotAfterEvent is { } awaited)
        {
            if (TryParseMoment(awaited, out ShotMoment moment))
            {
                if (Array.IndexOf(supported, moment) < 0)
                    warnings.Add($"--shot-after '{awaited}' never happens in {where}; the screenshot will not be taken.");
                else if (moment == ShotMoment.Ended && options.ShotAfterDelay >= ResultsStaySeconds)
                    warnings.Add(string.Create(CultureInfo.InvariantCulture,
                        $"--shot-after ended:{options.ShotAfterDelay} falls after the results have gone (they stay {ResultsStaySeconds} s); the screenshot will not be taken."));
                return (moment, options.ShotAfterDelay);
            }
            warnings.Add($"Unknown --shot-after moment '{awaited}'; use fire|hit|kill|prop|down|death|ended. Using --screenshot-at.");
        }
        if (options.FxShotDelay is { } fxDelay) return (ShotMoment.Fire, fxDelay);
        return (ShotMoment.Start, options.ScreenshotAt);
    }

    private static bool TryParseMoment(string name, out ShotMoment moment)
    {
        (bool known, moment) = name switch
        {
            "fire" => (true, ShotMoment.Fire),
            "hit" => (true, ShotMoment.Hit),
            "kill" => (true, ShotMoment.Kill),
            "prop" => (true, ShotMoment.Prop),
            "down" => (true, ShotMoment.Down),
            "death" => (true, ShotMoment.Death),
            "ended" => (true, ShotMoment.Ended),
            _ => (false, ShotMoment.Start),
        };
        return known;
    }

    private bool ScreenshotDue(double time)
    {
        double since = _firstSeen[(int)_shotAfter];
        return since >= 0 && time >= since + _shotDelay;
    }

    /// <summary>Every moment unseen (-1), but the start, which is at 0.</summary>
    private static double[] NotSeenYet()
    {
        var seen = new double[Enum.GetValues<ShotMoment>().Length];
        Array.Fill(seen, -1);
        seen[(int)ShotMoment.Start] = 0;
        return seen;
    }
}

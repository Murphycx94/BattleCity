using Tank.Client.Bootstrap;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class ShotPlanTests
{
    private const int Own = 1;
    private const int Other = 2;

    private static ShotPlan Sandbox(params string[] args) => ShotPlan.ForSandbox(LaunchOptions.Parse(args));
    private static ShotPlan Match(params string[] args) => ShotPlan.ForMatch(LaunchOptions.Parse(args));

    private static GameEvent Event(GameEventType type, int tankId = Own, int targetTankId = 0) =>
        new(type, Tick: 1, TankId: tankId, ShellId: 1, Position: default, Velocity: default, TargetTankId: targetTankId);

    [Fact]
    public void Screenshot_at_counts_from_the_start()
    {
        ShotPlan plan = Sandbox("--screenshot", "s.png", "--screenshot-at", "6");
        Assert.False(plan.TakeScreenshot(5.9));
        Assert.True(plan.TakeScreenshot(6.0));
        Assert.False(plan.TakeScreenshot(7.0));   // taken once, never again
        Assert.Empty(plan.Warnings);
    }

    [Theory]
    [InlineData("fire", GameEventType.ShellFired)]
    [InlineData("hit", GameEventType.TankHit)]
    [InlineData("kill", GameEventType.TankDestroyed)]
    [InlineData("prop", GameEventType.PropDamaged)]
    [InlineData("down", GameEventType.PropDestroyed)]
    public void Shot_after_waits_for_the_players_first_event_plus_the_delay(string moment, GameEventType type)
    {
        ShotPlan plan = Sandbox("--shot-after", $"{moment}:2");
        Assert.False(plan.TakeScreenshot(100));              // the moment hasn't happened, however long the run
        plan.OnEvent(Event(type, tankId: Other), Own, 3);    // somebody else's doesn't count
        Assert.False(plan.TakeScreenshot(100));
        plan.OnEvent(Event(type), Own, 4);
        Assert.False(plan.TakeScreenshot(5.9));
        Assert.True(plan.TakeScreenshot(6.0));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Only_the_first_moment_counts()
    {
        ShotPlan plan = Sandbox("--shot-after", "fire:1");
        plan.OnEvent(Event(GameEventType.ShellFired), Own, 2);
        plan.OnEvent(Event(GameEventType.ShellFired), Own, 50);   // a later shot doesn't push the screenshot back
        Assert.True(plan.TakeScreenshot(3));
        Assert.True(plan.Seen(ShotMoment.Fire));
    }

    [Fact]
    public void Death_is_the_own_tank_destroyed_by_anyone()
    {
        ShotPlan plan = Match("--shot-after", "death:1");
        plan.OnEvent(Event(GameEventType.TankDestroyed, tankId: Own, targetTankId: Other), Own, 2);   // the player's kill, not a death
        Assert.False(plan.TakeScreenshot(100));
        plan.OnEvent(Event(GameEventType.TankDestroyed, tankId: Other, targetTankId: Own), Own, 5);
        Assert.False(plan.TakeScreenshot(5.9));
        Assert.True(plan.TakeScreenshot(6.0));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Ended_comes_straight_from_the_caller()
    {
        ShotPlan plan = Match("--shot-after", "ended:3");
        Assert.False(plan.TakeScreenshot(100));
        plan.OnMoment(ShotMoment.Ended, 20);
        plan.OnMoment(ShotMoment.Ended, 21);   // the next frame's repeat doesn't move it
        Assert.False(plan.TakeScreenshot(22.9));
        Assert.True(plan.TakeScreenshot(23.0));
        Assert.Empty(plan.Warnings);
    }

    [Theory]
    [InlineData(8.0)]    // the results stay 8 s: the scene is gone by then
    [InlineData(12.5)]
    public void Ended_later_than_the_results_stay_warns_that_it_can_never_be_taken(double delay)
    {
        ShotPlan plan = Match("--shot-after", $"ended:{delay.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        string warning = Assert.Single(plan.Warnings);
        Assert.Contains("--shot-after ended:", warning);
        Assert.Contains($"{ShotPlan.ResultsStaySeconds} s", warning);
    }

    [Fact]
    public void Ended_within_the_results_stay_is_fine_and_the_stay_is_the_servers()
    {
        Assert.Equal(ModeRules.ResultsTicks / (double)SimConstants.TickRate, ShotPlan.ResultsStaySeconds);
        Assert.Empty(Match("--shot-after", "ended:7.5").Warnings);
    }

    [Fact]
    public void A_screenshot_taken_elsewhere_is_not_taken_again()
    {
        ShotPlan plan = Match("--screenshot", "s.png", "--screenshot-at", "6");
        Assert.False(plan.ScreenshotTaken);
        plan.MarkScreenshotTaken();   // Main took it in the room, or an earlier match did
        Assert.True(plan.ScreenshotTaken);
        Assert.False(plan.TakeScreenshot(6.0));
        Assert.False(plan.TakeScreenshot(100));
    }

    [Fact]
    public void Taking_the_screenshot_marks_it_taken_for_the_next_scene()
    {
        ShotPlan plan = Match("--screenshot", "s.png", "--screenshot-at", "2");
        Assert.True(plan.TakeScreenshot(2));
        Assert.True(plan.ScreenshotTaken);
    }

    [Theory]
    [InlineData(true, "--screenshot", "s.png")]
    [InlineData(true, "--screenshot", "s.png", "--screenshot-at", "20")]
    [InlineData(false, "--screenshot", "s.png", "--shot-after", "kill:2")]
    [InlineData(false, "--screenshot", "s.png", "--fx-shot", "0.5")]
    public void Only_a_plain_screenshot_at_is_on_the_start_schedule_that_Main_can_take(bool expected, params string[] args)
    {
        Assert.Equal(expected, ShotPlan.StartScheduleOnly(LaunchOptions.Parse(args)));
    }

    [Fact]
    public void Fx_shot_waits_for_the_first_shot_after_the_warmup()
    {
        ShotPlan plan = Sandbox("--fx-shot", "1.5");
        plan.OnEvent(Event(GameEventType.ShellFired), Own, 2);   // inside the warm-up: doesn't count
        Assert.False(plan.Seen(ShotMoment.Fire));
        Assert.False(plan.TakeScreenshot(100));
        plan.OnEvent(Event(GameEventType.ShellFired), Own, 4);
        Assert.True(plan.Seen(ShotMoment.Fire));
        Assert.False(plan.TakeScreenshot(5.4));
        Assert.True(plan.TakeScreenshot(5.5));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Quit_after_runs_out_and_zero_means_never()
    {
        ShotPlan plan = Sandbox("--quit-after", "12");
        Assert.False(plan.QuitDue(11.9));
        Assert.True(plan.QuitDue(12.0));
        Assert.False(Sandbox().QuitDue(100000));
    }

    [Fact]
    public void A_moment_the_sandbox_never_reaches_warns_and_never_fires()
    {
        ShotPlan plan = Sandbox("--shot-after", "ended:1");
        string warning = Assert.Single(plan.Warnings);
        Assert.Contains("'ended'", warning);
        Assert.Contains("the sandbox", warning);
        Assert.False(plan.TakeScreenshot(100));
    }

    [Fact]
    public void A_moment_a_match_never_reaches_warns_too()
    {
        ShotPlan plan = Match("--shot-after", "prop:1");
        Assert.Contains("'prop'", Assert.Single(plan.Warnings));
        plan.OnEvent(Event(GameEventType.PropDamaged), Own, 2);
        Assert.True(plan.TakeScreenshot(3));   // the moment is still honoured if it does happen
    }

    [Fact]
    public void An_unknown_moment_warns_and_falls_back_to_the_start_schedule()
    {
        // LaunchOptions rejects these itself; a plan handed one anyway must warn, not throw or go silent.
        var options = LaunchOptions.Parse(new[] { "--screenshot-at", "4" }) with { ShotAfterEvent = "wall", ShotAfterDelay = 1 };
        ShotPlan plan = ShotPlan.ForSandbox(options);
        string warning = Assert.Single(plan.Warnings);
        Assert.Contains("'wall'", warning);
        Assert.Contains("fire|hit|kill|prop|down|death|ended", warning);
        Assert.False(plan.TakeScreenshot(3.9));
        Assert.True(plan.TakeScreenshot(4.0));
    }
}

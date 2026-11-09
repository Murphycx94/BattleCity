using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Testing;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Two stalemates of whole matches (5C stalemate fix 2): survivors out of AP that never fired their HE, and enemies a few
/// metres apart inside one base circle, each too close for its gun to dip onto the other's hull centre. Out of AP a bot
/// fires HE, worked out for HE's own speed; too close for the hull centre it lays on the turret (a casemate's upper hull);
/// too close for that too it backs away until it can lay, inside the circle it is capturing if it can.
/// </summary>
public sealed class BotFireTests
{
    private const int Tps = SimConstants.TickRate;

    private readonly ITestOutputHelper _output;

    public BotFireTests(ITestOutputHelper output) => _output = output;

    private static IEnumerable<GameEvent> ShotsBy(BotArena arena, int tankId) =>
        arena.Events.Where(e => e.Type == GameEventType.ShellFired && e.TankId == tankId);

    private static bool HitThisTick(BotArena arena, int shooter, int target) =>
        arena.Sim.Events.ToArray().Any(e => e.Type is GameEventType.TankHit or GameEventType.SplashDamage && e.TankId == shooter && e.TargetTankId == target);

    /// <summary>A bot 150 m west of an idle medium, facing it, as <c>BotDriveTests</c> has one stop and fire.</summary>
    private static (BotArena Arena, Bot Bot, int Enemy) Duel()
    {
        var arena = new BotArena();
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 1, new Vector2(150, 200), BotArena.East);
        int enemy = arena.Idle(TankCatalog.Medium, 1, new Vector2(300, 200), BotArena.North);
        return (arena, bot, enemy);
    }

    [Fact]
    public void Out_of_ap_it_fires_he_and_hits()
    {
        var (arena, bot, enemy) = Duel();
        arena.Sim.TankRef(bot.TankId).ApLeft = 0;
        arena.Run(15 * Tps, _ => Assert.Equal(AmmoType.HE, arena.LastInput(bot.TankId).Ammo));   // never back to AP: no reload thrown away

        GameEvent[] shots = ShotsBy(arena, bot.TankId).ToArray();
        _output.WriteLine($"{shots.Length} HE shots; hits {arena.Events.Count(e => e.Type == GameEventType.TankHit && e.TankId == bot.TankId)}");
        Assert.NotEmpty(shots);
        Assert.All(shots, e => Assert.Equal(AmmoType.HE, e.Ammo));
        Assert.Contains(arena.Events, e => e.Type == GameEventType.TankHit && e.TankId == bot.TankId && e.TargetTankId == enemy && e.Ammo == AmmoType.HE);
        Assert.Equal(0, arena.Tank(bot.TankId).ApLeft);
        Assert.Equal(TankCatalog.Medium.He.Capacity - shots.Length, arena.Tank(bot.TankId).HeLeft);
    }

    [Fact]
    public void With_ap_left_it_fires_ap_and_keeps_its_he()
    {
        var (arena, bot, enemy) = Duel();
        arena.Run(15 * Tps, _ => Assert.Equal(AmmoType.AP, arena.LastInput(bot.TankId).Ammo));

        GameEvent[] shots = ShotsBy(arena, bot.TankId).ToArray();
        Assert.NotEmpty(shots);
        Assert.All(shots, e => Assert.Equal(AmmoType.AP, e.Ammo));
        Assert.Contains(arena.Events, e => e.Type == GameEventType.TankHit && e.TankId == bot.TankId && e.TargetTankId == enemy);
        Assert.Equal(TankCatalog.Medium.He.Capacity, arena.Tank(bot.TankId).HeLeft);
    }

    /// <summary>
    /// A bot holding where it stands (an attacker whose end is its own spot), facing east, and an idle enemy broadside
    /// <paramref name="distance"/> m east of it on the flat arena; in a standard battle, inside the enemy base.
    /// </summary>
    private static (BotArena Arena, Bot Bot, int Enemy) PointBlank(TankSpec shooter, TankSpec target, float distance)
    {
        var arena = new BotArena();
        var at = new Vector2(200, 200);
        Bot bot = arena.Bot(shooter, 0, seed: 1, at, BotArena.East);
        arena.Director.AssignRoles();
        arena.Director.Attack(bot, 0, Array.Empty<Vector2>(), at);
        int enemy = arena.Idle(target, 1, at + new Vector2(distance, 0), BotArena.North);
        return (arena, bot, enemy);
    }

    /// <summary>
    /// On flat ground a heavy's gun just reaches a medium's hull centre at 15 m (5.9° down, it dips 6°) and a tank
    /// destroyer's, with its gun 2.8 m forward, from about 11 m: the 15 m stalemate of the probe was on uneven ground. The
    /// nearer distances here put the hull centre out of reach on flat ground; the turret (the casemate) is within it.
    /// </summary>
    [Theory]
    [InlineData("heavy", 15f)]
    [InlineData("heavy", 12f)]
    [InlineData("td", 15f)]
    [InlineData("td", 10f)]
    public void Point_blank_to_a_medium_it_hits_it_from_where_it_stands(string shooter, float distance)
    {
        var (arena, bot, enemy) = PointBlank(TankCatalog.ById(shooter), TankCatalog.Medium, distance);
        Vector2 start = arena.Tank(bot.TankId).Position;
        int hitAt = -1;
        arena.Run(15 * Tps, tick =>
        {
            if (hitAt < 0 && HitThisTick(arena, bot.TankId, enemy)) hitAt = tick;
        });
        _output.WriteLine($"{shooter} {distance} m from a medium: hit after {hitAt / (float)Tps:F1} s, moved {Vector2.Distance(arena.Tank(bot.TankId).Position, start):F2} m");
        Assert.True(hitAt > 0, $"no hit in 15 s: {bot.Activity}, target {bot.TargetId}");
        Assert.True(Vector2.Distance(arena.Tank(bot.TankId).Position, start) < 1f, "it hit from where it stood");
    }

    [Fact]
    public void Too_close_to_lay_on_at_all_it_backs_away_until_it_can_and_hits()
    {
        // 6 m from a light: the heavy's gun dips 6°, the light's turret centre is 6.4° down, its hull centre further.
        var (arena, bot, enemy) = PointBlank(TankCatalog.Heavy, TankCatalog.Light, 6f);
        Vector2 start = arena.Tank(bot.TankId).Position, enemyAt = arena.Tank(enemy).Position;
        int hitAt = -1;
        float hitFrom = 0f;
        arena.Run(4 * Tps, tick =>
        {
            if (hitAt >= 0 || !HitThisTick(arena, bot.TankId, enemy)) return;
            (hitAt, hitFrom) = (tick, Vector2.Distance(arena.Tank(bot.TankId).Position, enemyAt));
        });
        Assert.True(hitAt > 0, $"no hit in 4 s: {bot.Activity}, target {bot.TargetId}, {Vector2.Distance(arena.Tank(bot.TankId).Position, enemyAt):F1} m off");
        _output.WriteLine($"hit after {hitAt / (float)Tps:F1} s from {hitFrom:F1} m");
        Assert.InRange(hitFrom, 6.3f, 9f);   // backed off just far enough
        Assert.True(arena.Tank(bot.TankId).Position.X < start.X, "it backed away from the light, west");
    }

    /// <summary>A heavy whose gun dips only <paramref name="degrees"/>°: it has to back off a long way from a light.</summary>
    private static TankSpec Stiff(float degrees) => TankCatalog.Heavy with { GunDepression = Angles.Deg(degrees) };

    [Fact]
    public void It_backs_away_past_close_range_if_need_be_and_drives_back_once_the_enemy_is_gone()
    {
        // 1° of depression: the light's turret is in reach from about 37 m, beyond CloseRange.
        var (arena, bot, enemy) = PointBlank(Stiff(1f), TankCatalog.Light, 6f);
        arena.Sim.TankRef(enemy).Health = 100;   // one hit
        Vector2 start = arena.Tank(bot.TankId).Position, enemyAt = arena.Tank(enemy).Position;
        int hitAt = -1;
        float hitFrom = 0f;
        for (int k = 0; k < 25 * Tps && hitAt < 0; k++)
        {
            arena.Tick();
            if (HitThisTick(arena, bot.TankId, enemy)) (hitAt, hitFrom) = (arena.Sim.Tick, Vector2.Distance(arena.Tank(bot.TankId).Position, enemyAt));
        }
        _output.WriteLine($"hit after {hitAt / (float)Tps:F1} s from {hitFrom:F1} m");
        Assert.True(hitAt > 0, $"no hit in 25 s: {bot.Activity}, {Vector2.Distance(arena.Tank(bot.TankId).Position, enemyAt):F1} m off");
        Assert.True(hitFrom > TargetPicker.CloseRange, $"hit from {hitFrom:F1} m");
        Assert.True(arena.Tank(enemy).IsDestroyed);

        // Back where it held, well before the light is revived (team battle: 10 s); as near as its path goes: the wreck's
        // disc closes the cells round it, the spot among them, so the path ends on the nearest open cell.
        int backAt = -1;
        arena.Run(8 * Tps, tick =>
        {
            if (backAt < 0 && bot.Activity == BotActivity.Holding && Vector2.Distance(arena.Tank(bot.TankId).Position, start) <= Bot.PassRadius) backAt = tick;
        });
        _output.WriteLine($"back after {(backAt - hitAt) / (float)Tps:F1} s; now {bot.Activity} at {arena.Tank(bot.TankId).Position}");
        Assert.True(backAt > 0, $"not back: {bot.Activity} at {arena.Tank(bot.TankId).Position}, {Vector2.Distance(arena.Tank(bot.TankId).Position, start):F1} m from where it held");
    }

    [Fact]
    public void Capturing_it_backs_off_inside_the_circle()
    {
        // A heavy whose gun dips 2°, 15 m east of the enemy base's centre and facing west, a light 6 m west of it: the
        // light's turret is in reach from about 19 m, which straight away is outside the 25 m circle. It curves round
        // inside instead.
        var arena = new BotArena(BattleMode.Standard);
        Vector2 centre = BotArena.Base1, at = centre + new Vector2(15, 0);
        Bot bot = arena.Bot(Stiff(2f), 0, seed: 1, at, BotArena.West);
        arena.Director.AssignRoles();
        arena.Director.Attack(bot, 0, Array.Empty<Vector2>(), at);
        int enemy = arena.Idle(TankCatalog.Light, 1, at - new Vector2(6, 0), BotArena.North);
        int hitAt = -1;
        float farthest = 0f, hitFrom = 0f;
        arena.Run(20 * Tps, tick =>
        {
            if (hitAt >= 0) return;
            farthest = MathF.Max(farthest, Vector2.Distance(arena.Tank(bot.TankId).Position, centre));
            if (HitThisTick(arena, bot.TankId, enemy)) (hitAt, hitFrom) = (tick, Vector2.Distance(arena.Tank(bot.TankId).Position, arena.Tank(enemy).Position));
        });
        _output.WriteLine($"hit after {hitAt / (float)Tps:F1} s from {hitFrom:F1} m; at most {farthest:F1} m from the centre");
        Assert.True(hitAt > 0, $"no hit in 20 s: {bot.Activity}, target {bot.TargetId}, at {arena.Tank(bot.TankId).Position}");
        Assert.True(hitFrom > 18f, $"hit from {hitFrom:F1} m");
        Assert.True(farthest <= BotArena.BaseRadius - 1f, $"it all but left the circle: {farthest:F1} m from the centre");
    }

    [Fact]
    public void Backing_off_and_firing_he_allocate_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var (arena, bot, _) = PointBlank(TankCatalog.Heavy, TankCatalog.Light, 6f);
            arena.Sim.TankRef(bot.TankId).ApLeft = 0;
            arena.Recording = false;
            arena.Run(10);
            Allocation.Window window = Allocation.Start();
            for (int k = 0; k < 120; k++) arena.Tick();
            return window.Stop();
        });
    }
}

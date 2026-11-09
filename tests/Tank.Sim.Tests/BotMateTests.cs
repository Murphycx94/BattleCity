using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Navigation;
using Tank.Sim.Vehicles;
using Tank.Testing;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Bots keep off their team-mates (5C batch C review follow-up): a driving bot bends its way round a team-mate it would
/// come close to, and one that stops to fire doesn't stop next to one. A team-mate's tank shoved by a bot is a correction
/// its player sees (the soak's corrections rose with the bots' engagements near the spawns).
/// </summary>
public sealed class BotMateTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>Hulls closer than this count as touching, m.</summary>
    private const float Touch = 0.3f;

    private readonly ITestOutputHelper _output;

    public BotMateTests(ITestOutputHelper output) => _output = output;

    private static Obb2 Hull(in TankState t, float grow) =>
        new(t.Position, new Vector2(t.Spec.HullWidth / 2 + grow, t.Spec.HullLength / 2 + grow), t.Yaw);

    private static bool Touching(in TankState a, in TankState b) => Obb2.TryGetPenetration(Hull(a, Touch / 2), Hull(b, Touch / 2), out _);

    /// <summary>The gap between two tanks' circles (half their hulls' diagonals), m.</summary>
    private static float CircleGap(in TankState a, in TankState b) =>
        Vector2.Distance(a.Position, b.Position) - NavDisc.RadiusOf(a.Spec) - NavDisc.RadiusOf(b.Spec);

    /// <summary>
    /// The flat arena with one waypoint far east: a medium bot sets off east from (90, 200) and a team-mate nobody steers
    /// drives at full throttle from <paramref name="mateAt"/> facing <paramref name="mateYaw"/>, on a course that meets it.
    /// </summary>
    private static (BotArena Arena, Bot Bot, int Mate) Meeting(Vector2 mateAt, float mateYaw)
    {
        var arena = new BotArena(map: BotArena.FlatMap(waypoints: new Vector2[] { new(340, 200) }));
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 1, new Vector2(90, 200), BotArena.East);
        int mate = arena.Idle(TankCatalog.Medium, 0, mateAt, mateYaw);
        arena.Order(mate, new InputCmd(0, 1, 0, mateYaw, 0f, false));
        return (arena, bot, mate);
    }

    public static TheoryData<string, Vector2, float> Courses => new()
    {
        { "head on", new Vector2(240, 200), BotArena.West },
        { "across from the north", new Vector2(175, 120), BotArena.South },
        { "across from the south", new Vector2(165, 270), BotArena.North },
    };

    [Theory]
    [MemberData(nameof(Courses))]
    public void A_driving_bot_steers_round_a_team_mate_on_a_collision_course(string course, Vector2 mateAt, float mateYaw)
    {
        var (arena, bot, mate) = Meeting(mateAt, mateYaw);
        float nearest = float.MaxValue;
        int touching = 0;
        arena.Run(20 * Tps, _ =>
        {
            TankState a = arena.Tank(bot.TankId), b = arena.Tank(mate);
            nearest = MathF.Min(nearest, Vector2.Distance(a.Position, b.Position));
            if (Touching(a, b)) touching++;
        });
        TankState me = arena.Tank(bot.TankId);
        _output.WriteLine($"{course}: nearest {nearest:F1} m between centres, {touching} ticks touching; the bot at {me.Position}, {bot.Activity}, stucks {bot.Stucks}");
        Assert.Equal(0, touching);
        Assert.True(me.Position.X > 280f, $"{course}: the bot only got to {me.Position}");
        Assert.Equal(0, bot.Stucks);
    }

    [Fact]
    public void A_bot_that_stops_to_fire_does_not_stop_next_to_a_team_mate()
    {
        // A medium beside the bot (centres 5 m apart, hulls 1.8 m), an enemy 150 m east in plain view.
        var arena = new BotArena();
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 1, new Vector2(150, 200), BotArena.East);
        int mate = arena.Idle(TankCatalog.Medium, 0, new Vector2(150, 205), BotArena.East);
        int enemy = arena.Idle(TankCatalog.Heavy, 1, new Vector2(300, 200), BotArena.West);
        TankState? atFirstShot = null;
        float gapAtFirstShot = 0f;
        arena.Run(15 * Tps, _ =>
        {
            if (atFirstShot is not null || !arena.Sim.Events.ToArray().Any(e => e.Type == GameEventType.ShellFired && e.TankId == bot.TankId)) return;
            atFirstShot = arena.Tank(bot.TankId);
            gapAtFirstShot = CircleGap(atFirstShot.Value, arena.Tank(mate));
        });

        Assert.NotNull(atFirstShot);
        _output.WriteLine($"first shot from {atFirstShot.Value.Position} at {atFirstShot.Value.Speed:F2} m/s, the circles {gapAtFirstShot:F1} m apart");
        Assert.True(gapAtFirstShot >= BotDirector.MateStopGap - 0.5f, $"it fired {gapAtFirstShot:F1} m (circles) from its team-mate");
        Assert.True(MathF.Abs(atFirstShot.Value.Speed) < BotDirector.FireSpeed, $"fired at {atFirstShot.Value.Speed:F2} m/s");
        Assert.Contains(arena.Events, e => e.Type == GameEventType.TankHit && e.TankId == bot.TankId && e.TargetTankId == enemy);
        Assert.Equal(BotActivity.Engaging, bot.Activity);
    }

    [Fact]
    public void A_team_mate_that_keeps_alongside_only_delays_the_first_shot_a_moment()
    {
        // The team-mate is put back beside the bot every tick: no spot is clear of it, so the bot stops anyway.
        var arena = new BotArena();
        Bot bot = arena.Bot(TankCatalog.Medium, 0, seed: 1, new Vector2(150, 200), BotArena.East);
        int mate = arena.Idle(TankCatalog.Medium, 0, new Vector2(150, 205), BotArena.East);
        arena.Idle(TankCatalog.Heavy, 1, new Vector2(300, 200), BotArena.West);
        int firstShot = -1;
        for (int k = 0; k < 15 * Tps && firstShot < 0; k++)
        {
            TankState me = arena.Tank(bot.TankId);
            arena.Place(mate, me.Position + new Vector2(0, 5), me.Yaw);
            arena.Tick();
            if (arena.Sim.Events.ToArray().Any(e => e.Type == GameEventType.ShellFired && e.TankId == bot.TankId)) firstShot = arena.Sim.Tick;
        }
        _output.WriteLine($"first shot at {firstShot / (float)Tps:F1} s");
        Assert.InRange(firstShot, 1, BotDirector.ShiftTicks + 5 * Tps);
    }

    [Fact]
    public void Keeping_off_team_mates_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var (arena, _, _) = Meeting(new Vector2(240, 200), BotArena.West);
            arena.Recording = false;
            arena.Run(2 * Tps);
            Allocation.Window window = Allocation.Start();
            arena.Run(6 * Tps);   // closing, bending round, passing
            return window.Stop();
        });
    }
}

using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Testing;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// Bots in a royale run the zone (stage 6 design §3.4; stage 6A plan, task 6): out of the ring they must be in they run
/// for a point in it, spread by tank id; inside they wander between points well inside it; short of time (or outside the
/// safe circle) they fire on the move instead of stopping to fight; in the last ring they hold within
/// <see cref="BotDirector.ZoneArriveRadius"/> of its centre; a run whose every point failed starts over. Played on the
/// flat 400 m arena with small hand-made rings (<see cref="Rings"/> and others).
/// </summary>
public sealed class BotZoneTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>Ring 0 round most of the arena, then rings 1–5 closing on (320, 300); ring 4 no smaller than a run's arrival (0.8 × 25 = <see cref="BotDirector.ZoneArriveRadius"/>).</summary>
    private static readonly ZoneRing[] Rings =
    {
        new(new Vector2(200, 200), 283f),
        new(new Vector2(300, 300), 80f),
        new(new Vector2(310, 300), 50f),
        new(new Vector2(320, 300), 30f),
        new(new Vector2(320, 300), 25f),
        new(new Vector2(320, 300), 0f),
    };

    private readonly ITestOutputHelper _output;

    public BotZoneTests(ITestOutputHelper output) => _output = output;

    private static BotArena Royale(IReadOnlyList<ZoneRing>? rings = null) => new(BattleMode.Royale, zone: rings ?? Rings);

    /// <summary>A bot without rounds: it never stops to fight, so only the zone moves it.</summary>
    private static Bot Unarmed(BotArena arena, Vector2 at, uint seed = 1, TankSpec? spec = null)
    {
        Bot bot = arena.Bot(spec ?? TankCatalog.Medium, 0, seed, at, BotArena.East);
        ref TankState t = ref arena.Sim.TankRef(bot.TankId);
        (t.ApLeft, t.HeLeft) = (0, 0);
        return bot;
    }

    [Theory]
    [InlineData(900, false, true)]    // 100 m at 6 m/s: 500 ticks, + 450 margin = 950 ≥ 900
    [InlineData(1000, false, false)]  // 950 < 1000
    [InlineData(1000, true, true)]    // outside the safe circle: always
    [InlineData(100000, true, true)]
    public void A_bot_must_run_when_the_way_and_the_margin_reach_the_rings_end(int ticksToTarget, bool outside, bool must) =>
        Assert.Equal(must, BotDirector.MustRun(100f, 10f, ticksToTarget, outside));

    [Fact]
    public void A_tank_that_cannot_move_must_always_run() => Assert.True(BotDirector.MustRun(10f, 0f, 100000, false));

    [Fact]
    public void A_bot_out_of_the_ring_runs_into_it_before_the_ring_has_shrunk()
    {
        var arena = Royale();
        Bot bot = Unarmed(arena, new Vector2(60, 60));
        arena.Idle(TankCatalog.Medium, 0, new Vector2(300, 372));   // keeps the match on: inside every ring until ring 2

        arena.Run(2 * Tps);
        Assert.True(bot.RunningToZone, "not running after 2 s");
        Assert.Equal(1, bot.ZoneRuns);
        Assert.True(Vector2.Distance(bot.Goal, Rings[1].Centre) <= BotDirector.ZoneInsideShare * Rings[1].Radius, $"its point {bot.Goal} isn't well inside ring 1");

        int inAt = -1;
        arena.Run(ZoneSchedule.ShrinkEnd(1) - arena.Sim.Tick, tick =>
        {
            if (inAt < 0 && !bot.RunningToZone) inAt = tick;
        });
        Vector2 at = arena.Tank(bot.TankId).Position;
        _output.WriteLine($"in at {inAt / (float)Tps:F1} s; at ring 1's end at {at}, {Vector2.Distance(at, Rings[1].Centre):F0} m from its centre; {arena.Director.Summary()}");
        Assert.Equal(ZoneSchedule.ShrinkEnd(1), arena.Sim.Tick);
        Assert.True(Rings[1].Contains(at), $"at {at}, outside ring 1 when it has shrunk");
        Assert.True(inAt > 0, "it never stopped running");
        Assert.False(arena.Tank(bot.TankId).IsDestroyed);
    }

    [Fact]
    public void Bots_out_of_the_ring_together_run_for_points_spread_apart()
    {
        var arena = Royale();
        Bot[] bots =
        {
            Unarmed(arena, new Vector2(40, 40), 1), Unarmed(arena, new Vector2(56, 40), 2),
            Unarmed(arena, new Vector2(40, 56), 3), Unarmed(arena, new Vector2(56, 56), 4),
        };

        arena.Run(2);
        for (int a = 0; a < bots.Length; a++)
        {
            Assert.True(bots[a].RunningToZone, $"bot {bots[a].TankId} isn't running");
            Assert.True(BotDirector.WellInside(bots[a].Goal, Rings[1]), $"bot {bots[a].TankId}'s point {bots[a].Goal} isn't well inside ring 1");
            for (int b = a + 1; b < bots.Length; b++)
                Assert.True(Vector2.Distance(bots[a].Goal, bots[b].Goal) >= 10f, $"bots {bots[a].TankId} and {bots[b].TankId} run for {bots[a].Goal} and {bots[b].Goal}");
        }
        Assert.StartsWith("4 zone runs; ", arena.Director.Summary());
    }

    [Fact]
    public void A_bot_in_the_ring_never_runs_and_wanders_well_inside_it()
    {
        var arena = Royale();
        Bot bot = Unarmed(arena, Rings[1].Centre);
        arena.Idle(TankCatalog.Medium, 0, new Vector2(300, 372));
        var goals = new HashSet<Vector2>();

        arena.Run(ZoneSchedule.ShrinkEnd(1) - 1, _ =>
        {
            Assert.False(bot.RunningToZone);
            Assert.True(BotDirector.WellInside(bot.Goal, Rings[1]), $"wandering to {bot.Goal}, not well inside ring 1");
            goals.Add(bot.Goal);
        });
        _output.WriteLine($"{goals.Count} goals: {string.Join(", ", goals)}");
        Assert.Equal(0, bot.ZoneRuns);
        Assert.True(goals.Count >= 3, $"only {goals.Count} goals in 180 s: it hardly wandered");
    }

    /// <summary>Ring 0 small and far off, so a bot starts outside the safe circle.</summary>
    private static readonly ZoneRing[] FarRings =
    {
        new(new Vector2(330, 330), 40f),
        new(new Vector2(330, 330), 20f),
        new(new Vector2(330, 330), 10f),
        new(new Vector2(330, 330), 5f),
        new(new Vector2(330, 330), 2f),
        new(new Vector2(330, 330), 0f),
    };

    [Fact]
    public void Outside_the_safe_circle_a_bot_fires_on_the_move_and_never_stops_to_fight()
    {
        var arena = Royale(FarRings);
        Bot bot = arena.Bot(TankCatalog.Medium, 0, 1, new Vector2(30, 150), BotArena.East);
        int enemy = arena.Idle(TankCatalog.Medium, 0, new Vector2(30, 210));
        int outside = 0, driving = 0, aimed = 0, shots = 0;

        arena.Run(20 * Tps, tick =>
        {
            TankState me = arena.Tank(bot.TankId);
            if (!arena.Rules.Zone.IsOutside(me.Position)) return;
            outside++;
            Assert.True(bot.ZoneUrgent, $"tick {tick}: outside the safe circle but not urgent");
            Assert.NotEqual(BotActivity.Engaging, bot.Activity);
            Assert.NotEqual(BotActivity.Reversing, bot.Activity);
            if (bot.Activity == BotActivity.Driving) driving++;
            if (bot.TargetId == enemy)
            {
                float bearing = Angles.YawOf(arena.Tank(enemy).Position - me.Position);
                Assert.True(MathF.Abs(Angles.Wrap(arena.LastInput(bot.TankId).AimYaw - bearing)) <= Angles.Deg(5), $"tick {tick}: the gun aimed away from the enemy");
                aimed++;
            }
            foreach (GameEvent e in arena.Sim.Events)
                if (e.Type == GameEventType.ShellFired && e.TankId == bot.TankId) shots++;
        });
        _output.WriteLine($"{outside} ticks outside: {driving} driving, {aimed} aimed at the enemy, {shots} shots");
        Assert.True(outside >= 10 * Tps, $"only {outside} ticks outside");
        Assert.True(driving >= outside * 9 / 10, $"driving only {driving} of {outside} ticks outside");
        Assert.True(aimed > 0, "it never had the enemy");
        Assert.True(shots >= 1, "it never fired on the move");
    }

    [Fact]
    public void With_time_to_spare_a_running_bot_stops_to_fight()
    {
        var arena = Royale();
        Bot bot = arena.Bot(TankCatalog.Medium, 0, 1, new Vector2(190, 300), BotArena.East);   // 30 m outside ring 1, at the start
        arena.Idle(TankCatalog.Medium, 0, new Vector2(190, 240));
        bool engaged = false;

        arena.Run(5 * Tps, _ =>
        {
            Assert.True(bot.RunningToZone);
            Assert.False(bot.ZoneUrgent);
            engaged |= bot.Activity == BotActivity.Engaging;
        });
        Assert.True(engaged, "it never stopped to fight");
    }

    /// <summary>
    /// Ring 5 (radius 0) is the target from its waiting on: every bot is (or runs to be) within
    /// <see cref="BotDirector.ZoneArriveRadius"/> of its centre by its shrink and holds there to the close and after,
    /// without going back and forth or running again (the plan's "after 16200, all head for the centre": nearer than that
    /// the first there would close the way for the rest).
    /// </summary>
    [Fact]
    public void Once_the_last_ring_is_the_target_the_bots_hold_near_its_centre()
    {
        var arena = Royale();
        Bot[] bots =
        {
            Unarmed(arena, new Vector2(60, 60), 1), Unarmed(arena, new Vector2(340, 60), 2),
            Unarmed(arena, new Vector2(60, 340), 3), Unarmed(arena, new Vector2(200, 200), 4),
        };
        arena.Recording = false;
        arena.Run(ZoneSchedule.ShrinkStart(ZoneSchedule.Rings) - arena.Sim.Tick);
        arena.Recording = true;

        Vector2 centre = Rings[5].Centre;
        var goals = new Vector2[bots.Length];
        var changes = new int[bots.Length];
        var runs = new int[bots.Length];
        for (int b = 0; b < bots.Length; b++)
        {
            (goals[b], runs[b]) = (bots[b].Goal, bots[b].ZoneRuns);
            TankState t = arena.Tank(bots[b].TankId);
            _output.WriteLine($"bot {bots[b].TankId} at ring 5's shrink: {Vector2.Distance(t.Position, centre):F1} m from the centre, {bots[b].Activity}, health {t.Health}, {bots[b].ZoneRuns} runs");
            Assert.False(t.IsDestroyed, $"bot {bots[b].TankId} is gone before ring 5 shrinks");
            Assert.True(Vector2.Distance(t.Position, centre) <= BotDirector.ZoneArriveRadius, $"bot {bots[b].TankId} is {Vector2.Distance(t.Position, centre):F1} m from the centre");
            Assert.Equal(BotActivity.Holding, bots[b].Activity);
        }
        arena.Run(2 * Tps, tick =>
        {
            for (int b = 0; b < bots.Length; b++)
                if (bots[b].Goal != goals[b]) (goals[b], changes[b]) = (bots[b].Goal, changes[b] + 1);
        });
        for (int b = 0; b < bots.Length; b++)
        {
            Assert.True(changes[b] <= 1, $"bot {bots[b].TankId}'s goal changed {changes[b]} times in 2 s");
            Assert.Equal(runs[b], bots[b].ZoneRuns);
        }

        arena.Run(ZoneSchedule.ClosedAt + 2 * Tps - arena.Sim.Tick, tick =>
        {
            for (int b = 0; b < bots.Length; b++)
            {
                if (arena.Tank(bots[b].TankId).IsDestroyed) continue;
                Assert.Equal(BotActivity.Holding, bots[b].Activity);
                Assert.True(runs[b] == bots[b].ZoneRuns, $"tick {tick}: bot {bots[b].TankId} ran again");
            }
        });
    }

    /// <summary>Rings 1–4 round a pen at (200, 200), small enough to hold in; the last ring closes on (232, 200), 32 m off.</summary>
    private static readonly ZoneRing[] PenRings =
    {
        new(new Vector2(200, 200), 283f),
        new(new Vector2(200, 200), 36f),
        new(new Vector2(200, 200), 35f),
        new(new Vector2(200, 200), 34f),
        new(new Vector2(200, 200), 33f),
        new(new Vector2(232, 200), 0f),
    };

    /// <summary>
    /// The last ring, at a point a bot can't reach for a while: penned in by six tanks standing still round it (their
    /// discs close every way out, so every search fails), it tries every point for the last ring and gives them up; once
    /// the pen opens it tries again (after <see cref="BotDirector.ZoneRetryTicks"/>, from the first point) and gets in,
    /// rather than holding where it is until the next ring, which after the last never comes (batch C review follow-up:
    /// that left 4–8 bots outside at the end of ring 5 in the unarmed probe).
    /// </summary>
    [Fact]
    public void A_bot_that_gave_every_point_of_the_last_ring_up_tries_again_and_gets_in_once_a_way_opens()
    {
        var arena = Royale(PenRings);
        Vector2 home = PenRings[1].Centre;
        Bot bot = Unarmed(arena, home);
        var pen = new int[6];
        for (int k = 0; k < pen.Length; k++)
        {
            (float sin, float cos) = MathF.SinCos(k * MathF.Tau / pen.Length);
            pen[k] = arena.Idle(TankCatalog.Medium, 0, home + new Vector2(cos, sin) * 9f);
        }
        arena.Recording = false;
        int open = ZoneSchedule.WaitStart(ZoneSchedule.Rings) + 20 * Tps;

        arena.Run(open - arena.Sim.Tick);
        Assert.True(Vector2.Distance(arena.Tank(bot.TankId).Position, home) < 2f, "it got out of the pen");
        Assert.True(bot.RunningToZone, "not running for the last ring");
        Assert.Equal(ZoneSchedule.Rings, arena.Rules.Zone.Ring);
        Assert.True(arena.Director.Paths.Failures >= BotDirector.ZoneTries, $"only {arena.Director.Paths.Failures} searches failed: the pen didn't hold");
        for (int k = 0; k < pen.Length; k++) arena.Place(pen[k], new Vector2(40 + 12 * k, 380), BotArena.East);

        int inAt = -1;
        arena.Run(10 * Tps, tick =>
        {
            if (inAt < 0 && !bot.RunningToZone) inAt = tick;
        });
        Vector2 at = arena.Tank(bot.TankId).Position;
        ZoneRing last = PenRings[ZoneSchedule.Rings];
        _output.WriteLine($"in {(inAt < 0 ? "never" : $"{(inAt - open) / (float)Tps:F1} s after the pen opened")}, at {at}, {Vector2.Distance(at, last.Centre):F1} m from the last ring's centre; {arena.Director.Summary()}");
        Assert.True(inAt > 0, "it never got in");
        Assert.True(Vector2.Distance(at, last.Centre) <= BotDirector.ZoneArriveRadius, $"at {at}, not within {BotDirector.ZoneArriveRadius} m of the last ring's centre");
    }

    /// <summary>Rings 1–4 round (200, 200), ring 4 small enough to hold in; ring 5 closes on its centre.</summary>
    private static readonly ZoneRing[] CrowdRings =
    {
        new(new Vector2(200, 200), 283f),
        new(new Vector2(200, 200), 150f),
        new(new Vector2(200, 200), 100f),
        new(new Vector2(200, 200), 70f),
        new(new Vector2(200, 200), 39f),
        new(new Vector2(200, 200), 0f),
    };

    /// <summary>
    /// Five tanks standing on the last ring's centre take the room within a few metres of it, as the first bots there do
    /// in a royale: the bots coming in from 32 m stop running once within <see cref="BotDirector.ZoneArriveRadius"/> and
    /// hold there, rather than trying point after point near the centre and holding, still running, wherever the last
    /// one left them (batch C review follow-up).
    /// </summary>
    [Fact]
    public void In_the_last_ring_bots_stop_running_within_the_arrive_radius_however_crowded_its_centre()
    {
        var arena = Royale(CrowdRings);
        Vector2 centre = CrowdRings[5].Centre;
        Bot[] bots =
        {
            Unarmed(arena, centre + new Vector2(32, 0), 1), Unarmed(arena, centre + new Vector2(-32, 0), 2),
            Unarmed(arena, centre + new Vector2(0, 32), 3), Unarmed(arena, centre + new Vector2(0, -32), 4),
        };
        arena.Idle(TankCatalog.Medium, 0, centre);
        for (int k = 0; k < 4; k++)
        {
            (float sin, float cos) = MathF.SinCos((k + 0.5f) * MathF.Tau / 4);
            arena.Idle(TankCatalog.Medium, 0, centre + new Vector2(cos, sin) * 6f);
        }
        arena.Recording = false;
        arena.Run(ZoneSchedule.WaitStart(ZoneSchedule.Rings) - arena.Sim.Tick);

        var runningStill = new int[bots.Length];
        int longest = 0;
        arena.Run(ZoneSchedule.ShrinkStart(ZoneSchedule.Rings) - arena.Sim.Tick, _ =>
        {
            for (int b = 0; b < bots.Length; b++)
            {
                runningStill[b] = bots[b].RunningToZone && bots[b].Activity == BotActivity.Holding ? runningStill[b] + 1 : 0;
                longest = Math.Max(longest, runningStill[b]);
            }
        });
        foreach (Bot bot in bots)
        {
            TankState t = arena.Tank(bot.TankId);
            float d = Vector2.Distance(t.Position, centre);
            _output.WriteLine($"bot {bot.TankId} at ring 5's shrink: {d:F1} m from the centre, {bot.Activity}, running {bot.RunningToZone}, {bot.ZoneRuns} runs");
            Assert.False(t.IsDestroyed);
            Assert.False(bot.RunningToZone, $"bot {bot.TankId} is still running, {d:F1} m from the centre");
            Assert.True(d <= BotDirector.ZoneArriveRadius, $"bot {bot.TankId} is {d:F1} m from the centre");
        }
        Assert.True(longest <= 3 * Tps, $"a bot held {longest / (float)Tps:F1} s while still running");
    }

    /// <summary>
    /// The time to get in is reckoned to the circle a run stops at (<see cref="BotDirector.ZoneInsideShare"/> of the
    /// radius), not to the point it runs for, further in: from 100 m short of that circle (its point at least 124 m off)
    /// with 32 s left a bot has time (100 m × 1.3 at 8.3 m/s is 15.6 s, plus the 15 s margin). From there with 29 s left
    /// it must hurry; once its path comes its estimate (the path, not × 1.3) drops below the time left, but a run that was
    /// urgent stays so until the bot is in (batch C review follow-up: no switching between fighting and running).
    /// </summary>
    [Fact]
    public void A_run_is_timed_to_the_circle_it_stops_at_and_once_urgent_stays_so()
    {
        Bot bot = RunFrom(32, out BotArena arena);
        Assert.True(bot.RunningToZone, "not running");
        arena.Run(2 * Tps, tick => Assert.False(bot.ZoneUrgent, $"tick {tick}: urgent with 30 s left for 100 m"));

        bot = RunFrom(29, out arena);
        Assert.True(bot.RunningToZone && bot.ZoneUrgent, $"running {bot.RunningToZone}, urgent {bot.ZoneUrgent} with 29 s left for 100 m");
        int flips = 0, running = 0;
        arena.Run(10 * Tps, _ =>
        {
            if (!bot.RunningToZone) return;
            running++;
            if (!bot.ZoneUrgent) flips++;
        });
        _output.WriteLine($"{running} ticks running after it was urgent, {flips} of them not urgent; {bot.ZoneRuns} runs");
        Assert.True(running >= 5 * Tps, $"running only {running} ticks");
        Assert.Equal(0, flips);
    }

    /// <summary>
    /// A bot (unarmed) that has waited in ring 1 until <paramref name="secondsLeft"/> before ring 1 has shrunk, then is
    /// put 164 m from its centre (100 m short of 0.8 × 80), facing it, and commanded once: it has started its run.
    /// </summary>
    private static Bot RunFrom(int secondsLeft, out BotArena arena)
    {
        arena = Royale();
        Bot bot = Unarmed(arena, Rings[1].Centre);
        arena.Idle(TankCatalog.Medium, 0, new Vector2(300, 372));
        Vector2 start = new(184, 184);
        arena.Run(ZoneSchedule.ShrinkEnd(1) - secondsLeft * Tps - arena.Sim.Tick);
        Assert.False(arena.Rules.Zone.IsOutside(start), "the start is outside the safe circle");
        arena.Place(bot.TankId, start, Angles.YawOf(Rings[1].Centre - start));
        arena.Run(1);
        return bot;
    }

    [Fact]
    public void Running_the_zone_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var arena = Royale();
            Bot[] bots =
            {
                Unarmed(arena, new Vector2(60, 60), 1), Unarmed(arena, new Vector2(340, 60), 2),
                Unarmed(arena, new Vector2(60, 340), 3), Unarmed(arena, new Vector2(270, 330), 4),
            };
            arena.Recording = false;
            const int Window = 300;
            arena.Run(ZoneSchedule.ShrinkEnd(1) - 5);   // the window starts in ring 1's last ticks and crosses into ring 2
            arena.Place(bots[3].TankId, new Vector2(250, 340), BotArena.East);   // in ring 1, out of ring 2: it runs once ring 2 is the target
            int runs = bots.Sum(b => b.ZoneRuns);
            Allocation.Window window = Allocation.Start();
            for (int k = 0; k < Window; k++) arena.Tick();
            Allocation.Measured measured = window.Stop();
            Assert.Equal(2, arena.Rules.Zone.Ring);
            Assert.True(bots.Sum(b => b.ZoneRuns) > runs, "no run started in the window");
            return measured;
        });
    }
}

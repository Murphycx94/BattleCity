using System.Numerics;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// Base capture (stage 5 design §3.2), in whole ticks: up to three attackers count, lowest ids first; a live defender in
/// the circle pauses everyone; any damage or death resets that one tank; points stay with a tank that leaves the circle.
/// The tanks and events are made by hand: <see cref="BaseCapture"/> reads nothing but the two spans.
/// </summary>
public sealed class BaseCaptureTests
{
    private static readonly TeamBase T0 = new(0, new Vector2(100, 100), 25);
    private static readonly TeamBase T1 = new(1, new Vector2(300, 100), 25);
    private const float Outside = 200;   // x on the line y = 100 that is in neither circle

    private static BaseCapture NewCapture() => new(new[] { T0, T1 });

    private static TankState Tank(int id, byte team, float x, float y = 100, bool destroyed = false) =>
        new() { Id = id, Team = team, Position = new Vector2(x, y), IsDestroyed = destroyed };

    private static GameEvent Hit(int target, int damage, int from = 99) =>
        new(GameEventType.TankHit, 0, from, 0, Vector3.Zero, Vector3.Zero, TargetTankId: target, Damage: damage);

    private static GameEvent Splash(int target, int damage, int from = 99) =>
        new(GameEventType.SplashDamage, 0, from, 0, Vector3.Zero, Vector3.Zero, TargetTankId: target, Damage: damage);

    private static GameEvent Destroyed(int target, int from = 99) =>
        new(GameEventType.TankDestroyed, 0, from, 0, Vector3.Zero, Vector3.Zero, TargetTankId: target);

    private static void Run(BaseCapture capture, TankState[] tanks, int ticks, params GameEvent[] events)
    {
        for (int i = 0; i < ticks; i++) capture.Update(tanks, i == 0 ? events : ReadOnlySpan<GameEvent>.Empty);
    }

    [Fact]
    public void A_lone_attacker_earns_a_point_a_second()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(5, 1, 110) };
        Run(capture, tanks, 29);
        Assert.Equal(29, capture.TicksOf(5));
        Assert.Equal(0, capture.Progress(0));
        Run(capture, tanks, 1);
        Assert.Equal(30, capture.TicksOf(5));
        Assert.Equal(1, capture.Progress(0));
        Assert.Equal(0, capture.Progress(1));
    }

    [Fact]
    public void At_most_three_attackers_count_and_the_lowest_ids_are_chosen()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(8, 1, 100), Tank(2, 1, 102), Tank(10, 1, 104), Tank(4, 1, 106), Tank(6, 1, 108) };
        Run(capture, tanks, 30);
        Assert.Equal(new[] { 0, 30, 0, 30, 30 }, tanks.Select(t => capture.TicksOf(t.Id)));
        Assert.Equal(3, capture.Progress(0));

        tanks[3].Position = new Vector2(Outside, 100);   // id 4 leaves: id 8 is next in line
        Run(capture, tanks, 30);
        Assert.Equal(60, capture.TicksOf(2));
        Assert.Equal(60, capture.TicksOf(6));
        Assert.Equal(30, capture.TicksOf(8));
        Assert.Equal(30, capture.TicksOf(4));
        Assert.Equal(0, capture.TicksOf(10));
    }

    [Fact]
    public void A_hurt_attacker_yields_its_place_among_the_three_to_the_next_in_line()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(10, 1, 110), Tank(2, 1, Outside), Tank(4, 1, Outside), Tank(6, 1, Outside), Tank(8, 1, Outside) };
        Run(capture, tanks, 30);   // id 10 alone first, so the one the cap leaves out has points to lose
        for (int k = 1; k < tanks.Length; k++) tanks[k].Position = new Vector2(100 + 2 * k, 100);
        Run(capture, tanks, 30);
        Assert.Equal(new[] { 30, 30, 30, 30, 0 }, new[] { 10, 2, 4, 6, 8 }.Select(capture.TicksOf));

        Run(capture, tanks, 1, Hit(target: 2, damage: 40));   // id 2 resets and earns nothing: id 8 takes its place
        Assert.Equal(0, capture.TicksOf(2));
        Assert.Equal(31, capture.TicksOf(4));
        Assert.Equal(31, capture.TicksOf(6));
        Assert.Equal(1, capture.TicksOf(8));
        Assert.Equal(30, capture.TicksOf(10));

        Run(capture, tanks, 1);   // id 2 is back among the three, id 8 out again
        Assert.Equal(new[] { 1, 32, 32, 1, 30 }, new[] { 2, 4, 6, 8, 10 }.Select(capture.TicksOf));

        Run(capture, tanks, 1, Hit(target: 10, damage: 40));   // one the cap leaves out still resets when hit
        Assert.Equal(new[] { 2, 33, 33, 1, 0 }, new[] { 2, 4, 6, 8, 10 }.Select(capture.TicksOf));
        Assert.Equal(2 + 33 + 33 + 1, capture.TicksOn(0));
    }

    [Fact]
    public void A_tank_taken_from_the_middle_leaves_the_others_their_points()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(1, 1, Outside), Tank(2, 1, Outside), Tank(3, 1, 110) };
        Run(capture, tanks, 20);   // id 3 alone first, so each tank has its own count
        tanks[0].Position = new Vector2(90, 100);
        tanks[1].Position = new Vector2(100, 100);
        Run(capture, tanks, 30);
        Assert.Equal(new[] { 30, 30, 50 }, new[] { 1, 2, 3 }.Select(capture.TicksOf));

        Run(capture, new[] { tanks[0], tanks[2] }, 1);   // id 2 leaves the battle: ids 1 and 3 shift in the roster
        Assert.Equal(31, capture.TicksOf(1));
        Assert.Equal(0, capture.TicksOf(2));
        Assert.Equal(51, capture.TicksOf(3));
        Assert.Equal(1, capture.BestOf(1));
        Assert.Equal(1, capture.BestOf(3));
        Assert.Equal(82, capture.TicksOn(0));
    }

    [Fact]
    public void The_hull_centre_decides_who_is_inside()
    {
        var onTheRim = NewCapture();
        onTheRim.Update(new[] { Tank(1, 1, 125) }, default);
        Assert.Equal(1, onTheRim.TicksOf(1));

        var justOutside = NewCapture();
        justOutside.Update(new[] { Tank(1, 1, 125.01f) }, default);
        Assert.Equal(0, justOutside.TicksOf(1));
    }

    [Fact]
    public void A_live_defender_inside_pauses_every_attacker_and_a_wreck_does_not()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(1, 1, 110), Tank(2, 1, 90), Tank(3, 0, Outside) };
        Run(capture, tanks, 60);
        Assert.Equal(60, capture.TicksOf(1));
        Assert.Equal(60, capture.TicksOf(2));

        tanks[2].Position = new Vector2(100, 100);
        Run(capture, tanks, 60);
        Assert.Equal(60, capture.TicksOf(1));
        Assert.Equal(60, capture.TicksOf(2));
        Assert.Equal(4, capture.Progress(0));   // paused, not lost

        tanks[2].IsDestroyed = true;
        Run(capture, tanks, 30);
        Assert.Equal(90, capture.TicksOf(1));
        Assert.Equal(90, capture.TicksOf(2));

        tanks[2] = Tank(3, 0, 140);   // alive again, but outside the circle
        Run(capture, tanks, 30);
        Assert.Equal(120, capture.TicksOf(1));
        Assert.Equal(120, capture.TicksOf(2));
    }

    [Fact]
    public void Damage_resets_only_the_tank_that_took_it()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(3, 1, 110), Tank(4, 1, 90) };
        Run(capture, tanks, 90);

        Run(capture, tanks, 1, Hit(target: 3, damage: 120));
        Assert.Equal(0, capture.TicksOf(3));
        Assert.Equal(91, capture.TicksOf(4));

        Run(capture, tanks, 1, Splash(target: 4, damage: 15));
        Assert.Equal(1, capture.TicksOf(3));
        Assert.Equal(0, capture.TicksOf(4));
    }

    [Fact]
    public void A_hit_for_no_damage_resets_nothing()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(3, 1, 110) };
        Run(capture, tanks, 90);
        Run(capture, tanks, 1, Hit(target: 3, damage: 0), Splash(target: 3, damage: 0));
        Assert.Equal(91, capture.TicksOf(3));
    }

    [Fact]
    public void Damage_from_a_team_mate_resets_too()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(3, 1, 110), Tank(4, 1, 90) };
        Run(capture, tanks, 90);
        Run(capture, tanks, 1, Hit(target: 3, damage: 50, from: 4));
        Assert.Equal(0, capture.TicksOf(3));
        Assert.Equal(91, capture.TicksOf(4));
    }

    [Fact]
    public void Death_resets_and_a_wreck_never_earns()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(3, 1, 110) };
        Run(capture, tanks, 90);

        tanks[0].IsDestroyed = true;
        Run(capture, tanks, 1, Destroyed(target: 3));
        Assert.Equal(0, capture.TicksOf(3));

        Run(capture, tanks, 60);
        Assert.Equal(0, capture.TicksOf(3));
        Assert.Equal(0, capture.Progress(0));
        Assert.Equal(3, capture.BestOf(3));
    }

    [Fact]
    public void Death_alone_resets_even_when_the_tank_still_reads_alive()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(3, 1, 110) };
        Run(capture, tanks, 90);
        Run(capture, tanks, 1, Destroyed(target: 3));   // the event is what counts, not the flag
        Assert.Equal(0, capture.TicksOf(3));
    }

    [Fact]
    public void A_wreck_keeps_no_points_even_without_its_destroyed_event()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(3, 1, 110), Tank(4, 1, 90) };
        Run(capture, tanks, 90);

        tanks[0].IsDestroyed = true;   // no TankDestroyed: the flag alone
        Run(capture, tanks, 1);
        Assert.Equal(0, capture.TicksOf(3));
        Assert.Equal(91, capture.TicksOf(4));
        Assert.Equal(91, capture.TicksOn(0));
        Assert.Equal(3, capture.BestOf(3));
    }

    /// <summary>
    /// The count learns of damage from events alone: a hit or splash for more than 0, or a death. This pins the kinds of
    /// event there are, so a new one fails here until someone decides whether it resets; and any new way for a tank to
    /// lose health (ramming, a fall, …) must report it as a <see cref="GameEventType.TankHit"/> or
    /// <see cref="GameEventType.SplashDamage"/> with its damage (and a death as <see cref="GameEventType.TankDestroyed"/>),
    /// or attackers would keep their points through it.
    /// </summary>
    [Fact]
    public void Only_hits_splashes_and_deaths_reset_and_every_kind_of_event_is_accounted_for()
    {
        Assert.Equal(
            new[]
            {
                GameEventType.ShellFired, GameEventType.ShellExploded, GameEventType.TankHit, GameEventType.SplashDamage,
                GameEventType.TankDestroyed, GameEventType.TankRevived, GameEventType.PropDamaged, GameEventType.PropDestroyed,
                GameEventType.CratePicked,
            },
            Enum.GetValues<GameEventType>());
        var resetting = new[] { GameEventType.TankHit, GameEventType.SplashDamage, GameEventType.TankDestroyed };
        foreach (GameEventType type in Enum.GetValues<GameEventType>())
        {
            var capture = NewCapture();
            TankState[] tanks = { Tank(3, 1, 110) };
            Run(capture, tanks, 90);
            Run(capture, tanks, 1, new GameEvent(type, 0, 99, 0, Vector3.Zero, Vector3.Zero, TargetTankId: 3, Damage: 50));
            Assert.True(capture.TicksOf(3) == (resetting.Contains(type) ? 0 : 91), $"{type}: {capture.TicksOf(3)} ticks");
        }
    }

    [Fact]
    public void Leaving_keeps_the_points_and_damage_outside_still_resets()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(3, 1, 110) };
        Run(capture, tanks, 60);

        tanks[0].Position = new Vector2(Outside, 100);
        Run(capture, tanks, 60);
        Assert.Equal(60, capture.TicksOf(3));
        Assert.Equal(2, capture.Progress(0));

        Run(capture, tanks, 1, Hit(target: 3, damage: 40));
        Assert.Equal(0, capture.TicksOf(3));
        Assert.Equal(0, capture.Progress(0));

        tanks[0].Position = new Vector2(110, 100);
        Run(capture, tanks, 30);
        Assert.Equal(30, capture.TicksOf(3));
    }

    [Fact]
    public void The_base_falls_at_one_hundred_points_and_not_a_tick_sooner()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(1, 1, 110), Tank(2, 1, 90), Tank(3, 1, 100, 110) };
        Run(capture, tanks, 999);
        Assert.Equal(2997, capture.TicksOn(0));
        Assert.Equal(99, capture.Progress(0));
        Assert.False(capture.IsCaptured(0));

        Run(capture, tanks, 1);
        Assert.Equal(BaseCapture.TicksToWin, capture.TicksOn(0));
        Assert.True(capture.IsCaptured(0));
        Assert.Equal(100, capture.Progress(0));
        Assert.False(capture.IsCaptured(1));
    }

    [Fact]
    public void The_shown_points_stop_at_one_hundred()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(1, 1, 110), Tank(2, 1, 90), Tank(3, 1, 100, 110) };
        Run(capture, tanks, 1100);
        Assert.Equal(3300, capture.TicksOn(0));
        Assert.Equal(100, capture.Progress(0));
    }

    [Fact]
    public void Attackers_outside_still_count_towards_the_base()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(1, 1, 110), Tank(2, 1, 90) };
        Run(capture, tanks, 600);
        Assert.Equal(1200, capture.TicksOn(0));

        tanks[1].Position = new Vector2(Outside, 100);
        Run(capture, tanks, 1);
        Assert.Equal(600, capture.TicksOf(2));
        Assert.Equal(601 + 600, capture.TicksOn(0));

        Run(capture, new[] { tanks[0] }, 1);   // tank 2 left the battle: its points go with it
        Assert.Equal(0, capture.TicksOf(2));
        Assert.Equal(0, capture.BestOf(2));
        Assert.Equal(602, capture.TicksOn(0));
    }

    [Fact]
    public void A_tank_that_comes_back_starts_from_nothing()
    {
        var capture = NewCapture();
        Run(capture, new[] { Tank(1, 1, 110) }, 90);
        Run(capture, Array.Empty<TankState>(), 1);
        Run(capture, new[] { Tank(1, 1, Outside) }, 1);
        Assert.Equal(0, capture.TicksOf(1));
        Assert.Equal(0, capture.BestOf(1));
    }

    [Fact]
    public void Each_team_earns_only_on_the_other_teams_base()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(1, 0, 100) };   // a team 0 tank on its own base: a defender
        Run(capture, tanks, 30);
        Assert.Equal(0, capture.TicksOf(1));
        Assert.Equal(0, capture.Progress(0));
        Assert.Equal(0, capture.Progress(1));

        tanks = new[] { Tank(1, 0, 300), Tank(2, 1, 100), Tank(3, 1, Outside) };
        Run(capture, tanks, 60);
        Assert.Equal(60, capture.TicksOn(1));
        Assert.Equal(60, capture.TicksOn(0));
        Assert.Equal(2, capture.Progress(1));
        Assert.Equal(2, capture.Progress(0));

        tanks[2].Position = new Vector2(310, 100);   // a team 1 defender pauses team 0 on team 1's base only
        Run(capture, tanks, 30);
        Assert.Equal(60, capture.TicksOn(1));
        Assert.Equal(90, capture.TicksOn(0));
        Assert.Equal(0, capture.TicksOf(3));
    }

    [Fact]
    public void Tanks_of_other_teams_take_no_part()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(1, 2, 100), Tank(2, 2, 300), Tank(3, 1, 110), Tank(4, 0, 290) };
        Run(capture, tanks, 30);
        Assert.Equal(0, capture.TicksOf(1));
        Assert.Equal(0, capture.TicksOf(2));
        Assert.Equal(30, capture.TicksOn(0));   // no defender, no pause
        Assert.Equal(30, capture.TicksOn(1));
    }

    [Fact]
    public void Both_bases_can_fall_on_the_same_tick()
    {
        var capture = NewCapture();
        TankState[] tanks =
        {
            Tank(1, 1, 110), Tank(2, 1, 90), Tank(3, 1, 100, 110),
            Tank(4, 0, 310), Tank(5, 0, 290), Tank(6, 0, 300, 110),
        };
        Run(capture, tanks, 999);
        Assert.False(capture.IsCaptured(0));
        Assert.False(capture.IsCaptured(1));
        Run(capture, tanks, 1);
        Assert.True(capture.IsCaptured(0));
        Assert.True(capture.IsCaptured(1));
    }

    [Fact]
    public void Best_keeps_the_most_whole_points_held_at_once()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(3, 1, 110) };
        Run(capture, tanks, 89);
        Assert.Equal(2, capture.BestOf(3));
        Run(capture, tanks, 1);
        Run(capture, tanks, 1, Hit(target: 3, damage: 30));
        Run(capture, tanks, 29);
        Assert.Equal(3, capture.BestOf(3));
        Assert.Equal(29, capture.TicksOf(3));
        Run(capture, tanks, 1);
        Assert.Equal(3, capture.BestOf(3));
        Assert.Equal(30, capture.TicksOf(3));
        Assert.Equal(0, capture.BestOf(77));
        Assert.Equal(0, capture.TicksOf(77));
    }

    [Fact]
    public void A_tanks_ticks_stop_at_the_win()
    {
        var capture = NewCapture();
        TankState[] tanks = { Tank(1, 1, 110) };
        Run(capture, tanks, 3100);
        Assert.Equal(BaseCapture.TicksToWin, capture.TicksOf(1));
        Assert.Equal(BaseCapture.PointsToWin, capture.BestOf(1));
        Assert.True(capture.IsCaptured(0));
    }

    [Fact]
    public void Bad_arguments_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new BaseCapture(Array.Empty<TeamBase>()));
        Assert.Throws<ArgumentException>(() => new BaseCapture(null!));
        Assert.Throws<ArgumentException>(() => new BaseCapture(new[] { T0, T0 with { Centre = new Vector2(300, 100) } }));

        var capture = NewCapture();
        Assert.Throws<ArgumentOutOfRangeException>(() => capture.Progress(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => capture.TicksOn(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => capture.IsCaptured(255));

        var tooMany = Enumerable.Range(1, Simulation.MaxTanks + 1).Select(id => Tank(id, 1, Outside)).ToArray();
        Assert.Throws<ArgumentException>(() => capture.Update(tooMany, default));
    }

    [Fact]
    public void The_bases_are_kept_as_given()
    {
        var bases = new[] { T1, T0 };
        Assert.Same(bases, new BaseCapture(bases).Bases);
    }

    [Fact]
    public void Updating_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var capture = NewCapture();
            TankState[] tanks =
            {
                Tank(1, 1, 110), Tank(2, 1, 90), Tank(3, 1, 100, 110),
                Tank(4, 0, 310), Tank(5, 0, 290), Tank(6, 0, 300, 110),
            };
            GameEvent[] hits = { Hit(target: 2, damage: 30), Splash(target: 5, damage: 10), Destroyed(target: 6) };
            GameEvent[] none = Array.Empty<GameEvent>();

            void Tick(int i)
            {
                float shift = i % 50 < 25 ? 0 : 60;   // the first tank of each team in and out of the circle
                tanks[0].Position = new Vector2(110 + shift, 100);
                tanks[3].Position = new Vector2(310 - shift, 100);
                tanks[1].Position = new Vector2(i % 40 < 20 ? 90 : 300, 100);   // now and then a defender on team 1's base
                capture.Update(tanks, i % 17 == 0 ? hits : none);
            }

            for (int i = 0; i < 100; i++) Tick(i);
            Allocation.Window window = Allocation.Start();
            for (int i = 100; i < 1100; i++) Tick(i);
            Allocation.Measured measured = window.Stop();
            Assert.True(capture.TicksOn(0) + capture.TicksOn(1) > 0, "someone earned something");
            return measured;
        });
    }
}

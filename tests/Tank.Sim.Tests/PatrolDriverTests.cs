using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// A light tank driven by a <see cref="PatrolDriver"/>, on flat open ground unless a test gives another field; a second
/// tank stands still at <c>shootAt</c>.
/// </summary>
public sealed class PatrolDriverTests
{
    private const int Tps = SimConstants.TickRate;

    private sealed class World
    {
        public readonly Simulation Sim;
        public readonly PatrolDriver Driver;
        public readonly int Patrol;
        public readonly Vector2 ShootAt;
        public readonly List<(int Tick, Vector3 At)> Fired = new(), Bursts = new();
        public readonly List<(Ballistics.Launch Launch, Vector2 From)> Aims = new();   // the gun's undispersed launch at each shot, and where the tank stood
        public readonly List<int> WaitStarts = new();
        public int Laps;

        public World(IReadOnlyList<PatrolPoint> route, Vector2 start, float yaw, Vector2 shootAt, Battlefield? field = null)
        {
            Sim = new Simulation(field ?? TestWorld.FlatField());
            Driver = new PatrolDriver(route);
            ShootAt = shootAt;
            Patrol = Sim.AddTank(TankCatalog.Light, start, yaw);
            Sim.AddTank(TankCatalog.Medium, shootAt, 0f);
        }

        public ref TankState Tank => ref Sim.TankRef(Patrol);

        public void Run(int ticks)
        {
            var inputs = new InputCmd[2];
            for (int i = 0; i < ticks; i++)
            {
                int tick = Sim.Tick + 1;
                bool waiting = Driver.Waiting;
                int next = Driver.Next;
                inputs[0] = Driver.Command(Sim.Tanks[0], Sim.Field, tick, ShootAt);
                inputs[1] = new InputCmd(tick, 0, 0, 0f, 0f, false);
                Sim.Step(inputs);
                if (!waiting && Driver.Waiting) WaitStarts.Add(tick);
                if (Driver.Next == 0 && next != 0) Laps++;
                foreach (GameEvent e in Sim.Events)
                {
                    if (e.TankId != Patrol) continue;
                    if (e.Type == GameEventType.ShellFired)
                    {
                        Fired.Add((tick, e.Position));
                        Aims.Add((Ballistics.Aim(Sim.Tanks[0], Sim.Field), Sim.Tanks[0].Position));
                    }
                    if (e.Type == GameEventType.ShellExploded) Bursts.Add((tick, e.Position));
                    Assert.False(e.Type is GameEventType.TankHit or GameEventType.SplashDamage, $"the patrol hit tank {e.TargetTankId}");
                }
            }
        }
    }

    private static readonly Vector2 Start = new(100, 180);

    /// <summary>Stop 10 s at (100, 120), 60 m north of the start; then 40 m east and back.</summary>
    private static PatrolPoint[] NorthThenEast() => new PatrolPoint[]
    {
        new(new Vector2(100, 120), 10f),
        new(new Vector2(140, 120), 0f),
        new(Start, 0f),
    };

    [Fact]
    public void A_route_needs_two_points()
    {
        Assert.Throws<ArgumentException>(() => new PatrolDriver(new[] { new PatrolPoint(Start, 1f) }));
    }

    [Fact]
    public void It_drives_to_a_point_it_waits_at_and_stops_on_it()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 40));
        world.Run(10 * Tps);
        Assert.Single(world.WaitStarts);
        Assert.True(world.WaitStarts[0] < 8 * Tps, $"took {world.WaitStarts[0] / (float)Tps:F1} s to drive 60 m");
        Assert.True(world.Driver.Waiting);
        Assert.Equal(0, world.Driver.Next);
        Assert.InRange(Vector2.Distance(world.Tank.Position, new Vector2(100, 120)), 0f, PatrolDriver.StopRadius);
        Assert.Equal(0f, world.Tank.Speed, 0.3f);
    }

    [Fact]
    public void It_waits_ten_seconds_then_drives_on_to_the_next_point()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 40));
        world.Run(8 * Tps);
        int stopped = world.WaitStarts.Single();
        world.Run(stopped + 10 * Tps - 1 - world.Sim.Tick);
        Assert.True(world.Driver.Waiting, "still waiting 1 tick short of 10 s");
        Vector2 there = world.Tank.Position;
        world.Run(1);
        Assert.False(world.Driver.Waiting);
        Assert.Equal(1, world.Driver.Next);
        world.Run(3 * Tps);
        Assert.True(world.Tank.Position.X > there.X + 5f, $"has not headed east: {world.Tank.Position}");
    }

    [Fact]
    public void A_point_behind_it_is_turned_to_on_the_spot_first()
    {
        var world = new World(NorthThenEast(), Start, MathF.PI, new Vector2(100, 40));   // facing south, away from the point
        var input = world.Driver.Command(world.Tank, world.Sim.Field, 1, world.ShootAt);
        Assert.Equal(0, input.Throttle);
        Assert.NotEqual(0, input.Steer);
        world.Run(10 * Tps);
        Assert.Single(world.WaitStarts);
        Assert.InRange(Vector2.Distance(world.Tank.Position, new Vector2(100, 120)), 0f, PatrolDriver.StopRadius);
    }

    [Fact]
    public void Where_the_route_turns_sharply_it_slows_down_and_turns_on_the_spot()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 20));   // turns 124° at (140, 120)
        float speedThere = -1f;
        for (int i = 0; i < 40 * Tps && speedThere < 0; i++)
        {
            int next = world.Driver.Next;
            world.Run(1);
            if (next == 1 && world.Driver.Next == 2) speedThere = world.Tank.Speed;
        }
        Assert.InRange(MathF.Abs(speedThere), 0f, 0.5f);
        Vector2 corner = new(140, 120);
        world.Run(2 * Tps);
        float off = SandboxMap.DistanceToSegment(world.Tank.Position, corner, Start);
        Assert.True(off < 1.5f, $"{off:F1} m off the leg after the corner");
    }

    [Fact]
    public void While_waiting_it_fires_every_four_seconds_starting_once_stopped_for_two_and_a_half()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 20));   // 100 m due north of the stop
        world.Run(8 * Tps);
        Assert.Empty(world.Fired);
        int stopped = world.WaitStarts.Single();
        world.Run(stopped + 10 * Tps - world.Sim.Tick);
        Assert.Equal(2, world.Fired.Count);
        Assert.InRange(world.Fired[0].Tick - stopped, (int)(PatrolDriver.FirstShot * Tps), (int)((PatrolDriver.FirstShot + 1) * Tps));
        Assert.Equal((int)(PatrolDriver.FireEvery * Tps), world.Fired[1].Tick - world.Fired[0].Tick);
        world.Run(20 * Tps);
        Assert.Equal(2, world.Fired.Count);   // none while driving on
    }

    /// <summary>
    /// The gun at a shot points from its muzzle at the ground 25 m short of <paramref name="point"/> and 8 m to its left,
    /// seen from where the tank stood (the numbers are the design's, written out).
    /// </summary>
    private static void AssertLaidOnTheGroundShortOf((Ballistics.Launch Launch, Vector2 From) shot, Vector2 point, Battlefield field)
    {
        var (aim, from) = shot;
        Vector2 toward = Vector2.Normalize(point - from), left = new(toward.Y, -toward.X);
        Vector2 ground = point - toward * 25f + left * 8f;
        var expected = new Vector3(ground.X, field.Terrain.HeightAt(ground.X, ground.Y), ground.Y);
        float off = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(aim.Velocity), Vector3.Normalize(expected - aim.Origin)), -1f, 1f));
        Assert.True(off < Angles.Deg(0.25f), $"the gun is {off * 180 / MathF.PI:F2}° off the aim at {expected}");
    }

    /// <summary>The stop is at (100, 120); the tank arrives facing north. The point is 100 m north of it, or 100 m south (behind).</summary>
    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void It_shoots_the_ground_25_m_short_of_the_point_and_8_m_to_its_left(float south)
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 120) + new Vector2(0, south) * 100f);
        world.Run(20 * Tps);
        Assert.Equal(2, world.Aims.Count);
        Assert.All(world.Aims, shot => AssertLaidOnTheGroundShortOf(shot, world.ShootAt, world.Sim.Field));
        Assert.Equal(2, world.Bursts.Count);
        Assert.All(world.Bursts, b => Assert.Equal(0f, b.At.Y, 0.5f));   // the real, dispersed shots burst on the ground
    }

    [Fact]
    public void On_rolling_ground_it_lays_the_gun_on_the_ground_where_it_lies()
    {
        // The sandbox patrol from its start into its first bush patch, firing towards a tank 109 m south-west of it.
        Battlefield field = SandboxMap.Create();
        Vector2 start = SandboxMap.PatrolStart;
        var world = new World(SandboxMap.PatrolRoute, start, Angles.YawOf(SandboxMap.PatrolRoute[0].Position - start), new Vector2(224, 352), field);
        world.Run(15 * Tps);
        Assert.Equal(2, world.Aims.Count);
        Assert.All(world.Aims, shot => AssertLaidOnTheGroundShortOf(shot, world.ShootAt, field));
    }

    [Fact]
    public void A_point_within_fifty_metres_gets_no_shot()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 120 - PatrolDriver.HoldFireWithin + 5f));
        world.Run(20 * Tps);
        Assert.Single(world.WaitStarts);
        Assert.Empty(world.Fired);
    }

    [Fact]
    public void It_keeps_going_round_the_route()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 20));
        world.Run(80 * Tps);
        Assert.InRange(world.Laps, 2, 4);
        Assert.InRange(world.WaitStarts.Count, world.Laps, world.Laps + 1);   // one stop a lap
    }

    [Fact]
    public void Driving_waiting_and_shooting_allocate_nothing_per_tick()
    {
        Allocation.AssertNone(() =>
        {
            var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 20));
            world.Run(20 * Tps);   // warm up: drive, stop, wait, shoot, drive on
            var inputs = new InputCmd[2];
            int shots = 0, stops = 0;
            Allocation.Window window = Allocation.Start();
            for (int i = 0; i < 40 * Tps; i++)   // round again to the stop and shoot there
            {
                int tick = world.Sim.Tick + 1;
                bool waiting = world.Driver.Waiting;
                inputs[0] = world.Driver.Command(world.Sim.Tanks[0], world.Sim.Field, tick, world.ShootAt);
                inputs[1] = new InputCmd(tick, 0, 0, 0f, 0f, false);
                world.Sim.Step(inputs);
                if (!waiting && world.Driver.Waiting) stops++;
                foreach (GameEvent e in world.Sim.Events) if (e.TankId == world.Patrol && e.Type == GameEventType.ShellFired) shots++;
            }
            Allocation.Measured measured = window.Stop();
            Assert.True(stops >= 1 && shots >= 2, $"{stops} stops and {shots} shots while measuring");
            return measured;
        });
    }

    [Fact]
    public void Knocked_out_while_waiting_it_drives_on_once_revived_after_the_wait()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 20));
        world.Run(8 * Tps);
        int stopped = world.WaitStarts.Single();
        world.Tank.IsDestroyed = true;
        world.Tank.Health = 0;
        world.Run(stopped + 12 * Tps - world.Sim.Tick);   // lies wrecked past the end of its wait
        Assert.Equal(new Vector2(100, 120), world.Tank.Position, new Near(PatrolDriver.StopRadius));
        world.Sim.Revive(world.Patrol);
        world.Run(3 * Tps);
        Assert.Equal(1, world.Driver.Next);
        Assert.True(world.Tank.Position.X > 105f, $"has not driven on east: {world.Tank.Position}");
    }

    private sealed class Near(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector2 v) => 0;
    }

    [Fact]
    public void A_wreck_holds_still_and_carries_on_once_revived()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 20));
        world.Run(2 * Tps);
        world.Tank.IsDestroyed = true;
        world.Tank.Health = 0;
        InputCmd input = world.Driver.Command(world.Tank, world.Sim.Field, world.Sim.Tick + 1, world.ShootAt);
        Assert.Equal(0, input.Throttle);
        Assert.Equal(0, input.Steer);
        Assert.False(input.Fire);
        world.Sim.Revive(world.Patrol);
        world.Run(10 * Tps);
        Assert.Single(world.WaitStarts);
        Assert.Equal(0, world.Driver.Next);
    }
}

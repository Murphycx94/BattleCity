using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>A medium tank of team 0 at (100, 190); a light tank of team 1 on the line due north of it, standing still.</summary>
public sealed class VisionTrackerTests
{
    private static readonly Vector2 ObserverAt = new(100, 190);

    private sealed class World
    {
        public readonly Simulation Sim;
        public readonly VisionTracker Vision;
        public readonly int Observer, Target;
        private readonly List<InputCmd> _inputs = new();

        public World(float targetDistance, params Prop[] props)
        {
            Sim = new Simulation(TestWorld.FlatField(props));
            Observer = Add(TankCatalog.Medium, ObserverAt, 0f, team: 0);
            Target = Add(TankCatalog.Light, ObserverAt - new Vector2(0, targetDistance), MathF.PI / 2, team: 1);
            Vision = new VisionTracker(Sim);
        }

        public int Add(TankSpec spec, Vector2 at, float yaw, byte team)
        {
            _inputs.Add(new InputCmd(0, 0, 0, yaw, 0f, false));
            return Sim.AddTank(spec, at, yaw, team);
        }

        /// <summary>Steps <paramref name="ticks"/> times with every tank holding still, updating the vision after each step.</summary>
        public void Run(int ticks, Func<int, InputCmd>? input = null)
        {
            InputCmd[] inputs = _inputs.ToArray();
            for (int i = 0; i < ticks; i++)
            {
                if (input is not null) for (int k = 0; k < inputs.Length; k++) inputs[k] = input(k);
                Sim.Step(inputs);
                Vision.Update();
            }
        }

        public void MoveTarget(float distance) => Sim.TankRef(Target).Position = ObserverAt - new Vector2(0, distance);
    }

    [Fact]
    public void The_first_update_happens_at_once_then_every_sixth_tick()
    {
        var world = new World(100f);
        world.Run(1);
        Assert.True(world.Vision.IsVisible(world.Target, 0), "seen on the first update");

        var far = new World(250f);   // out of range
        far.Run(1);
        Assert.False(far.Vision.IsVisible(far.Target, 0));
        far.MoveTarget(100f);
        far.Run(4);                  // ticks 2–5: no update
        Assert.False(far.Vision.IsVisible(far.Target, 0));
        far.Run(1);                  // tick 6
        Assert.True(far.Vision.IsVisible(far.Target, 0));
    }

    [Fact]
    public void A_tank_out_of_sight_stays_visible_for_two_seconds()
    {
        var world = new World(100f);
        world.Run(6);                 // seen at the tick-6 update
        world.MoveTarget(250f);       // gone out of range
        world.Run(VisionTracker.LingerTicks);
        Assert.True(world.Vision.IsVisible(world.Target, 0), "still within the linger");
        world.Run(1);
        Assert.False(world.Vision.IsVisible(world.Target, 0));
        Assert.Equal(6 + VisionTracker.LingerTicks + 1, world.Sim.Tick);
    }

    [Fact]
    public void A_team_sees_what_any_of_its_tanks_sees()
    {
        // The target hides behind a tall slab from the first observer, but a second one off to the side sees round it.
        var slab = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 150), new Vector2(4, 0.5f), 0), -1f, 7f);
        var world = new World(80f, slab);
        world.Run(1);
        Assert.False(world.Vision.IsVisible(world.Target, 0));
        world.Add(TankCatalog.Medium, new Vector2(140, 150), 0f, team: 0);
        world.Run(6);
        Assert.True(world.Vision.IsVisible(world.Target, 0));
        Assert.False(world.Vision.Inspect(world.Observer, world.Target).Seen, "not by the first observer");
    }

    [Fact]
    public void Own_tanks_and_wrecks_are_always_visible()
    {
        var world = new World(250f);
        world.Run(1);
        Assert.True(world.Vision.IsVisible(world.Observer, 0));
        Assert.True(world.Vision.IsVisible(world.Target, 1));
        Assert.False(world.Vision.IsVisible(world.Target, 0));
        world.Sim.TankRef(world.Target).IsDestroyed = true;
        Assert.True(world.Vision.IsVisible(world.Target, 0));
    }

    [Fact]
    public void A_revived_wreck_stays_in_view_for_the_linger()
    {
        var slab = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 150), new Vector2(4, 0.5f), 0), -1f, 7f);
        var world = new World(80f, slab);   // hidden behind the slab
        world.Run(6);
        Assert.False(world.Vision.IsVisible(world.Target, 0));
        world.Sim.TankRef(world.Target).IsDestroyed = true;
        world.Run(6);                       // the tick-12 update sees the wreck
        world.Sim.Revive(world.Target);
        world.Run(1);                       // tick 13: no update yet
        Assert.True(world.Vision.IsVisible(world.Target, 0), "blinked out on revival");
        world.Run(VisionTracker.LingerTicks);
        Assert.False(world.Vision.IsVisible(world.Target, 0), "hidden again once the linger is over");
    }

    [Fact]
    public void Tanks_between_do_not_block_the_view()
    {
        var world = new World(100f);
        world.Add(TankCatalog.Heavy, ObserverAt - new Vector2(0, 30f), MathF.PI / 2, team: 0);   // side on, right across the line
        world.Add(TankCatalog.Heavy, ObserverAt - new Vector2(0, 70f), MathF.PI / 2, team: 1);
        world.Run(1);
        Assert.True(world.Vision.Inspect(world.Observer, world.Target).LineClear);
        Assert.True(world.Vision.IsVisible(world.Target, 0));
    }

    [Fact]
    public void A_tank_that_does_not_watch_sees_nothing_but_is_still_seen()
    {
        var world = new World(100f);
        world.Vision.SetWatching(world.Observer, false);
        world.Run(6);
        Assert.False(world.Vision.IsVisible(world.Target, 0), "the only watcher of team 0 is blind");
        Assert.True(world.Vision.IsVisible(world.Observer, 1), "the light tank sees the dummy");
    }

    [Fact]
    public void A_wreck_watches_nothing()
    {
        var world = new World(100f);
        world.Sim.TankRef(world.Observer).IsDestroyed = true;
        world.Run(6);
        Assert.False(world.Vision.IsVisible(world.Target, 0));
    }

    [Fact]
    public void A_tank_driving_or_turning_counts_as_moving()
    {
        // 175 m: out of range of a still light tank (168 m), within range of a moving one (193.5 m).
        var world = new World(175f);
        world.Run(6);
        Assert.False(world.Vision.IsVisible(world.Target, 0));
        world.Run(6, k => k == 1 ? TestWorld.Cmd(steer: 1, aimYaw: MathF.PI / 2) : TestWorld.Cmd());   // turning on the spot
        Assert.Equal(193.5f, world.Vision.Inspect(world.Observer, world.Target).DetectionRange, 1e-3f);
        Assert.True(world.Vision.IsVisible(world.Target, 0));
    }

    [Fact]
    public void A_tank_driving_straight_counts_as_moving()
    {
        var world = new World(175f);   // side on: driving takes it west, across the line of sight
        world.Run(6);
        Assert.False(world.Vision.IsVisible(world.Target, 0));
        world.Run(6, k => k == 1 ? TestWorld.Cmd(throttle: 1, aimYaw: MathF.PI / 2) : TestWorld.Cmd());
        Assert.Equal(193.5f, world.Vision.Inspect(world.Observer, world.Target).DetectionRange, 1e-3f);
        Assert.True(world.Vision.IsVisible(world.Target, 0));
    }

    [Fact]
    public void A_tank_that_has_stopped_turning_is_still_again()
    {
        var world = new World(175f);
        world.Run(6);
        world.Run(6, k => k == 1 ? TestWorld.Cmd(steer: 1, aimYaw: MathF.PI / 2) : TestWorld.Cmd());
        Assert.True(world.Vision.IsVisible(world.Target, 0));
        world.Run(12, k => k == 1 ? TestWorld.Cmd(aimYaw: world.Sim.Tanks[1].Yaw) : TestWorld.Cmd());   // holds still
        Assert.Equal(168f, world.Vision.Inspect(world.Observer, world.Target).DetectionRange, 1e-3f);
        world.Run(VisionTracker.LingerTicks, k => k == 1 ? TestWorld.Cmd(aimYaw: world.Sim.Tanks[1].Yaw) : TestWorld.Cmd());
        Assert.False(world.Vision.IsVisible(world.Target, 0), "out of range once still and the linger is over");
    }

    [Fact]
    public void A_second_update_on_the_same_tick_changes_nothing()
    {
        var world = new World(175f);
        world.Run(6);
        world.Run(6, k => k == 1 ? TestWorld.Cmd(steer: 1, aimYaw: MathF.PI / 2) : TestWorld.Cmd());   // turning: moving at tick 12
        world.Vision.Update();
        Assert.Equal(193.5f, world.Vision.Inspect(world.Observer, world.Target).DetectionRange, 1e-3f);
    }

    [Fact]
    public void Inspect_reports_the_same_look_as_the_rules()
    {
        var world = new World(120f);
        world.Run(6);
        Sighting look = world.Vision.Inspect(world.Observer, world.Target);
        Assert.Equal(Spotting.Look(world.Sim.Tanks[0], world.Sim.Tanks[1], world.Sim.Field, world.Sim.Tick, false), look);
        Assert.True(look.Seen);
    }

    [Fact]
    public void Updating_allocates_nothing()
    {
        var world = new World(100f);
        for (int k = 0; k < 6; k++) world.Add(TankCatalog.Heavy, new Vector2(60 + 15 * k, 120), 0f, (byte)(k % 2));
        world.Run(12);
        InputCmd[] hold = Enumerable.Range(0, world.Sim.TankCount).Select(_ => TestWorld.Cmd()).ToArray();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++)
        {
            world.Sim.Step(hold);
            world.Vision.Update();
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Updating_on_the_sandbox_map_allocates_nothing()
    {
        // Rocks, bushes and walls in the sight lines, nine tanks of two teams, one of them driving round.
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Medium, new Vector2(224, 352), 0f, team: 0);   // in sight of the bush patch east of the spawn
        foreach (TargetSpot spot in SandboxMap.TargetSpots) sim.AddTank(spot.Spec, spot.Position, spot.Yaw, team: 1);
        sim.AddTank(TankCatalog.Light, SandboxMap.BushPatches[2] + new Vector2(-20, 0), 0f, team: 1);
        var vision = new VisionTracker(sim);
        var inputs = new InputCmd[sim.TankCount];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = TestWorld.Cmd(throttle: (sbyte)(i == inputs.Length - 1 ? 1 : 0), steer: (sbyte)(i == inputs.Length - 1 ? 1 : 0));
        for (int i = 0; i < 30; i++) { sim.Step(inputs); vision.Update(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        int seen = 0;
        for (int i = 0; i < 300; i++)
        {
            sim.Step(inputs);
            vision.Update();
            foreach (TankState t in sim.Tanks) if (vision.IsVisible(t.Id, 0)) seen++;
            if (vision.Inspect(sim.Tanks[0].Id, sim.Tanks[^1].Id).Bushes > 0) seen++;
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(seen > 300, "some of them were seen");
    }

    [Fact]
    public void Asking_about_a_team_out_of_range_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new World(100f).Vision.IsVisible(1, VisionTracker.MaxTeams));
}

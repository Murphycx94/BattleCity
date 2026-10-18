using Tank.Client.Sandbox;
using Tank.Sim;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

using SN = System.Numerics;

namespace Tank.Art.Tests;

public class RangeDemoTests
{
    private static readonly Battlefield Field = SandboxMap.Create();

    /// <summary>Every demo, with every tank --tank can give the player.</summary>
    public static TheoryData<string, string> DemosAndTanks()
    {
        var data = new TheoryData<string, string>();
        foreach (string name in RangeDemo.Names)
        foreach (TankSpec spec in TankCatalog.All)
            data.Add(name, spec.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(DemosAndTanks))]
    public void Parks_the_player_on_open_ground(string demo, string tank)
    {
        RangeDemo d = RangeDemo.Find(demo)!;
        Obb2 hull = TankMotion.Footprint(TankCatalog.ById(tank), d.PlayerPosition, d.PlayerYaw);
        Assert.Equal(d.PlayerPosition, Field.ClampToPlayable(d.PlayerPosition));
        foreach (Prop p in Field.Props)
            Assert.False(p.IsSolid && Obb2.TryGetPenetration(hull, p.Footprint, out _), $"parked on rock {p.Id}");
        foreach (TargetSpot s in SandboxMap.TargetSpots)
            Assert.False(Obb2.TryGetPenetration(hull, TankMotion.Footprint(s.Spec, s.Position, s.Yaw), out _), "parked on a target");
    }

    /// <summary>The demos whose shots must reach their target with nothing solid in the way.</summary>
    private static readonly string[] OpenDemos = { "pen", "ricochet", "nopen", "he", "kill", "nopen-heavy", "kill-td", "turn", "fence", "bush", "lamp" };

    public static TheoryData<string, string> OpenDemosAndTanks()
    {
        var data = new TheoryData<string, string>();
        foreach (string name in OpenDemos)
        foreach (TankSpec spec in TankCatalog.All)
            data.Add(name, spec.Id);
        return data;
    }

    public static TheoryData<string> Tanks() => new() { "light", "medium", "heavy", "td" };

    /// <summary>The parked player's trunnion and the demo's aim point.</summary>
    private static (SN.Vector3 From, SN.Vector3 To) LineOfFire(RangeDemo d, TankSpec spec)
    {
        var parked = new TankState { Spec = spec, Position = d.PlayerPosition, Yaw = d.PlayerYaw };
        SN.Vector3 trunnion = TankPose.Compute(parked, Field.Terrain).Trunnion(spec);
        SN.Vector2 spot = d.Spot.Position;
        return (trunnion, d.AimPoint(Field.Terrain.HeightAt(spot.X, spot.Y)));
    }

    [Theory]
    [MemberData(nameof(OpenDemosAndTanks))]
    public void Shoots_along_a_line_no_wall_or_rock_stands_in(string demo, string tank)
    {
        var (from, to) = LineOfFire(RangeDemo.Find(demo)!, TankCatalog.ById(tank));
        bool blocked = Field.Raycast(from, to, out RayHit hit);
        Assert.False(blocked, $"stopped by {(blocked && hit.PropId != RayHit.TerrainId ? Field.PropById(hit.PropId).Kind : "the ground")} at {hit.Point}");
    }

    [Theory]
    [InlineData("wall")]
    [InlineData("breach")]
    public void The_wall_demos_shoot_into_the_yards_stone_wall(string demo)
    {
        foreach (TankSpec spec in TankCatalog.All)
        {
            var (from, to) = LineOfFire(RangeDemo.Find(demo)!, spec);
            Assert.True(Field.Raycast(from, to, out RayHit hit), $"{spec.Id}: nothing in the way");
            Assert.True(hit.PropId != RayHit.TerrainId, $"{spec.Id}: the ground is in the way");
            Prop wall = Field.PropById(hit.PropId);
            Assert.Equal(PropKind.StoneWall, wall.Kind);
            Assert.True(wall.Footprint.Center.X > 169f, $"{spec.Id}: hits stone wall {wall.Id}, not the yard's east wall");
        }
    }

    [Theory]
    [MemberData(nameof(Tanks))]
    public void The_fence_demo_shoots_in_through_a_gate_panel(string tank)
    {
        var (from, to) = LineOfFire(RangeDemo.Find("fence")!, TankCatalog.ById(tank));
        Prop panel = Assert.Single(Field.Props, p => p.Kind == PropKind.Fence && Intersect.SegmentPrism(p.Footprint, p.Bottom, p.Top, from, to, out _));
        Assert.Equal(new SN.Vector2(150.69f, 338f), panel.Footprint.Center, new Near(0.01f));   // the gate's north panel
    }

    [Theory]
    [MemberData(nameof(Tanks))]
    public void The_bush_demo_shoots_through_bushes(string tank)
    {
        var (from, to) = LineOfFire(RangeDemo.Find("bush")!, TankCatalog.ById(tank));
        Assert.Contains(Field.Props, p => p.Kind == PropKind.Bush
            && Intersect.SegmentCylinder(p.Footprint.Center, p.Footprint.HalfExtents.X, p.Bottom, p.Top, from, to, out _));
    }

    [Theory]
    [InlineData("heavy", true)]
    [InlineData("medium", false)]
    [InlineData("light", false)]
    [InlineData("td", false)]
    public void The_ram_demo_breaks_into_the_yard_only_in_a_heavy_tank(string tank, bool breaksThrough)
    {
        RangeDemo d = RangeDemo.Find("ram")!;
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.ById(tank), d.PlayerPosition, d.PlayerYaw);
        var down = new List<int>();
        for (int tick = 1; tick < d.FireTick + d.DriveTicks + SimConstants.TickRate; tick++)
        {
            sim.Step(new[] { d.Command(new InputCmd(tick, 0, 0, d.PlayerYaw, 0f, false), sim.Tanks[0], sim.Field, hasFired: false) });   // as SandboxAutomation sends it
            foreach (GameEvent e in sim.Events)
                if (e.Type == GameEventType.PropDestroyed) down.Add(e.PropId);
        }
        Assert.Equal(breaksThrough, down.Count > 0);
        Assert.All(down, id => Assert.Equal(PropKind.BrickWall, sim.Field.PropById(id).Kind));
        Assert.Equal(0f, sim.Tanks[0].Speed);   // where it came to rest
        Assert.Equal(d.PlayerYaw, sim.Tanks[0].Yaw, 1e-4f);   // straight on, the gun straight ahead (a tank destroyer doesn't turn after its aim)
        Obb2 hull = TankMotion.Footprint(sim.Tanks[0].Spec, sim.Tanks[0].Position, sim.Tanks[0].Yaw);
        TargetSpot yard = SandboxMap.TargetSpots[SandboxMap.YardTarget];
        Assert.False(Obb2.TryGetPenetration(hull, TankMotion.Footprint(yard.Spec, yard.Position, yard.Yaw), out _), "drove into the target");
    }

    /// <summary>
    /// Runs a shooting demo as the sandbox does (the player in slot 0 commanded by <see cref="RangeDemo.Command"/>, the
    /// seven targets standing still after it) for <paramref name="seconds"/> of sim time; returns every event.
    /// </summary>
    private static (List<GameEvent> Events, int[] TargetIds) RunDemo(RangeDemo demo, TankSpec spec, float seconds)
    {
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(spec, demo.PlayerPosition, demo.PlayerYaw);
        int[] targets = SandboxMap.TargetSpots.Select(t => sim.AddTank(t.Spec, t.Position, t.Yaw)).ToArray();
        var inputs = new InputCmd[sim.TankCount];
        var events = new List<GameEvent>();
        bool fired = false;
        for (int tick = 1; tick <= seconds * SimConstants.TickRate; tick++)
        {
            inputs[0] = demo.Command(new InputCmd(tick, 0, 0, demo.PlayerYaw, 0f, false), sim.Tanks[0], sim.Field, fired);
            for (int i = 0; i < targets.Length; i++) inputs[1 + i] = new InputCmd(tick, 0, 0, SandboxMap.TargetSpots[i].Yaw, 0f, false);
            sim.Step(inputs);
            events.AddRange(sim.Events.ToArray());
            fired |= events.Any(e => e.Type == GameEventType.ShellFired);
        }
        return (events, targets);
    }

    [Fact]
    public void The_wall_demo_hurts_the_stone_wall_and_not_the_target_behind_it()
    {
        var (events, _) = RunDemo(RangeDemo.Find("wall")!, TankCatalog.Medium, 5f);
        GameEvent hurt = Assert.Single(events, e => e.Type == GameEventType.PropDamaged);
        Assert.Equal(PropKind.StoneWall, Field.PropById(hurt.PropId).Kind);
        Assert.DoesNotContain(events, e => e.Type is GameEventType.TankHit or GameEventType.PropDestroyed);
    }

    [Fact]
    public void The_breach_demo_brings_the_stone_wall_down_and_then_hits_the_target()
    {
        var (events, targets) = RunDemo(RangeDemo.Find("breach")!, TankCatalog.Td, 22f);   // HE every 6.5 s: two bring the wall down, the third hits
        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed && Field.PropById(e.PropId).Kind == PropKind.StoneWall);
        GameEvent hit = events.First(e => e.Type == GameEventType.TankHit);
        Assert.Equal(targets[SandboxMap.YardTarget], hit.TargetTankId);
        Assert.True(events.IndexOf(down) < events.IndexOf(hit), "the wall first, then the target behind it");
    }

    [Theory]
    [InlineData("fence", PropKind.Fence)]
    [InlineData("bush", PropKind.Bush)]
    public void The_fence_and_bush_demos_shoot_through_them_into_the_target(string name, PropKind kind)
    {
        RangeDemo demo = RangeDemo.Find(name)!;
        var (events, targets) = RunDemo(demo, TankCatalog.Medium, 5f);
        GameEvent[] down = events.Where(e => e.Type == GameEventType.PropDestroyed).ToArray();
        Assert.NotEmpty(down);
        Assert.All(down, e => Assert.Equal(kind, Field.PropById(e.PropId).Kind));
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(targets[demo.Target], hit.TargetTankId);
        Assert.True(events.IndexOf(down[^1]) < events.IndexOf(hit), "knocked down on the way to the target");
    }

    [Fact]
    public void The_fence_and_bush_demos_aim_at_the_hull_rear()
    {
        Assert.Equal(DemoAim.HullRear, RangeDemo.Find("fence")!.Aim);
        Assert.True(RangeDemo.Find("fence")!.AimLocal.Z > 0, "the rear face is behind the hull origin (+Z in hull space)");
        Assert.True(RangeDemo.Find("bush")!.AimLocal.Z > 0);
    }

    /// <summary>The demos that shoot at a target the player can see (the rest aim at, or drive into, the wall in front of it).</summary>
    public static TheoryData<string, string> SightedDemosAndTanks()
    {
        var data = new TheoryData<string, string>();
        foreach (string name in RangeDemo.Names.Except(new[] { "wall", "breach", "ram" }))
        foreach (TankSpec spec in TankCatalog.All)
            data.Add(name, spec.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(SightedDemosAndTanks))]
    public void The_target_is_in_sight_when_the_demo_fires(string demo, string tank)
    {
        RangeDemo d = RangeDemo.Find(demo)!;
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.ById(tank), d.PlayerPosition, d.PlayerYaw, team: 0);
        sim.AddTank(d.Spot.Spec, d.Spot.Position, d.Spot.Yaw, team: 1);
        Sighting look = Spotting.Look(sim.Tanks[0], sim.Tanks[1], sim.Field, d.FireTick, targetMoving: false);
        Assert.True(look.Seen, $"{look}");   // hidden, it would get no verdict or damage number on screen
    }

    [Fact]
    public void The_breach_demo_in_a_tank_destroyer_shows_the_yard_target_only_once_the_wall_is_down()
    {
        RangeDemo demo = RangeDemo.Find("breach")!;
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Td, demo.PlayerPosition, demo.PlayerYaw, team: 0);
        int target = sim.AddTank(demo.Spot.Spec, demo.Spot.Position, demo.Spot.Yaw, team: 1);
        var vision = new VisionTracker(sim);
        bool fired = false;
        int down = -1;
        for (int tick = 1; tick <= 22 * SimConstants.TickRate; tick++)
        {
            var inputs = new[]
            {
                demo.Command(new InputCmd(tick, 0, 0, demo.PlayerYaw, 0f, false), sim.Tanks[0], sim.Field, fired),
                new InputCmd(tick, 0, 0, demo.Spot.Yaw, 0f, false),
            };
            sim.Step(inputs);
            vision.Update();
            foreach (GameEvent e in sim.Events)
            {
                fired |= e.Type == GameEventType.ShellFired;
                if (e.Type == GameEventType.PropDestroyed && sim.Field.PropById(e.PropId).Kind == PropKind.StoneWall && down < 0) down = tick;
            }
            if (down < 0) Assert.False(vision.IsVisible(target, 0), $"seen through the wall at tick {tick}");
            else if (tick >= down + VisionTracker.UpdateInterval) Assert.True(vision.IsVisible(target, 0), $"unseen {tick - down} ticks after the breach");
        }
        Assert.True(down > 0, "the wall never came down");
    }

    [Fact]
    public void Only_the_lamp_demo_lets_the_patrol_drive()
    {
        Assert.Equal(new[] { "lamp" }, RangeDemo.Names.Where(n => RangeDemo.Find(n)!.PatrolDrives));
    }

    /// <summary>
    /// The lamp demo as the sandbox runs it: the player commanded by the demo, the still targets watching nobody, the
    /// patrol driving its route. The patrol doesn't see the player until the shot, then does within a vision update
    /// and for at least two seconds after.
    /// </summary>
    [Fact]
    public void The_lamp_demo_is_unseen_until_its_shot_then_seen()
    {
        RangeDemo demo = RangeDemo.Find("lamp")!;
        var sim = new Simulation(SandboxMap.Create());
        int player = sim.AddTank(TankCatalog.Medium, demo.PlayerPosition, demo.PlayerYaw, team: 0);
        var vision = new VisionTracker(sim);
        foreach (TargetSpot t in SandboxMap.TargetSpots) vision.SetWatching(sim.AddTank(t.Spec, t.Position, t.Yaw, team: 1), false);
        SN.Vector2 start = SandboxMap.PatrolStart;
        sim.AddTank(SandboxMap.PatrolSpec, start, Angles.YawOf(SandboxMap.PatrolRoute[0].Position - start), team: 1);
        var driver = new PatrolDriver(SandboxMap.PatrolRoute);
        var inputs = new InputCmd[sim.TankCount];
        int fired = -1;
        for (int tick = 1; tick <= demo.FireTick + 3 * SimConstants.TickRate; tick++)
        {
            inputs[0] = demo.Command(new InputCmd(tick, 0, 0, demo.PlayerYaw, 0f, false), sim.Tanks[0], sim.Field, hasFired: fired >= 0);
            for (int i = 1; i < inputs.Length - 1; i++) inputs[i] = new InputCmd(tick, 0, 0, SandboxMap.TargetSpots[i - 1].Yaw, 0f, false);
            inputs[^1] = driver.Command(sim.Tanks[^1], sim.Field, tick, sim.Tanks[0].Position);
            sim.Step(inputs);
            vision.Update();
            foreach (GameEvent e in sim.Events)
                if (e.Type == GameEventType.ShellFired && e.TankId == player && fired < 0)
                {
                    fired = tick;
                    Assert.True(driver.Waiting && driver.Next == 0, "the patrol is in its first bush patch when the player fires");
                }
            bool seen = vision.IsVisible(player, 1);
            if (fired < 0) Assert.False(seen, $"seen at {tick / (float)SimConstants.TickRate:F1} s, before the shot");
            else if (tick >= fired + VisionTracker.UpdateInterval && tick <= fired + 2 * SimConstants.TickRate) Assert.True(seen, $"unseen {tick - fired} ticks after the shot");
        }
        Assert.Equal(demo.FireTick, fired);
    }

    [Fact]
    public void Only_a_driving_demo_opens_the_throttle_and_only_for_its_drive()
    {
        RangeDemo ram = RangeDemo.Find("ram")!;
        Assert.Equal(78, ram.DriveTicks);   // 2.6 s
        Assert.Equal(0, ram.Throttle(ram.FireTick - 1));
        Assert.Equal(1, ram.Throttle(ram.FireTick));
        Assert.Equal(1, ram.Throttle(ram.FireTick + ram.DriveTicks - 1));
        Assert.Equal(0, ram.Throttle(ram.FireTick + ram.DriveTicks));
        Assert.All(RangeDemo.Names.Where(n => n != "ram"), n => Assert.Equal(0, RangeDemo.Find(n)!.Throttle(RangeDemo.Find(n)!.FireTick)));
    }

    [Fact]
    public void Knows_every_demo_and_nothing_else()
    {
        Assert.All(RangeDemo.Names, name => Assert.NotNull(RangeDemo.Find(name)));
        Assert.Null(RangeDemo.Find("tiger"));
        Assert.Null(RangeDemo.Find(null));
    }

    [Theory]
    [InlineData("pen", "medium")]
    [InlineData("ricochet", "medium")]
    [InlineData("nopen", "medium")]
    [InlineData("he", "medium")]
    [InlineData("kill", "medium")]
    [InlineData("turn", "medium")]
    [InlineData("nopen-heavy", "heavy")]
    [InlineData("kill-td", "td")]
    [InlineData("wall", "medium")]
    [InlineData("breach", "medium")]
    [InlineData("fence", "medium")]
    [InlineData("ram", "medium")]
    [InlineData("bush", "medium")]
    [InlineData("lamp", "medium")]
    public void Shoots_the_kind_of_tank_it_was_written_for(string demo, string kind) =>
        Assert.Equal(kind, RangeDemo.Find(demo)!.Spot.Spec.Id);

    private sealed class Near(float tolerance) : IEqualityComparer<SN.Vector2>
    {
        public bool Equals(SN.Vector2 a, SN.Vector2 b) => SN.Vector2.Distance(a, b) <= tolerance;
        public int GetHashCode(SN.Vector2 v) => 0;
    }
}

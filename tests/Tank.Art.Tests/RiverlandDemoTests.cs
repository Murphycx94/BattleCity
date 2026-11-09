using Tank.Client.Sandbox;
using Tank.Sim;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

using SN = System.Numerics;

namespace Tank.Art.Tests;

/// <summary>
/// The riverland's range demos (riverland design §6.8), each run headless as the test drive runs it: the player in slot 0
/// commanded by <see cref="RangeDemo.Command"/>, the eight practice targets standing still after it. The map is generated
/// once (<see cref="MapDefinition.NewBattlefield"/> keeps it) and each run plays on a fresh copy.
/// </summary>
public class RiverlandDemoTests
{
    private const float Kmh = 1f / 3.6f;
    private const byte PlayerTeam = 0, TargetTeam = 1;   // TargetRange's

    [Fact]
    public void Riverland_lists_its_four_demos()
    {
        Assert.Equal(new[] { "house", "ram-house", "trees", "ford" }, RiverlandDemos.Names);
        Assert.All(RiverlandDemos.Names, name => Assert.NotNull(RiverlandDemos.Find(name)));
        Assert.All(RiverlandDemos.Names, name => Assert.Same(RiverlandMap.TargetSpots, RiverlandDemos.Find(name)!.Spots));
        Assert.All(RiverlandDemos.Names, name => Assert.NotNull(RiverlandDemos.Find(name)!.AimAt));
        Assert.Null(RiverlandDemos.Find("pen"));
        Assert.Null(RiverlandDemos.Find(null));
        Assert.Equal(RiverlandDemos.Names, MapSites.Riverland.DemoNames);
        Assert.Equal(RiverlandDemos.Find("ford"), MapSites.Riverland.FindDemo("ford"));
        Assert.Equal(new[] { "pen", "ricochet", "nopen", "he", "kill", "nopen-heavy", "kill-td", "turn", "wall", "breach", "fence", "ram", "bush", "lamp" },
            RangeDemo.Names);   // the sandbox's are as they were
    }

    [Fact]
    public void The_house_demo_brings_its_house_down()
    {
        RangeDemo demo = RiverlandDemos.Find("house")!;
        Run run = RunDemo(demo, TankCatalog.Medium, 30f);
        int house = HouseAt(run.Field, demo.AimAt!.Value);
        GameEvent first = run.Events.First(e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal(house, first.PropId);   // nothing else (a fence, a bush, a tree on the way) goes down before it
        int rounds = run.Events.Count(e => e.Type == GameEventType.ShellFired && e.Tick <= first.Tick);
        Assert.True(rounds <= 3, $"took {rounds} rounds");   // RiverlandDemos.Find's three (here, 12.1 s in)
        Assert.All(run.Field.Props.Where(p => p.Kind == PropKind.House && p.Id != house), p => Assert.True(run.Field.IsStanding(p.Id), $"house {p.Id} is down"));
    }

    [Fact]
    public void The_ram_demo_knocks_its_house_down_over_ram_speed()
    {
        RangeDemo demo = RiverlandDemos.Find("ram-house")!;
        Run run = RunDemo(demo, TankCatalog.Medium, (float)demo.FireAfter + demo.DriveSeconds + 1f);
        int house = HouseAt(run.Field, demo.AimAt!.Value);
        GameEvent down = Assert.Single(run.Events, e => e.Type == GameEventType.PropDestroyed && e.PropId == house);
        Assert.Equal(0, down.ShellId);   // driven into, not shot
        Assert.Equal(run.Player, down.TankId);
        float before = run.Speeds[down.Tick - 1];   // the speed the tick before it hit
        Assert.True(before > 20 * Kmh, $"rammed at {before / Kmh:F1} km/h");
        Assert.DoesNotContain(run.Events, e => e.Type == GameEventType.ShellFired);
    }

    /// <summary>
    /// Here the drive fells three trees under throttle and a fourth as it rolls to a stop, but the headings a hair either
    /// side fell only two (the trees at the forest's edge stand 5–10 m apart), and CI runs on Linux x64, whose MathF may
    /// differ from Apple arm64's in the last bit: so two trees felled under throttle, and coming to rest inside the east
    /// forest, are what is pinned, not the exact count.
    /// </summary>
    [Fact]
    public void The_trees_demo_fells_trees_under_throttle_and_comes_to_rest_in_the_east_forest()
    {
        RangeDemo demo = RiverlandDemos.Find("trees")!;
        Run run = RunDemo(demo, TankCatalog.Heavy, (float)demo.FireAfter + demo.DriveSeconds + 3f);
        int throttleEnds = demo.FireTick + demo.DriveTicks;
        int[] felled = run.Events.Where(e => e.Type == GameEventType.PropDestroyed && e.Tick <= throttleEnds && run.Field.PropById(e.PropId).Kind == PropKind.Tree)
            .Select(e => e.PropId).ToArray();
        Assert.True(felled.Length >= 2, $"felled {felled.Length} trees under throttle");
        Assert.All(run.Events.Where(e => e.Type == GameEventType.PropDestroyed), e => Assert.Equal(0, e.ShellId));
        TankState tank = run.Final;
        Assert.Equal(0f, tank.Speed);   // where it came to rest
        RiverlandForest east = RiverlandMap.Forests.Single(f => f.Name == "east");
        Assert.True(tank.Position.X >= east.Min.X && tank.Position.X <= east.Max.X && tank.Position.Y >= east.Min.Y && tank.Position.Y <= east.Max.Y,
            $"stopped at {tank.Position}, outside the east forest");
    }

    [Fact]
    public void The_ford_demo_is_mid_river_at_the_shot()
    {
        RangeDemo demo = RiverlandDemos.Find("ford")!;
        int shot = demo.FireTick + (int)MathF.Round(4.5f * SimConstants.TickRate);   // --screenshot-at 7.5: 4.5 s into the drive
        Run run = RunDemo(demo, TankCatalog.Medium, shot / (float)SimConstants.TickRate);
        SN.Vector2 at = run.Positions[shot];
        Assert.True(run.Field.WaterDepthAt(at) >= 0.3f, $"{run.Field.WaterDepthAt(at):F2} m of water at {at}");
        Assert.True(SN.Vector2.Distance(at, RiverlandMap.Landmarks.Ford) <= 15f, $"{at} is {SN.Vector2.Distance(at, RiverlandMap.Landmarks.Ford):F1} m from the ford");
        Assert.DoesNotContain(run.Events, e => e.Type is GameEventType.ShellFired or GameEventType.PropDestroyed);
    }

    public static TheoryData<string, string> DemosAndTanks()
    {
        var data = new TheoryData<string, string>();
        foreach (string name in RiverlandDemos.Names)
        foreach (string tank in new[] { "medium", "heavy" })
            data.Add(name, tank);
        return data;
    }

    [Theory]
    [MemberData(nameof(DemosAndTanks))]
    public void Demo_stands_are_clear_ground(string name, string tank)
    {
        Battlefield field = MapCatalog.Riverland.NewBattlefield();
        RangeDemo demo = RiverlandDemos.Find(name)!;
        SN.Vector2 stand = demo.PlayerPosition;
        Obb2 hull = TankMotion.Footprint(TankCatalog.ById(tank), stand, demo.PlayerYaw);
        Assert.Equal(stand, field.ClampToPlayable(stand));
        Assert.Equal(0f, field.WaterDepthAt(stand));
        foreach (Prop p in field.Props)
            Assert.False(p.IsSolid && Obb2.TryGetPenetration(hull, p.Footprint, out _), $"parked on {p.Kind} {p.Id}");
        foreach (TargetSpot s in RiverlandMap.TargetSpots)
            Assert.False(Obb2.TryGetPenetration(hull, TankMotion.Footprint(s.Spec, s.Position, s.Yaw), out _), "parked on a target");
    }

    /// <summary>A demo's run: every event, the player's speed and position after each tick (index = tick), its last state and the field.</summary>
    private sealed record Run(List<GameEvent> Events, float[] Speeds, SN.Vector2[] Positions, TankState Final, int Player, Battlefield Field);

    /// <summary>
    /// Runs <paramref name="demo"/> as the test drive does for <paramref name="seconds"/> of sim time: the player
    /// (team <see cref="PlayerTeam"/>) commanded by the demo, the eight targets (team <see cref="TargetTeam"/>) holding
    /// still, as <c>TargetRange.FillInputs</c> commands them on a map without a patrol.
    /// </summary>
    private static Run RunDemo(RangeDemo demo, TankSpec spec, float seconds)
    {
        var sim = new Simulation(MapCatalog.Riverland.NewBattlefield());
        int player = sim.AddTank(spec, demo.PlayerPosition, demo.PlayerYaw, PlayerTeam);
        foreach (TargetSpot t in RiverlandMap.TargetSpots) sim.AddTank(t.Spec, t.Position, t.Yaw, TargetTeam);
        int ticks = (int)MathF.Ceiling(seconds * SimConstants.TickRate);
        var inputs = new InputCmd[sim.TankCount];
        var events = new List<GameEvent>();
        var speeds = new float[ticks + 1];
        var positions = new SN.Vector2[ticks + 1];
        (speeds[0], positions[0]) = (sim.Tanks[0].Speed, sim.Tanks[0].Position);
        bool fired = false;
        for (int tick = 1; tick <= ticks; tick++)
        {
            inputs[0] = demo.Command(new InputCmd(tick, 0, 0, demo.PlayerYaw, 0f, false), sim.Tanks[0], sim.Field, fired);
            for (int i = 0; i < RiverlandMap.TargetSpots.Count; i++) inputs[1 + i] = new InputCmd(tick, 0, 0, RiverlandMap.TargetSpots[i].Yaw, 0f, false);
            sim.Step(inputs);
            foreach (GameEvent e in sim.Events)
            {
                events.Add(e);
                fired |= e.Type == GameEventType.ShellFired && e.TankId == player;
            }
            (speeds[tick], positions[tick]) = (sim.Tanks[0].Speed, sim.Tanks[0].Position);
        }
        return new Run(events, speeds, positions, sim.Tanks[0], player, sim.Field);
    }

    /// <summary>The house whose footprint <paramref name="aimAt"/> lies in or on (a wall's middle, or the middle of the house).</summary>
    private static int HouseAt(Battlefield field, SN.Vector3 aimAt)
    {
        var ground = new SN.Vector2(aimAt.X, aimAt.Z);
        return Assert.Single(field.Props, p => p.Kind == PropKind.House && p.Footprint.DistanceToPoint(ground) < 0.05f).Id;
    }
}

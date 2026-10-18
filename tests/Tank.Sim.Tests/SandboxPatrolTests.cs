using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>The patrolling target's route on the sandbox map (stage 3 design §4.2).</summary>
public sealed class SandboxPatrolTests
{
    private const int Tps = SimConstants.TickRate;
    private static readonly Battlefield Field = SandboxMap.Create();
    private static readonly IReadOnlyList<PatrolPoint> Route = SandboxMap.PatrolRoute;

    private static IEnumerable<(Vector2 From, Vector2 To)> Legs() =>
        Route.Select((p, i) => (p.Position, Route[(i + 1) % Route.Count].Position));

    private static float DistanceToRoute(Vector2 p) => Legs().Min(leg => SandboxMap.DistanceToSegment(p, leg.From, leg.To));

    [Fact]
    public void It_stops_ten_seconds_in_two_bush_patches_about_120_m_from_the_spawn()
    {
        PatrolPoint[] stops = Route.Where(p => p.WaitSeconds > 0).ToArray();
        Assert.Equal(2, stops.Length);
        Assert.All(stops, stop =>
        {
            Assert.Equal(10f, stop.WaitSeconds);
            Assert.Contains(stop.Position, SandboxMap.BushPatches);
            Assert.InRange(Vector2.Distance(stop.Position, SandboxMap.Spawn), 105f, 135f);
        });
        Assert.Equal(SandboxMap.PatrolStart, Route[^1].Position);
    }

    [Fact]
    public void The_route_keeps_clear_of_rocks_walls_targets_and_the_lane()
    {
        foreach (var (from, to) in Legs())
        {
            float yaw = Angles.YawOf(to - from), length = Vector2.Distance(from, to);
            for (float s = 0; s <= length; s += 0.5f)
            {
                Vector2 at = Vector2.Lerp(from, to, s / length);
                Obb2 hull = TankMotion.Footprint(SandboxMap.PatrolSpec, at, yaw);
                Obb2 roomy = hull with { HalfExtents = hull.HalfExtents + new Vector2(1f) };   // a metre to spare all round
                Assert.All(Field.Props.Where(p => p.Kind != PropKind.Bush), p =>
                    Assert.False(Obb2.TryGetPenetration(roomy, p.Footprint, out _), $"{p.Kind} {p.Id} is in the way at {at}"));
                Assert.All(SandboxMap.TargetSpots, t => Assert.True(Vector2.Distance(t.Position, at) >= 20f, $"target at {t.Position} is near {at}"));
                Assert.True(SandboxMap.DistanceToSegment(at, SandboxMap.Spawn, SandboxMap.BigHill) >= 30f, $"{at} is near the lane");
                float slope = MathF.Acos(Field.Terrain.NormalAt(at.X, at.Y).Y);
                Assert.True(slope <= Angles.Deg(10), $"{slope * 180 / MathF.PI:F1}° at {at}");
            }
        }
    }

    [Fact]
    public void It_laps_three_times_without_straying_coming_near_anything_solid_or_knocking_anything_down()
    {
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Medium, SandboxMap.Spawn, SandboxMap.SpawnYaw, team: 0);
        foreach (TargetSpot spot in SandboxMap.TargetSpots) sim.AddTank(spot.Spec, spot.Position, spot.Yaw, team: 1);
        int patrol = sim.AddTank(SandboxMap.PatrolSpec, SandboxMap.PatrolStart, Angles.YawOf(Route[0].Position - SandboxMap.PatrolStart), team: 1);
        int slot = sim.TankCount - 1;
        var driver = new PatrolDriver(Route);
        var inputs = new InputCmd[sim.TankCount];
        Prop[] solid = sim.Field.Props.Where(p => p.Kind != PropKind.Bush).ToArray();
        int laps = 0, tick = 0;
        float farthest = 0f;
        while (laps < 3 && tick < 3 * 60 * Tps)
        {
            tick++;
            for (int i = 0; i < inputs.Length; i++) inputs[i] = new InputCmd(tick, 0, 0, sim.Tanks[i].Yaw, 0f, false);
            int next = driver.Next;
            inputs[slot] = driver.Command(sim.Tanks[slot], sim.Field, tick, sim.Tanks[0].Position);
            sim.Step(inputs);
            if (next != 0 && driver.Next == 0) laps++;
            TankState tank = sim.Tanks[slot];
            farthest = MathF.Max(farthest, DistanceToRoute(tank.Position));
            Obb2 hull = TankMotion.Footprint(tank.Spec, tank.Position, tank.Yaw);
            Obb2 roomy = hull with { HalfExtents = hull.HalfExtents + new Vector2(2f) };
            Assert.All(solid, p => Assert.False(Obb2.TryGetPenetration(roomy, p.Footprint, out _), $"within 2 m of {p.Kind} {p.Id} at {tank.Position}"));
            foreach (GameEvent e in sim.Events)
                Assert.False(e.TankId == patrol && e.Type is GameEventType.TankHit or GameEventType.SplashDamage, $"the patrol hit tank {e.TargetTankId}");
        }
        Assert.Equal(3, laps);
        Assert.True(tick <= 3 * 50 * Tps, $"three laps took {tick / (float)Tps:F0} s");
        Assert.True(farthest <= 3f, $"strayed {farthest:F1} m off the route");
        Assert.All(sim.Field.Props.Where(p => p.Kind != PropKind.Bush), p => Assert.True(sim.Field.IsStanding(p.Id), $"{p.Kind} {p.Id} was knocked down"));
    }

    /// <summary>The spawn, the watch point, and eight points round each bush patch it stops in, 60, 100 and 150 m out (kept on the map).</summary>
    public static TheoryData<float, float> Bystanders()
    {
        var places = new HashSet<Vector2> { SandboxMap.Spawn, SandboxMap.PatrolWatch };
        foreach (PatrolPoint stop in Route.Where(p => p.WaitSeconds > 0))
        foreach (float distance in new[] { 60f, 100f, 150f })
            for (int k = 0; k < 8; k++)
                places.Add(Field.ClampToPlayable(stop.Position + Angles.Forward(k * MathF.PI / 4) * distance));
        var data = new TheoryData<float, float>();
        foreach (Vector2 at in places) data.Add(at.X, at.Y);
        return data;
    }

    [Theory]
    [MemberData(nameof(Bystanders))]
    public void Its_shots_never_hit_the_player(float x, float z)
    {
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Medium, new Vector2(x, z), 0f, team: 0);
        int patrol = sim.AddTank(SandboxMap.PatrolSpec, SandboxMap.PatrolStart, Angles.YawOf(Route[0].Position - SandboxMap.PatrolStart), team: 1);
        var driver = new PatrolDriver(Route);
        var inputs = new InputCmd[2];
        int shots = 0;
        for (int tick = 1; tick <= 40 * Tps; tick++)   // both waits, the second over at about 38 s
        {
            inputs[0] = new InputCmd(tick, 0, 0, 0f, 0f, false);
            inputs[1] = driver.Command(sim.Tanks[1], sim.Field, tick, sim.Tanks[0].Position);
            sim.Step(inputs);
            foreach (GameEvent e in sim.Events)
            {
                if (e.TankId == patrol && e.Type == GameEventType.ShellFired) shots++;
                Assert.False(e.TankId == patrol && e.Type is GameEventType.TankHit or GameEventType.SplashDamage, $"hit the player after {tick / (float)Tps:F1} s");
            }
        }
        if (Vector2.Distance(new Vector2(x, z), Route[0].Position) > PatrolDriver.HoldFireWithin) Assert.True(shots >= 2, $"only {shots} shots");
    }

    [Fact]
    public void From_the_watch_point_it_is_seen_in_the_open_lost_in_the_bushes_and_seen_again_when_it_fires()
    {
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Medium, SandboxMap.PatrolWatch, SandboxMap.SpawnYaw, team: 0);
        int patrol = sim.AddTank(SandboxMap.PatrolSpec, SandboxMap.PatrolStart, Angles.YawOf(Route[0].Position - SandboxMap.PatrolStart), team: 1);
        var vision = new VisionTracker(sim);
        var driver = new PatrolDriver(Route);
        var inputs = new InputCmd[2];
        bool seenDriving = false, lostWaiting = false, seenAfterShot = false;
        int fired = -1;
        for (int tick = 1; tick <= 20 * Tps; tick++)
        {
            inputs[0] = new InputCmd(tick, 0, 0, SandboxMap.SpawnYaw, 0f, false);
            inputs[1] = driver.Command(sim.Tanks[1], sim.Field, tick, sim.Tanks[0].Position);
            sim.Step(inputs);
            vision.Update();
            foreach (GameEvent e in sim.Events) if (e.Type == GameEventType.ShellFired && fired < 0) fired = tick;
            bool seen = vision.IsVisible(patrol, 0);
            if (!driver.Waiting && driver.Next == 0 && seen) seenDriving = true;
            if (driver.Waiting && fired < 0 && !seen) lostWaiting = true;
            if (fired >= 0 && tick <= fired + Tps && seen) seenAfterShot = true;
        }
        Assert.True(seenDriving, "never seen driving to the patch");
        Assert.True(lostWaiting, "never lost in the bushes");
        Assert.True(fired > 0, "never fired");
        Assert.True(seenAfterShot, "not seen after its shot");
    }
}

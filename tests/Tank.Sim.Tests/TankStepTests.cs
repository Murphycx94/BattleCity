using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>Stepping one tank outside the simulation, as a client predicting its own tank does (stage 4 design §3).</summary>
public sealed class TankStepTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>Drive north, swinging the turret about and firing whenever loaded: everything Advance touches.</summary>
    private static InputCmd Busy(int tick) => new(tick, (sbyte)(tick % 90 < 60 ? 1 : -1), (sbyte)(tick % 40 < 10 ? 1 : 0),
        Angles.Deg(tick % 120 - 60), Angles.Deg(5), tick % 7 == 0, tick < 6 * Tps ? AmmoType.AP : AmmoType.HE);

    [Fact]
    public void Advance_steps_a_tank_exactly_as_the_simulation_does()
    {
        // Each side on its own copy of the map, so what one knocks down the other must knock down too.
        var sim = new Simulation(SandboxMap.Create());
        Battlefield field = SandboxMap.Create();
        int id = sim.AddTank(TankCatalog.Medium, SandboxMap.Spawn, SandboxMap.SpawnYaw);
        TankState mine = sim.Tanks[0];
        var inputs = new InputCmd[1];
        int shots = 0;
        for (int tick = 1; tick <= 10 * Tps; tick++)
        {
            inputs[0] = Busy(tick);
            sim.Step(inputs);
            GameEvent[] events = sim.Events.ToArray();
            bool fired = TankStep.Advance(ref mine, inputs[0], tick, field, ReadOnlySpan<Obb2>.Empty, knockDown: true, null, canFire: true, out TankStep.Shot shot);
            if (fired)
            {
                shots++;
                GameEvent e = Assert.Single(events, e => e.Type == GameEventType.ShellFired);
                Assert.Equal(shot.Shell.Position, e.Position);
                Assert.Equal(shot.Shell.Velocity, e.Velocity);
                Assert.Equal(shot.Shell.Ammo, e.Ammo);
                Assert.Equal(0, shot.Shell.Id);
            }
            else Assert.DoesNotContain(events, e => e.Type == GameEventType.ShellFired);
            Assert.Equal(sim.Tanks[0], mine);   // every field, every tick
        }
        Assert.True(shots >= 2, $"{shots} shots");
        Assert.Equal(id, mine.Id);
    }

    [Fact]
    public void Advance_knocks_down_what_the_simulation_knocks_down_and_reports_the_same_props()
    {
        // A fence at 90 m and a brick wall at 60 m across the path of a heavy tank driving north from 110 m.
        Prop fence = Fence(1, new Vector2(100, 90));
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(94, 60), new Vector2(106, 60)).Segments(TestWorld.Flat(), 2).ElementAt(2);
        var sim = new Simulation(TestWorld.FlatField(fence, wall));
        Battlefield field = TestWorld.FlatField(fence, wall);
        sim.AddTank(TankCatalog.Heavy, new Vector2(100, 110), 0f);
        TankState mine = sim.Tanks[0];
        var inputs = new InputCmd[1];
        var felled = new List<int>();
        var reported = new List<int>();
        for (int tick = 1; tick <= 8 * Tps; tick++)
        {
            inputs[0] = TestWorld.Cmd(throttle: 1);
            sim.Step(inputs);
            foreach (GameEvent e in sim.Events) if (e.Type == GameEventType.PropDestroyed) reported.Add(e.PropId);
            TankStep.Advance(ref mine, inputs[0], tick, field, ReadOnlySpan<Obb2>.Empty, knockDown: true, felled, canFire: false, out _);
            Assert.Equal(sim.Tanks[0], mine);
            Assert.Equal(sim.Field.IsStanding(fence.Id), field.IsStanding(fence.Id));
            Assert.Equal(sim.Field.IsStanding(wall.Id), field.IsStanding(wall.Id));
        }
        Assert.Equal(new[] { fence.Id, wall.Id }, felled);
        Assert.Equal(felled, reported);
        Assert.True(mine.Position.Y < 56f, $"drove on through both: {mine.Position}");   // slowed by the break and the rubble
    }

    [Fact]
    public void The_simulation_itself_stops_a_slow_tank_at_a_brick_wall()
    {
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(94, 60), new Vector2(106, 60)).Segments(TestWorld.Flat(), 1).ElementAt(2);
        var sim = new Simulation(TestWorld.FlatField(wall));
        sim.AddTank(TankCatalog.Medium, new Vector2(100, 70), 0f);
        var inputs = new[] { TestWorld.Cmd(throttle: 1) };
        for (int tick = 1; tick <= 3 * Tps; tick++) sim.Step(inputs);
        Assert.True(sim.Tanks[0].Position.Y > 62f, $"stopped by the wall: {sim.Tanks[0].Position}");
        Assert.True(sim.Field.IsStanding(wall.Id));
    }

    [Fact]
    public void Advance_leaves_a_wreck_alone_and_an_unarmed_or_protected_gun_silent()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f);
        TankState wreck = sim.Tanks[0] with { IsDestroyed = true, Health = 0 };
        TankState before = wreck;
        Assert.False(TankStep.Advance(ref wreck, TestWorld.Cmd(throttle: 1, fire: true), 1, sim.Field, ReadOnlySpan<Obb2>.Empty, true, null, true, out _));
        Assert.Equal(before, wreck);

        TankState live = sim.Tanks[0];
        Assert.False(TankStep.Advance(ref live, TestWorld.Cmd(fire: true), 1, sim.Field, ReadOnlySpan<Obb2>.Empty, true, null, canFire: false, out _));
        Assert.Equal(0, live.ShotCount);
        Assert.True(TankStep.Advance(ref live, TestWorld.Cmd(fire: true), 2, sim.Field, ReadOnlySpan<Obb2>.Empty, true, null, canFire: true, out TankStep.Shot shot));
        Assert.Equal(1, live.ShotCount);
        Assert.Equal(2, live.LastFired);
        Assert.Equal(ShotRandom.Seed(2, live.Id, 0), shot.Shell.Seed);
        Assert.Equal(live.Spec.ReloadTime, live.Reload);
    }

    private static Prop Fence(int id, Vector2 at) => new WallLine(PropKind.Fence, at - new Vector2(3, 0), at + new Vector2(3, 0)).Segments(TestWorld.Flat(), id).First();

    [Fact]
    public void Without_knocking_down_a_tank_drives_through_a_fence_that_stays_standing()
    {
        Prop fence = Fence(1, new Vector2(100, 90));
        Battlefield server = TestWorld.FlatField(fence), client = TestWorld.FlatField(fence);
        TankState onServer = TestWorld.Tank(100, 100), onClient = onServer;
        var felled = new List<int>();
        for (int tick = 1; tick <= 3 * Tps; tick++)
        {
            InputCmd cmd = TestWorld.Cmd(throttle: 1);
            TankStep.Advance(ref onServer, cmd, tick, server, ReadOnlySpan<Obb2>.Empty, knockDown: true, felled, false, out _);
            TankStep.Advance(ref onClient, cmd, tick, client, ReadOnlySpan<Obb2>.Empty, knockDown: false, null, false, out _);
            Assert.Equal(onServer.Position, onClient.Position);
            Assert.Equal(onServer.Speed, onClient.Speed);
        }
        Assert.Equal(new[] { fence.Id }, felled);
        Assert.False(server.IsStanding(fence.Id));
        Assert.True(client.IsStanding(fence.Id), "the client leaves it to the server");
        Assert.True(onClient.Position.Y < 86f, $"drove on through: {onClient.Position}");
    }

    /// <param name="runUp">Where the heavy tank starts, north of the wall at z = 60: 25 m reaches top speed, 13 m only ~7 m/s
    /// (just over the ram speed), the band where the break leaves it under ram speed inside the wall.</param>
    [Theory]
    [InlineData(85f)]
    [InlineData(73f)]
    public void Without_knocking_down_a_ramming_heavy_tank_passes_a_brick_wall_at_half_speed_and_a_slow_one_is_stopped(float runUp)
    {
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(94, 60), new Vector2(106, 60)).Segments(TestWorld.Flat(), 1).ElementAt(2);
        Battlefield server = TestWorld.FlatField(wall), client = TestWorld.FlatField(wall);
        TankState onServer = TestWorld.Tank(100, runUp) with { Spec = TankCatalog.Heavy }, onClient = onServer;
        int brokeAt = -1;
        float serverSpeedBefore = 0f;
        for (int tick = 1; tick <= 6 * Tps; tick++)
        {
            InputCmd cmd = TestWorld.Cmd(throttle: 1);
            if (server.IsStanding(wall.Id)) serverSpeedBefore = onServer.Speed;
            TankStep.Advance(ref onServer, cmd, tick, server, ReadOnlySpan<Obb2>.Empty, knockDown: true, null, false, out _);
            TankStep.Advance(ref onClient, cmd, tick, client, ReadOnlySpan<Obb2>.Empty, knockDown: false, null, false, out _);
            if (brokeAt < 0 && !server.IsStanding(wall.Id))
            {
                brokeAt = tick;
                Assert.True(onServer.Speed < serverSpeedBefore * 0.6f, "the break costs half the speed on the server");
                Assert.Equal(onServer.Speed, onClient.Speed);   // and the same on the client
            }
            // Identical until the wall is down; then the server's tank crosses rubble (slower) while the client's wall still
            // stands, until the server's word arrives: the two drift apart a little there, which reconciliation smooths out.
            if (brokeAt < 0) Assert.Equal(onServer.Position, onClient.Position);
        }
        Assert.True(brokeAt > 0, "never broke through");
        Assert.True(client.IsStanding(wall.Id));
        Assert.True(onClient.Position.Y < 56f, $"broke through: {onClient.Position}");
        Assert.True(Vector2.Distance(onServer.Position, onClient.Position) < 3f, $"server {onServer.Position} vs client {onClient.Position}");

        Battlefield slow = TestWorld.FlatField(wall);
        TankState medium = TestWorld.Tank(100, 70);
        for (int tick = 1; tick <= 3 * Tps; tick++) TankStep.Advance(ref medium, TestWorld.Cmd(throttle: 1), tick, slow, ReadOnlySpan<Obb2>.Empty, knockDown: false, null, false, out _);
        Assert.True(medium.Position.Y > 62f, $"a medium tank is stopped by the wall: {medium.Position}");
        Assert.True(slow.IsStanding(wall.Id));
    }

    [Fact]
    public void SetDurability_puts_a_prop_down_or_back_up_from_outside()
    {
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(98, 60), new Vector2(102, 60)).Segments(TestWorld.Flat(), 1).First();
        var rock = new Prop(5, PropKind.Rock, new Obb2(new Vector2(50, 50), new Vector2(3, 3), 0), -1f, 4f);
        Battlefield field = TestWorld.FlatField(wall, rock);
        field.SetDurability(wall.Id, 120);
        Assert.True(field.IsStanding(wall.Id));
        Assert.Equal(120, field.Durability(wall.Id));
        field.SetDurability(wall.Id, 0);
        Assert.False(field.IsStanding(wall.Id));
        Assert.False(field.Raycast(new Vector3(100, 1, 80), new Vector3(100, 1, 40), out _), "rubble doesn't stop rays");
        field.SetDurability(wall.Id, wall.MaxDurability);
        Assert.True(field.IsStanding(wall.Id));
        Assert.True(field.Raycast(new Vector3(100, 1, 80), new Vector3(100, 1, 40), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.SetDurability(wall.Id, wall.MaxDurability + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.SetDurability(wall.Id, -1));
        Assert.Throws<InvalidOperationException>(() => field.SetDurability(rock.Id, 0));
    }

    [Fact]
    public void Map_data_rebuilds_the_same_battlefield_and_is_stable_under_a_round_trip()
    {
        Battlefield original = SandboxMap.Create();
        MapData data = MapData.Of(original);
        Battlefield rebuilt = data.Build();
        MapData again = MapData.Of(rebuilt);
        Assert.Equal(data.HeightsCm, again.HeightsCm);
        Assert.Equal(data.Props, again.Props);
        Assert.Equal(original.Props, rebuilt.Props);
        Assert.Equal(original.Border, rebuilt.Border);
        Assert.Equal(original.Terrain.SizeX, rebuilt.Terrain.SizeX);
        for (float x = 0; x <= 400; x += 37f)
        for (float z = 0; z <= 400; z += 41f)
            Assert.Equal(original.Terrain.HeightAt(x, z), rebuilt.Terrain.HeightAt(x, z), 0.011f);   // within a centimetre
        Assert.All(rebuilt.Props, p => Assert.True(rebuilt.IsStanding(p.Id)));
        Assert.Throws<ArgumentException>(() => new MapData(3, 3, 2f, new int[8], Array.Empty<Prop>(), 4f));
        Assert.Throws<ArgumentException>(() => new MapData(1, 3, 2f, new int[3], Array.Empty<Prop>(), 4f));
        Assert.Throws<ArgumentException>(() => new MapData(65536, 65536, 2f, Array.Empty<int>(), Array.Empty<Prop>(), 4f));   // the product overflows to 0
        Assert.Throws<ArgumentException>(() => new MapData(3, 3, 0f, new int[9], Array.Empty<Prop>(), 4f));
    }

    [Fact]
    public void Without_reload_layers_the_reload_time_is_the_specs_after_a_shot_and_after_a_change_of_ammunition()
    {
        var sim = new Simulation(TestWorld.FlatField());
        foreach (TankSpec spec in TankCatalog.All)
        {
            sim.AddTank(spec, new Vector2(100, 100), 0f);
            TankState tank = sim.Tanks[^1];
            Assert.Equal(0, tank.ReloadLayers);
            Assert.True(TankStep.Advance(ref tank, TestWorld.Cmd(fire: true), 1, sim.Field, ReadOnlySpan<Obb2>.Empty, true, null, true, out _));
            Assert.Equal(spec.ReloadTime, tank.Reload);
            TankStep.Advance(ref tank, TestWorld.Cmd(ammo: AmmoType.HE), 2, sim.Field, ReadOnlySpan<Obb2>.Empty, true, null, true, out _);
            Assert.Equal(spec.ReloadTime, tank.Reload);
        }
    }
}

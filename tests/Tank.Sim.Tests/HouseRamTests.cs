using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// Tanks driving north (−Z) at x = 100 into the front of <see cref="HouseTests"/>' house (id 1, its south face at
/// z = 63.5), square on to the wall (riverland design §2.5).
/// </summary>
public sealed class HouseRamTests
{
    private const float Kmh = 1f / 3.6f;
    private static readonly InputCmd Full = TestWorld.Cmd(throttle: 1);
    private static readonly InputCmd Coast = TestWorld.Cmd();

    private static Prop TheHouse => HouseTests.House(1, HouseTests.HouseAt);

    /// <summary>A <paramref name="spec"/> tank heading north at x = 100 at <paramref name="kmh"/>, its nose <paramref name="gap"/> m short of the house.</summary>
    private static TankState Facing(TankSpec spec, float gap, float kmh) => new()
    {
        Id = 1, Spec = spec, Position = new Vector2(100, HouseTests.SouthFace + gap + spec.HullLength / 2), Yaw = 0f, Speed = kmh * Kmh,
    };

    private static bool InTheHouse(in TankState t) =>
        Obb2.TryGetPenetration(TankMotion.Footprint(t.Spec, t.Position, t.Yaw), TheHouse.Footprint, out _);

    private static bool OnTheRuin(in TankState t) =>
        Obb2.TryGetPenetration(TankMotion.Footprint(t.Spec, t.Position, t.Yaw), TheHouse.RubbleFootprint, out _);

    [Fact]
    public void Ram_speeds_are_the_designs()
    {
        Assert.Equal(20f / 3.6f, TankCatalog.Medium.HouseRamSpeed, 1e-5f);
        Assert.Equal(20f / 3.6f, TankCatalog.Heavy.HouseRamSpeed, 1e-5f);
        Assert.Equal(float.PositiveInfinity, TankCatalog.Light.HouseRamSpeed);
        Assert.Equal(float.PositiveInfinity, TankCatalog.Td.HouseRamSpeed);
        Assert.Equal(15f / 3.6f, TankCatalog.Heavy.BrickWallRamSpeed, 1e-5f);
        Assert.All(new[] { TankCatalog.Light, TankCatalog.Medium, TankCatalog.Td }, s => Assert.Equal(float.PositiveInfinity, s.BrickWallRamSpeed));
    }

    [Theory]
    [InlineData("medium")]
    [InlineData("heavy")]
    public void At_21_kmh_medium_and_heavy_tanks_knock_a_house_down(string id)
    {
        TankSpec spec = TankCatalog.ById(id);
        var sim = new Simulation(TestWorld.FlatField(TheHouse));
        TankState start = Facing(spec, 10f, 21f);
        int tank = sim.AddTank(spec, start.Position, 0f);
        sim.TankRef(tank).Speed = start.Speed;
        var events = new List<GameEvent>();
        float before = 0f, fastestOnRuin = 0f;
        int rammedAt = -1;
        for (int tick = 0; tick < 8 * SimConstants.TickRate && sim.Tanks[0].Position.Y > HouseTests.NorthFace - 1f - spec.HullLength; tick++)
        {
            bool onRuin = !sim.Field.IsStanding(1) && OnTheRuin(sim.Tanks[0]);
            if (sim.Field.IsStanding(1)) before = sim.Tanks[0].Speed;
            sim.Step(new[] { Full });
            events.AddRange(sim.Events.ToArray());
            if (rammedAt < 0 && !sim.Field.IsStanding(1))
            {
                rammedAt = tick;
                Assert.True(before > spec.HouseRamSpeed, $"rammed at {before * 3.6f:F1} km/h");
                float accelerated = MathF.Min(before + spec.Acceleration * SimConstants.Dt, spec.MaxForwardSpeed);
                Assert.Equal(accelerated * 0.5f, sim.Tanks[0].Speed, accelerated * 0.5f * 1e-4f);
            }
            if (onRuin)
            {
                Assert.True(sim.Tanks[0].Speed <= spec.MaxForwardSpeed * TankMotion.RubbleSpeedFactor + 1e-4f, $"{sim.Tanks[0].Speed * 3.6f:F1} km/h on the ruin");
                fastestOnRuin = MathF.Max(fastestOnRuin, sim.Tanks[0].Speed);
            }
        }

        Assert.True(rammedAt >= 0, "the house still stands");
        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal((1, tank, 0), (down.PropId, down.TankId, down.ShellId));
        Assert.True(sim.Tanks[0].Position.Y < HouseTests.NorthFace - Prop.RubbleSpread - spec.HullLength / 2, $"stuck at z = {sim.Tanks[0].Position.Y}");
        Assert.Equal(spec.MaxForwardSpeed * TankMotion.RubbleSpeedFactor, fastestOnRuin, 1e-4f);   // across the ruin at 70 %
    }

    [Theory]
    [InlineData("light", 40f)]
    [InlineData("td", 35f)]
    public void Light_tanks_and_tank_destroyers_are_stopped_at_any_speed(string id, float kmh)
    {
        TankSpec spec = TankCatalog.ById(id);
        Battlefield field = TestWorld.FlatField(TheHouse);
        TankState tank = Facing(spec, 10f, kmh);
        int? contact = null;
        for (int tick = 0; tick < 3 * SimConstants.TickRate; tick++)
        {
            TankMotion.Drive(ref tank, Full, field, ReadOnlySpan<Obb2>.Empty);
            Assert.False(InTheHouse(tank), $"tick {tick}: the hull is in the house at z = {tank.Position.Y}");
            float nose = tank.Position.Y - spec.HullLength / 2;
            contact ??= nose < HouseTests.SouthFace + 0.01f ? tick : null;
            if (contact is { } c && tick >= c + 3) Assert.Equal(0f, tank.Speed);
        }
        Assert.NotNull(contact);
        Assert.True(field.IsStanding(1));
    }

    [Theory]
    [InlineData("light")]
    [InlineData("medium")]
    [InlineData("heavy")]
    [InlineData("td")]
    public void At_19_kmh_a_house_stops_every_tank(string id)
    {
        // Coasting from 19 km/h with its nose 10 cm off the wall: it reaches the wall on the first tick, slower than that.
        TankSpec spec = TankCatalog.ById(id);
        Battlefield field = TestWorld.FlatField(TheHouse);
        TankState tank = Facing(spec, 0.1f, 19f);
        for (int tick = 0; tick < SimConstants.TickRate; tick++)
        {
            TankMotion.Drive(ref tank, Coast, field, ReadOnlySpan<Obb2>.Empty);
            Assert.False(InTheHouse(tank), $"tick {tick}: the hull is in the house");
        }
        Assert.True(field.IsStanding(1));
        Assert.Equal(0f, tank.Speed);
        Assert.Equal(HouseTests.SouthFace, tank.Position.Y - spec.HullLength / 2, 0.01f);   // it did reach the wall
    }

    [Fact]
    public void Reversing_into_a_house_fast_enough_knocks_it_down()
    {
        // A heavy tank north of the house, facing north, backing at 25 km/h with its tail 0.5 m off the north face. It
        // brakes towards its 12 km/h top speed in reverse, but is still over 20 km/h when it gets there.
        TankSpec spec = TankCatalog.Heavy;
        Battlefield field = TestWorld.FlatField(TheHouse);
        var tank = new TankState
        {
            Id = 1, Spec = spec, Position = new Vector2(100, HouseTests.NorthFace - 0.5f - spec.HullLength / 2), Yaw = 0f, Speed = -25f * Kmh,
        };
        var felled = new List<int>();
        for (int tick = 0; tick < 2 * SimConstants.TickRate && felled.Count == 0; tick++)
            TankMotion.Drive(ref tank, TestWorld.Cmd(throttle: -1), field, ReadOnlySpan<Obb2>.Empty, felled);
        Assert.Equal(new[] { 1 }, felled);
        Assert.False(field.IsStanding(1));
    }

    [Fact]
    public void A_brick_wall_and_a_house_in_one_tick_halve_the_speed_once()
    {
        // A brick segment across x = 106 … 108 beside the house's east end, its south face in line with the house's front;
        // a heavy tank at x = 106, its nose 10 cm short of both, at 30 km/h.
        Prop brick = new WallLine(PropKind.BrickWall, new Vector2(106, 63.2f), new Vector2(108, 63.2f)).Segments(TestWorld.Flat(), 2).Single();
        Assert.Equal(HouseTests.SouthFace, brick.Footprint.Center.Y + brick.Footprint.HalfExtents.Y, 1e-4f);
        Battlefield field = TestWorld.FlatField(TheHouse, brick);
        TankSpec spec = TankCatalog.Heavy;
        TankState tank = Facing(spec, 0.1f, 30f) with { Position = new Vector2(106, HouseTests.SouthFace + 0.1f + spec.HullLength / 2) };
        var felled = new List<int>();
        float accelerated = tank.Speed + spec.Acceleration * SimConstants.Dt;

        TankMotion.Drive(ref tank, Full, field, ReadOnlySpan<Obb2>.Empty, felled);

        Assert.Equal(new[] { 1, 2 }, felled.Order());
        Assert.Equal(accelerated * 0.5f, tank.Speed, 1e-4f);
    }

    [Fact]
    public void Prediction_drives_into_the_house_and_on_without_a_second_halving()
    {
        // From 21 km/h, 10 m short: the medium rams the house within the 60 ticks and is still in it at the end, slower
        // than 35 km/h, so the rubble the server's tank drives over holds it back no more than the client's standing house.
        Battlefield predicted = TestWorld.FlatField(TheHouse), server = TestWorld.FlatField(TheHouse);
        TankState client = Facing(TankCatalog.Medium, 10f, 21f), authority = client;
        int rammedAt = -1;
        for (int tick = 0; tick < 60; tick++)
        {
            float before = client.Speed;
            bool wasIn = InTheHouse(client);
            TankMotion.Drive(ref client, Full, predicted, ReadOnlySpan<Obb2>.Empty, knockDown: false);
            TankMotion.Drive(ref authority, Full, server, ReadOnlySpan<Obb2>.Empty);
            Assert.True(client.Position.Equals(authority.Position) && client.Speed.Equals(authority.Speed),
                $"tick {tick}: {client.Position} at {client.Speed} against {authority.Position} at {authority.Speed}");
            if (rammedAt < 0 && InTheHouse(client))
            {
                rammedAt = tick;
                Assert.False(wasIn);
                Assert.Equal((before + TankCatalog.Medium.Acceleration * SimConstants.Dt) * 0.5f, client.Speed, 1e-4f);
            }
            else if (rammedAt >= 0)
            {
                Assert.True(InTheHouse(client), $"tick {tick}: pushed out of the house");
                Assert.True(client.Speed > before, $"tick {tick}: slowed from {before} to {client.Speed}");
            }
        }

        Assert.True(rammedAt >= 0, "never drove into the house");
        Assert.True(predicted.IsStanding(1), "the client leaves it to the server");
        Assert.False(server.IsStanding(1));
    }
}

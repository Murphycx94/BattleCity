using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>The ground and the water set a tank's top speed, taken under its hull's centre (riverland design §2.1–2.3).</summary>
public sealed class GroundDrivingTests
{
    private const int Tps = SimConstants.TickRate;
    private const int Cells = TestWorld.Samples - 1;
    private const float Kmh = 3.6f;
    private static readonly InputCmd Full = TestWorld.Cmd(throttle: 1);

    /// <summary>The 200 m test map, flat at <paramref name="height"/>, all of it <paramref name="kind"/>.</summary>
    private static Battlefield Field(SurfaceKind kind, float height = 0f, float? waterLevel = null, params Prop[] props) =>
        new(TestWorld.Flat(height), props, TestWorld.Border, SurfaceMap.Uniform(Cells, Cells, TestWorld.Cell, kind), waterLevel);

    /// <summary>
    /// A medium tank's speed, km/h, after 15 s from rest at full throttle (or full reverse), along the map's diagonal so it
    /// never reaches the edge.
    /// </summary>
    private static float SpeedAfter15s(Battlefield field, sbyte throttle = 1)
    {
        TankState tank = throttle > 0 ? TestWorld.Tank(10, 190, yaw: -MathF.PI / 4) : TestWorld.Tank(190, 10, yaw: -MathF.PI / 4);
        TestWorld.Drive(ref tank, field, TestWorld.Cmd(throttle), 15 * Tps);
        Assert.True(Vector2.Distance(tank.Position, new Vector2(100, 100)) < 120, $"still on the map: {tank.Position}");
        return tank.Speed * Kmh;
    }

    [Theory]
    [InlineData(SurfaceKind.Road, 57.5f)]
    [InlineData(SurfaceKind.Sand, 40f)]
    [InlineData(SurfaceKind.Grass, 50f)]
    [InlineData(SurfaceKind.Dirt, 50f)]
    public void Top_speed_on_each_ground(SurfaceKind kind, float kmh) =>
        Assert.Equal(kmh, SpeedAfter15s(Field(kind)), 0.05f);

    [Fact]
    public void In_water_the_depth_sets_the_top_speed()
    {
        Assert.Equal(30f, SpeedAfter15s(Field(SurfaceKind.Sand, -1.2f, 0f)), 0.05f);
        Assert.Equal(40f, SpeedAfter15s(Field(SurfaceKind.Road, -0.3f, 0f)), 0.05f);
        Assert.Equal(25f, SpeedAfter15s(Field(SurfaceKind.Grass, -1.6f, 0f)), 0.05f);
        Assert.Equal(50f, SpeedAfter15s(Field(SurfaceKind.Grass, -1.6f)), 0.05f);   // no water: just a low field
    }

    [Fact]
    public void Reverse_is_scaled_too() =>
        Assert.Equal(-18f * 1.15f, SpeedAfter15s(Field(SurfaceKind.Road), throttle: -1), 0.05f);

    [Fact]
    public void Ground_and_rubble_multiply()
    {
        // A long brick wall shot to rubble along the tank's whole run east, on sand.
        Prop[] wall = new WallLine(PropKind.BrickWall, new Vector2(20, 100), new Vector2(190, 100)).Segments(TestWorld.Flat(), 1).ToArray();
        Battlefield field = Field(SurfaceKind.Sand, props: wall);
        foreach (Prop segment in wall) field.Destroy(field.IndexOf(segment.Id));
        TankState tank = TestWorld.Tank(25, 100, yaw: -MathF.PI / 2);
        TestWorld.Drive(ref tank, field, Full, 15 * Tps);

        Assert.True(tank.Position.X < 185, $"still on the rubble: {tank.Position}");
        Assert.Equal(50f * 0.8f * 0.7f, tank.Speed * Kmh, 0.05f);
    }

    [Fact]
    public void Acceleration_is_unchanged()
    {
        Battlefield road = Field(SurfaceKind.Road), grass = Field(SurfaceKind.Grass);
        TankState onRoad = TestWorld.Tank(100, 150), onGrass = TestWorld.Tank(100, 150);
        for (int tick = 0; tick < 10; tick++)
        {
            TankMotion.Drive(ref onRoad, Full, road, ReadOnlySpan<Obb2>.Empty);
            TankMotion.Drive(ref onGrass, Full, grass, ReadOnlySpan<Obb2>.Empty);
            Assert.Equal(onGrass.Speed, onRoad.Speed);
            Assert.Equal(onGrass.Position, onRoad.Position);
        }
        Assert.True(onRoad.Speed > 1f);
    }

    [Fact]
    public void The_hull_centre_decides()
    {
        // Road west of x = 100, sand east of it; heading west at the sand's top speed from x = 110.
        var cells = new SurfaceKind[Cells * Cells];
        for (int j = 0; j < Cells; j++)
        for (int i = 0; i < Cells; i++)
            cells[j * Cells + i] = i < 50 ? SurfaceKind.Road : SurfaceKind.Sand;
        var field = new Battlefield(TestWorld.Flat(), [], TestWorld.Border, new SurfaceMap(Cells, Cells, TestWorld.Cell, cells));
        float sandTop = TankCatalog.Medium.MaxForwardSpeed * GroundSpeed.Sand, roadTop = TankCatalog.Medium.MaxForwardSpeed * GroundSpeed.Road;
        TankState tank = TestWorld.Tank(110, 100, yaw: MathF.PI / 2) with { Speed = sandTop };
        float half = tank.Spec.HullLength / 2;

        bool noseOnRoad = false, centreOnRoad = false;
        for (int tick = 0; tick < 5 * Tps; tick++)
        {
            float centre = tank.Position.X;
            TankMotion.Drive(ref tank, Full, field, ReadOnlySpan<Obb2>.Empty);
            if (centre >= 100)
            {
                Assert.Equal(sandTop, tank.Speed);   // however far the nose is over the road
                noseOnRoad |= centre - half < 99;
            }
            else if (!centreOnRoad)
            {
                centreOnRoad = true;
                Assert.True(tank.Speed > sandTop, "the road's limit applies as soon as the centre is on it");
            }
        }
        Assert.True(noseOnRoad && centreOnRoad);
        Assert.Equal(roadTop, tank.Speed, 1e-4f);
    }

    [Fact]
    public void Prediction_and_simulation_agree_on_road_and_water()
    {
        // Road west of x = 80, sand to x = 140, grass beyond; a ditch 1 m deep across z = 120 … 130, under a water level of 0.
        int samples = TestWorld.Samples;
        var heights = new float[samples * samples];
        for (int j = 0; j < samples; j++)
        for (int i = 0; i < samples; i++)
            heights[j * samples + i] = j is >= 60 and <= 65 ? -1f : 0f;
        var cells = new SurfaceKind[Cells * Cells];
        for (int j = 0; j < Cells; j++)
        for (int i = 0; i < Cells; i++)
            cells[j * Cells + i] = i < 40 ? SurfaceKind.Road : i < 70 ? SurfaceKind.Sand : SurfaceKind.Grass;
        var original = new Battlefield(new HeightField(samples, samples, TestWorld.Cell, heights), [], TestWorld.Border,
            new SurfaceMap(Cells, Cells, TestWorld.Cell, cells), waterLevel: 0f);
        MapData data = MapData.Of(original);

        var sim = new Simulation(data.Build());
        Battlefield predicted = data.Build();
        var start = new Vector2(50, 170);
        sim.AddTank(TankCatalog.Medium, start, -MathF.PI / 4);
        TankState mine = sim.Tanks[0];
        var inputs = new InputCmd[1];
        bool road = false, sand = false, water = false;
        for (int tick = 1; tick <= 300; tick++)
        {
            sbyte throttle = (sbyte)(tick % 150 < 130 ? 1 : -1);
            sbyte steer = (sbyte)(tick % 100 < 12 ? 1 : tick % 100 >= 88 ? -1 : 0);
            inputs[0] = new InputCmd(tick, throttle, steer, 0f, 0f, false);
            sim.Step(inputs);
            TankStep.Advance(ref mine, inputs[0], tick, predicted, ReadOnlySpan<Obb2>.Empty, knockDown: false, null, canFire: false, out _);
            Assert.Equal(sim.Tanks[0].Position, mine.Position);
            Assert.Equal(sim.Tanks[0].Speed, mine.Speed);

            Assert.Equal(original.SpeedFactorAt(mine.Position), predicted.SpeedFactorAt(mine.Position));
            road |= predicted.WaterDepthAt(mine.Position) == 0 && predicted.SurfaceAt(mine.Position) == SurfaceKind.Road;
            sand |= predicted.WaterDepthAt(mine.Position) == 0 && predicted.SurfaceAt(mine.Position) == SurfaceKind.Sand;
            water |= predicted.WaterDepthAt(mine.Position) > 0.9f;
        }
        Assert.True(road && sand && water, $"crossed road {road}, sand {sand}, the ditch {water}; ended at {mine.Position}");
    }
}

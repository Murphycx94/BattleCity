using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class SandboxMapTests
{
    [Fact]
    public void Create_IsDeterministic()
    {
        Battlefield a = SandboxMap.Create(), b = SandboxMap.Create();
        Assert.Equal(a.Props.Count, b.Props.Count);
        Assert.Equal(a.Props[0], b.Props[0]);
        Assert.Equal(a.Terrain.HeightAt(123, 45), b.Terrain.HeightAt(123, 45));
    }

    [Fact]
    public void Spawn_IsPlayable_AndClearOfRocks()
    {
        Battlefield field = SandboxMap.Create();
        Assert.Equal(SandboxMap.Spawn, field.ClampToPlayable(SandboxMap.Spawn));
        Obb2 hull = TankMotion.Footprint(TankCatalog.Medium, SandboxMap.Spawn, 0);
        Assert.All(field.Props, p => Assert.False(Obb2.TryGetPenetration(hull, p.Footprint, out _)));
    }

    [Fact]
    public void Terrain_HasAHillWorthClimbing()
    {
        Battlefield field = SandboxMap.Create();
        float rise = field.Terrain.HeightAt(200, 150) - field.Terrain.HeightAt(SandboxMap.Spawn.X, SandboxMap.Spawn.Y);
        Assert.True(rise > 15, $"hill only {rise}m above spawn");
    }

    [Fact]
    public void Rocks_SitOnTheGround()
    {
        Battlefield field = SandboxMap.Create();
        Assert.All(field.Props, p =>
            Assert.True(p.Top > field.Terrain.HeightAt(p.Footprint.Center.X, p.Footprint.Center.Y) + 1f, $"rock {p.Id} is buried"));
    }

    [Fact]
    public void DrivingStraightFromSpawn_ClimbsTheBigHill()
    {
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Medium, SandboxMap.Spawn, SandboxMap.SpawnYaw);
        var input = new[] { TestWorld.Cmd(throttle: 1) };
        float highest = 0f;
        for (int i = 0; i < 20 * SimConstants.TickRate; i++)
        {
            sim.Step(input);
            Vector2 p = sim.Tanks[0].Position;
            highest = MathF.Max(highest, sim.Field.Terrain.HeightAt(p.X, p.Y));
        }
        Assert.True(highest > 18f, $"only reached {highest}m");
    }

    [Fact]
    public void Terrain_HasSlopesSteeperThanTheClimbLimit()
    {
        var terrain = SandboxMap.Create().Terrain;
        float steepest = 0f;
        for (float z = 0; z <= terrain.SizeZ; z += 2f)
        for (float x = 0; x <= terrain.SizeX; x += 2f)
            steepest = MathF.Max(steepest, MathF.Acos(terrain.NormalAt(x, z).Y));
        Assert.True(steepest > TankCatalog.Medium.MaxClimb, $"steepest {steepest} rad");
    }
}

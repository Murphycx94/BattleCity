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
}

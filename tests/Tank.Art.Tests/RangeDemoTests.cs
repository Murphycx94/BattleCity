using Tank.Client.Sandbox;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

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
    public void Shoots_the_kind_of_tank_it_was_written_for(string demo, string kind) =>
        Assert.Equal(kind, RangeDemo.Find(demo)!.Spot.Spec.Id);
}

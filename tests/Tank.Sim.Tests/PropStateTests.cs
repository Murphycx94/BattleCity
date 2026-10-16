using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

public sealed class PropStateTests
{
    // Across the X axis at x = 60, facing a ray along +X at z = 100.
    private static Prop Wall(int id, PropKind kind = PropKind.BrickWall, float x = 60) =>
        new WallLine(kind, new Vector2(x, 99), new Vector2(x, 101)).Segments(TestWorld.Flat(), id).Single();

    private static Prop Rock(int id, float x) => new(id, PropKind.Rock, new Obb2(new Vector2(x, 100), new Vector2(1, 5), 0), -1, 10);

    private static Prop Bush(int id, float x, float z = 100) => new(id, PropKind.Bush, new Obb2(new Vector2(x, z), new Vector2(3, 3), 0), -0.2f, 4);

    private static readonly Vector3 From = new(20, 1, 100), To = new(120, 1, 100);

    [Fact]
    public void Every_prop_starts_standing_at_full_hit_points()
    {
        Battlefield field = TestWorld.FlatField(Wall(1), Wall(2, PropKind.StoneWall, 70), Wall(3, PropKind.Fence, 80), Rock(4, 90), Bush(5, 40));
        Assert.All(field.Props, p => Assert.True(field.IsStanding(p.Id)));
        Assert.Equal(new[] { 300, 900, 1, 0, 1 }, field.Props.Select(p => field.Durability(p.Id)));
    }

    [Fact]
    public void Asking_about_an_unknown_prop_throws() =>
        Assert.Throws<ArgumentException>(() => TestWorld.FlatField(Wall(1)).IsStanding(2));

    [Fact]
    public void PropById_finds_a_prop_by_its_id()
    {
        Battlefield field = TestWorld.FlatField(Rock(7, 90), Wall(3));
        Assert.Equal(PropKind.BrickWall, field.PropById(3).Kind);
        Assert.Equal(7, field.PropById(7).Id);
        Assert.Throws<ArgumentException>(() => field.PropById(1));
    }

    [Fact]
    public void A_standing_wall_stops_rays()
    {
        Assert.True(TestWorld.FlatField(Wall(1)).Raycast(From, To, out RayHit hit));
        Assert.Equal(1, hit.PropId);
        Assert.Equal(59.7f, hit.Point.X, 1e-3f);
    }

    [Fact]
    public void Rays_pass_over_rubble_and_stop_at_what_lies_behind()
    {
        Battlefield field = TestWorld.FlatField(Wall(1), Rock(2, 80));
        Assert.True(field.Damage(field.IndexOf(1), 300));
        Assert.True(field.Raycast(From, To, out RayHit hit));
        Assert.Equal(2, hit.PropId);
    }

    [Fact]
    public void Rays_pass_through_fences_and_bushes()
    {
        Battlefield field = TestWorld.FlatField(Wall(1, PropKind.Fence), Bush(2, 40));
        Assert.False(field.Raycast(From, To, out _));
    }

    [Fact]
    public void Damage_wears_a_wall_down_and_the_blow_that_empties_it_destroys_it()
    {
        Battlefield field = TestWorld.FlatField(Wall(1));
        int wall = field.IndexOf(1);
        Assert.False(field.Damage(wall, 120));
        Assert.Equal(180, field.Durability(1));
        Assert.True(field.IsStanding(1));
        Assert.True(field.Damage(wall, 500));
        Assert.Equal(0, field.Durability(1));
        Assert.False(field.IsStanding(1));
        Assert.False(field.Damage(wall, 50));   // already down
    }

    [Fact]
    public void Destroy_knocks_a_prop_down_once()
    {
        Battlefield field = TestWorld.FlatField(Wall(1, PropKind.Fence));
        int fence = field.IndexOf(1);
        Assert.True(field.Destroy(fence));
        Assert.False(field.IsStanding(1));
        Assert.False(field.Destroy(fence));
    }

    [Fact]
    public void Rocks_cannot_be_damaged_or_destroyed()
    {
        Battlefield field = TestWorld.FlatField(Rock(1, 60));
        int rock = field.IndexOf(1);
        Assert.Throws<InvalidOperationException>(() => field.Damage(rock, 100));
        Assert.Throws<InvalidOperationException>(() => field.Destroy(rock));
        Assert.True(field.IsStanding(1));
    }

    [Fact]
    public void Restore_puts_a_prop_back_at_full_hit_points()
    {
        Battlefield field = TestWorld.FlatField(Wall(1, PropKind.StoneWall));
        int wall = field.IndexOf(1);
        field.Damage(wall, 1000);
        field.Restore(wall);
        Assert.True(field.IsStanding(1));
        Assert.Equal(900, field.Durability(1));
    }

    [Fact]
    public void BushesNear_skips_bushes_that_were_shot_away()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, 100), Bush(2, 110));
        field.Destroy(field.IndexOf(1));
        Assert.Equal(new[] { 2 }, field.BushesNear(new Vector2(105, 100), 5f).Select(p => p.Id));
        var results = new List<Prop>();
        field.BushesNear(new Vector2(105, 100), 5f, results);
        Assert.Equal(new[] { 2 }, results.Select(p => p.Id));
    }

    [Fact]
    public void A_tank_is_stopped_by_a_wall_but_drives_over_its_rubble()
    {
        // A three-segment brick wall across the road at z = 90; the tank starts at z = 110 heading north (−Z).
        Prop[] wall = new WallLine(PropKind.BrickWall, new Vector2(97, 90), new Vector2(103, 90)).Segments(TestWorld.Flat(), 1).ToArray();
        Battlefield field = TestWorld.FlatField(wall);
        var blocked = TestWorld.Tank(100, 110);
        TestWorld.Drive(ref blocked, field, TestWorld.Cmd(throttle: 1), 5 * SimConstants.TickRate);
        Assert.True(blocked.Position.Y > 90 + 0.3f + TankFront, $"drove into the wall: z = {blocked.Position.Y}");

        foreach (Prop segment in wall) field.Destroy(field.IndexOf(segment.Id));
        var through = TestWorld.Tank(100, 110);
        TestWorld.Drive(ref through, field, TestWorld.Cmd(throttle: 1), 5 * SimConstants.TickRate);
        Assert.True(through.Position.Y < 90 - TankFront, $"stopped at the rubble: z = {through.Position.Y}");
    }

    private static float TankFront => TestWorld.Tank(0, 0).Spec.HullLength / 2;
}

public sealed class PropDistanceTests
{
    [Fact]
    public void Measures_to_a_wall_box_and_its_top()
    {
        var wall = new Prop(1, PropKind.BrickWall, new Obb2(new Vector2(0, 0), new Vector2(1, 0.3f), 0), 0, 3);
        Assert.Equal(0f, wall.DistanceTo(new Vector3(0.5f, 1, 0)));
        Assert.Equal(2f, wall.DistanceTo(new Vector3(0, 1, 2.3f)), 1e-5f);
        Assert.Equal(5f, wall.DistanceTo(new Vector3(4, 7, 0)), 1e-5f);   // 3 out past the end, 4 above the top
    }

    [Fact]
    public void Measures_to_a_bush_as_a_cylinder()
    {
        var bush = new Prop(1, PropKind.Bush, new Obb2(new Vector2(0, 0), new Vector2(2, 2), 0), 0, 3);
        Assert.Equal(1f, bush.DistanceTo(new Vector3(3, 1, 0)), 1e-5f);
        Assert.Equal(1f, bush.DistanceTo(new Vector3(0, 1, -3)), 1e-5f);   // off a side a box would say 1 too
        Assert.Equal(MathF.Sqrt(2) * 3 - 2, bush.DistanceTo(new Vector3(3, 1, 3)), 1e-4f);   // off a corner a box would say √2: only this tells them apart
    }
}

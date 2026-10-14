using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class BushTests
{
    private static Prop Bush(int id, float x, float z, float radius = 3f, float height = 3.5f) =>
        new(id, PropKind.Bush, new Obb2(new Vector2(x, z), new Vector2(radius, radius), 0), -0.2f, height);

    private static Prop Rock(int id, float x, float z) =>
        new(id, PropKind.Rock, new Obb2(new Vector2(x, z), new Vector2(1, 5), 0), -1, 10);

    [Fact]
    public void Rocks_are_solid_and_bushes_are_not()
    {
        Assert.True(Rock(1, 0, 0).IsSolid);
        Assert.False(Bush(2, 0, 0).IsSolid);
    }

    [Fact]
    public void A_tank_drives_through_a_bush_as_if_it_were_not_there()
    {
        var open = TestWorld.Tank(100, 130);
        var through = TestWorld.Tank(100, 130);
        TestWorld.Drive(ref open, TestWorld.FlatField(), TestWorld.Cmd(throttle: 1), 4 * SimConstants.TickRate);
        TestWorld.Drive(ref through, TestWorld.FlatField(Bush(1, 100, 110)), TestWorld.Cmd(throttle: 1), 4 * SimConstants.TickRate);
        Assert.True(open.Position.Y < 100, "the run passes the bush at z = 110");
        Assert.Equal(open.Position, through.Position);
        Assert.Equal(open.Speed, through.Speed);
    }

    [Fact]
    public void A_tank_can_stop_and_turn_inside_a_bush()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, 100, 100));
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, field, TestWorld.Cmd(steer: 1), SimConstants.TickRate);
        Assert.Equal(new Vector2(100, 100), t.Position);
        Assert.NotEqual(0f, t.Yaw);
    }

    [Fact]
    public void Rays_pass_through_a_bush_and_hit_the_ground_behind_it()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, 60, 100));
        // Descends from 1 m to −1 m over 100 m: crosses the bush at x≈60 (y≈0.2) and meets the ground at x = 70.
        Assert.True(field.Raycast(new Vector3(20, 1, 100), new Vector3(120, -1, 100), out RayHit hit));
        Assert.Equal(RayHit.TerrainId, hit.PropId);
        Assert.Equal(70f, hit.Point.X, 1e-2f);
    }

    [Fact]
    public void Rays_still_stop_at_a_rock_behind_a_bush()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, 50, 100), Rock(2, 60, 100));
        Assert.True(field.Raycast(new Vector3(20, 1, 100), new Vector3(120, 1, 100), out RayHit hit));
        Assert.Equal(2, hit.PropId);
        Assert.Equal(59f, hit.Point.X, 1e-3f);
    }

    [Fact]
    public void BushesNear_finds_bushes_whose_edge_is_within_the_radius()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, 100, 100, radius: 2f), Bush(2, 120, 100, radius: 3f), Rock(3, 102, 100), Bush(4, 150, 100));
        int[] near = field.BushesNear(new Vector2(110, 100), 8f).Select(p => p.Id).OrderBy(id => id).ToArray();
        Assert.Equal(new[] { 1, 2 }, near);   // bush 1's edge is 8 m away, bush 2's 7 m; the rock and bush 4 don't count
        Assert.DoesNotContain(field.BushesNear(new Vector2(110, 100), 7.5f), p => p.Id == 1);
    }

    [Fact]
    public void A_bush_footprint_must_be_round()
    {
        var oval = new Prop(1, PropKind.Bush, new Obb2(new Vector2(60, 100), new Vector2(2, 3), 0), 0, 3);
        Assert.Throws<ArgumentException>(() => TestWorld.FlatField(oval));
    }

    [Fact]
    public void BushesNear_can_fill_a_caller_owned_list_without_allocating_an_enumerator()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, 100, 100, radius: 2f), Bush(2, 120, 100, radius: 3f), Rock(3, 102, 100), Bush(4, 150, 100));
        var results = new List<Prop> { Bush(99, 0, 0) };   // stale content is cleared
        field.BushesNear(new Vector2(110, 100), 8f, results);
        Assert.Equal(new[] { 1, 2 }, results.Select(p => p.Id).OrderBy(id => id).ToArray());
    }

    [Fact]
    public void Obb2_distance_to_a_point_is_zero_inside_and_euclidean_outside()
    {
        var box = new Obb2(new Vector2(10, 10), new Vector2(2, 1), 0);   // x 8..12 across, z 9..11 along
        Assert.Equal(0f, box.DistanceToPoint(new Vector2(11, 10.5f)));
        Assert.Equal(3f, box.DistanceToPoint(new Vector2(15, 10)), 1e-4f);
        Assert.Equal(5f, box.DistanceToPoint(new Vector2(15, 15)), 1e-4f);   // 3 across the corner gap in x, 4 in z
        var turned = new Obb2(new Vector2(0, 0), new Vector2(2, 1), MathF.PI / 2);   // swaps the axes
        Assert.Equal(0f, turned.DistanceToPoint(new Vector2(0, 1.9f)) , 1e-4f);
        Assert.Equal(1f, turned.DistanceToPoint(new Vector2(0, 3f)), 1e-4f);
    }

    [Fact]
    public void Obb2_intersects_a_circle_when_its_edge_comes_within_the_radius()
    {
        var box = new Obb2(new Vector2(0, 0), new Vector2(1.6f, 3.2f), 0.4f);
        Assert.True(box.IntersectsCircle(new Vector2(0, 0), 0.1f));
        Assert.True(box.IntersectsCircle(new Vector2(6, 0), 2.6f) == (box.DistanceToPoint(new Vector2(6, 0)) <= 2.6f));
        Assert.False(box.IntersectsCircle(new Vector2(20, 0), 3f));
    }
}

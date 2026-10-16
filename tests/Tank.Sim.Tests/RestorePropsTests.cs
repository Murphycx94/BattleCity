using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// Every tank here is parked clear of whatever solid still stands (as a real one always is), so a wall or fence it holds
/// down is one knocked down all the way across its hull.
/// </summary>
public sealed class RestorePropsTests
{
    private static readonly int[] Wall = { 1, 2, 3 }, Fence = { 4, 5, 6 };

    // A brick wall (ids 1–3) and a fence (4–6) across x = 97 … 103, at z = 60 and z = 140, a bush (7) at (40, 100) and,
    // last, a lone brick segment (8) across x = 150 … 152 at z = 100.
    private static Simulation Yard()
    {
        HeightField flat = TestWorld.Flat();
        var props = new List<Prop>();
        props.AddRange(new WallLine(PropKind.BrickWall, new Vector2(97, 60), new Vector2(103, 60)).Segments(flat, 1));
        props.AddRange(new WallLine(PropKind.Fence, new Vector2(97, 140), new Vector2(103, 140)).Segments(flat, 4));
        props.Add(new Prop(7, PropKind.Bush, new Obb2(new Vector2(40, 100), new Vector2(3, 3), 0), -0.2f, 4f));
        props.AddRange(new WallLine(PropKind.BrickWall, new Vector2(150, 100), new Vector2(152, 100)).Segments(flat, 8));
        return new Simulation(TestWorld.Field(flat, props.ToArray()));
    }

    /// <summary>Adds a tank, first making sure its hull is clear of every solid prop still standing.</summary>
    private static int Park(Simulation sim, TankSpec spec, Vector2 at, float yaw)
    {
        Obb2 hull = TankMotion.Footprint(spec, at, yaw);
        foreach (Prop p in sim.Field.Props)
            Assert.False(p.IsSolid && sim.Field.IsStanding(p.Id) && Obb2.TryGetPenetration(hull, p.Footprint, out _), $"parked inside {p.Kind} {p.Id}");
        return sim.AddTank(spec, at, yaw);
    }

    private static void KnockDown(Battlefield field, IEnumerable<int> ids)
    {
        foreach (int id in ids) field.Destroy(field.IndexOf(id));
    }

    private static bool Covers(TankSpec spec, Vector2 at, float yaw, in Obb2 footprint) =>
        Obb2.TryGetPenetration(TankMotion.Footprint(spec, at, yaw), footprint, out _);

    [Fact]
    public void Everything_comes_back_as_it_was()
    {
        Simulation sim = Yard();
        Battlefield field = sim.Field;
        field.Damage(field.IndexOf(1), 120);   // damaged
        field.Damage(field.IndexOf(2), 999);   // rubble
        field.Destroy(field.IndexOf(5));        // knocked flat
        field.Destroy(field.IndexOf(7));        // shot away

        sim.RestoreProps();

        Assert.All(field.Props, p =>
        {
            Assert.True(field.IsStanding(p.Id), $"prop {p.Id} still down");
            Assert.Equal(p.MaxDurability, field.Durability(p.Id));
        });
    }

    [Fact]
    public void A_wall_or_fence_with_a_tank_on_it_waits_for_the_next_time()
    {
        Simulation sim = Yard();
        Battlefield field = sim.Field;
        KnockDown(field, Wall.Concat(Fence));
        Vector2 onWall = new(100, 60), onFence = new(100, 140);
        int onRubble = Park(sim, TankCatalog.Heavy, onWall, 0f);
        Park(sim, TankCatalog.Medium, onFence, 0f);
        Assert.All(Wall, id => Assert.True(Covers(TankCatalog.Heavy, onWall, 0f, field.PropById(id).Footprint), $"wall {id} isn't under the hull"));
        Assert.All(Fence, id => Assert.True(Covers(TankCatalog.Medium, onFence, 0f, field.PropById(id).Footprint), $"fence {id} isn't under the hull"));

        sim.RestoreProps();
        Assert.All(Wall.Concat(Fence), id => Assert.Equal((false, 0), (field.IsStanding(id), field.Durability(id))));

        // Knocked out where it stands: a wreck holds the rubble down too.
        sim.TankRef(onRubble).IsDestroyed = true;
        sim.RestoreProps();
        Assert.All(Wall, id => Assert.False(field.IsStanding(id)));
    }

    [Fact]
    public void The_same_call_that_leaves_a_covered_wall_down_brings_back_what_no_tank_is_on()
    {
        Simulation sim = Yard();
        Battlefield field = sim.Field;
        KnockDown(field, Wall);
        field.Destroy(field.IndexOf(5));
        field.Damage(field.IndexOf(8), 120);
        Park(sim, TankCatalog.Heavy, new Vector2(100, 60), 0f);   // on the wall's rubble only
        Assert.True(field.IndexOf(3) < field.IndexOf(5) && field.IndexOf(5) < field.IndexOf(8), "the free ones come after the covered ones");

        sim.RestoreProps();

        Assert.All(Wall, id => Assert.False(field.IsStanding(id)));
        Assert.True(field.IsStanding(5), "the fence nobody is on");
        Assert.Equal(300, field.Durability(8));   // and the damaged segment far away
    }

    [Fact]
    public void Once_the_tank_has_moved_off_the_next_restore_brings_it_back()
    {
        Simulation sim = Yard();
        Battlefield field = sim.Field;
        KnockDown(field, Wall);
        Park(sim, TankCatalog.Medium, new Vector2(100, 60), MathF.PI / 2);   // along the rubble, heading west
        sim.RestoreProps();
        Assert.All(Wall, id => Assert.False(field.IsStanding(id)));

        var west = new[] { TestWorld.Cmd(throttle: 1) };
        for (int i = 0; i < 3 * SimConstants.TickRate; i++) sim.Step(west);
        Assert.True(sim.Tanks[0].Position.X < 90, "drove off the rubble");
        Assert.Equal(60f, sim.Tanks[0].Position.Y, 1e-3f);   // straight off it: nothing pushed it aside
        sim.RestoreProps();
        Assert.All(Wall, id => Assert.Equal((true, 300), (field.IsStanding(id), field.Durability(id))));
    }

    [Fact]
    public void A_tank_on_the_rubble_spread_but_clear_of_the_wall_does_not_hold_it_down()
    {
        Simulation sim = Yard();
        Battlefield field = sim.Field;
        KnockDown(field, Wall);
        // Heading east just south of the wall, the hull's edge 25 cm past its face (z = 60.3): on the half metre the
        // rubble spreads, not where the wall stood.
        TankSpec spec = TankCatalog.Medium;
        var at = new Vector2(100, 60.3f + 0.25f + spec.HullWidth / 2);
        const float east = -MathF.PI / 2;
        Assert.All(Wall, id =>
        {
            Prop wall = field.PropById(id);
            Assert.True(Covers(spec, at, east, wall.RubbleFootprint), $"clear of wall {id}'s rubble");
            Assert.False(Covers(spec, at, east, wall.Footprint), $"on wall {id}'s own footprint");
        });
        Park(sim, spec, at, east);

        sim.RestoreProps();

        Assert.All(Wall, id => Assert.True(field.IsStanding(id)));
    }

    [Fact]
    public void A_bush_comes_back_even_around_a_tank()
    {
        Simulation sim = Yard();
        sim.Field.Destroy(sim.Field.IndexOf(7));
        Park(sim, TankCatalog.Medium, new Vector2(40, 100), 0f);
        sim.RestoreProps();
        Assert.True(sim.Field.IsStanding(7));
    }

    [Fact]
    public void Restoring_raises_no_events()
    {
        Simulation sim = Yard();
        Battlefield field = sim.Field;
        field.Damage(field.IndexOf(1), 120);
        field.Damage(field.IndexOf(2), 999);
        field.Destroy(field.IndexOf(5));
        field.Destroy(field.IndexOf(7));
        Park(sim, TankCatalog.Medium, new Vector2(100, 100), 0f);   // in the open, sitting still
        var hold = new[] { TestWorld.Cmd() };
        sim.Step(hold);

        sim.RestoreProps();
        sim.Step(hold);

        Assert.True(field.IsStanding(2) && field.IsStanding(5) && field.IsStanding(7), "restored");
        Assert.Empty(sim.Events.ToArray());
    }
}

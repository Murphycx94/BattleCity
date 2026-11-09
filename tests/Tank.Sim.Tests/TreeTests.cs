using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>
/// Trees (riverland design §2.4, §4.3) on flat ground: a trunk of radius 0.5 m, 12 m tall, at (100, 80) unless a test
/// says otherwise; its crown reaches 3.5 m.
/// </summary>
public sealed class TreeTests
{
    private static readonly Vector2 TreeAt = new(100, 80);
    private const float Trunk = 0.5f;
    private static readonly InputCmd Full = TestWorld.Cmd(throttle: 1);

    /// <summary>A tree <paramref name="height"/> m tall: its prop starts 0.3 m under the ground, like every planted prop.</summary>
    internal static Prop Tree(int id, Vector2 at, float trunk = Trunk, float height = 12f) =>
        new(id, PropKind.Tree, new Obb2(at, new Vector2(trunk, trunk), 0), -0.3f, height + 0.3f);

    [Fact]
    public void A_tree_is_solid_flimsy_round_foliage()
    {
        Prop tree = Tree(1, TreeAt);
        Assert.True(tree.IsSolid && tree.IsFlimsy && tree.IsRound && tree.IsFoliage);
        Assert.False(tree.StopsShells);
        Assert.False(tree.IsWall);
        Assert.Equal(1, tree.MaxDurability);
        Assert.Equal(3.5f, tree.FoliageRadius);
        Assert.Equal(PropKind.Tree, Prop.LastKind);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 6 },
            new[] { PropKind.Rock, PropKind.Bush, PropKind.BrickWall, PropKind.StoneWall, PropKind.Fence, PropKind.Tree }.Select(k => (byte)k));
    }

    [Fact]
    public void A_tree_must_have_a_round_footprint()
    {
        var oval = new Prop(1, PropKind.Tree, new Obb2(TreeAt, new Vector2(0.5f, 0.4f), 0), -0.3f, 12.3f);
        Assert.Throws<ArgumentException>(() => TestWorld.FlatField(oval));
    }

    [Theory]
    [InlineData("light")]
    [InlineData("medium")]
    [InlineData("heavy")]
    [InlineData("td")]
    public void Any_tank_knocks_a_tree_down_without_being_slowed(string id)
    {
        TankSpec spec = TankCatalog.ById(id);
        var start = new Vector2(TreeAt.X, TreeAt.Y + 15f);
        Simulation wooded = Moving(spec, start, Tree(1, TreeAt)), open = Moving(spec, start);
        var events = new List<GameEvent>();
        for (int tick = 0; tick < 4 * SimConstants.TickRate; tick++)
        {
            wooded.Step(new[] { Full });
            open.Step(new[] { Full });
            events.AddRange(wooded.Events);
            TankState a = wooded.Tanks[0], b = open.Tanks[0];
            Assert.True(a.Position.Equals(b.Position) && a.Speed.Equals(b.Speed), $"tick {tick}: {a.Position} at {a.Speed} against {b.Position} at {b.Speed}");
        }

        Assert.True(wooded.Tanks[0].Position.Y < TreeAt.Y - spec.HullLength, "the run passes the tree");
        Assert.False(wooded.Field.IsStanding(1));
        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal((1, wooded.Tanks[0].Id, 0), (down.PropId, down.TankId, down.ShellId));
        Assert.True(Vector3.Distance(new Vector3(0, 0, -1), down.Velocity) < 1e-3f, $"pushed {down.Velocity}");
    }

    /// <summary>A <paramref name="spec"/> tank at <paramref name="start"/> heading north at 10 km/h, alone on a flat field with <paramref name="props"/>.</summary>
    private static Simulation Moving(TankSpec spec, Vector2 start, params Prop[] props)
    {
        var sim = new Simulation(TestWorld.FlatField(props));
        int id = sim.AddTank(spec, start, 0f);
        sim.TankRef(id).Speed = 10f / 3.6f;
        return sim;
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(MathF.PI / 4)]   // the trunk's square footprint would stick out 0.71 m towards the hull's side here
    public void Passing_beside_the_trunk_leaves_it_standing(float yaw)
    {
        Assert.True(PassesStanding(yaw, gap: 0.55f), "0.55 m off: only through the crown");
        Assert.False(PassesStanding(yaw, gap: 0.45f), "0.45 m off: the hull's side grazes the trunk");
    }

    /// <summary>
    /// Drives a medium tank along <paramref name="yaw"/> past a tree whose trunk's centre is <paramref name="gap"/> m off
    /// the line of the hull's right side.
    /// </summary>
    /// <returns>Whether the tree still stands.</returns>
    private static bool PassesStanding(float yaw, float gap)
    {
        TankSpec spec = TankCatalog.Medium;
        Vector2 start = new(100, 100), ahead = Angles.Forward(yaw), right = Angles.Right(yaw);
        Prop tree = Tree(1, start + ahead * 20f + right * (spec.HullWidth / 2 + gap));
        Battlefield field = TestWorld.FlatField(tree);
        var t = new TankState { Id = 1, Spec = spec, Position = start, Yaw = yaw };
        TestWorld.Drive(ref t, field, Full, 6 * SimConstants.TickRate);
        Assert.True(Vector2.Dot(t.Position - tree.Footprint.Center, ahead) > spec.HullLength, "the hull drove past the tree");
        return field.IsStanding(1);
    }

    [Fact]
    public void Prediction_drives_through_trees_and_leaves_them_standing()
    {
        Battlefield predicted = TestWorld.FlatField(Tree(1, TreeAt), Tree(2, TreeAt + new Vector2(1.5f, -8f)));
        Battlefield server = TestWorld.FlatField(Tree(1, TreeAt), Tree(2, TreeAt + new Vector2(1.5f, -8f)));
        TankState client = TestWorld.Tank(TreeAt.X, TreeAt.Y + 15f), authority = client;
        for (int tick = 0; tick < 4 * SimConstants.TickRate; tick++)
        {
            TankMotion.Drive(ref client, Full, predicted, ReadOnlySpan<Obb2>.Empty, knockDown: false);
            TankMotion.Drive(ref authority, Full, server, ReadOnlySpan<Obb2>.Empty);
            Assert.True(client.Equals(authority), $"tick {tick}: {client.Position} against {authority.Position}");
        }
        Assert.True(client.Position.Y < TreeAt.Y - 15f, "the run passes both trees");
        Assert.True(predicted.IsStanding(1) && predicted.IsStanding(2));
        Assert.False(server.IsStanding(1) || server.IsStanding(2));
    }

    [Fact]
    public void A_shell_through_the_trunk_fells_it_and_flies_on()
    {
        // A stone wall 20 m behind the tree (north), its south face at z = 60.5; the shooter 50 m south of the tree.
        Prop[] wall = new WallLine(PropKind.StoneWall, new Vector2(97, 60), new Vector2(103, 60)).Segments(TestWorld.Flat(), 2).ToArray();
        Simulation sim = Range(wall.Prepend(Tree(1, TreeAt)).ToArray());
        List<GameEvent> events = Fire(sim, new Vector3(100, 1.5f, 60.5f), AmmoType.AP);

        GameEvent[] props = events.Where(e => e.Type is GameEventType.PropDestroyed or GameEventType.PropDamaged).ToArray();
        Assert.Equal(new[] { (GameEventType.PropDestroyed, 1), (GameEventType.PropDamaged, 3) }, props.Select(e => (e.Type, e.PropId)));
        Assert.Equal(props[0].ShellId, props[1].ShellId);
        Assert.Equal(sim.Tanks[0].Id, props[0].TankId);
        var onBark = new Vector2(props[0].Position.X, props[0].Position.Z);
        Assert.Equal(Trunk, Vector2.Distance(onBark, TreeAt), 0.02f);
        Assert.True(onBark.Y > TreeAt.Y, "where the shell went in: the trunk's south side");
        Assert.False(sim.Field.IsStanding(1));
    }

    [Fact]
    public void A_shell_through_the_crown_only_leaves_it_standing()
    {
        Simulation sim = Range(Tree(1, TreeAt));
        List<GameEvent> events = Fire(sim, new Vector3(TreeAt.X + 1.5f, 8f, TreeAt.Y), AmmoType.AP);
        Assert.Contains(events, e => e.Type == GameEventType.ShellFired);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.True(sim.Field.IsStanding(1));
    }

    [Theory]
    [InlineData(1f, false)]
    [InlineData(10f, true)]
    public void An_HE_burst_fells_the_trees_in_its_radius(float off, bool standing)
    {
        // Medium HE splashes 3 m. Aimed at the ground south of the trunk, off m from its bark, from 20 m further south:
        // steep enough (6°) for the shell to land close to where it is aimed, short of the trunk.
        var aim = new Vector3(TreeAt.X, 0f, TreeAt.Y + Trunk + off);
        Simulation sim = Range(new Vector2(aim.X, aim.Z + 20f), Tree(1, TreeAt));
        List<GameEvent> events = Fire(sim, aim, AmmoType.HE);

        GameEvent burst = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.True(Vector3.Distance(burst.Position, aim) < 1f, $"burst at {burst.Position}, aimed at {aim}");
        Assert.Equal(standing, sim.Field.IsStanding(1));
        if (standing) return;
        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal((1, AmmoType.HE, burst.Position), (down.PropId, down.Ammo, down.Position));   // a splash, not a shell through it
        Assert.True(events.IndexOf(burst) < events.IndexOf(down));
    }

    [Fact]
    public void A_gun_fired_from_inside_a_trunk_leaves_that_tree_standing()
    {
        // Turret turned east, so the muzzle is 1.6 m clear of the hull's side; a tree is planted on it.
        var hold = new InputCmd(0, 0, 0, -MathF.PI / 2, 0f, false);
        Simulation bare = Range();
        Run(bare, hold, 90);
        Vector3 muzzle = Ballistics.Aim(bare.Tanks[0], bare.Field).Origin;
        var trunk = new Vector2(muzzle.X, muzzle.Z);
        Assert.False(TankMotion.Footprint(TankCatalog.Medium, bare.Tanks[0].Position, 0f).IntersectsCircle(trunk, Trunk), "the hull clear of the trunk");

        Simulation sim = Range(Tree(1, trunk), Tree(2, trunk + new Vector2(20f, 0f)));
        Run(sim, hold, 90);
        Assert.Equal(muzzle, Ballistics.Aim(sim.Tanks[0], sim.Field).Origin);
        var events = Run(sim, hold with { Fire = true }, 1);
        events.AddRange(Run(sim, hold, 15));

        Assert.Contains(events, e => e.Type == GameEventType.ShellFired);
        Assert.True(sim.Field.IsStanding(1), "the tree the gun fired from");
        Assert.Equal(2, Assert.Single(events, e => e.Type == GameEventType.PropDestroyed).PropId);   // the next one, shot through
    }

    [Fact]
    public void Restore_skips_a_tree_with_a_tank_on_its_trunk()
    {
        var sim = new Simulation(TestWorld.FlatField(Tree(1, TreeAt), Tree(2, TreeAt + new Vector2(30f, 0f))));
        sim.Field.Destroy(0);
        sim.Field.Destroy(1);
        sim.AddTank(TankCatalog.Medium, TreeAt + new Vector2(0.5f, 1f), 0f);

        sim.RestoreProps();

        Assert.False(sim.Field.IsStanding(1));
        Assert.True(sim.Field.IsStanding(2));
    }

    [Fact]
    public void Restore_brings_back_a_tree_a_hull_overlaps_by_its_square_but_not_its_trunk()
    {
        // The hull's corner 0.4 m off the trunk's centre both ways: inside the trunk's 1 × 1 m square, 0.57 m from its centre.
        var sim = new Simulation(TestWorld.FlatField(Tree(1, TreeAt)));
        sim.Field.Destroy(0);
        TankSpec spec = TankCatalog.Medium;
        Vector2 at = TreeAt + new Vector2(0.4f + spec.HullWidth / 2, 0.4f + spec.HullLength / 2);
        Obb2 hull = TankMotion.Footprint(spec, at, 0f);
        Prop tree = sim.Field.PropById(1);
        Assert.True(Obb2.TryGetPenetration(hull, tree.Footprint, out _), "the hull overlaps the trunk's square");
        Assert.False(hull.IntersectsCircle(tree.Footprint.Center, Trunk), "the hull touches the trunk");
        sim.AddTank(spec, at, 0f);

        sim.RestoreProps();

        Assert.True(sim.Field.IsStanding(1));
    }

    [Fact]
    public void Rays_and_sight_lines_pass_through_trees()
    {
        Battlefield field = TestWorld.FlatField(Tree(1, TreeAt));
        Assert.False(field.Raycast(new Vector3(90, 2, TreeAt.Y), new Vector3(110, 2, TreeAt.Y), out _));
        Assert.True(field.Raycast(new Vector3(90, 2, TreeAt.Y), new Vector3(110, -0.5f, TreeAt.Y), out RayHit ground));
        Assert.Equal(RayHit.TerrainId, ground.PropId);   // through the trunk at about 1.2 m up, into the ground 6 m beyond it
        Assert.Equal(106f, ground.Point.X, 1e-2f);

        var observer = new TankState { Id = 1, Spec = TankCatalog.Medium, Position = TreeAt + new Vector2(0, 60), LastFired = TankState.NeverFired };
        var target = new TankState { Id = 2, Spec = TankCatalog.Light, Team = 1, Position = TreeAt - new Vector2(0, 20), LastFired = TankState.NeverFired };
        Sighting look = Spotting.Look(observer, target, field, 1000, targetMoving: false);
        Assert.True(look.LineClear);
        Assert.True(look.Seen);
    }

    [Fact]
    public void Distance_to_a_tree_is_to_its_trunk_cylinder()
    {
        Prop tree = Tree(1, new Vector2(100, 100));
        Assert.Equal(2.5f, tree.DistanceTo(new Vector3(103, 1, 100)), 1e-5f);
        Assert.Equal(20f - tree.Top, tree.DistanceTo(new Vector3(100, 20, 100)), 1e-5f);
        Assert.Equal(0f, tree.DistanceTo(new Vector3(100.3f, 5, 100.3f)));
    }

    /// <summary>A medium tank at (100, 130) heading north (50 m south of the tree), alone on a flat field with <paramref name="props"/>.</summary>
    private static Simulation Range(params Prop[] props) => Range(new Vector2(TreeAt.X, TreeAt.Y + 50f), props);

    private static Simulation Range(Vector2 shooter, params Prop[] props)
    {
        var sim = new Simulation(TestWorld.FlatField(props));
        sim.AddTank(TankCatalog.Medium, shooter, 0f);
        return sim;
    }

    /// <summary>
    /// Lays the gun on <paramref name="point"/> with <paramref name="ammo"/> loaded (two seconds to settle, and the reload
    /// a change of round starts), fires once and follows the shell for half a second.
    /// </summary>
    private static List<GameEvent> Fire(Simulation sim, Vector3 point, AmmoType ammo)
    {
        TankPose pose = TankPose.Compute(sim.Tanks[0], sim.Field.Terrain);
        var (yaw, pitch) = Angles.AimAngles(point - pose.Trunnion(TankCatalog.Medium));
        var aim = new InputCmd(0, 0, 0, yaw, pitch, false, ammo);
        int settle = 2 * SimConstants.TickRate + (ammo == AmmoType.HE ? (int)MathF.Ceiling(TankCatalog.Medium.ReloadTime * SimConstants.TickRate) : 0);
        Run(sim, aim, settle);
        var events = Run(sim, aim with { Fire = true }, 1);
        events.AddRange(Run(sim, aim, 15));
        return events;
    }

    private static List<GameEvent> Run(Simulation sim, InputCmd input, int ticks)
    {
        var events = new List<GameEvent>();
        for (int i = 0; i < ticks; i++)
        {
            sim.Step(new[] { input });
            events.AddRange(sim.Events);
        }
        return events;
    }
}

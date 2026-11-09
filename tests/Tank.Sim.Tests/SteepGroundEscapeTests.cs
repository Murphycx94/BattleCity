using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// A tank whose centre stands on ground too steep to climb can still get somewhere: at the foot of a cliff it drives
/// away or along it, on the lip of one it drives over, on its face it drives down, and a rock in its way down it turns
/// from and drives along. The slope over a metre either side (<see cref="HeightField.NormalAt"/>) reads the flat ground
/// within a metre of a cliff as steep as the cliff, and judging every step there by how much it drops against the
/// cliff's grade refused all but one: a bot pushed into the corner below the riverland's south ramp sat there 90 s.
/// Nor is a tank of any kind pocketed on a cliff face (stage 6A, the riverland's cliff-foot traps): with rocks lining the
/// foot below it, it slides along the face, past rocks leaning into its way, and off it; slid over a lip, it drives on
/// along it; wedged so it can't turn, it still drives on.
/// </summary>
public sealed class SteepGroundEscapeTests
{
    /// <summary>A pose counts as one the tank gets out of when it gets this far from where it stood.</summary>
    private const float MoveNeeded = 1f;
    private const int Ticks = 3 * SimConstants.TickRate;
    private const int Headings = 8;
    /// <summary>A 70° cliff: 2.75 m up per metre.</summary>
    private const float CliffGrade = 2.75f;

    /// <summary>Flat at 0 south of z = <paramref name="footZ"/>, a cliff rising north at <paramref name="grade"/>, flat again on top at <paramref name="top"/>.</summary>
    private static HeightField Step(float footZ, float grade, float top)
    {
        int n = TestWorld.Samples;
        var heights = new float[n * n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
            heights[j * n + i] = System.Math.Clamp((footZ - j * TestWorld.Cell) * grade, 0f, top);
        return new HeightField(n, n, TestWorld.Cell, heights);
    }

    /// <summary>Ways to try to get out from a pose: drive (and steer) straight away, or turn in place first, then drive.</summary>
    private static readonly (sbyte Turn, int TurnTicks, sbyte Throttle, sbyte Steer)[] Ways =
    {
        (0, 0, 1, 0), (0, 0, -1, 0), (0, 0, 1, 1), (0, 0, 1, -1), (0, 0, -1, 1), (0, 0, -1, -1),
        (1, 45, 1, 0), (1, 45, -1, 0), (-1, 45, 1, 0), (-1, 45, -1, 0),
        (1, 90, 1, 0), (1, 90, -1, 0), (-1, 90, 1, 0), (-1, 90, -1, 0),
    };

    /// <summary>
    /// Whether a tank of <paramref name="spec"/> set down at <paramref name="p"/> facing <paramref name="yaw"/> gets out
    /// one of the <see cref="Ways"/>: <see cref="MoveNeeded"/> away, and with <paramref name="ontoClimbable"/> onto ground
    /// it may climb as well (<see cref="IsOut"/>).
    /// </summary>
    private static bool CanGetOut(Battlefield field, TankSpec spec, Vector2 p, float yaw, bool ontoClimbable)
    {
        foreach (var way in Ways)
        {
            var tank = new TankState { Id = 1, Spec = spec, Position = p, Yaw = yaw };
            Hold(ref tank, field, TestWorld.Cmd(steer: way.Turn), way.TurnTicks);
            InputCmd cmd = TestWorld.Cmd(way.Throttle, way.Steer);
            for (int tick = 0; tick < Ticks; tick++)
            {
                TankMotion.Drive(ref tank, cmd, field, ReadOnlySpan<Obb2>.Empty, null, knockDown: false);
                if (IsOut(field.Terrain, spec, p, tank.Position, ontoClimbable)) return true;
            }
        }
        return false;
    }

    private static void Hold(ref TankState tank, Battlefield field, InputCmd cmd, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++) TankMotion.Drive(ref tank, cmd, field, ReadOnlySpan<Obb2>.Empty, null, knockDown: false);
    }

    /// <summary><paramref name="at"/> is <see cref="MoveNeeded"/> from <paramref name="from"/>, and with <paramref name="ontoClimbable"/> on ground a tank of <paramref name="spec"/> may climb by either measure.</summary>
    private static bool IsOut(HeightField terrain, TankSpec spec, Vector2 from, Vector2 at, bool ontoClimbable) =>
        Vector2.Distance(from, at) >= MoveNeeded && (!ontoClimbable || !TankMotion.IsOnSteepGround(spec, terrain, at));

    /// <summary>A search for a way out holds each command this long, then brakes to a stop, ticks …</summary>
    private const int SearchHold = 24;

    /// <summary>… tries at most this many poses (the farthest from the start first) …</summary>
    private const int SearchPoses = 1000;

    /// <summary>… and counts poses this close as one, m and radians.</summary>
    private const float SearchCell = 0.5f;
    private static readonly float SearchTurn = Angles.Deg(10);

    /// <summary>
    /// Whether a tank of <paramref name="spec"/> set down at <paramref name="p"/> facing <paramref name="yaw"/> gets onto
    /// ground it may climb (<see cref="IsOut"/>) by some run of drive commands, as a player feeling a way out would: a
    /// best-first search over the eight throttle and steer commands, each held <see cref="SearchHold"/> ticks.
    /// </summary>
    private static bool SearchOut(Battlefield field, TankSpec spec, Vector2 p, float yaw)
    {
        var seen = new HashSet<(int, int, int)>();
        var queue = new PriorityQueue<TankState, float>();
        queue.Enqueue(new TankState { Id = 1, Spec = spec, Position = p, Yaw = yaw }, 0f);
        InputCmd stop = TestWorld.Cmd();
        for (int poses = 0; poses < SearchPoses && queue.TryDequeue(out TankState from, out _); poses++)
        for (sbyte throttle = -1; throttle <= 1; throttle++)
        for (sbyte steer = -1; steer <= 1; steer++)
        {
            if (throttle == 0 && steer == 0) continue;
            TankState tank = from;
            InputCmd cmd = TestWorld.Cmd(throttle, steer);
            for (int tick = 0; tick < SearchHold; tick++)
            {
                TankMotion.Drive(ref tank, cmd, field, ReadOnlySpan<Obb2>.Empty, null, knockDown: false);
                if (IsOut(field.Terrain, spec, p, tank.Position, ontoClimbable: true)) return true;
            }
            for (int tick = 0; tick < 2 * SimConstants.TickRate && tank.Speed != 0; tick++)
                TankMotion.Drive(ref tank, stop, field, ReadOnlySpan<Obb2>.Empty, null, knockDown: false);
            tank.Speed = 0f;
            if (IsOut(field.Terrain, spec, p, tank.Position, ontoClimbable: true)) return true;
            var key = ((int)MathF.Round(tank.Position.X / SearchCell), (int)MathF.Round(tank.Position.Y / SearchCell), (int)MathF.Round(Angles.Wrap(tank.Yaw) / SearchTurn));
            if (seen.Add(key)) queue.Enqueue(tank, -Vector2.Distance(tank.Position, p));
        }
        return false;
    }

    /// <summary>A trap: the hull fits at <paramref name="p"/> at one of eight headings at least, and from none it fits does it get out.</summary>
    private static bool IsTrap(Battlefield field, Vector2 p) => IsTrap(field, TankCatalog.Medium, p, pocket: false);

    /// <summary>
    /// A trap for a tank of <paramref name="spec"/>: the hull fits at <paramref name="p"/> at one of eight headings at
    /// least, and from none it fits does it get out: <see cref="MoveNeeded"/> away; for a <paramref name="pocket"/>, onto
    /// ground it may climb, one of the <see cref="Ways"/> or else by a search (<see cref="SearchOut"/>).
    /// </summary>
    private static bool IsTrap(Battlefield field, TankSpec spec, Vector2 p, bool pocket)
    {
        Span<float> fitting = stackalloc float[Headings];
        int n = 0;
        for (int k = 0; k < Headings; k++)
        {
            float yaw = k * MathF.Tau / Headings;
            if (InASolidProp(field, spec, p, yaw)) continue;
            if (CanGetOut(field, spec, p, yaw, ontoClimbable: pocket)) return false;
            fitting[n++] = yaw;
        }
        if (!pocket) return n > 0;
        foreach (float yaw in fitting[..n])
            if (SearchOut(field, spec, p, yaw)) return false;
        return n > 0;
    }

    private static float SlopeAt(HeightField terrain, Vector2 p) => MathF.Acos(System.Math.Clamp(terrain.NormalAt(p.X, p.Y).Y, -1f, 1f));

    /// <summary>
    /// The traps (<see cref="IsTrap(Battlefield, TankSpec, Vector2, bool)"/>) for a tank of <paramref name="spec"/>
    /// among the points of a <paramref name="step"/> m grid over the playable area whose ground is too steep to climb
    /// (elsewhere a tank's first, tiny step from rest stays on ground it may enter).
    /// </summary>
    private static List<Vector2> Traps(Battlefield field, float step, TankSpec? spec = null, bool pocket = false)
    {
        HeightField terrain = field.Terrain;
        float climb = TankCatalog.All.Min(s => s.MaxClimb);
        float lo = field.Border, hi = terrain.SizeX - field.Border;
        var steep = new List<Vector2>();
        for (float z = lo; z <= hi; z += step)
        for (float x = lo; x <= hi; x += step)
        {
            var p = new Vector2(x, z);
            if (SlopeAt(terrain, p) > climb) steep.Add(p);
        }
        var traps = new System.Collections.Concurrent.ConcurrentBag<Vector2>();
        Parallel.ForEach(steep, p =>
        {
            if (IsTrap(field, spec ?? TankCatalog.Medium, p, pocket)) traps.Add(p);
        });
        return traps.OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
    }

    /// <summary>The hull of a tank of <paramref name="spec"/> at <paramref name="p"/> facing <paramref name="yaw"/> overlaps a prop that holds a hull up (not a tree, bush or fence).</summary>
    private static bool InASolidProp(Battlefield field, TankSpec spec, Vector2 p, float yaw)
    {
        Obb2 hull = TankMotion.Footprint(spec, p, yaw);
        foreach (Prop prop in field.Props)
        {
            if (!prop.IsSolid || prop.Kind is PropKind.Tree or PropKind.Fence) continue;
            if (Vector2.DistanceSquared(prop.Footprint.Center, p) > 400f) continue;
            if (Obb2.TryGetPenetration(hull, prop.Footprint, out _)) return true;
        }
        return false;
    }

    [Fact]
    public void At_the_foot_of_a_cliff_the_hull_drives_away_forward_or_in_reverse()
    {
        Battlefield field = TestWorld.Field(Step(100, CliffGrade, 30));
        var foot = new Vector2(100, 100.5f);   // flat ground, half a metre from the cliff
        Assert.True(SlopeAt(field.Terrain, foot) > Angles.Deg(32), "the foot should read as steep as the cliff");

        TankState forward = TestWorld.Tank(foot.X, foot.Y, yaw: MathF.PI);   // facing +z, away from the cliff
        TestWorld.Drive(ref forward, field, TestWorld.Cmd(throttle: 1), 2 * SimConstants.TickRate);
        TankState reverse = TestWorld.Tank(foot.X, foot.Y);                     // facing the cliff
        TestWorld.Drive(ref reverse, field, TestWorld.Cmd(throttle: -1), 2 * SimConstants.TickRate);

        Assert.True(forward.Position.Y > 103, $"forward: z was {forward.Position.Y}");
        Assert.True(reverse.Position.Y > 102, $"reverse: z was {reverse.Position.Y}");
    }

    [Fact]
    public void On_the_lip_of_a_cliff_the_hull_drives_over_it()
    {
        Battlefield field = TestWorld.Field(Step(100, CliffGrade, 22));
        float lipZ = 100 - 22 / CliffGrade;   // the top's edge
        var lip = new Vector2(100, lipZ - 0.5f);
        Assert.True(SlopeAt(field.Terrain, lip) > Angles.Deg(32), "the lip should read as steep as the cliff");

        TankState tank = TestWorld.Tank(lip.X, lip.Y, yaw: MathF.PI);
        TestWorld.Drive(ref tank, field, TestWorld.Cmd(throttle: 1), 3 * SimConstants.TickRate);

        Assert.True(tank.Position.Y > 101, $"z was {tank.Position.Y}");
    }

    [Fact]
    public void Still_no_climbing_the_cliff_nor_driving_across_its_face()
    {
        Battlefield field = TestWorld.Field(Step(100, CliffGrade, 30));
        TankState up = TestWorld.Tank(100, 100.5f);                          // at the foot, facing the cliff
        TestWorld.Drive(ref up, field, TestWorld.Cmd(throttle: 1), 2 * SimConstants.TickRate);
        TankState across = TestWorld.Tank(100, 97, yaw: -MathF.PI / 2);     // on the face, facing +x
        TestWorld.Drive(ref across, field, TestWorld.Cmd(throttle: 1), 2 * SimConstants.TickRate);

        Assert.True(field.Terrain.HeightAt(up.Position.X, up.Position.Y) < 0.5f, $"climbed to {up.Position}");
        Assert.True(MathF.Abs(across.Position.X - 100) < 1f, $"drove across the face to {across.Position}");
    }

    [Fact]
    public void Along_the_foot_of_a_cliff_the_hull_drives_on_the_flat()
    {
        // Half a metre from the cliff the ground is flat, though the slope over a metre either side reads 35°.
        Battlefield field = TestWorld.Field(Step(100, CliffGrade, 30));
        TankState along = TestWorld.Tank(100, 100.5f, yaw: -MathF.PI / 2);
        TestWorld.Drive(ref along, field, TestWorld.Cmd(throttle: 1), 2 * SimConstants.TickRate);
        Assert.True(along.Position.X > 105 && MathF.Abs(along.Position.Y - 100.5f) < 0.01f, $"at {along.Position}");
    }

    [Fact]
    public void No_pose_across_a_cliff_is_a_trap()
    {
        Battlefield field = TestWorld.Field(Step(100, CliffGrade, 22));
        var traps = new List<float>();
        for (float z = 88; z <= 104; z += 0.25f)
            if (IsTrap(field, new Vector2(100, z))) traps.Add(z);
        Assert.True(traps.Count == 0, $"trapped at z = {string.Join(", ", traps)}");
    }

    /// <summary>A 40° slope falling towards +z, too steep to climb, with a 10 m rock across it at z = 110.</summary>
    private static Battlefield SlopeWithARockBelow() =>
        TestWorld.Field(TestWorld.RampNorth(0.84f), new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 110), new Vector2(5, 1), 0), -1, 4));

    [Fact]
    public void Driven_down_a_slope_into_a_rock_the_hull_turns_away_and_drives_on()
    {
        Battlefield field = SlopeWithARockBelow();
        TankState tank = TestWorld.Tank(100, 95, yaw: MathF.PI);   // facing +z, downhill
        TestWorld.Drive(ref tank, field, TestWorld.Cmd(throttle: 1), 3 * SimConstants.TickRate);
        Assert.True(tank.Position.Y > 105 && tank.Speed == 0, $"should rest against the rock: {tank.Position}, speed {tank.Speed}");

        TestWorld.Drive(ref tank, field, TestWorld.Cmd(steer: 1), SimConstants.TickRate);
        Assert.True(MathF.Abs(Angles.Wrap(tank.Yaw - MathF.PI)) > Angles.Deg(30), $"the hull didn't turn: yaw {tank.Yaw}");
        TestWorld.Drive(ref tank, field, TestWorld.Cmd(throttle: 1), 4 * SimConstants.TickRate);
        Assert.True(tank.Position.Y > 112, $"never got past the rock: {tank.Position}");
    }

    [Fact]
    public void A_rock_below_lets_the_hull_along_the_slope_and_up_it_only_while_in_reach()
    {
        Battlefield field = SlopeWithARockBelow();
        HeightField terrain = field.Terrain;
        float above = 109 - TankCatalog.Medium.HullWidth / 2 - 0.05f;   // the hull's downhill side just clear of the rock
        TankState along = TestWorld.Tank(102, above, yaw: MathF.PI / 2);                     // facing west, across the slope
        TestWorld.Drive(ref along, field, TestWorld.Cmd(throttle: 1), 3 * SimConstants.TickRate);
        TankState up = TestWorld.Tank(102, above, yaw: MathF.PI / 2 - Angles.Deg(45));      // west and 45° up the slope
        float before = terrain.HeightAt(up.Position.X, up.Position.Y);
        TestWorld.Drive(ref up, field, TestWorld.Cmd(throttle: 1), 5 * SimConstants.TickRate);

        Assert.True(along.Position.X < 94 && MathF.Abs(along.Position.Y - above) < 0.5f, $"along: at {along.Position}");
        // Up the slope no steeper than the climb limit, and only while the rock bars the way down: a couple of metres.
        Assert.True(terrain.HeightAt(up.Position.X, up.Position.Y) - before < 3f, $"up: climbed to {up.Position}");
        TestWorld.Drive(ref up, field, TestWorld.Cmd(throttle: 1), 5 * SimConstants.TickRate);
        Assert.True(terrain.HeightAt(up.Position.X, up.Position.Y) - before < 3f, $"up: kept climbing to {up.Position}");
    }

    [Fact]
    public void A_tank_pushed_into_the_corner_below_the_riverlands_south_ramp_drives_out()
    {
        // Where the south ramp's east side meets the plateau's foot; a bot sat here 88–92 s in an unarmed royale.
        Battlefield field = RiverlandFixture.Field;
        var corner = new Vector2(246.5f, 326.5f);
        Assert.False(RiverlandFixture.Grid.IsOpenAt(corner), "the bots' grid should have the corner closed");

        TankState forward = TestWorld.Tank(corner.X, corner.Y, yaw: -3 * MathF.PI / 4);   // facing +x +z, out of the corner
        TestWorld.Drive(ref forward, field, TestWorld.Cmd(throttle: 1), 3 * SimConstants.TickRate);
        TankState reverse = TestWorld.Tank(corner.X, corner.Y, yaw: MathF.PI / 4);         // facing into the corner
        TestWorld.Drive(ref reverse, field, TestWorld.Cmd(throttle: -1), 3 * SimConstants.TickRate);

        Assert.True(Vector2.Distance(forward.Position, corner) > 5 && SlopeAt(field.Terrain, forward.Position) < Angles.Deg(32), $"forward: at {forward.Position}");
        Assert.True(Vector2.Distance(reverse.Position, corner) > 3 && SlopeAt(field.Terrain, reverse.Position) < Angles.Deg(32), $"reverse: at {reverse.Position}");
    }

    [Theory]
    [InlineData("sandbox")]
    [InlineData("riverland")]
    public void No_steep_spot_on_a_map_is_a_trap(string map)
    {
        Battlefield field = map == "riverland" ? RiverlandFixture.Field : MapCatalog.Sandbox.NewBattlefield();
        List<Vector2> traps = Traps(field, 2f);
        Assert.True(traps.Count == 0, $"{traps.Count} traps: {string.Join(" ", traps.Take(20).Select(p => $"({p.X},{p.Y})"))}");
    }

    /// <summary>
    /// No steep spot is a pocket for any tank (stage 6A, the riverland's cliff-foot traps): the scan above asked only that
    /// the hull get a metre from where it stood, as a medium tank. In the pockets the bots of whole royales sat in (rocks
    /// lining the foot of a cliff, closer to it than a hull is wide) a tank shuffled a metre about the cliff face but
    /// every way off it was refused; wider hulls fit fewer gaps. Now every tank, from a spot it fits at, gets onto ground
    /// it may climb: straight away (<see cref="Ways"/>) or, as a player feeling a way out would, by a run of commands.
    /// </summary>
    [Theory]
    [InlineData("riverland", "light")]
    [InlineData("riverland", "medium")]
    [InlineData("riverland", "heavy")]
    [InlineData("riverland", "td")]
    [InlineData("sandbox", "light")]
    [InlineData("sandbox", "medium")]
    [InlineData("sandbox", "heavy")]
    [InlineData("sandbox", "td")]
    public void No_steep_spot_on_a_map_is_a_pocket_for_any_tank(string map, string tank)
    {
        Battlefield field = map == "riverland" ? RiverlandFixture.Field : MapCatalog.Sandbox.NewBattlefield();
        List<Vector2> traps = Traps(field, 2f, TankCatalog.ById(tank), pocket: true);
        Assert.True(traps.Count == 0, $"{traps.Count} pockets: {string.Join(" ", traps.Take(20).Select(p => $"({p.X},{p.Y})"))}");
    }

    /// <summary>
    /// A 70° cliff (foot at z = 100, top 22 m up at z = 92) with a rock along its foot whose near side lies 1.6 m out from
    /// it: narrower than any hull, so a hull on the face can't get down, but further below a hull halfway up than the
    /// metre <see cref="SlopeWithARockBelow"/> looks.
    /// </summary>
    private static Battlefield CliffWithARockAlongItsFoot(float rockNearSide) =>
        TestWorld.Field(Step(100, CliffGrade, 22), new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, rockNearSide + 0.6f), new Vector2(40, 0.6f), 0), -1, 4));

    [Fact]
    public void A_rock_along_a_cliffs_foot_lets_a_hull_on_the_face_along_it_however_far_below()
    {
        Battlefield field = CliffWithARockAlongItsFoot(101.6f);
        var start = new Vector2(100, 97.5f);   // the hull's downhill side 2.5 m above the rock
        TankState along = TestWorld.Tank(start.X, start.Y, yaw: -MathF.PI / 2);   // facing east, across the face
        Hold(ref along, field, TestWorld.Cmd(throttle: 1), 3 * SimConstants.TickRate);
        Assert.True(along.Position.X > 110 && MathF.Abs(along.Position.Y - start.Y) < 0.5f, $"at {along.Position}");
    }

    [Fact]
    public void A_rock_along_a_cliffs_foot_still_lets_no_hull_climb_the_face()
    {
        Battlefield field = CliffWithARockAlongItsFoot(101.6f);
        HeightField terrain = field.Terrain;
        TankState up = TestWorld.Tank(100, 97.5f, yaw: -MathF.PI / 2 + Angles.Deg(20));   // east and 20° up the face
        float before = terrain.HeightAt(up.Position.X, up.Position.Y);
        Hold(ref up, field, TestWorld.Cmd(throttle: 1), 5 * SimConstants.TickRate);
        Assert.True(terrain.HeightAt(up.Position.X, up.Position.Y) - before < 0.5f, $"climbed to {up.Position}");
    }

    /// <summary>
    /// The same cliff, the rock along its foot a metre below a hull sliding along the face (as the climb rule's own probe
    /// lets it: <see cref="SlopeWithARockBelow"/>), and a rock set corner-up at x = 110 leaning into its way: the hull
    /// meets its slanting face, which pushes it back and up the slope, and that push made every step a climb. It slides
    /// up and over it, as along any rock.
    /// </summary>
    [Fact]
    public void Sliding_along_a_cliff_face_a_hull_gets_past_a_rock_leaning_into_its_way()
    {
        float rockTop = 100.7f;
        Prop[] rocks =
        {
            new(1, PropKind.Rock, new Obb2(new Vector2(100, rockTop + 0.5f), new Vector2(40, 0.5f), 0), -1, 4),
            new(2, PropKind.Rock, new Obb2(new Vector2(110, rockTop + 0.4f), new Vector2(1.2f, 1.2f), MathF.PI / 4), -1, 4),
        };
        Battlefield field = TestWorld.Field(Step(100, CliffGrade, 22), rocks);
        TankState tank = TestWorld.Tank(100, rockTop - TankCatalog.Medium.HullWidth / 2 - 0.4f, yaw: -MathF.PI / 2);
        Hold(ref tank, field, TestWorld.Cmd(throttle: 1), 5 * SimConstants.TickRate);
        Assert.True(tank.Position.X > 116, $"stuck at {tank.Position}");
    }

    /// <summary>
    /// A hull whose centre has slid half a metre over a cliff's lip (onto the face, its uphill side on the top) drives on
    /// along the lip, as it drove there. Every step but straight down the cliff was refused: a bot driving up the
    /// riverland's south ramp close to its edge was left to go down the cliff or nowhere.
    /// </summary>
    [Fact]
    public void A_hull_slid_over_a_cliffs_lip_drives_on_along_it()
    {
        Battlefield field = TestWorld.Field(Step(100, CliffGrade, 22));
        float lipZ = 100 - 22 / CliffGrade;
        var over = new Vector2(100, lipZ + 0.6f);
        Assert.True(TankMotion.IsOnSteepGround(TankCatalog.Medium, field.Terrain, over), "the centre should be on the face");

        TankState along = TestWorld.Tank(over.X, over.Y, yaw: -MathF.PI / 2);   // facing east, along the lip
        Hold(ref along, field, TestWorld.Cmd(throttle: 1), 2 * SimConstants.TickRate);

        Assert.True(along.Position.X > 105 && MathF.Abs(along.Position.Y - over.Y) < 0.5f, $"at {along.Position}");
    }

    /// <summary>
    /// A step as high as one 2 m cell of the ground (5.5 m at 70°) still can't be climbed, by any tank at any heading up it:
    /// from its foot the top lies further up than the half of a hull's width that rests a hull slid over a lip on it.
    /// </summary>
    [Fact]
    public void A_low_cliff_still_cannot_be_climbed()
    {
        Battlefield field = TestWorld.Field(Step(100, CliffGrade, 2 * CliffGrade));
        foreach (TankSpec spec in TankCatalog.All)
        for (int k = -2; k <= 2; k++)
        {
            var tank = new TankState { Id = 1, Spec = spec, Position = new Vector2(100, 100 + spec.HullLength / 2), Yaw = k * Angles.Deg(20) };
            Hold(ref tank, field, TestWorld.Cmd(throttle: 1), 3 * SimConstants.TickRate);
            Assert.True(field.Terrain.HeightAt(tank.Position.X, tank.Position.Y) < 0.5f, $"{spec.Id} heading {k * 20}°: climbed to {tank.Position}");
        }
    }

    /// <summary>
    /// A hull wedged between two rocks at its corners (front right, back left), so that it can't turn right: steered
    /// right as it drives, it drives straight on. It went nowhere at all, holding a turn it couldn't make: a bot between
    /// two rocks below the riverland plateau's east cliff, steering for its path, sat there for minutes.
    /// </summary>
    [Fact]
    public void A_hull_wedged_so_it_cannot_turn_still_drives_on_while_steered()
    {
        TankSpec spec = TankCatalog.Medium;
        float w = spec.HullWidth / 2, l = spec.HullLength / 2;
        var at = new Vector2(100, 100);
        Battlefield field = TestWorld.FlatField(
            new Prop(1, PropKind.Rock, new Obb2(at + new Vector2(w + 1.1f, -l + 1f), new Vector2(1f, 1f), 0f), -1f, 4f),
            new Prop(2, PropKind.Rock, new Obb2(at + new Vector2(-w - 1.1f, l - 1f), new Vector2(1f, 1f), 0f), -1f, 4f));
        TankState turning = TestWorld.Tank(at.X, at.Y);   // facing north
        Hold(ref turning, field, TestWorld.Cmd(steer: 1), SimConstants.TickRate);
        Assert.True(MathF.Abs(Angles.Wrap(turning.Yaw)) < Angles.Deg(10), $"turned to {turning.Yaw * 180 / MathF.PI:F0}°: the rocks should hold the turn");

        TankState driving = TestWorld.Tank(at.X, at.Y);
        Hold(ref driving, field, TestWorld.Cmd(throttle: 1, steer: 1), SimConstants.TickRate);
        Assert.True(at.Y - driving.Position.Y > 2, $"at {driving.Position}");
    }

    /// <summary>
    /// Where bots sat in whole royales (stage 6A batch C follow-up, seeds 1–20) no tank got out straight away: a heavy on
    /// the plateau's east cliff between two rocks (415, 239), a light in the corner of its north and east cliffs above a
    /// rock (417, 230). Each drives out now, straight or turning first (<see cref="Ways"/>), onto ground it may climb.
    /// </summary>
    [Theory]
    [InlineData("heavy", 415.05f, 239.33f, 1.281f)]
    [InlineData("light", 417.00f, 230.34f, -0.883f)]
    public void A_tank_in_a_pocket_on_a_riverland_cliff_face_drives_out(string tank, float x, float z, float yaw)
    {
        Battlefield field = RiverlandFixture.Field;
        var p = new Vector2(x, z);
        Assert.True(TankMotion.IsOnSteepGround(TankCatalog.ById(tank), field.Terrain, p), "the pose should be on a cliff face");
        Assert.True(CanGetOut(field, TankCatalog.ById(tank), p, yaw, ontoClimbable: true));
    }

    /// <summary>
    /// A heavy halfway up the riverland's north cliff, above the rocks lining its foot (396, 81): by no run of commands
    /// did it get off the face (every step across it was refused, its way down shut more than a metre below, and the
    /// rocks it slid along pushed it up the slope). Now it gets off.
    /// </summary>
    [Fact]
    public void A_heavy_on_the_riverland_north_cliff_above_its_rock_lined_foot_gets_off_the_face()
    {
        Battlefield field = RiverlandFixture.Field;
        var p = new Vector2(396, 81);
        Assert.False(InASolidProp(field, TankCatalog.Heavy, p, MathF.PI / 2));
        Assert.True(SearchOut(field, TankCatalog.Heavy, p, MathF.PI / 2));
    }

    /// <summary>
    /// A heavy whose centre slid over the south ramp's east lip, by the corner below the plateau, facing up the ramp (a
    /// bot sat there 17 s in an unarmed royale, every step it drove refused): it drives on up the ramp, along the lip (till
    /// the lip bends under its tail and the hull, nose up as steep as it can climb, stops).
    /// </summary>
    [Fact]
    public void A_heavy_slid_over_the_riverland_south_ramps_lip_drives_on_up_the_ramp()
    {
        Battlefield field = RiverlandFixture.Field;
        TankState tank = new() { Id = 1, Spec = TankCatalog.Heavy, Position = new Vector2(241.1f, 326f), Yaw = 0f };
        Assert.True(TankMotion.IsOnSteepGround(tank.Spec, field.Terrain, tank.Position), "the centre should be over the lip");
        Hold(ref tank, field, TestWorld.Cmd(throttle: 1), 2 * SimConstants.TickRate);
        Assert.True(tank.Position.Y < 325 && field.Terrain.HeightAt(tank.Position.X, tank.Position.Y) > 20, $"at {tank.Position}");
    }
}

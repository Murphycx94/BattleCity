using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// The push-out's second look (<see cref="TankMotion"/>'s <c>Resolve</c>): a hull pushed more than its slack (4 m) in one
/// round looks again round where it now is and carries on after the blocker that pushed it, so it meets the blockers in
/// the same order, at the same places, as a scan of them all. Hulls dropped inside a 12 × 7 m house and inside a heap of
/// big overlapping rocks are pushed 5–12 m at a time; each drive must come out the same to the bit with the index on and
/// off, knocking down or not.
/// </summary>
public sealed class ResolveRequeryTests
{
    private static readonly Vector2 Middle = new(100, 100);
    private static readonly float[] Yaws = { 0f, 0.7f, 1.9f, 3f, -2.3f };
    private static readonly InputCmd[] Inputs = { TestWorld.Cmd(throttle: 1), TestWorld.Cmd(throttle: -1, steer: 1), TestWorld.Cmd(steer: -1) };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_hull_inside_a_house_comes_out_the_same_with_and_without_the_index(bool knockDown)
    {
        // The house across the middle, its ridge along x (faces at z = ±3.5, ends at x = ±6). Beyond it, after it in prop
        // order, rows of thin upright rocks pointing at it, 9–13 m out across and 14–15 m out along the ridge: where a hull
        // pushed out of the house lands, yet too far from where it started for the first look (its reach + 4 m) to see
        // them. Only the second look finds them, as a scan of them all does. Two rocks and a tree come before the house.
        var props = new List<Prop>
        {
            Rock(1, Middle + new Vector2(-16, 12), new Vector2(3, 2), 0.4f),
            Rock(2, Middle + new Vector2(15, -12), new Vector2(2.5f, 3), 1.1f),
            TreeTests.Tree(3, Middle + new Vector2(0, 6)),
            HouseTests.House(4, Middle, yaw: 0f),
        };
        foreach (float z in new[] { -12.5f, -10.5f, -9f, 9f, 10.5f, 12.5f })
        for (float x = -7f; x <= 7f; x += 3.5f)
            props.Add(Rock(props.Count + 1, Middle + new Vector2(x + z * 0.1f, z), new Vector2(0.4f, 1.2f), 0.05f * z));
        foreach (float x in new[] { -15f, -14f, 14f, 15f })
        for (float z = -2f; z <= 2f; z += 2f)
            props.Add(Rock(props.Count + 1, Middle + new Vector2(x, z), new Vector2(1.2f, 0.4f), 0.05f * x));
        props.Add(TreeTests.Tree(props.Count + 1, Middle + new Vector2(5, -6.5f)));
        // Light and tank destroyer can't ram a house, the others are too slow from rest: knocking down, it holds the hull
        // and pushes it out. Without knocking down (a client's prediction) a house the hull starts in is one it broke
        // into, so it gives way and nothing pushes: the drives must still agree.
        int pushedFar = DriveEverywhere(TestWorld.FlatField(props.ToArray()), new Vector2(5.5f, 3f), step: 0.5f, knockDown);
        if (knockDown) Assert.True(pushedFar > 1000, $"only {pushedFar} drives pushed the hull more than 5 m");
        else Assert.Equal(0, pushedFar);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_hull_in_a_heap_of_big_rocks_comes_out_the_same_with_and_without_the_index(bool knockDown)
    {
        // 30 rocks 8–20 m across, overlapping, in a 50 m heap round the middle, in no particular order, and a few
        // fences and trees among them (what knocking down changes).
        var rng = new XorShift(41);
        var props = new List<Prop>();
        for (int k = 0; k < 30; k++)
        {
            var at = Middle + new Vector2(Between(ref rng, -25, 25), Between(ref rng, -25, 25));
            props.Add(Rock(props.Count + 1, at, new Vector2(Between(ref rng, 4, 10), Between(ref rng, 4, 10)), Between(ref rng, 0, Angles.TwoPi)));
        }
        for (int k = 0; k < 6; k++)
        {
            var at = Middle + new Vector2(Between(ref rng, -30, 30), Between(ref rng, -30, 30));
            props.Add(TreeTests.Tree(props.Count + 1, at));
            var line = new WallLine(PropKind.Fence, at + new Vector2(2, 0), at + new Vector2(2, 2 * WallLine.SegmentLength));
            props.AddRange(line.Segments(TestWorld.Flat(), props.Count + 1));
        }
        int pushedFar = DriveEverywhere(TestWorld.FlatField(props.ToArray()), new Vector2(30, 30), step: 2f, knockDown);
        Assert.True(pushedFar > 200, $"only {pushedFar} drives pushed the hull more than 5 m");
    }

    /// <summary>
    /// Drops every kind of tank at every point of a grid of <paramref name="step"/> over ±<paramref name="half"/> round
    /// the middle, at each of <see cref="Yaws"/>, and drives it one tick with each of <see cref="Inputs"/> on two fresh
    /// copies of <paramref name="field"/>, one with the index and one without; the tanks must come out equal to the bit.
    /// </summary>
    /// <returns>How many drives moved the hull more than 5 m: pushed out of something, past the slack.</returns>
    private static int DriveEverywhere(Battlefield field, Vector2 half, float step, bool knockDown)
    {
        int pushedFar = 0;
        var felledA = new List<int>();
        var felledB = new List<int>();
        for (float dx = -half.X; dx <= half.X; dx += step)
        for (float dz = -half.Y; dz <= half.Y; dz += step)
        foreach (float yaw in Yaws)
        foreach (TankSpec spec in TankCatalog.All)
        foreach (InputCmd input in Inputs)
        {
            Battlefield indexed = field.Fresh(), scanned = field.Fresh();
            scanned.UseIndex = false;
            var start = new TankState { Id = 1, Spec = spec, Position = Middle + new Vector2(dx, dz), Yaw = yaw };
            TankState a = start, b = start;
            felledA.Clear();
            felledB.Clear();
            TankMotion.Drive(ref a, input, indexed, ReadOnlySpan<Obb2>.Empty, felledA, knockDown);
            TankMotion.Drive(ref b, input, scanned, ReadOnlySpan<Obb2>.Empty, felledB, knockDown);

            string where = $"{spec.Name} at {start.Position}, yaw {yaw}, input {input.Throttle}/{input.Steer}";
            Assert.True(a.Equals(b), $"{where}: the tanks differ ({a.Position} vs {b.Position})");
            Assert.Equal(
                (Bits(b.Position.X), Bits(b.Position.Y), Bits(b.Yaw), Bits(b.Speed)),
                (Bits(a.Position.X), Bits(a.Position.Y), Bits(a.Yaw), Bits(a.Speed)));
            Assert.Equal(felledB, felledA);
            if (Vector2.Distance(a.Position, start.Position) > 5f) pushedFar++;
        }
        return pushedFar;
    }

    private static Prop Rock(int id, Vector2 at, Vector2 half, float yaw) => new(id, PropKind.Rock, new Obb2(at, half, yaw), -0.3f, 3f);

    private static uint Bits(float v) => BitConverter.SingleToUInt32Bits(v);

    private static float Between(ref XorShift rng, float min, float max) => min + rng.NextFloat() * (max - min);
}

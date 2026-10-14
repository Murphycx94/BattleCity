using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class BeltPathTests
{
    /// <summary>Medium tank running gear in (z, y): idler at the back, sprocket at the front, five road wheels.</summary>
    internal static readonly Circle2[] RunningGear =
    {
        new(new Vector2(2.95f, 0.80f), 0.33f),
        new(new Vector2(-2.95f, 0.85f), 0.36f),
        new(new Vector2(-2.1f, 0.42f), 0.39f),
        new(new Vector2(-1.05f, 0.42f), 0.39f),
        new(new Vector2(0f, 0.42f), 0.39f),
        new(new Vector2(1.05f, 0.42f), 0.39f),
        new(new Vector2(2.1f, 0.42f), 0.39f),
    };

    [Fact]
    public void Two_equal_wheels_make_a_stadium()
    {
        var belt = new BeltPath(new[] { new Circle2(Vector2.Zero, 1), new Circle2(new Vector2(4, 0), 1) });
        Assert.Equal(8 + 2 * MathF.PI, belt.Length, 4);
        var (start, startDir) = belt.Sample(0);
        MeshAssert.Near(new Vector3(0, -1, 0), new Vector3(start, 0));
        MeshAssert.Near(new Vector3(1, 0, 0), new Vector3(startDir, 0));
        var (top, topDir) = belt.Sample(4 + MathF.PI);
        MeshAssert.Near(new Vector3(4, 1, 0), new Vector3(top, 0));
        MeshAssert.Near(new Vector3(-1, 0, 0), new Vector3(topDir, 0));
    }

    [Fact]
    public void Distance_wraps_both_ways()
    {
        var belt = new BeltPath(new[] { new Circle2(Vector2.Zero, 1), new Circle2(new Vector2(4, 0), 1) });
        MeshAssert.Near(new Vector3(belt.Sample(1).Position, 0), new Vector3(belt.Sample(1 + belt.Length).Position, 0));
        MeshAssert.Near(new Vector3(belt.Sample(1).Position, 0), new Vector3(belt.Sample(1 - 2 * belt.Length).Position, 0));
    }

    [Fact]
    public void Unequal_wheels_match_the_open_belt_formula()
    {
        float ra = 1, rb = 2, d = 5;
        var belt = new BeltPath(new[] { new Circle2(Vector2.Zero, ra), new Circle2(new Vector2(d, 0), rb) });
        float expected = 2 * MathF.Sqrt(d * d - (rb - ra) * (rb - ra)) + MathF.PI * (ra + rb) + 2 * (rb - ra) * MathF.Asin((rb - ra) / d);
        Assert.Equal(expected, belt.Length, 3);
    }

    [Fact]
    public void Track_hugs_but_never_cuts_into_any_wheel()
    {
        var belt = new BeltPath(RunningGear);
        for (int i = 0; i < 400; i++)
        {
            Vector2 p = belt.Sample(belt.Length * i / 400f).Position;
            float clearance = RunningGear.Min(c => Vector2.Distance(p, c.Center) - c.Radius);
            Assert.True(clearance > -1e-4f, $"sample {i} at {p} is inside a wheel");
        }
        float lowest = Enumerable.Range(0, 400).Min(i => belt.Sample(belt.Length * i / 400f).Position.Y);
        Assert.Equal(0.03f, lowest, 4); // link centre line: half a 6 cm link above the ground
        Assert.InRange(belt.Length, 13.5f, 15.5f);
    }

    [Fact]
    public void Direction_is_unit_and_turns_smoothly()
    {
        var belt = new BeltPath(RunningGear);
        Vector2 previous = belt.Sample(0).Tangent;
        for (int i = 1; i <= 1000; i++)
        {
            Vector2 tangent = belt.Sample(belt.Length * i / 1000f).Tangent;
            Assert.Equal(1f, tangent.Length(), 4);
            Assert.True(Vector2.Dot(previous, tangent) > 0.9f, $"kink at sample {i}");
            previous = tangent;
        }
    }

    [Fact]
    public void Nested_wheels_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new BeltPath(new[] { new Circle2(Vector2.Zero, 2), new Circle2(new Vector2(0.5f, 0), 1) }));
    }

    private static Circle2[] Gear(float middleLift)
    {
        var g = (Circle2[])RunningGear.Clone();
        g[4] = g[4] with { Center = g[4].Center + new Vector2(0, middleLift) };
        return g;
    }

    [Fact]
    public void Tiny_hull_violations_do_not_add_a_wrap()
    {
        float baseline = new BeltPath(RunningGear).Length;
        Assert.Equal(baseline, new BeltPath(Gear(0.0001f)).Length, 3);
    }

    [Fact]
    public void Wheel_slightly_outside_the_hull_adds_no_wrap()
    {
        float baseline = new BeltPath(RunningGear).Length;
        Assert.InRange(new BeltPath(Gear(-0.01f)).Length, baseline - 0.1f, baseline + 0.1f);
    }

    [Fact]
    public void Wheel_clearly_inside_the_hull_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new BeltPath(Gear(0.01f)));
    }

    [Fact]
    public void Clockwise_input_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new BeltPath(RunningGear.Reverse().ToArray()));
    }

    [Fact]
    public void Non_positive_radius_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new BeltPath(new[] { new Circle2(Vector2.Zero, -1), new Circle2(new Vector2(4, 0), 1) }));
        Assert.Throws<ArgumentException>(() => new BeltPath(new[] { new Circle2(Vector2.Zero, 0), new Circle2(new Vector2(4, 0), 1) }));
    }
}

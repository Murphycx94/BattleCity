using System.Numerics;
using Tank.Client.World;
using Tank.Sim.Modes;
using Tank.Testing;

namespace Tank.Art.Tests;

public class ZoneWallShapeTests
{
    private static float Flat(float x, float z) => 7f;
    private static float Slope(float x, float z) => x;

    [Fact]
    public void The_constants_are_the_design_s()
    {
        Assert.Equal(256, ZoneWallShape.Segments);
        Assert.Equal(60f, ZoneWallShape.Height);
        Assert.Equal(3f, ZoneWallShape.Sink);
        Assert.Equal(1f, ZoneWallShape.MinRadius);
    }

    [Fact]
    public void Flat_ground_gives_the_same_height_at_every_point()
    {
        var heights = new float[ZoneWallShape.Segments];

        ZoneWallShape.SampleBase(Flat, new ZoneRing(new Vector2(300, 400), 50f), heights);

        Assert.All(heights, h => Assert.Equal(7f, h));
    }

    [Fact]
    public void Points_go_round_from_plus_x_towards_plus_z()
    {
        // Ground height = x: the point at angle 0 is at x = 110, at π/2 x = 100 and at π x = 90.
        var heights = new float[ZoneWallShape.Segments];

        ZoneWallShape.SampleBase(Slope, new ZoneRing(new Vector2(100, 0), 10f), heights);

        Assert.Equal(110f, heights[0], 3);
        Assert.Equal(100f, heights[ZoneWallShape.Segments / 4], 3);
        Assert.Equal(90f, heights[ZoneWallShape.Segments / 2], 3);
        Assert.Equal(100f, heights[3 * ZoneWallShape.Segments / 4], 3);
    }

    [Fact]
    public void The_sampled_point_lies_on_the_circle()
    {
        var centre = new Vector2(250, 125);
        const float radius = 35f;
        var heights = new float[ZoneWallShape.Segments];
        float Distance(float x, float z) => Vector2.Distance(new Vector2(x, z), centre);

        ZoneWallShape.SampleBase(Distance, new ZoneRing(centre, radius), heights);

        Assert.All(heights, h => Assert.Equal(radius, h, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(257)]
    public void A_span_of_the_wrong_length_is_refused(int length)
    {
        var heights = new float[length];
        Assert.Throws<ArgumentException>(() => ZoneWallShape.SampleBase(Flat, new ZoneRing(Vector2.Zero, 10f), heights));
    }

    [Fact]
    public void Sampling_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var heights = new float[ZoneWallShape.Segments];
            var ring = new ZoneRing(new Vector2(600, 600), 420f);
            Func<float, float, float> groundAt = Slope;
            ZoneWallShape.SampleBase(groundAt, ring, heights);   // warm-up
            Allocation.Window window = Allocation.Start();
            for (int i = 0; i < 10; i++) ZoneWallShape.SampleBase(groundAt, ring, heights);
            Allocation.Measured measured = window.Stop();
            Assert.Equal(600f + 420f, heights[0], 3);
            return measured;
        });
    }
}

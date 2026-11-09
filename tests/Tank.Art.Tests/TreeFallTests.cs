using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class TreeFallTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void The_fall_starts_slow_and_rests_after_1_2_seconds()
    {
        Assert.Equal(0f, TreeFall.Angle(0f));
        Assert.Equal(0f, TreeFall.Angle(-1f));
        Assert.True(TreeFall.Angle(0.3f) < 0.1f * TreeFall.RestAngle, "slow at first, like a tree starting to go");
        Assert.Equal(0.25f * TreeFall.RestAngle, TreeFall.Angle(0.6f), Tolerance);
        Assert.Equal(TreeFall.RestAngle, TreeFall.Angle(1.2f), Tolerance);
        Assert.Equal(TreeFall.RestAngle, TreeFall.Angle(5f), Tolerance);
    }

    [Fact]
    public void The_fall_never_swings_back()
    {
        float previous = TreeFall.Angle(0f);
        for (int step = 1; step <= 150; step++)
        {
            float angle = TreeFall.Angle(step * 0.01f);
            Assert.True(angle >= previous, $"at {step * 0.01f} s the angle went back from {previous} to {angle}");
            previous = angle;
        }
    }

    [Fact]
    public void A_tree_falls_the_way_it_was_pushed()
    {
        var push = new Vector2(0.6f, 0.8f);
        Vector3 axis = TreeFall.Axis(push, 17);
        Assert.Equal(0f, axis.Y, Tolerance);
        Assert.Equal(0f, axis.X * push.X + axis.Z * push.Y, Tolerance);
        Assert.Equal(1f, axis.Length(), Tolerance);
        Vector3 top = Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(axis, MathF.PI / 2));
        Assert.Equal(0.6f, top.X, Tolerance);
        Assert.Equal(0f, top.Y, Tolerance);
        Assert.Equal(0.8f, top.Z, Tolerance);
    }

    /// <summary>
    /// End to end, as ForestViews lays a tree down: its upright transform (turned by its yaw, scaled to its prop, rooted on
    /// the ground) turned by <see cref="TreeFall.Pose"/>; the crown comes to rest along the push, and the crown check
    /// looks for it along <see cref="TreeFall.Along"/>, the same way.
    /// </summary>
    [Theory]
    [InlineData(0.6f, 0.8f)]
    [InlineData(-1f, 0.2f)]
    [InlineData(0f, -3f)]
    [InlineData(2f, 0f)]
    public void A_felled_tree_lies_with_its_crown_along_the_push(float pushX, float pushZ)
    {
        var push = new Vector2(pushX, pushZ);
        Vector2 way = Vector2.Normalize(push);
        Vector3 axis = TreeFall.Axis(push, 17);
        Assert.Equal(way.X, TreeFall.Along(axis).X, Tolerance);
        Assert.Equal(way.Y, TreeFall.Along(axis).Y, Tolerance);

        var root = new Vector3(100f, 5f, 200f);
        Matrix4x4 upright = Matrix4x4.CreateScale(1.3f, 1.6f, 1.3f) * Matrix4x4.CreateRotationY(2.1f) * Matrix4x4.CreateTranslation(root);
        Matrix4x4 lying = TreeFall.Pose(upright, axis, TreeFall.Seconds);
        Vector3 top = Vector3.Transform(new Vector3(0f, 8f, 0f), lying) - root;   // the mesh's top, 8 m up at scale 1
        float height = 8f * 1.6f;
        var level = new Vector2(top.X, top.Z);
        Assert.Equal(height * MathF.Sin(TreeFall.RestAngle), level.Length(), 1e-3f);
        Assert.Equal(1f, Vector2.Dot(Vector2.Normalize(level), way), 1e-4f);
        Assert.Equal(height * MathF.Cos(TreeFall.RestAngle), top.Y, 1e-3f);
        Assert.Equal(Vector3.Zero, Vector3.Transform(Vector3.Zero, lying) - root);   // it turns about its root

        Matrix4x4 standing = TreeFall.Pose(upright, axis, 0f);
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 4; column++)
            Assert.Equal(upright[row, column], standing[row, column], Tolerance);
    }

    [Fact]
    public void An_unpushed_tree_still_falls_somewhere_fixed()
    {
        var directions = new HashSet<(int, int)>();
        for (int id = 1; id <= 20; id++)
        {
            Vector3 axis = TreeFall.Axis(Vector2.Zero, id);
            Assert.Equal(axis, TreeFall.Axis(Vector2.Zero, id));
            Assert.Equal(1f, axis.Length(), Tolerance);
            Assert.Equal(0f, axis.Y, Tolerance);
            directions.Add(((int)MathF.Round(axis.X * 10), (int)MathF.Round(axis.Z * 10)));
        }
        Assert.True(directions.Count >= 10, $"only {directions.Count} directions for 20 trees");
    }
}

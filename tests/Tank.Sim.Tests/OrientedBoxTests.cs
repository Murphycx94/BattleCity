using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Tests;

public sealed class OrientedBoxTests
{
    // 2 wide, 1 tall, 4 long, centred at (10, 5, 10).
    private static readonly OrientedBox Box = new(new Vector3(10, 5, 10), new Vector3(1, 0.5f, 2), Quaternion.Identity);

    [Theory]
    [InlineData(10, 5, 0, BoxFace.NegZ, 0.4f)]     // from −Z: the box face at z = 8 is 8 of 20 m in
    [InlineData(10, 5, 20, BoxFace.PosZ, 0.4f)]
    [InlineData(0, 5, 10, BoxFace.NegX, 0.45f)]
    [InlineData(20, 5, 10, BoxFace.PosX, 0.45f)]
    [InlineData(10, 15, 10, BoxFace.PosY, 0.475f)]
    [InlineData(10, -5, 10, BoxFace.NegY, 0.475f)]
    public void Raycast_ReportsTheEntryFace(float x, float y, float z, BoxFace expected, float expectedT)
    {
        Vector3 from = new(x, y, z);
        Vector3 to = Box.Center * 2 - from;   // straight through the centre and out the far side
        Assert.True(Box.Raycast(from, to, out float t, out BoxFace face));
        Assert.Equal(expected, face);
        Assert.Equal(expectedT, t, 1e-5f);
    }

    [Fact]
    public void Raycast_MissesAndStopsShort()
    {
        Assert.False(Box.Raycast(new Vector3(0, 5, 0), new Vector3(0, 5, 20), out _, out _));   // passes beside it
        Assert.False(Box.Raycast(new Vector3(10, 5, 0), new Vector3(10, 5, 7.9f), out _, out _)); // ends before the face
    }

    [Fact]
    public void Raycast_FromInside_ReportsTheFaceItWouldHaveComeThrough()
    {
        // Starting near the +X face but flying +X: it came in through the −X face, whose normal opposes its flight.
        Assert.True(Box.Raycast(new Vector3(10.8f, 5, 10), new Vector3(30, 5, 11), out float t, out BoxFace face));
        Assert.Equal(0f, t);
        Assert.Equal(BoxFace.NegX, face);
        Assert.True(Vector3.Dot(Box.Normal(face), new Vector3(19.2f, 0, 1)) < 0, "the face must oppose the flight");
    }

    [Fact]
    public void Raycast_ParallelToAFace_HitsOnlyWithinItsSlab()
    {
        Assert.True(Box.Raycast(new Vector3(0, 5.4f, 10), new Vector3(20, 5.4f, 10), out _, out BoxFace face));   // just under the top
        Assert.Equal(BoxFace.NegX, face);
        Assert.False(Box.Raycast(new Vector3(0, 5.6f, 10), new Vector3(20, 5.6f, 10), out _, out _));              // just over it
    }

    [Fact]
    public void Raycast_ThroughAnEdge_PicksTheLaterEntryPlane()
    {
        // Diagonal through the (−X, −Z) vertical edge region: it enters the X slab at t = 0.45 and the Z slab at 0.4,
        // so the entry is through the −X face at 0.45.
        Assert.True(Box.Raycast(new Vector3(0, 5, 0), new Vector3(20, 5, 20), out float t, out BoxFace face));
        Assert.Equal(BoxFace.NegX, face);
        Assert.Equal(0.45f, t, 1e-5f);
    }

    [Fact]
    public void Raycast_UsesTheBoxRotation()
    {
        // Turned 90° about Y: the box's −Z (front) face now looks along world −X.
        var turned = Box with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) };
        Assert.True(turned.Raycast(new Vector3(0, 5, 10), new Vector3(20, 5, 10), out float t, out BoxFace face));
        Assert.Equal(BoxFace.NegZ, face);
        Assert.Equal(0.4f, t, 1e-5f);
        Vector3 normal = turned.Normal(face);
        Assert.Equal(-1f, normal.X, 1e-5f);
    }

    [Fact]
    public void DistanceTo_IsZeroInside_AndMeasuresToTheNearestPoint()
    {
        Assert.Equal(0f, Box.DistanceTo(new Vector3(10.5f, 5, 11)));
        Assert.Equal(3f, Box.DistanceTo(new Vector3(14, 5, 10)), 1e-5f);
        Assert.Equal(5f, Box.DistanceTo(new Vector3(14, 9.5f, 10)), 1e-5f);   // 3 along X, 4 along Y
    }
}

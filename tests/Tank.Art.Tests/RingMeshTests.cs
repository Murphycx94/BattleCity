using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class RingMeshTests
{
    private static readonly Vector2 Centre = new(10, 20);
    private const float Inner = 3f, Outer = 5f, Middle = (Inner + Outer) / 2, Lift = 0.15f;

    private static float Slope(float x, float z) => 0.1f * x;

    private static MeshData Ring(int segments = 8) => RingMesh.Build(Centre, Inner, Outer, segments, Slope, Lift);

    private static float DistanceFromCentre(Vector3 p) => Vector2.Distance(new Vector2(p.X, p.Z), Centre);

    [Fact]
    public void A_ring_has_three_vertices_and_four_triangles_per_segment()
    {
        MeshData ring = Ring(8);
        Assert.Equal(24, ring.VertexCount);
        Assert.Equal(32, ring.TriangleCount);
    }

    [Fact]
    public void Every_vertex_lies_on_its_circle_at_the_ground_plus_the_lift()
    {
        MeshData ring = Ring(8);
        int inner = 0, middle = 0, outer = 0;
        for (int v = 0; v < ring.VertexCount; v++)
        {
            Vector3 p = ring.Positions[v];
            float r = DistanceFromCentre(p);
            if (MathF.Abs(r - Inner) <= 1e-4f) inner++;
            else if (MathF.Abs(r - Middle) <= 1e-4f) middle++;
            else if (MathF.Abs(r - Outer) <= 1e-4f) outer++;
            else Assert.Fail($"vertex {v} at {p} is {r} m from the centre");
            Assert.Equal(0.1f * p.X + Lift, p.Y, 4);
            Assert.Equal(Vector3.UnitY, ring.Normals[v]);
        }
        Assert.Equal(8, inner);
        Assert.Equal(8, middle);
        Assert.Equal(8, outer);
    }

    [Fact]
    public void The_middle_of_the_band_follows_ground_that_bends_across_it()
    {
        // A ridge along the middle circle: straight lines from the inner to the outer edge would cut under it.
        float Ridge(float x, float z) => 1f - MathF.Abs(Vector2.Distance(new Vector2(x, z), Centre) - Middle);
        MeshData ring = RingMesh.Build(Centre, Inner, Outer, 8, Ridge, Lift);
        for (int k = 0; k < 8; k++)
        {
            Vector3 mid = ring.Positions[3 * k + 1];
            Assert.Equal(Middle, DistanceFromCentre(mid), 4);
            Assert.Equal(1f + Lift, mid.Y, 4);
            Assert.Equal(0.5f, ring.Uvs[3 * k + 1].X);
            Assert.Equal(Lift, ring.Positions[3 * k].Y, 4);       // the edges, 1 m off the ridge, at 0 + lift
            Assert.Equal(Lift, ring.Positions[3 * k + 2].Y, 4);
        }
    }

    [Fact]
    public void The_ring_closes_without_a_seam()
    {
        const int segments = 8;
        MeshData ring = Ring(segments);
        // No two vertices in the same place: the first pair is not repeated at the end.
        for (int a = 0; a < ring.VertexCount; a++)
        for (int b = a + 1; b < ring.VertexCount; b++)
            Assert.True(Vector3.Distance(ring.Positions[a], ring.Positions[b]) > 1e-3f, $"vertices {a} and {b} coincide");
        // The last segment's four triangles use the first trio (vertices 0, 1 and 2) and the last trio.
        int[] last = Enumerable.Range(3 * 4 * (segments - 1), 12).Select(i => ring.Indices[i]).Distinct().Order().ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3 * segments - 3, 3 * segments - 2, 3 * segments - 1 }, last);
        // Every vertex is used.
        Assert.Equal(Enumerable.Range(0, ring.VertexCount), ring.Indices.Distinct().Order());
    }

    [Fact]
    public void Triangles_face_up_and_uv_runs_inner_to_outer()
    {
        MeshData ring = Ring(12);
        for (int t = 0; t < ring.TriangleCount; t++)
        {
            Vector3 a = ring.Positions[ring.Indices[3 * t]], b = ring.Positions[ring.Indices[3 * t + 1]], c = ring.Positions[ring.Indices[3 * t + 2]];
            Vector3 face = Vector3.Cross(b - a, c - a);
            Assert.True(face.Y > 0, $"triangle {t} faces down");
            Assert.True(Vector3.Dot(Vector3.Normalize(face), Vector3.UnitY) > 0);
        }
        MeshAssert.WindingMatchesNormals(ring);
        for (int v = 0; v < ring.VertexCount; v++)
        {
            float r = DistanceFromCentre(ring.Positions[v]);
            Assert.Equal((r - Inner) / (Outer - Inner), ring.Uvs[v].X, 4);
            Assert.InRange(ring.Uvs[v].Y, 0f, 1f);
        }
        // UV.y is the turn: it grows with the angle round the centre.
        Assert.Equal(0f, ring.Uvs[0].Y);
        Assert.Equal(0.5f, ring.Uvs[18].Y, 5);
    }

    [Fact]
    public void Bad_sizes_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RingMesh.Build(Centre, 5f, 5f, 8, Slope, Lift));
        Assert.Throws<ArgumentOutOfRangeException>(() => RingMesh.Build(Centre, 6f, 5f, 8, Slope, Lift));
        Assert.Throws<ArgumentOutOfRangeException>(() => RingMesh.Build(Centre, -1f, 5f, 8, Slope, Lift));
        Assert.Throws<ArgumentOutOfRangeException>(() => RingMesh.Build(Centre, Inner, Outer, 2, Slope, Lift));
        Assert.Throws<ArgumentOutOfRangeException>(() => RingMesh.Build(Centre, Inner, Outer, 8, Slope, -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => RingMesh.Build(Centre, Inner, float.NaN, 8, Slope, Lift));
    }
}

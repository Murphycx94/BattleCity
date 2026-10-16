using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class LatheTests
{
    private static readonly Vector2[] Cylinder = { new(0.5f, 0), new(0.5f, 2) };
    private static readonly Vector2[] Puck = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };

    /// <summary>Gun barrel along +Y: thick sleeve, thin tube, muzzle brake, closed by a flat cap at the breech.</summary>
    private static readonly Vector2[] Barrel =
    {
        new(0.14f, 0), new(0.14f, 1.2f), new(0.11f, 1.3f), new(0.11f, 3.6f), new(0.17f, 3.62f), new(0.17f, 3.95f), new(0, 3.95f),
    };

    [Fact]
    public void Capped_cylinder_counts()
    {
        MeshData mesh = Lathe.Build(Cylinder, 8);
        Assert.Equal(2 * 9 + 2 * 10, mesh.VertexCount); // one wall edge (2 rings of 9) + two caps (centre + 9)
        Assert.Equal(16 + 2 * 8, mesh.TriangleCount);
    }

    [Fact]
    public void Ends_on_the_axis_need_no_caps()
    {
        MeshData mesh = Lathe.Build(Puck, 6);
        Assert.Equal(3 * 2 * 7, mesh.VertexCount);
        Assert.Equal(6 + 12 + 6, mesh.TriangleCount); // pole fans have one triangle per segment
    }

    public static TheoryData<Vector2[], Vector3> Solids => new()
    {
        { Cylinder, new Vector3(0, 1, 0) },
        { Puck, new Vector3(0, 0.5f, 0) },
        { Barrel, new Vector3(0, 2, 0) },
    };

    [Theory]
    [MemberData(nameof(Solids))]
    public void Results_are_closed_and_outward(Vector2[] profile, Vector3 center)
    {
        MeshData mesh = Lathe.Build(profile, 12);
        MeshAssert.Closed(mesh);
        MeshAssert.WindingMatchesNormals(mesh);
        MeshAssert.UnitNormals(mesh);
        Assert.True(MeshAssert.SignedVolume(mesh) > 0);
        if (profile != Barrel) MeshAssert.NormalsPointAwayFrom(mesh, center);
    }

    [Fact]
    public void Squareness_makes_a_rounded_block_inside_its_square()
    {
        MeshData mesh = Lathe.Build(Puck, 72, squareness: 4f);
        MeshAssert.ConvexSolid(mesh, new Vector3(0, 0.5f, 0));
        var (min, max) = MeshAssert.Bounds(mesh);
        Assert.Equal(new Vector3(-1, 0, -1), min, new Vector3EqualityTolerance(1e-4f));
        Assert.Equal(new Vector3(1, 1, 1), max, new Vector3EqualityTolerance(1e-4f));
        // Its corners reach past the round puck's: 2^¼ out along the diagonals.
        float corner = mesh.Positions.Max(p => (MathF.Abs(p.X) + MathF.Abs(p.Z)) / MathF.Sqrt(2));
        Assert.Equal(MathF.Pow(2f, 0.25f), corner, 3);
    }

    [Fact]
    public void Square_wall_normals_are_square_to_the_wall()
    {
        MeshData mesh = Lathe.Build(Cylinder, 48, squareness: 4f);
        for (int i = 0; i + 1 < 49; i++)   // the bottom wall ring: neighbours along the ring span the wall's tangent
        {
            Vector3 along = mesh.Positions[i + 1] - mesh.Positions[i];
            Vector3 normal = (mesh.Normals[i] + mesh.Normals[i + 1]) / 2;
            Assert.True(MathF.Abs(Vector3.Dot(Vector3.Normalize(along), Vector3.Normalize(normal))) < 0.05f, $"normal {i} is not square to the wall");
            Assert.Equal(0f, mesh.Normals[i].Y, 4);
        }
    }

    /// <summary>
    /// A leaning wall is where the profile normal's vertical part, and so its rise term, matters. The cone frustum's wall
    /// is the surface (r(t)·k(φ)·cos φ, y(t), −r(t)·k(φ)·sin φ), k the superellipse's radius (Lathe sweeps with
    /// sin = −sin φ); each wall vertex's normal must be that surface's, the cross product of its tangents along the
    /// profile and round the ring, taken by finite differences.
    /// </summary>
    [Fact]
    public void Sloped_square_wall_normals_are_the_surface_normals()
    {
        const int Segments = 48;
        const float Squareness = 4f, H = 1e-3f;
        Vector2 bottom = new(1, 0), top = new(0.5f, 1);
        MeshData mesh = Lathe.Build(new[] { bottom, top, new Vector2(0, 1) }, Segments, squareness: Squareness);
        Vector3 Surface(float t, float angle)
        {
            Vector2 p = Vector2.Lerp(bottom, top, t);
            float cos = MathF.Cos(angle), sin = -MathF.Sin(angle), k = Superellipse.Radius(cos, sin, Squareness);
            return new Vector3(p.X * k * cos, p.Y, p.X * k * sin);
        }
        for (int ring = 0; ring < 2; ring++)   // the wall's bottom and top rings come first, Segments + 1 vertices each
        for (int j = 0; j <= Segments; j++)
        {
            int i = ring * (Segments + 1) + j;
            float angle = MathF.Tau * j / Segments;
            Assert.Equal(Surface(ring, angle), mesh.Positions[i], new Vector3EqualityTolerance(1e-5f));
            Vector3 alongProfile = Surface(ring + H, angle) - Surface(ring - H, angle);   // the wall is straight: exact past its ends too
            Vector3 alongRing = Surface(ring, angle + H) - Surface(ring, angle - H);
            Vector3 surface = Vector3.Normalize(Vector3.Cross(alongProfile, alongRing));
            if (Vector3.Dot(surface, mesh.Positions[i] with { Y = 0 }) < 0) surface = -surface;   // outward
            float degrees = MathF.Acos(Math.Clamp(Vector3.Dot(surface, mesh.Normals[i]), -1f, 1f)) * 180f / MathF.PI;
            Assert.True(degrees < 1f, $"ring {ring}, vertex {j}: normal {mesh.Normals[i]} is {degrees:F2}° off the surface's {surface}");
        }
    }

    /// <summary>Exactly 2 takes the round fast path; just above it the general superellipse path must give the same lathe.</summary>
    [Fact]
    public void Squareness_two_is_the_round_lathe()
    {
        MeshData round = Lathe.Build(Barrel, 10), squareTwo = Lathe.Build(Barrel, 10, squareness: 2f);
        Assert.Equal(round.Positions, squareTwo.Positions);
        Assert.Equal(round.Normals, squareTwo.Normals);
        MeshData nearlyRound = Lathe.Build(Barrel, 10, squareness: 2.001f);
        Assert.Equal(round.Positions, nearlyRound.Positions, new Vector3EqualityTolerance(1e-3f));
        Assert.Equal(round.Normals, nearlyRound.Normals, new Vector3EqualityTolerance(1e-3f));
    }

    [Fact]
    public void Squareness_is_capped_at_32()
    {
        MeshData squarest = Lathe.Build(Puck, 72, squareness: Lathe.MaxSquareness);
        Assert.All(squarest.Positions.Concat(squarest.Normals), v => Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z), $"{v}"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Lathe.Build(Puck, 72, squareness: 33f));
    }

    [Fact]
    public void Ring_vertices_sit_at_the_profile_radius()
    {
        MeshData mesh = Lathe.Build(Barrel, 10);
        foreach (Vector3 p in mesh.Positions)
        {
            float r = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
            bool onProfile = Barrel.Any(q => MathF.Abs(q.Y - p.Y) < 1e-5f && (MathF.Abs(q.X - r) < 1e-5f || r < 1e-5f));
            Assert.True(onProfile, $"{p} (radius {r}) is not on a profile ring");
        }
        var volumeOfTube = MathF.PI * 0.11f * 0.11f * 2.3f;
        Assert.True(MeshAssert.SignedVolume(mesh) > volumeOfTube);
    }

    [Fact]
    public void Gentle_turns_share_normals_and_sharp_turns_do_not()
    {
        Vector2[] gentle = { new(1, 0), new(1, 1), new(0.95f, 2) };   // ~3° bend
        Vector2[] sharp = { new(1, 0), new(1, 1), new(0.2f, 1.1f) };    // ~83° bend
        Assert.Equal(1, DistinctNormalsAt(Lathe.Build(gentle, 8), new Vector3(1, 1, 0)));
        Assert.Equal(2, DistinctNormalsAt(Lathe.Build(sharp, 8), new Vector3(1, 1, 0)));
    }

    [Fact]
    public void Invalid_profiles_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Lathe.Build(new[] { new Vector2(-1, 0), new Vector2(1, 1) }, 8));
        Assert.Throws<ArgumentException>(() => Lathe.Build(new[] { new Vector2(0, 0), new Vector2(0, 1) }, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => Lathe.Build(Cylinder, 2));
    }

    [Fact]
    public void Reversed_profile_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Lathe.Build(new[] { new Vector2(0.5f, 2), new Vector2(0.5f, 0) }, 8));
    }

    [Fact]
    public void Near_axis_endpoint_is_treated_as_on_the_axis()
    {
        Vector2[] almost = { new(1e-7f, 0), new(1, 0), new(1, 1), new(0, 1) };
        Assert.Equal(Lathe.Build(Puck, 6).TriangleCount, Lathe.Build(almost, 6).TriangleCount);
    }

    private static int DistinctNormalsAt(MeshData mesh, Vector3 position)
    {
        var normals = new List<Vector3>();
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (Vector3.Distance(mesh.Positions[i], position) > 1e-4f) continue;
            if (!normals.Any(n => Vector3.Distance(n, mesh.Normals[i]) < 1e-4f)) normals.Add(mesh.Normals[i]);
        }
        return normals.Count;
    }
}

/// <summary>Component-wise comparison within a tolerance.</summary>
internal sealed class Vector3EqualityTolerance(float tolerance) : IEqualityComparer<Vector3>
{
    public bool Equals(Vector3 a, Vector3 b) => MathF.Abs(a.X - b.X) <= tolerance && MathF.Abs(a.Y - b.Y) <= tolerance && MathF.Abs(a.Z - b.Z) <= tolerance;

    public int GetHashCode(Vector3 v) => 0;
}

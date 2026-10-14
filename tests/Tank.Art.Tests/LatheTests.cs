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

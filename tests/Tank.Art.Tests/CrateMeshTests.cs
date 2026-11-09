using System.Numerics;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class CrateMeshTests
{
    private const float SizeSlack = 0.06f;
    private static readonly CrateKind[] Kinds = { CrateKind.Repair, CrateKind.Ammo, CrateKind.Armor, CrateKind.Reload, CrateKind.Optics };

    public static IEnumerable<object[]> EveryKind() => Kinds.Select(k => new object[] { k });

    [Fact]
    public void The_constants_follow_the_simulation_s_crate_and_the_design()
    {
        Assert.Equal(1.55f, CrateMesh.SignCentreHeight);
        Assert.Equal(0.12f, CrateMesh.BobAmplitude);
        Assert.Equal(2.4f, CrateMesh.BobPeriod);
        Assert.Equal(2f * CrateRules.HalfSize, CrateMesh.BodySize);
        Assert.Equal(CrateRules.Height, CrateMesh.BodyHeight);
        Assert.Equal(0.7f, CrateMesh.SignWidth);
        // what the simulation tests sight against is on the plate at rest (its top edge)
        Assert.InRange(CrateRules.SignTop, CrateMesh.SignCentreHeight - CrateMesh.SignHeight / 2, CrateMesh.SignCentreHeight + CrateMesh.SignHeight / 2 + 1e-5f);
    }

    [Fact]
    public void The_body_is_the_simulation_s_box_standing_on_the_ground()
    {
        MeshData body = CrateMesh.Body();
        var (min, max) = MeshAssert.Bounds(body);

        Assert.True(body.TriangleCount > 0);
        Assert.Equal(1.2f, max.X - min.X, SizeSlack);
        Assert.Equal(1.0f, max.Y - min.Y, SizeSlack);
        Assert.Equal(1.2f, max.Z - min.Z, SizeSlack);
        Assert.Equal(0f, min.Y, 4);
        Assert.Equal(0f, (min.X + max.X) / 2, 4);
        Assert.Equal(0f, (min.Z + max.Z) / 2, 4);
        Sound(body);
    }

    [Fact]
    public void The_body_is_wood_with_plank_straps_on_every_side()
    {
        MeshData body = CrateMesh.Body();
        Vector4 wood = Colour(Palette.Wood), planks = Colour(Palette.Planks);
        Assert.Contains(wood, body.Colors);
        Assert.Contains(planks, body.Colors);
        Assert.All(body.Colors, c => Assert.True(c == wood || c == planks, $"a body vertex is {c}, neither wood nor planks"));
        // straps stand a little proud of each of the four faces
        foreach (var (axis, sign) in new[] { (0, 1f), (0, -1f), (2, 1f), (2, -1f) })
            Assert.Contains(body.Positions.Where((_, i) => body.Colors[i] == planks), p => sign * (axis == 0 ? p.X : p.Z) > CrateRules.HalfSize + 0.005f);
    }

    [Fact]
    public void The_post_stands_from_inside_the_box_up_into_the_sign()
    {
        MeshData post = CrateMesh.Post();
        var (min, max) = MeshAssert.Bounds(post);

        Assert.Equal(0.08f, max.X - min.X, 4);
        Assert.Equal(0.08f, max.Z - min.Z, 4);
        Assert.Equal(0.9f, max.Y - min.Y, 4);
        Assert.True(min.Y > 0f && min.Y < CrateRules.Height, "the post's foot is inside the box");
        Assert.True(max.Y > CrateMesh.SignCentreHeight - CrateMesh.SignHeight / 2, "the post reaches into the sign");
        Assert.True(max.Y < CrateMesh.SignCentreHeight, "the post stops below the sign's middle");
        Assert.All(post.Colors, c => Assert.Equal(Colour(Palette.Door), c));
        Sound(post);
        MeshAssert.Closed(post);
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void A_sign_is_a_plate_of_its_kind_s_colour_centred_at_the_sign_height_with_a_white_icon_on_both_faces(CrateKind kind)
    {
        MeshData sign = CrateMesh.Sign(kind);
        var (min, max) = MeshAssert.Bounds(sign);

        Assert.True(sign.TriangleCount > 0);
        Assert.Equal(CrateMesh.SignWidth, max.X - min.X, 0.01f);
        Assert.Equal(CrateMesh.SignCentreHeight, (min.Y + max.Y) / 2, 0.02f);
        Assert.Equal(CrateMesh.SignHeight, max.Y - min.Y, 0.02f);
        Assert.True(max.Z > CrateMesh.SignThickness / 2 + 0.001f, "the icon stands proud of the front face");
        Assert.True(max.Z < CrateMesh.SignThickness / 2 + 0.03f, "the icon is a thin relief");
        Assert.Equal(max.Z, -min.Z, 4);   // the same icon on the back
        Vector4 plate = Colour(Palette.Crate(kind)), white = Colour(Palette.UiText);
        Assert.Contains(plate, sign.Colors);
        Assert.Contains(white, sign.Colors);
        Assert.All(sign.Colors, c => Assert.True(c == plate || c == white, $"a sign vertex is {c}, neither the kind's colour nor white"));
        // the icon (white) lies inside the plate's face, in front of and behind it
        foreach (Vector3 p in sign.Positions.Where((_, i) => sign.Colors[i] == white))
        {
            Assert.True(MathF.Abs(p.X) < CrateMesh.SignWidth / 2 - 0.02f, $"icon vertex {p} runs off the plate's side");
            Assert.True(MathF.Abs(p.Y - CrateMesh.SignCentreHeight) < CrateMesh.SignHeight / 2 - 0.02f, $"icon vertex {p} runs off the plate's top or bottom");
            Assert.True(MathF.Abs(p.Z) > CrateMesh.SignThickness / 2 - 0.02f, $"icon vertex {p} is buried in the plate");
        }
        Sound(sign);
    }

    [Fact]
    public void The_five_signs_are_told_apart()
    {
        MeshData[] signs = Kinds.Select(CrateMesh.Sign).ToArray();
        for (int a = 0; a < signs.Length; a++)
        for (int b = a + 1; b < signs.Length; b++)
            Assert.True(signs[a].VertexCount != signs[b].VertexCount || MeshAssert.Bounds(signs[a]) != MeshAssert.Bounds(signs[b]),
                $"{Kinds[a]} and {Kinds[b]} signs are the same mesh");
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void Build_gives_the_box_and_the_sign_with_its_post_hung_from_the_plate_s_centre(CrateKind kind)
    {
        var (box, sign) = CrateMesh.Build(kind);
        MeshData body = CrateMesh.Body();
        MeshData plate = CrateMesh.Sign(kind), post = CrateMesh.Post();

        Assert.Equal(body.VertexCount, box.VertexCount);
        Assert.Equal(body.TriangleCount, box.TriangleCount);
        Assert.Equal(plate.VertexCount + post.VertexCount, sign.VertexCount);
        var (min, max) = MeshAssert.Bounds(sign);
        var (plateMin, plateMax) = MeshAssert.Bounds(plate);
        var (postMin, _) = MeshAssert.Bounds(post);
        Assert.Equal(plateMax.Y - CrateMesh.SignCentreHeight, max.Y, 4);   // the plate's top, measured from its centre
        Assert.Equal(postMin.Y - CrateMesh.SignCentreHeight, min.Y, 4);    // the post's foot, likewise
        Assert.Equal(plateMin.X, min.X, 4);
        Assert.True(box.HasColors && sign.HasColors);
        Sound(box);
        Sound(sign);
    }

    [Fact]
    public void A_full_ring_is_a_closed_solid_with_the_volume_of_its_annulus()
    {
        MeshData ring = CrateMesh.Arc(0.1f, 0.2f, 0.02f, 0f, 1f, 24);
        var (min, max) = MeshAssert.Bounds(ring);

        Assert.Equal(0.4f, max.X - min.X, 4);
        Assert.Equal(0.4f, max.Y - min.Y, 4);
        Assert.Equal(0.02f, max.Z - min.Z, 5);
        Assert.Equal(8 * 24, ring.VertexCount);
        Assert.Equal(8 * 24, ring.TriangleCount);
        MeshAssert.Closed(ring);
        MeshAssert.WindingMatchesNormals(ring);
        MeshAssert.UnitNormals(ring);
        float polygon = 24 / 2f * MathF.Sin(MathF.Tau / 24);   // area factor of a 24-gon against π for its circle
        Assert.Equal(polygon * (0.2f * 0.2f - 0.1f * 0.1f) * 0.02f, MeshAssert.SignedVolume(ring), 6);
    }

    [Fact]
    public void An_open_arc_is_capped_at_both_ends_and_spans_only_its_turn()
    {
        MeshData arc = CrateMesh.Arc(0.1f, 0.2f, 0.02f, 0.125f, 0.875f, 12);   // three quarters, open towards +X
        var (min, max) = MeshAssert.Bounds(arc);

        Assert.Equal(8 * 13 + 8, arc.VertexCount);   // the two caps carry their own vertices (their normals are the tangents)
        Assert.Equal(8 * 12 + 4, arc.TriangleCount);   // the two caps
        Assert.True(max.X < 0.2f * MathF.Cos(MathF.Tau / 8) + 1e-4f, "nothing reaches the gap at +X");
        Assert.Equal(-0.2f, min.X, 4);
        MeshAssert.Closed(arc);
        MeshAssert.WindingMatchesNormals(arc);
        MeshAssert.UnitNormals(arc);
        Assert.True(MeshAssert.SignedVolume(arc) > 0);
    }

    [Theory]
    [InlineData(-0.1f, 0.2f, 0.02f, 0f, 1f, 12)]
    [InlineData(0.2f, 0.1f, 0.02f, 0f, 1f, 12)]
    [InlineData(0.1f, 0.2f, 0f, 0f, 1f, 12)]
    [InlineData(0.1f, 0.2f, 0.02f, 0.5f, 0.5f, 12)]
    [InlineData(0.1f, 0.2f, 0.02f, 0f, 1.5f, 12)]
    [InlineData(0.1f, 0.2f, 0.02f, 0f, 1f, 2)]
    public void Bad_arcs_are_refused(float inner, float outer, float thickness, float from, float to, int segments) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CrateMesh.Arc(inner, outer, thickness, from, to, segments));

    [Fact]
    public void An_unknown_kind_is_refused() => Assert.Throws<ArgumentOutOfRangeException>(() => CrateMesh.Sign((CrateKind)5));

    private static Vector4 Colour(Vector3 srgb) => new(Palette.Linear(srgb), 1f);

    /// <summary>The checks every crate part passes: coloured, finite, unit normals the winding agrees with.</summary>
    private static void Sound(MeshData mesh)
    {
        Assert.True(mesh.HasColors);
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            Vector3 p = mesh.Positions[i], n = mesh.Normals[i];
            Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), $"position {i} is not finite");
            Assert.True(float.IsFinite(n.X) && float.IsFinite(n.Y) && float.IsFinite(n.Z), $"normal {i} is not finite");
        }
        MeshAssert.UnitNormals(mesh);
        MeshAssert.WindingMatchesNormals(mesh);
    }
}

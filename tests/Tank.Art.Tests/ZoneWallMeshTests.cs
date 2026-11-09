using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class ZoneWallMeshTests
{
    private const int Segments = 8;

    private static MeshData Wall() => ZoneWallMesh.Build(Segments);

    [Fact]
    public void A_wall_has_two_rows_of_segments_plus_one_columns_and_a_quad_per_segment()
    {
        MeshData wall = Wall();

        Assert.Equal(2 * (Segments + 1), wall.VertexCount);
        Assert.Equal(2 * Segments, wall.TriangleCount);   // one-sided: the shader draws both sides (cull_disabled)
    }

    [Fact]
    public void Columns_run_round_the_unit_circle_from_plus_x_towards_plus_z_with_uv_x_the_turn_and_uv_y_the_row()
    {
        MeshData wall = Wall();
        for (int k = 0; k <= Segments; k++)
        {
            float angle = MathF.Tau * k / Segments;
            for (int row = 0; row < 2; row++)
            {
                int v = 2 * k + row;
                Vector3 p = wall.Positions[v];
                Assert.Equal(MathF.Cos(angle), p.X, 5);
                Assert.Equal(MathF.Sin(angle), p.Z, 5);
                Assert.Equal(row, p.Y);
                Assert.Equal((float)k / Segments, wall.Uvs[v].X, 6);
                Assert.Equal(row, wall.Uvs[v].Y);
            }
        }
        Assert.Equal(0f, wall.Uvs[0].X);
        Assert.Equal(1f, wall.Uvs[2 * Segments].X);
    }

    [Fact]
    public void The_last_column_repeats_the_first_in_place_so_the_stripes_do_not_seam()
    {
        MeshData wall = Wall();
        for (int row = 0; row < 2; row++)
        {
            MeshAssert.Near(wall.Positions[row], wall.Positions[2 * Segments + row], 1e-6f);
            Assert.Equal(0f, wall.Uvs[row].X);
            Assert.Equal(1f, wall.Uvs[2 * Segments + row].X);
        }
    }

    [Fact]
    public void Every_triangle_s_corners_lie_on_the_unit_circle_and_every_vertex_is_used()
    {
        MeshData wall = Wall();
        for (int i = 0; i < wall.Indices.Count; i++)
        {
            Vector3 p = wall.Positions[wall.Indices[i]];
            Assert.Equal(1f, p.X * p.X + p.Z * p.Z, 5);
            Assert.True(p.Y is 0f or 1f);
        }
        Assert.Equal(Enumerable.Range(0, wall.VertexCount), wall.Indices.Distinct().Order());
        MeshAssert.WindingMatchesNormals(wall);
    }

    [Fact]
    public void Normals_point_outwards_and_are_unit_length()
    {
        MeshData wall = Wall();
        MeshAssert.UnitNormals(wall);
        for (int v = 0; v < wall.VertexCount; v++)
        {
            Vector3 p = wall.Positions[v];
            Assert.Equal(0f, wall.Normals[v].Y);
            Assert.True(Vector3.Dot(wall.Normals[v], new Vector3(p.X, 0f, p.Z)) > 0.999f, $"vertex {v}'s normal points off its radius");
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(-8)]
    public void Fewer_than_three_segments_are_refused(int segments) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoneWallMesh.Build(segments));
}

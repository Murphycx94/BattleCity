using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// The zone's light wall (stage 6 design §3.3): an open unit cylinder the zone_wall shader bends onto the safe circle
/// (the vertex shader reads the centre, the radius and the ground heights from uniforms, so one mesh serves every ring
/// and every frame of the shrink). <paramref name="segments"/> + 1 columns, two rows: vertex 2k + row is
/// (cos θ, row, sin θ) with θ = 2πk / segments from +X towards +Z, UV = (k / segments, row); the last column repeats the
/// first in place at UV.x 1, so the stripes the shader draws from UV.x meet without a seam. One quad a segment, wound
/// to face outwards (normals along the radius): the shader renders both sides (cull_disabled), so the inside shows too.
/// </summary>
public static class ZoneWallMesh
{
    public static MeshData Build(int segments)
    {
        if (segments < 3) throw new ArgumentOutOfRangeException(nameof(segments), segments, "A wall needs at least 3 segments.");
        var b = new MeshBuilder();
        for (int k = 0; k <= segments; k++)
        {
            float turn = (float)k / segments;
            float angle = turn * MathF.Tau;
            var radial = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            b.AddVertex(radial, radial, new Vector2(turn, 0f));
            b.AddVertex(radial + Vector3.UnitY, radial, new Vector2(turn, 1f));
        }
        for (int k = 0; k < segments; k++)
        {
            int foot = 2 * k, top = foot + 1, nextFoot = foot + 2, nextTop = foot + 3;
            // Counter-clockwise seen from outside the cylinder (from +X at θ 0 the next column lies towards +Z, on the right).
            b.AddQuad(foot, top, nextTop, nextFoot);
        }
        return b.Build();
    }
}

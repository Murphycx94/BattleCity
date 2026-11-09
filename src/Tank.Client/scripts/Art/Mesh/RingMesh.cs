using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>A flat band round a centre, draped on the ground: the base rings of the standard battle (stage 5 design §3.4).</summary>
public static class RingMesh
{
    /// <summary>
    /// A flat annulus draped on the ground: <paramref name="segments"/> pieces around, each vertex at heightAt(x, z) +
    /// <paramref name="lift"/> with its normal up; UV.x 0 on the inner edge, 0.5 on the middle circle, 1 on the outer;
    /// UV.y the turn (0…1). Vertex 3k is on the inner circle, 3k + 1 on the middle one and 3k + 2 on the outer, at angle
    /// 2πk / segments from +X towards +Z: the middle circle lets the band bend with the ground across its width, not
    /// only along it. Each piece is two quads (inner–middle, middle–outer), four triangles. The last piece closes on the
    /// first trio, so there is no seam in the band (UV.y then runs back from its last value to 0 over that piece).
    /// Triangles face up (counter-clockwise seen from above). <paramref name="inner"/> may be 0 (a disc with a point
    /// in the middle), and must be below <paramref name="outer"/>; at least 3 segments; a lift of 0 or more.
    /// </summary>
    public static MeshData Build(Vector2 centre, float inner, float outer, int segments, Func<float, float, float> heightAt, float lift)
    {
        if (!(inner >= 0f)) throw new ArgumentOutOfRangeException(nameof(inner), $"Inner radius {inner} must be 0 or more.");
        if (!(outer > inner) || !float.IsFinite(outer)) throw new ArgumentOutOfRangeException(nameof(outer), $"Outer radius {outer} must be finite and above the inner {inner}.");
        if (segments < 3) throw new ArgumentOutOfRangeException(nameof(segments), $"A ring needs at least 3 segments, got {segments}.");
        if (!(lift >= 0f) || !float.IsFinite(lift)) throw new ArgumentOutOfRangeException(nameof(lift), $"Lift {lift} must be finite and 0 or more.");
        float middle = (inner + outer) / 2;
        var b = new MeshBuilder();
        for (int k = 0; k < segments; k++)
        {
            float turn = (float)k / segments;
            float angle = turn * MathF.Tau;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            AddVertex(b, centre + direction * inner, heightAt, lift, new Vector2(0f, turn));
            AddVertex(b, centre + direction * middle, heightAt, lift, new Vector2(0.5f, turn));
            AddVertex(b, centre + direction * outer, heightAt, lift, new Vector2(1f, turn));
        }
        for (int k = 0; k < segments; k++)
        {
            int i0 = 3 * k, m0 = i0 + 1, o0 = i0 + 2;
            int i1 = 3 * ((k + 1) % segments), m1 = i1 + 1, o1 = i1 + 2;
            b.AddTriangle(i0, m1, m0);
            b.AddTriangle(i0, i1, m1);
            b.AddTriangle(m0, o1, o0);
            b.AddTriangle(m0, m1, o1);
        }
        return b.Build();
    }

    private static void AddVertex(MeshBuilder b, Vector2 at, Func<float, float, float> heightAt, float lift, Vector2 uv) =>
        b.AddVertex(new Vector3(at.X, heightAt(at.X, at.Y) + lift, at.Y), Vector3.UnitY, uv);
}

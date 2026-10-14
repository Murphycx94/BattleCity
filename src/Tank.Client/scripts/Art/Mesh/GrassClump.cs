using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// A tuft of 5–7 wide, short cartoon blades rooted at y = 0. Each blade is a strip of <c>segments</c> rows of vertex
/// pairs that closes in a single tip vertex; the rows bunch up towards the top and stay wide, so the tip is a short,
/// blunt triangle rather than a needle. Vertex colour R is the height along the blade (0 at the root, 1 at the tip)
/// for wind and shading; G is a per-blade shade in [0, 1]. Single-sided geometry; the grass shader draws both faces.
/// </summary>
public static class GrassClump
{
    public const int Segments = 3;
    public const int VerticesPerBlade = Segments * 2 + 1;
    public const int TrianglesPerBlade = Segments * 2 - 1;
    public const float MinHeight = 0.25f;
    public const float MaxHeight = 0.45f;

    private const float SpreadRadius = 0.2f;
    private const float RootWidth = 0.16f;
    private const float Taper = 0.45f;   // width lost by the top row, as a fraction of the root width

    /// <param name="segments">Segments per blade (default <see cref="Segments"/>); the last one is the tip triangle.</param>
    /// <param name="blades">Blade count; 0 (default) picks 5-7 from the seed.</param>
    public static MeshData Build(uint seed, int segments = Segments, int blades = 0)
    {
        if (segments < 1) throw new ArgumentOutOfRangeException(nameof(segments), "Need at least one segment.");
        if (blades < 0) throw new ArgumentOutOfRangeException(nameof(blades), "Blade count cannot be negative.");
        if (blades == 0) blades = 5 + (int)(SeededNoise.Hash(0, 0, 0, seed) % 3);
        var b = new MeshBuilder();
        for (int i = 0; i < blades; i++)
        {
            float R(int k) => SeededNoise.Hash01(i, k, 1, seed);
            float yaw = R(0) * MathF.Tau;
            float radius = MathF.Sqrt(R(1)) * SpreadRadius;
            float rootAngle = R(2) * MathF.Tau;
            var root = new Vector3(MathF.Cos(rootAngle) * radius, 0, MathF.Sin(rootAngle) * radius);
            float height = MinHeight + R(3) * (MaxHeight - MinHeight);
            float lean = 0.15f + R(4) * 0.45f;
            float shade = R(5);
            var facing = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));   // the way the blade bends
            var across = new Vector3(facing.Z, 0, -facing.X);               // blade width direction
            Vector3 Spine(float t) => root + Vector3.UnitY * (height * t * (1 - 0.25f * lean * t)) + facing * (lean * height * t * t);
            Vector3 Normal(float t)
            {
                Vector3 tangent = Vector3.Normalize(Vector3.UnitY * height + facing * (2 * lean * height * t) + Vector3.UnitY * 1e-3f);
                return Vector3.Normalize(Vector3.Cross(across, tangent) + Vector3.UnitY * 0.6f);
            }

            int first = b.VertexCount;
            for (int row = 0; row < segments; row++)
            {
                float t = MathF.Sin((float)row / segments * MathF.PI / 2);   // rows bunch up near the top: a short tip
                float width = RootWidth * (1 - Taper * t * t);
                var color = new Vector4(t, shade, 0, 1);
                b.AddVertex(Spine(t) - across * (width / 2), Normal(t), new Vector2(0, t), color);
                b.AddVertex(Spine(t) + across * (width / 2), Normal(t), new Vector2(1, t), color);
            }
            int tip = b.AddVertex(Spine(1f), Normal(1f), new Vector2(0.5f, 1f), new Vector4(1f, shade, 0, 1));
            for (int row = 0; row < segments - 1; row++)
            {
                int l0 = first + row * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                b.AddQuad(l0, r0, r1, l1);
            }
            int lastL = first + (segments - 1) * 2;
            b.AddTriangle(lastL, lastL + 1, tip);
        }
        return b.Build();
    }
}

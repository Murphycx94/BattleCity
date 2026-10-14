using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Immutable triangle mesh in engine-neutral form. Triangles wind counter-clockwise seen from the side their normals
/// point to, i.e. (b − a) × (c − a) faces outward. Godot wants the opposite, so the adapter flips the order.
/// UVs are placeholders; materials use object- or world-space projection. Uncoloured parts become white (1,1,1,1)
/// when merged with coloured ones.
/// Colours are free-form per-vertex data (RGBA); each shader decides what the channels mean.
/// </summary>
public sealed class MeshData
{
    private readonly Vector3[] _positions;
    private readonly Vector3[] _normals;
    private readonly Vector2[] _uvs;
    private readonly Vector4[] _colors;
    private readonly int[] _indices;

    public MeshData(Vector3[] positions, Vector3[] normals, Vector2[] uvs, Vector4[] colors, int[] indices, bool hasColors)
    {
        int n = positions.Length;
        if (normals.Length != n || uvs.Length != n || colors.Length != n)
            throw new ArgumentException("Every vertex needs a position, normal, uv and colour.");
        if (indices.Length % 3 != 0) throw new ArgumentException("Index count must be a multiple of 3.", nameof(indices));
        foreach (int i in indices)
            if ((uint)i >= (uint)n) throw new ArgumentOutOfRangeException(nameof(indices), $"Index {i} outside 0..{n - 1}.");
        _positions = (Vector3[])positions.Clone();
        _normals = (Vector3[])normals.Clone();
        _uvs = (Vector2[])uvs.Clone();
        _colors = (Vector4[])colors.Clone();
        _indices = (int[])indices.Clone();
        HasColors = hasColors;
    }

    public static MeshData Empty { get; } = new(Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<Vector2>(),
        Array.Empty<Vector4>(), Array.Empty<int>(), false);

    public IReadOnlyList<Vector3> Positions => _positions;
    public IReadOnlyList<Vector3> Normals => _normals;
    public IReadOnlyList<Vector2> Uvs => _uvs;
    public IReadOnlyList<Vector4> Colors => _colors;
    public IReadOnlyList<int> Indices => _indices;
    public bool HasColors { get; }
    public int VertexCount => _positions.Length;
    public int TriangleCount => _indices.Length / 3;

    /// <summary>
    /// Applies an affine transform. System.Numerics row-vector convention: in A * B, A is applied first (opposite of
    /// Godot Transform3D). Normals use the inverse transpose, so non-uniform scale keeps them perpendicular;
    /// a mirroring transform (negative determinant) also reverses the winding so faces stay outward.
    /// </summary>
    public MeshData Transformed(Matrix4x4 transform)
    {
        if (!Matrix4x4.Invert(transform, out Matrix4x4 inverse))
            throw new ArgumentException("Transform is not invertible.", nameof(transform));
        Matrix4x4 normalMatrix = Matrix4x4.Transpose(inverse);
        var positions = new Vector3[_positions.Length];
        var normals = new Vector3[_normals.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = Vector3.Transform(_positions[i], transform);
            normals[i] = Vector3.Normalize(Vector3.TransformNormal(_normals[i], normalMatrix));
        }
        int[] indices = (int[])_indices.Clone();
        if (transform.GetDeterminant() < 0)
            for (int t = 0; t < indices.Length; t += 3)
                (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
        return new MeshData(positions, normals, _uvs, _colors, indices, HasColors);
    }

    /// <summary>Same geometry with every vertex colour set to <paramref name="color"/>.</summary>
    public MeshData WithColor(Vector4 color)
    {
        var colors = new Vector4[_positions.Length];
        Array.Fill(colors, color);
        return new MeshData(_positions, _normals, _uvs, colors, _indices, true);
    }

    /// <summary>Same geometry with each vertex colour computed from its position.</summary>
    public MeshData WithColors(Func<Vector3, Vector4> colorAt)
    {
        var colors = new Vector4[_positions.Length];
        for (int i = 0; i < colors.Length; i++) colors[i] = colorAt(_positions[i]);
        return new MeshData(_positions, _normals, _uvs, colors, _indices, true);
    }
}

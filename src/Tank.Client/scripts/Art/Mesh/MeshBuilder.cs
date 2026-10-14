using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>Accumulates vertices and triangles, then freezes them into a <see cref="MeshData"/>.</summary>
public sealed class MeshBuilder
{
    private static readonly Vector4 White = Vector4.One;

    private readonly List<Vector3> _positions = new();
    private readonly List<Vector3> _normals = new();
    private readonly List<Vector2> _uvs = new();
    private readonly List<Vector4> _colors = new();
    private readonly List<int> _indices = new();
    private bool _hasColors;

    public int VertexCount => _positions.Count;

    public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv, Vector4? color = null)
    {
        _positions.Add(position);
        _normals.Add(normal);
        _uvs.Add(uv);
        _colors.Add(color ?? White);
        _hasColors |= color.HasValue;
        return _positions.Count - 1;
    }

    /// <summary>Counter-clockwise seen from outside.</summary>
    public void AddTriangle(int a, int b, int c)
    {
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
    }

    /// <summary>Quad a-b-c-d, counter-clockwise seen from outside, split along a-c.</summary>
    public void AddQuad(int a, int b, int c, int d)
    {
        AddTriangle(a, b, c);
        AddTriangle(a, c, d);
    }

    /// <summary>Appends a whole mesh (already in this builder's space).</summary>
    public void Append(MeshData mesh)
    {
        int offset = _positions.Count;
        _positions.AddRange(mesh.Positions);
        _normals.AddRange(mesh.Normals);
        _uvs.AddRange(mesh.Uvs);
        _colors.AddRange(mesh.Colors);
        _hasColors |= mesh.HasColors;
        foreach (int i in mesh.Indices) _indices.Add(i + offset);
    }

    public MeshData Build() =>
        new(_positions.ToArray(), _normals.ToArray(), _uvs.ToArray(), _colors.ToArray(), _indices.ToArray(), _hasColors);
}

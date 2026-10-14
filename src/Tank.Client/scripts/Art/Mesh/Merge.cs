using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Combines transformed parts into one mesh so a whole hull or turret costs a single draw call.
/// System.Numerics row-vector convention: in A * B, A is applied first (opposite of Godot Transform3D).
/// </summary>
public static class Merge
{
    public static MeshData Build(IEnumerable<(MeshData Mesh, Matrix4x4 Transform)> parts)
    {
        var builder = new MeshBuilder();
        foreach (var (mesh, transform) in parts) builder.Append(mesh.Transformed(transform));
        return builder.Build();
    }

    public static MeshData Build(params (MeshData Mesh, Matrix4x4 Transform)[] parts) =>
        Build((IEnumerable<(MeshData, Matrix4x4)>)parts);
}

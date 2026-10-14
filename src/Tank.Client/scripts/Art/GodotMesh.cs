using Godot;
using Tank.Client.Art.Meshes;

namespace Tank.Client.Art;

/// <summary>Turns engine-neutral <see cref="MeshData"/> into a Godot <see cref="ArrayMesh"/>.</summary>
public static class GodotMesh
{
    public static ArrayMesh ToArrayMesh(this MeshData data)
    {
        int n = data.VertexCount;
        var vertices = new Vector3[n];
        var normals = new Vector3[n];
        var uvs = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            System.Numerics.Vector3 p = data.Positions[i], nn = data.Normals[i];
            System.Numerics.Vector2 uv = data.Uvs[i];
            vertices[i] = new Vector3(p.X, p.Y, p.Z);
            normals[i] = new Vector3(nn.X, nn.Y, nn.Z);
            uvs[i] = new Vector2(uv.X, uv.Y);
        }
        // MeshData winds counter-clockwise; Godot treats clockwise as the front face.
        var indices = new int[data.Indices.Count];
        for (int t = 0; t < indices.Length; t += 3)
        {
            indices[t] = data.Indices[t];
            indices[t + 1] = data.Indices[t + 2];
            indices[t + 2] = data.Indices[t + 1];
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        if (data.HasColors)
        {
            var colors = new Color[n];
            for (int i = 0; i < n; i++)
            {
                System.Numerics.Vector4 c = data.Colors[i];
                colors[i] = new Color(c.X, c.Y, c.Z, c.W);
            }
            arrays[(int)Mesh.ArrayType.Color] = colors;
        }
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}

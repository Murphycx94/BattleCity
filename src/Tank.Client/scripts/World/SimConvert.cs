using Godot;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>Tank.Sim uses System.Numerics with Godot's axes (Y up, -Z forward), so conversion is a plain copy.</summary>
public static class SimConvert
{
    public static Vector3 ToGodot(this SN.Vector3 v) => new(v.X, v.Y, v.Z);

    public static SN.Vector3 ToSim(this Vector3 v) => new(v.X, v.Y, v.Z);

    public static Quaternion ToGodot(this SN.Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    /// <summary>A System.Numerics affine matrix (row vectors: the image of X is row 1, the translation row 4) as a Godot transform.</summary>
    public static Transform3D ToGodot(this SN.Matrix4x4 m) =>
        new(new Basis(new Vector3(m.M11, m.M12, m.M13), new Vector3(m.M21, m.M22, m.M23), new Vector3(m.M31, m.M32, m.M33)), new Vector3(m.M41, m.M42, m.M43));

    /// <summary>Ground point (x, z) lifted to height <paramref name="y"/>.</summary>
    public static Vector3 Ground(this SN.Vector2 p, float y) => new(p.X, y, p.Y);
}

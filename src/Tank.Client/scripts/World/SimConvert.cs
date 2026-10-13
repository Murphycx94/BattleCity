using Godot;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>Tank.Sim uses System.Numerics with Godot's axes (Y up, -Z forward), so conversion is a plain copy.</summary>
public static class SimConvert
{
    public static Vector3 ToGodot(this SN.Vector3 v) => new(v.X, v.Y, v.Z);

    public static SN.Vector3 ToSim(this Vector3 v) => new(v.X, v.Y, v.Z);

    public static Quaternion ToGodot(this SN.Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    /// <summary>Ground point (x, z) lifted to height <paramref name="y"/>.</summary>
    public static Vector3 Ground(this SN.Vector2 p, float y) => new(p.X, y, p.Y);
}

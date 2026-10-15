using System.Numerics;

namespace Tank.Sim.Geometry;

/// <summary>A face of an <see cref="OrientedBox"/>, named by its outward normal in the box's own axes.</summary>
public enum BoxFace : byte { NegX, PosX, NegY, PosY, NegZ, PosZ }

/// <summary>
/// A box in 3D space: centre, half sizes along its own X, Y and Z axes, and the rotation from those axes to world space.
/// </summary>
public readonly record struct OrientedBox(Vector3 Center, Vector3 HalfExtents, Quaternion Rotation)
{
    /// <summary>Radius of the smallest sphere around the centre that holds the whole box.</summary>
    public float BoundingRadius => HalfExtents.Length();

    public static Vector3 LocalNormal(BoxFace face) => face switch
    {
        BoxFace.NegX => -Vector3.UnitX,
        BoxFace.PosX => Vector3.UnitX,
        BoxFace.NegY => -Vector3.UnitY,
        BoxFace.PosY => Vector3.UnitY,
        BoxFace.NegZ => -Vector3.UnitZ,
        _ => Vector3.UnitZ,
    };

    /// <summary>Outward normal of <paramref name="face"/> in world space.</summary>
    public Vector3 Normal(BoxFace face) => Vector3.Transform(LocalNormal(face), Rotation);

    /// <summary>A world point in the box's own axes, relative to its centre.</summary>
    public Vector3 ToLocal(Vector3 world) => Vector3.Transform(world - Center, Quaternion.Conjugate(Rotation));

    /// <summary>Distance from <paramref name="point"/> to the box (0 inside).</summary>
    public float DistanceTo(Vector3 point)
    {
        Vector3 local = Vector3.Abs(ToLocal(point));
        return Vector3.Max(local - HalfExtents, Vector3.Zero).Length();
    }

    /// <summary>
    /// Where segment <paramref name="from"/>→<paramref name="to"/> first touches the box: <paramref name="t"/> in [0, 1]
    /// and the face it enters through. A segment that starts inside reports t = 0 and the face it would have come in
    /// through: the one facing most squarely against its direction.
    /// </summary>
    public bool Raycast(Vector3 from, Vector3 to, out float t, out BoxFace face)
    {
        t = 0;
        face = BoxFace.NegZ;
        Quaternion inverse = Quaternion.Conjugate(Rotation);
        Vector3 start = Vector3.Transform(from - Center, inverse);
        Vector3 delta = Vector3.Transform(to - from, inverse);
        float tMin = 0f, tMax = 1f;
        int entryAxis = -1;
        bool entryPositive = false;
        for (int axis = 0; axis < 3; axis++)
        {
            float s = start[axis], d = delta[axis], h = HalfExtents[axis];
            if (MathF.Abs(d) < 1e-8f)
            {
                if (MathF.Abs(s) > h) return false;
                continue;
            }
            // Moving towards +axis enters through the −axis face, and the other way round.
            float near = ((d > 0 ? -h : h) - s) / d, far = ((d > 0 ? h : -h) - s) / d;
            if (near > tMin)
            {
                tMin = near;
                entryAxis = axis;
                entryPositive = d < 0;
            }
            tMax = MathF.Min(tMax, far);
            if (tMin > tMax) return false;
        }
        t = tMin;
        face = entryAxis < 0 ? FaceAgainst(delta) : FaceOf(entryAxis, entryPositive);
        return true;
    }

    /// <summary>The face whose outward normal points most directly against <paramref name="localDirection"/>.</summary>
    private static BoxFace FaceAgainst(Vector3 localDirection)
    {
        int best = 0;
        for (int axis = 1; axis < 3; axis++)
            if (MathF.Abs(localDirection[axis]) > MathF.Abs(localDirection[best])) best = axis;
        return FaceOf(best, localDirection[best] < 0);
    }

    private static BoxFace FaceOf(int axis, bool positive) => (BoxFace)(axis * 2 + (positive ? 1 : 0));
}

using System.Numerics;

namespace Tank.Sim.Geometry;

/// <summary>
/// Oriented rectangle on the ground. <see cref="HalfExtents"/>.X runs along <see cref="AxisRight"/> (width),
/// .Y along <see cref="AxisForward"/> (length). Center.Y is world Z.
/// </summary>
public readonly record struct Obb2(Vector2 Center, Vector2 HalfExtents, float Yaw)
{
    public Vector2 AxisRight => Angles.Right(Yaw);

    public Vector2 AxisForward => Angles.Forward(Yaw);

    public Obb2 WithCenter(Vector2 center) => this with { Center = center };

    /// <summary>Half the box's extent projected onto unit <paramref name="axis"/>.</summary>
    public float Radius(Vector2 axis) =>
        HalfExtents.X * MathF.Abs(Vector2.Dot(AxisRight, axis)) + HalfExtents.Y * MathF.Abs(Vector2.Dot(AxisForward, axis));

    public bool Contains(Vector2 point)
    {
        Vector2 d = point - Center;
        return MathF.Abs(Vector2.Dot(d, AxisRight)) <= HalfExtents.X && MathF.Abs(Vector2.Dot(d, AxisForward)) <= HalfExtents.Y;
    }

    /// <summary>Separating-axis test. On overlap, <paramref name="push"/> is the shortest move that takes <paramref name="a"/> out of <paramref name="b"/>.</summary>
    public static bool TryGetPenetration(in Obb2 a, in Obb2 b, out Vector2 push)
    {
        push = Vector2.Zero;
        float best = float.MaxValue;
        Vector2 d = a.Center - b.Center;
        Span<Vector2> axes = stackalloc Vector2[4] { a.AxisRight, a.AxisForward, b.AxisRight, b.AxisForward };
        foreach (Vector2 axis in axes)
        {
            float distance = Vector2.Dot(d, axis);
            float overlap = a.Radius(axis) + b.Radius(axis) - MathF.Abs(distance);
            if (overlap <= 0)
            {
                push = Vector2.Zero;
                return false;
            }
            if (overlap < best)
            {
                best = overlap;
                push = axis * (distance >= 0 ? overlap : -overlap);
            }
        }
        return true;
    }
}

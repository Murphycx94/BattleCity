using System.Numerics;

namespace Tank.Sim.Geometry;

public static class Intersect
{
    /// <summary>
    /// Where segment <paramref name="from"/>→<paramref name="to"/> first enters <paramref name="box"/> extruded
    /// vertically between <paramref name="bottom"/> and <paramref name="top"/>; <paramref name="t"/> is in [0, 1].
    /// </summary>
    public static bool SegmentPrism(in Obb2 box, float bottom, float top, Vector3 from, Vector3 to, out float t)
    {
        t = 0;
        Vector2 offset = new Vector2(from.X, from.Z) - box.Center;
        Vector2 delta = new(to.X - from.X, to.Z - from.Z);
        Vector2 right = box.AxisRight, forward = box.AxisForward;
        float tMin = 0f, tMax = 1f;
        if (!Slab(Vector2.Dot(offset, right), Vector2.Dot(delta, right), -box.HalfExtents.X, box.HalfExtents.X, ref tMin, ref tMax)) return false;
        if (!Slab(Vector2.Dot(offset, forward), Vector2.Dot(delta, forward), -box.HalfExtents.Y, box.HalfExtents.Y, ref tMin, ref tMax)) return false;
        if (!Slab(from.Y, to.Y - from.Y, bottom, top, ref tMin, ref tMax)) return false;
        t = tMin;
        return true;
    }

    /// <summary>
    /// Where segment <paramref name="from"/>→<paramref name="to"/> first enters the upright cylinder of radius
    /// <paramref name="radius"/> round <paramref name="center"/> (ground X, Z) between <paramref name="bottom"/> and
    /// <paramref name="top"/>; <paramref name="t"/> is in [0, 1] (0 when it starts inside).
    /// </summary>
    public static bool SegmentCylinder(Vector2 center, float radius, float bottom, float top, Vector3 from, Vector3 to, out float t)
    {
        t = 0;
        float tMin = 0f, tMax = 1f;
        if (!Slab(from.Y, to.Y - from.Y, bottom, top, ref tMin, ref tMax)) return false;
        Vector2 offset = new Vector2(from.X, from.Z) - center;
        Vector2 delta = new(to.X - from.X, to.Z - from.Z);
        float a = delta.LengthSquared(), b = Vector2.Dot(offset, delta), c = offset.LengthSquared() - radius * radius;
        if (a < 1e-12f)
        {
            if (c > 0) return false;   // a vertical segment: inside the circle or not at all
        }
        else
        {
            float discriminant = b * b - a * c;
            if (discriminant < 0) return false;
            float root = MathF.Sqrt(discriminant);
            tMin = MathF.Max(tMin, (-b - root) / a);
            tMax = MathF.Min(tMax, (-b + root) / a);
            if (tMin > tMax) return false;
        }
        t = tMin;
        return true;
    }

    private static bool Slab(float start, float delta, float min, float max, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(delta) < 1e-8f) return start >= min && start <= max;
        float t0 = (min - start) / delta, t1 = (max - start) / delta;
        if (t0 > t1) (t0, t1) = (t1, t0);
        tMin = MathF.Max(tMin, t0);
        tMax = MathF.Min(tMax, t1);
        return tMin <= tMax;
    }
}

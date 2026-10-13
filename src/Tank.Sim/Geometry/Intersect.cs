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

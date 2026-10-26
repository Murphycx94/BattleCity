using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Maps.Building;

/// <summary>Shaping helpers for height samples laid out like <see cref="Terrain.HeightField"/>'s: sample (i, j) at (i·cell, j·cell), index j·samplesX + i.</summary>
public static class HeightSculpt
{
    /// <summary>0 at or below <paramref name="edge0"/>, 1 at or above <paramref name="edge1"/>, the cubic t²(3 − 2t) between.</summary>
    public static float SmoothStep(float edge0, float edge1, float x)
    {
        if (edge1 == edge0) return x < edge0 ? 0f : 1f;
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// <see cref="Terrain.TerrainGenerator.Hill"/>'s round profile (1 − d²)² with d = <paramref name="distance"/> /
    /// <paramref name="radius"/>: 1 at the centre, easing to 0 at the radius and staying 0 beyond (and for a radius ≤ 0).
    /// </summary>
    public static float Bump(float distance, float radius)
    {
        if (!(radius > 0)) return 0f;
        float d = MathF.Abs(distance) / radius;
        if (d >= 1) return 0f;
        float s = 1 - d * d;
        return s * s;
    }

    /// <summary>
    /// Sets every sample inside <paramref name="area"/> to <paramref name="height"/>, and eases the samples within
    /// <paramref name="blend"/> m outside it back to their old heights (<see cref="SmoothStep"/> on the distance to the
    /// box). Samples farther out are not touched.
    /// </summary>
    /// <param name="heights">Row-major samples, changed in place; its length must be a whole number of rows of <paramref name="samplesX"/>.</param>
    public static void Flatten(float[] heights, int samplesX, float cellSize, in Obb2 area, float height, float blend)
    {
        ArgumentNullException.ThrowIfNull(heights);
        if (samplesX < 1 || heights.Length % samplesX != 0)
            throw new ArgumentException($"{heights.Length} heights are not whole rows of {samplesX}.", nameof(samplesX));
        if (!float.IsFinite(cellSize) || cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be finite and positive.");
        if (!float.IsFinite(height)) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be finite.");
        if (!float.IsFinite(blend) || blend < 0) throw new ArgumentOutOfRangeException(nameof(blend), blend, "Blend must be finite and not negative.");
        int samplesZ = heights.Length / samplesX;
        Vector2 right = area.AxisRight, forward = area.AxisForward, half = area.HalfExtents, center = area.Center;
        float reachX = area.Radius(Vector2.UnitX) + blend, reachZ = area.Radius(Vector2.UnitY) + blend;
        int i0 = Math.Max(0, (int)MathF.Floor((center.X - reachX) / cellSize)), i1 = Math.Min(samplesX - 1, (int)MathF.Ceiling((center.X + reachX) / cellSize));
        int j0 = Math.Max(0, (int)MathF.Floor((center.Y - reachZ) / cellSize)), j1 = Math.Min(samplesZ - 1, (int)MathF.Ceiling((center.Y + reachZ) / cellSize));
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            Vector2 d = new Vector2(i * cellSize, j * cellSize) - center;
            float across = MathF.Max(MathF.Abs(Vector2.Dot(d, right)) - half.X, 0f);
            float along = MathF.Max(MathF.Abs(Vector2.Dot(d, forward)) - half.Y, 0f);
            float distance = MathF.Sqrt(across * across + along * along);
            int at = j * samplesX + i;
            if (distance <= 0) heights[at] = height;
            else if (distance < blend) heights[at] = height + (heights[at] - height) * SmoothStep(0, blend, distance);
        }
    }
}

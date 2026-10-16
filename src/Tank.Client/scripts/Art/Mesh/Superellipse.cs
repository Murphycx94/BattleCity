using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// The superellipse |x|ⁿ + |z|ⁿ = 1 in polar form, the cross-section of a squarish lathe: n = 2 is the unit circle, a
/// larger n a squarer shape with flatter sides, always inside the square [−1, 1]² and touching it mid-side.
/// </summary>
public static class Superellipse
{
    /// <summary>Distance from the centre to the curve in the direction (<paramref name="cos"/>, <paramref name="sin"/>).</summary>
    public static float Radius(float cos, float sin, float n) =>
        MathF.Pow(MathF.Pow(MathF.Abs(cos), n) + MathF.Pow(MathF.Abs(sin), n), -1f / n);

    /// <summary>The curve's outward unit normal at the point in the direction (<paramref name="cos"/>, <paramref name="sin"/>).</summary>
    public static Vector2 Normal(float cos, float sin, float n) =>
        Vector2.Normalize(new Vector2(MathF.CopySign(MathF.Pow(MathF.Abs(cos), n - 1), cos), MathF.CopySign(MathF.Pow(MathF.Abs(sin), n - 1), sin)));
}

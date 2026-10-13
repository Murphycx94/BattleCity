namespace Tank.Sim.Geometry;

public static class Scalar
{
    /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> by at most <paramref name="maxStep"/> (≥ 0).</summary>
    public static float MoveTowards(float current, float target, float maxStep) =>
        MathF.Abs(target - current) <= maxStep ? target : current + MathF.CopySign(maxStep, target - current);
}

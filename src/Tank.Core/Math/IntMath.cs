namespace Tank.Core.Math;

public static class IntMath
{
    /// <summary>Floor division that is correct for negative numerators.</summary>
    public static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    public static int CeilDiv(int a, int b) => -FloorDiv(-a, b);

    /// <summary>Rounds to the nearest multiple of <paramref name="step"/>; ties round up.</summary>
    public static int RoundTo(int value, int step) => FloorDiv(value + step / 2, step) * step;

    public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
}

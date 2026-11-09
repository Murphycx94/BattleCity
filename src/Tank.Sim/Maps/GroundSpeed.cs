namespace Tank.Sim.Maps;

/// <summary>
/// Top-speed factors of the ground and of shallow water (riverland design §2.1–2.3): the tank's top speed, forward and
/// reverse, times the factor under its hull's centre. Water takes the ground's place wherever it stands.
/// </summary>
public static class GroundSpeed
{
    public const float Road = 1.15f, Sand = 0.8f;

    /// <summary>In water the factor is 1 − depth / <see cref="WaterDepthScale"/>, kept within <see cref="WaterMin"/> … <see cref="WaterMax"/>.</summary>
    public const float WaterDepthScale = 3f, WaterMin = 0.5f, WaterMax = 0.8f;

    /// <summary>The dry ground's factor: road 1.15, sand 0.8, grass and dirt 1.</summary>
    public static float Of(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Road => Road,
        SurfaceKind.Sand => Sand,
        _ => 1f,
    };

    /// <summary>The factor in water <paramref name="depth"/> m deep: 0.8 to 0.6 m, 0.6 at 1.2 m, 0.5 from 1.5 m.</summary>
    public static float InWater(float depth) => System.Math.Clamp(1f - depth / WaterDepthScale, WaterMin, WaterMax);

    /// <summary>The water's factor where there is water (<paramref name="waterDepth"/> &gt; 0), else the ground's.</summary>
    public static float Factor(SurfaceKind kind, float waterDepth) => waterDepth > 0f ? InWater(waterDepth) : Of(kind);
}

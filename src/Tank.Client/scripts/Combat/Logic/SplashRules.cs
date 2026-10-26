namespace Tank.Client.Combat;

/// <summary>
/// When the effects turn to water (riverland design §6.6): a tank wading kicks up spray instead of dust, and a shell
/// bursting on flooded ground near the surface raises a water column instead of smoke. Engine-free, so it is tested.
/// </summary>
public static class SplashRules
{
    /// <summary>Water deeper than this (m) under the hull centre: spray instead of dust.</summary>
    public const float WadeDepth = 0.05f;

    /// <summary>A wading tank keeps spraying until the water under its hull centre is this shallow (m) or less, so a depth jittering around <see cref="WadeDepth"/> on a shore doesn't flip dust and spray every frame.</summary>
    public const float WadeOutDepth = 0.03f;

    /// <summary>A burst at most this high (m) over the water, on ground under water, raises a column.</summary>
    public const float BurstAboveWater = 0.5f;

    private const float SprayStartSpeed = 3f / 3.6f, SprayFullSpeed = 45f / 3.6f;   // m/s: 3 and 45 km/h

    /// <summary>
    /// Whether a tank with <paramref name="depth"/> m of water under its hull centre is wading: deeper than
    /// <see cref="WadeDepth"/> to start, deeper than <see cref="WadeOutDepth"/> to go on (<paramref name="wasWading"/>).
    /// </summary>
    public static bool Wading(float depth, bool wasWading) => depth > (wasWading ? WadeOutDepth : WadeDepth);

    /// <summary>
    /// Whether a burst at height <paramref name="burstY"/>, over ground <paramref name="depthUnderBurst"/> m under water,
    /// splashes: the ground there is flooded and the burst is no more than <see cref="BurstAboveWater"/> over the surface
    /// (or under it: a shell striking the riverbed). Never on a map without water (<paramref name="waterLevel"/> null).
    /// </summary>
    public static bool IsWaterBurst(float depthUnderBurst, float burstY, float? waterLevel) =>
        waterLevel is { } level && depthUnderBurst > 0f && burstY <= level + BurstAboveWater;

    /// <summary>How hard a wading tank sprays at <paramref name="speed"/> (m/s, either way): 0 below 3 km/h, rising linearly to 1 at 45 km/h.</summary>
    public static float SprayStrength(float speed) =>
        Math.Clamp((MathF.Abs(speed) - SprayStartSpeed) / (SprayFullSpeed - SprayStartSpeed), 0f, 1f);
}

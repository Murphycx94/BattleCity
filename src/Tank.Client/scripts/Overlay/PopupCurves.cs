namespace Tank.Client.Overlay;

/// <summary>
/// Timing of the cartoon hit popups, engine-neutral so Tank.Art.Tests can check it. A verdict ("击穿！") pops in past
/// full size, springs back, holds and fades; a damage number pops smaller, floats up and fades. Ages in seconds.
/// </summary>
public static class PopupCurves
{
    public const float VerdictLife = 1.1f;
    public const float DamageLife = 1.3f;
    public const float FadeTime = 0.35f;
    public const float DamageRise = 2.0f;           // metres a damage number floats up over its life
    private const float PopIn = 0.12f, Settle = 0.25f;
    private const float VerdictPeak = 1.35f, DamagePeak = 1.15f;
    private const float VerdictStart = 0.2f, DamageStart = 0.5f;

    public static float VerdictScale(float age) => Pop(age, VerdictStart, VerdictPeak);

    public static float DamageScale(float age) => Pop(age, DamageStart, DamagePeak);

    /// <summary>1 until the last <see cref="FadeTime"/> s of <paramref name="life"/>, then down to 0.</summary>
    public static float Alpha(float age, float life) => Math.Clamp((life - age) / FadeTime, 0f, 1f);

    /// <summary>Height a damage number has floated, m: quick at first, easing out.</summary>
    public static float Rise(float age)
    {
        float t = Math.Clamp(age / DamageLife, 0f, 1f);
        return DamageRise * (1f - (1f - t) * (1f - t));
    }

    /// <summary>From <paramref name="start"/> up to <paramref name="peak"/> at <see cref="PopIn"/>, back to 1 at <see cref="Settle"/>, then 1.</summary>
    private static float Pop(float age, float start, float peak)
    {
        if (age <= 0f) return start;
        if (age < PopIn) return start + (peak - start) * Smooth(age / PopIn);
        if (age < Settle) return peak + (1f - peak) * Smooth((age - PopIn) / (Settle - PopIn));
        return 1f;
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}

namespace Tank.Client.World;

/// <summary>
/// The band of a base's ring on the ground (stage 5 design §3.4): from <see cref="Inner"/> to <see cref="Outer"/> m round
/// the base's centre, and the share of it (UV.x from <see cref="SolidFrom"/> to 1) that is the solid outer line.
/// </summary>
public readonly record struct RingBand(float Inner, float Outer, float SolidFrom)
{
    /// <summary>m: the band's full width, and how far its outer edge lies outside the base's edge.</summary>
    public const float Width = 1.8f, OuterOver = 0.2f;

    /// <summary>
    /// m: the width of the solid line along the band's outer edge. 0.2 m broke into dashes on the ring's far side seen
    /// by the follow camera from the spawn distance (it is under a pixel there); 0.4 m holds together.
    /// </summary>
    public const float SolidLine = 0.4f;

    /// <summary>
    /// The band of a base of <paramref name="radius"/> m: <see cref="OuterOver"/> outside its edge to <see cref="Width"/>
    /// inwards of that, stopping at the centre for a base smaller than that. The solid line is measured on the band there
    /// is, so it stays <see cref="SolidLine"/> m wide on a small base too.
    /// </summary>
    public static RingBand Around(float radius)
    {
        if (!(radius > 0f) || !float.IsFinite(radius))
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "A base's radius must be finite and above 0.");
        float outer = radius + OuterOver;
        float inner = MathF.Max(0f, outer - Width);
        return new RingBand(inner, outer, MathF.Max(0f, 1f - SolidLine / (outer - inner)));
    }
}

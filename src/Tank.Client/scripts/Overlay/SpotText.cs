using System.Text;
using Tank.Sim.Vision;

namespace Tank.Client.Overlay;

/// <summary>
/// What a spotting debug line shows, at the precision it shows it (whole metres, concealment in hundredths), so two
/// looks with equal lines read the same and the text needn't be rebuilt.
/// </summary>
public readonly record struct SpotLine(bool Seen, bool Lingering, int Distance, int DetectionRange, int CamoHundredths, int Bushes, bool Fired, bool Blocked);

/// <summary>
/// The spotting debug line over an enemy (stage 3 design §4.3): seen or not ("保持中" when not seen now but still
/// shown, for up to 2 s after it was last seen), distance and detection range, and the effective concealment with what
/// went into it — "看不见  109 m / 66 m  隐蔽 0.80（草×2）". The bracket lists the bushes counted, a recent shot
/// ("开火") and both sight lines blocked ("挡住", only past the 15 m rule); it is left out when there is nothing to
/// list. Engine-neutral so Tank.Art.Tests can check it.
/// </summary>
public static class SpotText
{
    /// <param name="lingering">The tank is still shown, though not seen now.</param>
    public static SpotLine Of(in Sighting s, bool lingering = false) => new(
        s.Seen, lingering && !s.Seen, (int)MathF.Round(s.Distance), (int)MathF.Round(s.DetectionRange), (int)MathF.Round(s.Camo * 100f),
        s.Bushes, s.FiredRecently, !s.LineClear && s.Distance > Spotting.DirectRange);

    /// <param name="lingering">The tank is still shown, though not seen now.</param>
    public static string Format(in Sighting s, bool lingering = false) => Format(Of(s, lingering));

    public static string Format(in SpotLine line)
    {
        var text = new StringBuilder(48);
        text.Append(line.Seen ? "看见" : line.Lingering ? "保持中" : "看不见").Append("  ")
            .Append(line.Distance).Append(" m / ").Append(line.DetectionRange).Append(" m  隐蔽 ")
            .Append(line.CamoHundredths / 100).Append('.');
        if (line.CamoHundredths % 100 < 10) text.Append('0');
        text.Append(line.CamoHundredths % 100);
        bool any = false;
        if (line.Bushes > 0) Note(text, ref any).Append("草×").Append(line.Bushes);
        if (line.Fired) Note(text, ref any).Append("开火");
        if (line.Blocked) Note(text, ref any).Append("挡住");
        if (any) text.Append('）');
        return text.ToString();
    }

    /// <summary>Opens the bracket before the first note and puts a space before each later one.</summary>
    private static StringBuilder Note(StringBuilder text, ref bool any)
    {
        text.Append(any ? ' ' : '（');
        any = true;
        return text;
    }
}

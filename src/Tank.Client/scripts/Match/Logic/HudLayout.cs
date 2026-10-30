using System.Numerics;

namespace Tank.Client.Match;

/// <summary>A screen rectangle in the HUD's base units (the 1600×900 canvas, stretched to the window).</summary>
public readonly record struct HudRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public Vector2 Position => new(X, Y);
    public Vector2 Size => new(Width, Height);
    public float CenterX => X + Width / 2;

    public bool Intersects(HudRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

    /// <summary>Whether the rectangle lies wholly inside a screen of <paramref name="screen"/>.</summary>
    public bool FitsIn(Vector2 screen) => X >= 0 && Y >= 0 && Right <= screen.X && Bottom <= screen.Y;
}

/// <summary>Where every HUD block goes on one screen size. <c>ZoneLine</c> and <c>ZoneWarning</c> are a royale's (stage 6 design §3.3).</summary>
public readonly record struct HudBoxes(
    HudRect ScorePlate, HudRect ModeLabel, HudRect KillFeed, HudRect NetPanel, HudRect Banner, HudRect SpectateBar,
    HudRect Scoreboard, HudRect StatusPanel, HudRect Results, HudRect ResultsTable, HudRect CaptureBars, HudRect Minimap, HudRect FullMap,
    HudRect ZoneLine, HudRect ZoneWarning)
{
    /// <summary>Row <paramref name="index"/> (0 = top) of the kill feed.</summary>
    public HudRect KillRow(int index) => new(KillFeed.X, KillFeed.Y + index * (HudLayout.KillRowHeight + HudLayout.KillRowGap), KillFeed.Width, HudLayout.KillRowHeight);

    /// <summary>Row <paramref name="index"/> (0 = top, 1 under it) of the capture bars.</summary>
    public HudRect CaptureRow(int index) => new(CaptureBars.X, CaptureBars.Y + index * (HudLayout.CaptureBarHeight + HudLayout.CaptureBarGap), CaptureBars.Width, HudLayout.CaptureBarHeight);
}

/// <summary>
/// The HUD's layout (blueprint §4.2) in base units: blocks hang off the screen's edges and centre, so a wider or taller
/// window just moves them; only the banner scales with the height (0.28 of it down). The 1600×900 canvas is what
/// <c>canvas_items</c> + <c>expand</c> gives the HUD whatever the window, so nothing here scales by the window's pixels.
/// </summary>
public static class HudLayout
{
    public const float Margin = 24f;
    public const float Top = 16f;

    public const float PlateWidth = 360f, PlateHeight = 72f;
    public const float PlateSideWidth = 112f;   // each score block; the clock takes what is left in the middle
    public const float ModeLabelHeight = 24f, ModeLabelGap = 4f;

    public const int KillRows = 5;
    public const float KillWidth = 440f, KillRowHeight = 36f, KillRowGap = 6f;

    // The capture bars (stage 5 design §3.4) hang under the mode label (which ends at 116), as wide as the plate: two rows at most.
    public const float CaptureTop = 124f, CaptureBarHeight = 26f, CaptureBarGap = 6f;
    public const int CaptureRows = 2;

    /// <summary>The zone line (stage 6 design §3.3) hangs under the mode label where a standard battle's capture bars go, as wide as the plate.</summary>
    public const float ZoneLineHeight = 28f;

    /// <summary>The outside warning: centred, this far down the screen (between the zone line and the banner's 0.28).</summary>
    public const float ZoneWarningWidth = 600f, ZoneWarningHeight = 44f, ZoneWarningTopShare = 0.18f;

    public const float NetWidth = 500f, NetHeight = 280f;

    public const float BannerWidth = 420f, BannerHeight = 210f, BannerTopShare = 0.28f;

    public const float SpectateWidth = 680f, SpectateHeight = 56f, SpectateFromBottom = 36f;

    // The board hangs below the mode label (which ends at 116), clear of it.
    public const float ScoreboardWidth = 900f, ScoreboardTop = 120f, ScoreboardHeight = 576f;

    // The results screen (§4.2): a 960-wide column from y 60; title and subtitle baselines, the table (the scoreboard's, at its own
    // top) and the footer's baseline are measured from the column's top.
    public const float ResultsWidth = 960f, ResultsTop = 60f, ResultsHeight = 780f;
    public const float ResultsTitleBaseline = 80f, ResultsSubtitleBaseline = 128f, ResultsTableTop = 164f, ResultsFooterBaseline = 752f;

    /// <summary>The status panel's room, its vehicle name above and the lamp included (see <c>StatusPanel</c>: 24 from the edges, 178 tall, about 360 wide).</summary>
    public const float StatusWidth = 360f, StatusHeight = 178f, StatusNameRoom = 36f;

    /// <summary>The corner map (stage 5 design §5): a square this wide, <see cref="Margin"/> in from the right and bottom edges.</summary>
    public const float MinimapSize = 240f;

    /// <summary>
    /// The full map: the largest square that leaves this much above and below (and at the sides), centred across. The
    /// bottom margin is wider than <see cref="FullMapHintHeight"/>, so the hint's strip under the square stays on screen.
    /// </summary>
    public const float FullMapMargin = 64f;

    /// <summary>The strip under the full map's square that holds "M / Esc 关闭": inside the bottom <see cref="FullMapMargin"/>.</summary>
    public const float FullMapHintHeight = 28f;

    public static HudBoxes For(Vector2 size)
    {
        float cx = size.X / 2;
        var plate = new HudRect(cx - PlateWidth / 2, Top, PlateWidth, PlateHeight);
        var label = new HudRect(plate.X, plate.Bottom + ModeLabelGap, PlateWidth, ModeLabelHeight);
        var results = new HudRect(cx - ResultsWidth / 2, ResultsTop, ResultsWidth, ResultsHeight);
        float feedHeight = KillRows * KillRowHeight + (KillRows - 1) * KillRowGap;
        float fullSide = MathF.Max(0, MathF.Min(size.X, size.Y) - 2 * FullMapMargin);   // a window too small for the margins gets an empty square
        return new HudBoxes(
            plate,
            label,
            new HudRect(size.X - Margin - KillWidth, Top, KillWidth, feedHeight),
            new HudRect(Margin, Top, NetWidth, NetHeight),
            new HudRect(cx - BannerWidth / 2, size.Y * BannerTopShare, BannerWidth, BannerHeight),
            new HudRect(cx - SpectateWidth / 2, size.Y - SpectateFromBottom - SpectateHeight, SpectateWidth, SpectateHeight),
            new HudRect(cx - ScoreboardWidth / 2, ScoreboardTop, ScoreboardWidth, ScoreboardHeight),
            new HudRect(Margin, size.Y - Margin - StatusHeight - StatusNameRoom, StatusWidth, StatusHeight + StatusNameRoom),
            results,
            new HudRect(cx - ScoreboardWidth / 2, results.Y + ResultsTableTop, ScoreboardWidth, ScoreboardHeight),
            new HudRect(plate.X, CaptureTop, PlateWidth, CaptureRows * CaptureBarHeight + (CaptureRows - 1) * CaptureBarGap),
            new HudRect(size.X - Margin - MinimapSize, size.Y - Margin - MinimapSize, MinimapSize, MinimapSize),
            new HudRect(cx - fullSide / 2, FullMapMargin, fullSide, fullSide),
            new HudRect(plate.X, CaptureTop, PlateWidth, ZoneLineHeight),
            new HudRect(cx - ZoneWarningWidth / 2, size.Y * ZoneWarningTopShare, ZoneWarningWidth, ZoneWarningHeight));
    }
}

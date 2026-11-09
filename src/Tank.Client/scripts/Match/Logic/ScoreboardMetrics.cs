namespace Tank.Client.Match;

/// <summary>
/// The scoreboard's vertical sizes (blueprint §4.2; stage 6 design §3.3): a row's height and the body and status text
/// sizes. <see cref="Regular"/> is the ten-seat modes' table as it has always been; <see cref="Compact"/> is a royale's
/// sixteen rows, which must fit the same <see cref="HudLayout.ScoreboardHeight"/> (56 + 16 × 30 + 14 = 550 ≤ 576) and,
/// at the results position, end above the footer's line. The header, the team banners and the bottom padding keep
/// their heights in both.
/// </summary>
public readonly record struct ScoreboardMetrics(float RowHeight, int BodySize, int StatusSize)
{
    public const float HeaderHeight = 56f, TeamBannerHeight = 38f, PadBottom = 14f;

    /// <summary>A board with more rows than this is drawn <see cref="Compact"/>.</summary>
    public const int RegularRows = 10;

    public static readonly ScoreboardMetrics Regular = new(RowHeight: 42f, BodySize: 24, StatusSize: 22);
    public static readonly ScoreboardMetrics Compact = new(RowHeight: 30f, BodySize: 20, StatusSize: 18);

    /// <summary>The sizes for a board of <paramref name="rows"/> player lines.</summary>
    public static ScoreboardMetrics For(int rows) => rows > RegularRows ? Compact : Regular;

    /// <summary>The panel's height: the header, <paramref name="groups"/> team banners, <paramref name="rows"/> lines and the padding under them.</summary>
    public float Height(int groups, int rows) => HeaderHeight + groups * TeamBannerHeight + rows * RowHeight + PadBottom;
}

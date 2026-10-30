using System.Numerics;
using Tank.Client.Match;

namespace Tank.Art.Tests;

public class ScoreboardMetricsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void Up_to_ten_rows_keep_the_regular_sizes(int rows)
    {
        ScoreboardMetrics m = ScoreboardMetrics.For(rows);

        Assert.Equal(ScoreboardMetrics.Regular, m);
        Assert.Equal(42f, m.RowHeight);
        Assert.Equal(24, m.BodySize);
        Assert.Equal(22, m.StatusSize);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(16)]
    public void More_than_ten_rows_go_compact(int rows)
    {
        ScoreboardMetrics m = ScoreboardMetrics.For(rows);

        Assert.Equal(ScoreboardMetrics.Compact, m);
        Assert.Equal(30f, m.RowHeight);
        Assert.Equal(20, m.BodySize);
        Assert.Equal(18, m.StatusSize);
    }

    [Fact]
    public void The_height_is_the_header_the_banners_the_rows_and_the_bottom_padding()
    {
        Assert.Equal(56 + 2 * 38 + 10 * 42 + 14, ScoreboardMetrics.Regular.Height(groups: 2, rows: 10));
        Assert.Equal(56 + 16 * 30 + 14, ScoreboardMetrics.Compact.Height(groups: 0, rows: 16));
    }

    [Fact]
    public void A_full_team_board_and_a_full_royale_board_fit_the_scoreboard_box()
    {
        Assert.True(ScoreboardMetrics.Regular.Height(2, 10) <= HudLayout.ScoreboardHeight);
        Assert.True(ScoreboardMetrics.For(16).Height(0, 16) <= HudLayout.ScoreboardHeight);
    }

    [Fact]
    public void A_full_royale_results_table_ends_above_the_footer_line()
    {
        HudBoxes b = HudLayout.For(new Vector2(1600, 900));
        float bottom = b.ResultsTable.Y + ScoreboardMetrics.For(16).Height(0, 16);
        float footer = b.Results.Y + HudLayout.ResultsFooterBaseline;

        const float FooterTextSize = 26f;   // CenterBanner.ResultsFooterSize: the footer's glyphs rise about this far above its baseline
        Assert.True(bottom + FooterTextSize <= footer, $"table bottom {bottom}, footer baseline {footer}");
    }
}

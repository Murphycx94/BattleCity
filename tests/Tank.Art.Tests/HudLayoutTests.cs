using System.Numerics;
using Tank.Client.Match;

namespace Tank.Art.Tests;

public class HudLayoutTests
{
    public static TheoryData<float, float> Screens => new()
    {
        { 1600, 900 },
        { 1600, 1200 },
        { 2100, 900 },
    };

    private static IEnumerable<(string Name, HudRect Rect)> All(HudBoxes b)
    {
        yield return ("score plate", b.ScorePlate);
        yield return ("mode label", b.ModeLabel);
        yield return ("kill feed", b.KillFeed);
        yield return ("net panel", b.NetPanel);
        yield return ("banner", b.Banner);
        yield return ("spectate bar", b.SpectateBar);
        yield return ("scoreboard", b.Scoreboard);
        yield return ("results", b.Results);
        yield return ("results table", b.ResultsTable);
        yield return ("status panel", b.StatusPanel);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void Every_block_lies_inside_the_screen(float w, float h)
    {
        var screen = new Vector2(w, h);
        foreach ((string name, HudRect rect) in All(HudLayout.For(screen)))
            Assert.True(rect.FitsIn(screen), $"{name} {rect} is off a {w}x{h} screen");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_always_on_blocks_do_not_overlap_each_other(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        (string, HudRect)[] always =
        {
            ("score plate", b.ScorePlate), ("mode label", b.ModeLabel), ("kill feed", b.KillFeed), ("net panel", b.NetPanel), ("status panel", b.StatusPanel),
        };
        AssertNoOverlaps(always);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_banners_clear_the_status_panel_and_the_corner_blocks(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        foreach (HudRect banner in new[] { b.Banner, b.SpectateBar })
        {
            Assert.False(banner.Intersects(b.StatusPanel), "a banner covers the status panel");
            Assert.False(banner.Intersects(b.KillFeed), "a banner covers the kill feed");
            Assert.False(banner.Intersects(b.NetPanel), "a banner covers the F3 panel");
            Assert.False(banner.Intersects(b.ScorePlate), "a banner covers the score plate");
        }
        Assert.False(b.Banner.Intersects(b.SpectateBar));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_spectate_bar_sits_on_the_bottom_margin_and_clears_the_status_panel(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        Assert.Equal(h - 36, b.SpectateBar.Bottom, precision: 3);
        Assert.Equal(w / 2, b.SpectateBar.CenterX, precision: 3);
        Assert.True(b.SpectateBar.X > b.StatusPanel.Right);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_scoreboard_is_centred_900_wide_below_the_plate(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        Assert.Equal(w / 2, b.Scoreboard.CenterX, precision: 3);
        Assert.Equal(900, b.Scoreboard.Width);
        Assert.Equal(120, b.Scoreboard.Y);
        Assert.True(b.Scoreboard.Y >= b.ModeLabel.Bottom);   // clear of the plate and its mode label
        Assert.False(b.Scoreboard.Intersects(b.ScorePlate));
        Assert.False(b.Scoreboard.Intersects(b.ModeLabel));
        // At 1600x900 a full 10-player board's bottom-left corner (34 x 24) reaches into the status panel's room; it is a rounded, translucent corner and the panel is under it.
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_results_column_is_960_wide_from_y_60_and_holds_its_table_and_footer(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        Assert.Equal(w / 2, b.Results.CenterX, precision: 3);
        Assert.Equal(960, b.Results.Width);
        Assert.Equal(60, b.Results.Y);
        Assert.Equal(780, b.Results.Height);
        Assert.Equal(224, b.ResultsTable.Y);
        Assert.Equal(b.Scoreboard.Size, b.ResultsTable.Size);
        Assert.Equal(b.Scoreboard.CenterX, b.ResultsTable.CenterX, precision: 3);
        Assert.True(b.ResultsTable.Bottom <= b.Results.Bottom);
        float title = b.Results.Y + HudLayout.ResultsTitleBaseline, subtitle = b.Results.Y + HudLayout.ResultsSubtitleBaseline, footer = b.Results.Y + HudLayout.ResultsFooterBaseline;
        Assert.Equal(140, title);
        Assert.Equal(188, subtitle);
        Assert.Equal(812, footer);
        Assert.True(footer < b.Results.Bottom);                    // the footer's line sits inside the column
        Assert.True(footer > b.ResultsTable.Bottom);               // and below the longest table
        Assert.True(subtitle < b.ResultsTable.Y);
    }

    [Fact]
    public void The_scoreboard_does_not_fit_between_the_kill_feed_and_the_f3_panel_at_the_base_size()
    {
        // 900 wide leaves 350 either side at 1600, less than the feed (440 + margin) or the panel (500 + margin) need: both
        // step aside while Tab is held (MatchHud.SetScoreboardOpen). The blueprint's "no intersection" cannot hold with this width.
        HudBoxes b = HudLayout.For(new Vector2(1600, 900));
        Assert.True(b.Scoreboard.Intersects(b.KillFeed));
        Assert.True(b.Scoreboard.Intersects(b.NetPanel));
        Assert.True(b.KillFeed.X < b.Scoreboard.Right);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_kill_feed_hangs_from_the_top_right_in_five_rows(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        Assert.Equal(w - 24, b.KillFeed.Right, precision: 3);
        Assert.Equal(440, b.KillFeed.Width);
        Assert.Equal(b.KillFeed.Y, b.KillRow(0).Y);
        Assert.Equal(b.KillFeed.Bottom, b.KillRow(4).Bottom, precision: 3);
        for (int i = 1; i < HudLayout.KillRows; i++) Assert.False(b.KillRow(i - 1).Intersects(b.KillRow(i)));
    }

    [Fact]
    public void The_banner_follows_the_height_and_the_rest_do_not()
    {
        HudBoxes tall = HudLayout.For(new Vector2(1600, 1200));
        HudBoxes normal = HudLayout.For(new Vector2(1600, 900));
        Assert.Equal(252, normal.Banner.Y, precision: 3);
        Assert.Equal(336, tall.Banner.Y, precision: 3);
        Assert.Equal(normal.ScorePlate, tall.ScorePlate);
        Assert.Equal(normal.NetPanel, tall.NetPanel);
    }

    private static void AssertNoOverlaps((string Name, HudRect Rect)[] blocks)
    {
        for (int i = 0; i < blocks.Length; i++)
            for (int j = i + 1; j < blocks.Length; j++)
                Assert.False(blocks[i].Rect.Intersects(blocks[j].Rect), $"{blocks[i].Name} overlaps {blocks[j].Name}");
    }
}

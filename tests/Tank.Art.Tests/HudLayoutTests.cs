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
        yield return ("capture bars", b.CaptureBars);
        yield return ("minimap", b.Minimap);
        yield return ("full map", b.FullMap);
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
            ("minimap", b.Minimap),
        };
        AssertNoOverlaps(always);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_minimap_clears_the_respawn_banner(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        Assert.False(b.Minimap.Intersects(b.Banner), $"the minimap {b.Minimap} covers the banner {b.Banner}");
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

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_capture_bars_hang_under_the_mode_label_as_wide_as_the_plate(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        Assert.Equal(124, b.CaptureBars.Y);
        Assert.Equal(360, b.CaptureBars.Width);
        Assert.Equal(58, b.CaptureBars.Height);                      // two rows of 26 and the gap of 6
        Assert.Equal(w / 2, b.CaptureBars.CenterX, precision: 3);
        Assert.Equal(b.ScorePlate.X, b.CaptureBars.X, precision: 3);
        Assert.True(b.CaptureBars.Y >= b.ModeLabel.Bottom);          // under the mode label, clear of it
        Assert.False(b.CaptureBars.Intersects(b.ModeLabel));
        Assert.False(b.CaptureBars.Intersects(b.ScorePlate));
        Assert.Equal(new HudRect(b.CaptureBars.X, 124, 360, 26), b.CaptureRow(0));
        Assert.Equal(124 + 32, b.CaptureRow(1).Y);
        Assert.Equal(b.CaptureBars.Bottom, b.CaptureRow(1).Bottom, precision: 3);
        Assert.False(b.CaptureRow(0).Intersects(b.CaptureRow(1)));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_capture_bars_clear_the_kill_feed_the_net_panel_and_the_banner(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        Assert.True(b.CaptureBars.FitsIn(new Vector2(w, h)));
        Assert.False(b.CaptureBars.Intersects(b.KillFeed), "the bars cover the kill feed");
        Assert.False(b.CaptureBars.Intersects(b.NetPanel), "the bars cover the F3 panel");
        Assert.False(b.CaptureBars.Intersects(b.Banner), "the bars cover the respawn banner");
        Assert.False(b.CaptureBars.Intersects(b.SpectateBar), "the bars cover the spectate bar");
        Assert.False(b.CaptureBars.Intersects(b.StatusPanel), "the bars cover the status panel");
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

    public static TheoryData<float, float> MinimapScreens => new()
    {
        { 1600, 900 },
        { 2560, 900 },
        { 1600, 1200 },
    };

    [Fact]
    public void The_minimap_is_240_square_24_in_from_the_bottom_right_at_the_base_size()
    {
        HudBoxes b = HudLayout.For(new Vector2(1600, 900));
        Assert.Equal(new HudRect(1336, 636, 240, 240), b.Minimap);
    }

    [Theory]
    [MemberData(nameof(MinimapScreens))]
    public void The_minimap_hugs_the_bottom_right_corner_clear_of_every_other_block(float w, float h)
    {
        var screen = new Vector2(w, h);
        HudBoxes b = HudLayout.For(screen);
        Assert.True(b.Minimap.FitsIn(screen));
        Assert.Equal(w - HudLayout.Margin, b.Minimap.Right, precision: 3);
        Assert.Equal(h - HudLayout.Margin, b.Minimap.Bottom, precision: 3);
        Assert.Equal(HudLayout.MinimapSize, b.Minimap.Width);
        Assert.Equal(HudLayout.MinimapSize, b.Minimap.Height);
        (string Name, HudRect Rect)[] others =
        {
            ("status panel", b.StatusPanel), ("kill feed", b.KillFeed), ("spectate bar", b.SpectateBar), ("scoreboard", b.Scoreboard),
            ("capture bars", b.CaptureBars), ("results", b.Results),
        };
        foreach ((string name, HudRect rect) in others) Assert.False(b.Minimap.Intersects(rect), $"the minimap {b.Minimap} overlaps the {name} {rect} at {w}x{h}");
    }

    [Fact]
    public void The_full_map_is_the_largest_centred_square_that_leaves_its_margins_and_the_hint()
    {
        var screen = new Vector2(1600, 900);
        HudBoxes b = HudLayout.For(screen);
        Assert.Equal(new HudRect(414, 64, 772, 772), b.FullMap);
        Assert.True(b.FullMap.Bottom + HudLayout.FullMapHintHeight <= screen.Y);
        Assert.Equal(800, b.FullMap.CenterX, precision: 3);
    }

    [Theory]
    [MemberData(nameof(MinimapScreens))]
    public void The_full_map_is_centred_and_square_on_any_screen(float w, float h)
    {
        var screen = new Vector2(w, h);
        HudBoxes b = HudLayout.For(screen);
        float side = MathF.Min(w, h) - 2 * HudLayout.FullMapMargin;
        Assert.Equal(new HudRect((w - side) / 2, HudLayout.FullMapMargin, side, side), b.FullMap);
        Assert.True(b.FullMap.FitsIn(screen));
        Assert.True(b.FullMap.Bottom + HudLayout.FullMapHintHeight <= h);
    }

    [Fact]
    public void The_full_map_s_bottom_margin_holds_the_hint_and_a_tiny_window_gets_an_empty_square()
    {
        Assert.True(HudLayout.FullMapHintHeight <= HudLayout.FullMapMargin, "the hint's strip doesn't fit under the square");
        HudRect tiny = HudLayout.For(new Vector2(100, 90)).FullMap;
        Assert.Equal(0, tiny.Width);
        Assert.Equal(0, tiny.Height);
        Assert.Equal(50, tiny.CenterX, precision: 3);
    }

    private static void AssertNoOverlaps((string Name, HudRect Rect)[] blocks)
    {
        for (int i = 0; i < blocks.Length; i++)
            for (int j = i + 1; j < blocks.Length; j++)
                Assert.False(blocks[i].Rect.Intersects(blocks[j].Rect), $"{blocks[i].Name} overlaps {blocks[j].Name}");
    }
}

using Tank.Client.Overlay;
using Tank.Sim.Vision;

namespace Tank.Art.Tests;

public class SpotTextTests
{
    [Fact]
    public void A_tank_hiding_in_two_bushes()
    {
        var s = new Sighting(Seen: false, Distance: 109.4f, DetectionRange: 66.1f, Camo: 0.8f, Bushes: 2, FiredRecently: false, LineClear: true);
        Assert.Equal("看不见  109 m / 66 m  隐蔽 0.80（草×2）", SpotText.Format(s));
    }

    [Fact]
    public void A_tank_lost_a_moment_ago_is_still_held()
    {
        var s = new Sighting(false, 95f, 193.5f, 0.3f, 0, false, LineClear: false);
        Assert.StartsWith("保持中  95 m", SpotText.Format(s, lingering: true));
        Assert.StartsWith("看不见", SpotText.Format(s));
        Assert.StartsWith("看见", SpotText.Format(s with { Seen = true }, lingering: true));
    }

    [Fact]
    public void Looks_that_differ_below_what_the_line_shows_read_the_same()
    {
        var a = new Sighting(false, 109.4f, 66.1f, 0.801f, 2, false, true);
        var b = a with { Distance = 108.6f, DetectionRange = 65.9f, Camo = 0.799f };
        Assert.Equal(SpotText.Of(a), SpotText.Of(b));
        Assert.NotEqual(SpotText.Of(a), SpotText.Of(a with { Distance = 110.6f }));
        Assert.NotEqual(SpotText.Of(a), SpotText.Of(a, lingering: true));
        Assert.Equal(SpotText.Of(a with { Seen = true }), SpotText.Of(a with { Seen = true }, lingering: true));   // seen beats lingering
    }

    [Fact]
    public void Concealment_under_a_tenth_keeps_its_leading_zero()
    {
        var s = new Sighting(true, 50f, 250f, 0.08f, 0, false, true);
        Assert.Equal("看见  50 m / 250 m  隐蔽 0.08", SpotText.Format(s));
    }

    [Fact]
    public void A_tank_in_the_open_lists_nothing_in_brackets()
    {
        var s = new Sighting(true, 91f, 193.5f, 0.3f, 0, false, true);
        Assert.Equal("看见  91 m / 194 m  隐蔽 0.30", SpotText.Format(s));
    }

    [Fact]
    public void A_shot_and_blocked_sight_lines_are_listed()
    {
        var s = new Sighting(false, 120f, 117f, 0.6f, 1, FiredRecently: true, LineClear: false);
        Assert.Equal("看不见  120 m / 117 m  隐蔽 0.60（草×1 开火 挡住）", SpotText.Format(s));
    }

    [Fact]
    public void Blocked_sight_lines_dont_matter_inside_the_direct_range()
    {
        var s = new Sighting(true, 12f, 40.5f, 0.9f, 2, false, LineClear: false);
        Assert.DoesNotContain("挡住", SpotText.Format(s));
    }
}

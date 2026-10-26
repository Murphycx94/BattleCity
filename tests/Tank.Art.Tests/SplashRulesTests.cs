using Tank.Client.Combat;

namespace Tank.Art.Tests;

public class SplashRulesTests
{
    private const float Kmh = 1f / 3.6f;

    [Fact]
    public void Wading_starts_just_under_the_hull()
    {
        Assert.False(SplashRules.Wading(0.04f, wasWading: false));
        Assert.True(SplashRules.Wading(0.06f, wasWading: false));
    }

    [Fact]
    public void Wading_ends_only_in_shallower_water()
    {
        Assert.True(SplashRules.Wading(0.04f, wasWading: true));
        Assert.True(SplashRules.Wading(0.031f, wasWading: true));
        Assert.False(SplashRules.Wading(0.029f, wasWading: true));
        Assert.False(SplashRules.Wading(0f, wasWading: true));
        Assert.True(SplashRules.WadeOutDepth < SplashRules.WadeDepth);
    }

    [Fact]
    public void A_depth_jittering_on_the_shore_does_not_flicker()
    {
        float[] depths = { 0.04f, 0.049f, 0.051f, 0.046f, 0.055f, 0.042f, 0.052f, 0.035f, 0.048f, 0.02f, 0.045f, 0.049f };
        bool wading = false;
        var seen = new List<bool>();
        foreach (float depth in depths) seen.Add(wading = SplashRules.Wading(depth, wading));
        // In at 0.051, out only at 0.02, not again until deeper than 0.05: two switches instead of one a frame.
        Assert.Equal(new[] { false, false, true, true, true, true, true, true, true, false, false, false }, seen);
    }

    [Fact]
    public void A_burst_on_flooded_ground_near_the_surface_raises_a_column()
    {
        Assert.True(SplashRules.IsWaterBurst(0.8f, 0.3f, 0f));
        Assert.False(SplashRules.IsWaterBurst(0.8f, 0.6f, 0f));
        Assert.True(SplashRules.IsWaterBurst(0.8f, 2.3f, 2f));   // measured from the level, wherever it is
    }

    [Fact]
    public void Dry_ground_or_no_water_never_splashes()
    {
        Assert.False(SplashRules.IsWaterBurst(0f, 0.1f, 0f));
        Assert.False(SplashRules.IsWaterBurst(0.8f, 0.1f, null));
    }

    [Fact]
    public void A_burst_under_the_surface_still_splashes() => Assert.True(SplashRules.IsWaterBurst(0.8f, -0.4f, 0f));

    [Fact]
    public void Spray_grows_with_speed()
    {
        Assert.Equal(0f, SplashRules.SprayStrength(0f));
        Assert.Equal(0f, SplashRules.SprayStrength(2.9f * Kmh));
        Assert.Equal(1f, SplashRules.SprayStrength(45f * Kmh));
        Assert.Equal(1f, SplashRules.SprayStrength(70f * Kmh));
        float middle = SplashRules.SprayStrength(24f * Kmh);
        Assert.InRange(middle, 0.4f, 0.6f);
        Assert.Equal(middle, SplashRules.SprayStrength(-24f * Kmh));   // reversing through water splashes the same
    }
}

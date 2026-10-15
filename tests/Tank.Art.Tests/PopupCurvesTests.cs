using Tank.Client.Overlay;

namespace Tank.Art.Tests;

public class PopupCurvesTests
{
    [Fact]
    public void Verdict_pops_past_full_size_then_settles()
    {
        float peak = 0f;
        for (float t = 0; t < 0.3f; t += 0.005f) peak = MathF.Max(peak, PopupCurves.VerdictScale(t));
        Assert.True(peak > 1.25f, $"peak {peak}");
        Assert.True(PopupCurves.VerdictScale(0f) < 0.5f);
        Assert.Equal(1f, PopupCurves.VerdictScale(0.3f));
        Assert.Equal(1f, PopupCurves.VerdictScale(PopupCurves.VerdictLife));
    }

    [Fact]
    public void Damage_pops_less_than_a_verdict()
    {
        float peak = 0f;
        for (float t = 0; t < 0.3f; t += 0.005f) peak = MathF.Max(peak, PopupCurves.DamageScale(t));
        Assert.InRange(peak, 1.05f, 1.25f);
    }

    [Fact]
    public void Alpha_holds_then_fades_to_nothing()
    {
        Assert.Equal(1f, PopupCurves.Alpha(0.2f, PopupCurves.VerdictLife));
        Assert.InRange(PopupCurves.Alpha(PopupCurves.VerdictLife - PopupCurves.FadeTime / 2, PopupCurves.VerdictLife), 0.45f, 0.55f);
        Assert.Equal(0f, PopupCurves.Alpha(PopupCurves.VerdictLife, PopupCurves.VerdictLife));
    }

    [Fact]
    public void Rise_climbs_steadily_to_its_height()
    {
        Assert.Equal(0f, PopupCurves.Rise(0f));
        float last = 0f;
        for (float t = 0.05f; t <= PopupCurves.DamageLife; t += 0.05f)
        {
            float rise = PopupCurves.Rise(t);
            Assert.True(rise > last);
            last = rise;
        }
        Assert.Equal(PopupCurves.DamageRise, PopupCurves.Rise(PopupCurves.DamageLife), 3);
    }
}

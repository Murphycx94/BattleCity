using Tank.Client.World;

namespace Tank.Art.Tests;

/// <summary>The trees' and houses' fade when they are in the way of the view (riverland design §6.4, §6.5; as the bushes).</summary>
public class SeeThroughFadesTests
{
    private const float Frame = 0.05f;

    [Fact]
    public void An_item_in_the_way_fades_to_half_in_0_3_seconds_and_back()
    {
        var fades = new SeeThroughFades(3);
        var changed = new List<int>();
        float time = 0f;
        while (fades.OpacityOf(1) > SeeThroughFades.SeeThrough)
        {
            fades.BeginFrame();
            fades.MarkInWay(1);
            fades.Step(Frame, changed);
            Assert.Equal(new[] { 1 }, changed);
            time += Frame;
            Assert.True(time < 1f, "never got there");
        }
        Assert.InRange(time, SeeThroughFades.FadeSeconds - 1e-4f, SeeThroughFades.FadeSeconds + Frame + 1e-4f);   // to within a frame's rounding
        Assert.Equal(SeeThroughFades.SeeThrough, fades.OpacityOf(1));
        Assert.Equal(1f, fades.OpacityOf(0));
        Assert.Equal(1f, fades.OpacityOf(2));

        fades.BeginFrame();
        fades.MarkInWay(1);
        fades.Step(Frame, changed);
        Assert.Empty(changed);   // still in the way and there: nothing to upload
        Assert.Equal(1, fades.FadingCount);

        time = 0f;
        do
        {
            fades.BeginFrame();
            fades.Step(Frame, changed);
            Assert.Equal(new[] { 1 }, changed);
            time += Frame;
        } while (fades.OpacityOf(1) < 1f);
        Assert.InRange(time, SeeThroughFades.FadeSeconds - 1e-4f, SeeThroughFades.FadeSeconds + Frame + 1e-4f);   // to within a frame's rounding
        Assert.Equal(0, fades.FadingCount);   // back to solid and out of the way: dropped
        fades.BeginFrame();
        fades.Step(Frame, changed);
        Assert.Empty(changed);
    }

    [Fact]
    public void A_step_moves_by_the_fade_rate()
    {
        var fades = new SeeThroughFades(1);
        var changed = new List<int>();
        fades.BeginFrame();
        fades.MarkInWay(0);
        fades.MarkInWay(0);   // marked twice in a frame: still one item fading
        fades.Step(0.1f, changed);
        Assert.Equal(new[] { 0 }, changed);
        Assert.Equal(1, fades.FadingCount);
        Assert.Equal(1f - (1f - SeeThroughFades.SeeThrough) * 0.1f / SeeThroughFades.FadeSeconds, fades.OpacityOf(0), 1e-6f);
    }

    [Fact]
    public void Coming_back_into_the_way_turns_the_fade_round()
    {
        var fades = new SeeThroughFades(2);
        var changed = new List<int>();
        fades.BeginFrame();
        fades.MarkInWay(0);
        fades.Step(0.15f, changed);
        float half = fades.OpacityOf(0);
        fades.BeginFrame();
        fades.Step(0.05f, changed);   // out of the way: back up
        Assert.True(fades.OpacityOf(0) > half);
        fades.BeginFrame();
        fades.MarkInWay(0);
        fades.Step(0.1f, changed);    // in the way again: down
        Assert.True(fades.OpacityOf(0) < half);
        Assert.Equal(1, fades.FadingCount);
    }
}

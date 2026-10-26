using System.Numerics;
using Tank.Client.World;

namespace Tank.Art.Tests;

/// <summary>
/// A tree, house or fence going down as the views show it: the push may come before the battlefield says it is down
/// (the test drive) or after (a match: the prop state is applied as it arrives, its event when the render clock gets
/// there), and the first look after a view is built shows what is already down at its end.
/// </summary>
public class PropDownTests
{
    [Fact]
    public void A_push_before_the_fall_is_kept_for_it()
    {
        var down = new PropDown(1.2f);
        Assert.True(down.Standing);
        Assert.False(down.Pushed(new Vector2(1f, 0f)), "still standing: nothing to aim again yet");
        Assert.Equal(PropChange.Fell, down.See(standing: false, snap: false));
        Assert.Equal(new Vector2(1f, 0f), down.Push);
        Assert.True(down.Moving);
        Assert.Equal(0f, down.Elapsed);
    }

    [Fact]
    public void A_push_after_the_fall_asks_for_it_to_be_aimed_again()
    {
        var down = new PropDown(1.2f);
        Assert.Equal(PropChange.Fell, down.See(standing: false, snap: false));
        Assert.Null(down.Push);
        Assert.False(down.Step(0.5f));
        Assert.True(down.Pushed(new Vector2(0f, -2f)), "on its way down: aim it the pushed way from here");
        Assert.Equal(new Vector2(0f, -2f), down.Push);
        Assert.True(down.Moving);
        Assert.True(down.Step(1f));
        Assert.True(down.AtRest);
        Assert.True(down.Pushed(new Vector2(1f, 1f)), "at rest: laid the pushed way at once");
        Assert.True(down.AtRest);
    }

    [Fact]
    public void The_first_look_puts_a_prop_already_down_at_its_end()
    {
        var down = new PropDown(0.5f);
        Assert.Equal(PropChange.Fell, down.See(standing: false, snap: true));
        Assert.False(down.Moving);
        Assert.True(down.AtRest);
        Assert.Equal(0.5f, down.Elapsed);
    }

    [Fact]
    public void Standing_up_again_forgets_the_push()
    {
        var down = new PropDown(0.35f);
        down.Pushed(new Vector2(3f, 4f));
        down.See(standing: false, snap: false);
        Assert.Equal(PropChange.Rose, down.See(standing: true, snap: false));
        Assert.True(down.Standing);
        Assert.False(down.Moving);
        Assert.False(down.AtRest);
        Assert.Null(down.Push);
        Assert.Equal(-1f, down.Elapsed);
    }

    [Fact]
    public void Nothing_changes_while_the_battlefield_agrees()
    {
        var down = new PropDown(1.2f);
        Assert.Equal(PropChange.None, down.See(standing: true, snap: true));
        Assert.True(down.Standing);
        down.See(standing: false, snap: false);
        down.Step(0.3f);
        Assert.Equal(PropChange.None, down.See(standing: false, snap: true));
        Assert.Equal(0.3f, down.Elapsed, 1e-6f);
    }

    [Fact]
    public void It_comes_to_rest_after_its_duration()
    {
        var down = new PropDown(0.5f);
        down.See(standing: false, snap: false);
        Assert.False(down.Step(0.2f));
        Assert.False(down.Step(0.2f));
        Assert.True(down.Step(0.2f));
        Assert.False(down.Moving);
        Assert.True(down.AtRest);
    }
}

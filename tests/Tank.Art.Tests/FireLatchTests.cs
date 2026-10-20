using Tank.Client.Driving;

namespace Tank.Art.Tests;

public class FireLatchTests
{
    [Fact]
    public void Nothing_pressed_means_no_fire() => Assert.False(new FireLatch().Peek());

    [Fact]
    public void A_press_latches_and_peeking_does_not_clear_it()
    {
        var latch = new FireLatch();
        latch.Press();
        Assert.True(latch.Peek());
        Assert.True(latch.Peek());   // a frame without a tick looked and must leave the click for the next frame
    }

    [Fact]
    public void A_tap_released_before_the_tick_still_fires_once_then_ends_with_the_tick()
    {
        var latch = new FireLatch();
        latch.Press();
        latch.Release();
        Assert.True(latch.Peek());
        latch.EndTick();
        Assert.False(latch.Peek());
    }

    [Fact]
    public void A_held_button_stays_true_across_ticks_until_released()
    {
        var latch = new FireLatch();
        latch.Press();
        latch.EndTick();
        Assert.True(latch.Peek());
        latch.EndTick();
        Assert.True(latch.Peek());
        latch.Release();
        Assert.False(latch.Peek());
    }

    [Fact]
    public void Clear_drops_a_latched_and_a_held_press()
    {
        var latch = new FireLatch();
        latch.Press();
        latch.Clear();
        Assert.False(latch.Peek());
    }
}

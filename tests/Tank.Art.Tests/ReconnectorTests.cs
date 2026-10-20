using Tank.Client.Online;

namespace Tank.Art.Tests;

public class ReconnectorTests
{
    private const string Dropped = "Timeout";

    [Fact]
    public void A_timeout_of_a_seated_guest_schedules_an_attempt_two_seconds_later()
    {
        var r = new Reconnector();

        Assert.Equal(Reconnector.Step.Wait, r.Observe(Dropped, hadSeat: true, now: 10));
        Assert.Equal(1, r.Attempt);
        Assert.Equal(Reconnector.Step.Wait, r.Observe(Dropped, true, 11.9));
        Assert.Equal(Reconnector.Step.Connect, r.Observe(Dropped, true, 12));
    }

    [Fact]
    public void Three_failed_attempts_give_up_on_the_fourth_drop()
    {
        var r = new Reconnector();
        double now = 0;
        for (int attempt = 1; attempt <= Reconnector.MaxAttempts; attempt++)
        {
            Assert.Equal(Reconnector.Step.Wait, r.Observe(Dropped, true, now));
            Assert.Equal(attempt, r.Attempt);
            now += Reconnector.RetrySeconds;
            Assert.Equal(Reconnector.Step.Connect, r.Observe(Dropped, true, now));
        }

        Assert.Equal(Reconnector.Step.GiveUp, r.Observe(Dropped, true, now));
        Assert.Equal(Reconnector.MaxAttempts, r.Attempt);
    }

    [Fact]
    public void Making_it_back_resets_the_attempts()
    {
        var r = new Reconnector();
        r.Observe(Dropped, true, 0);
        r.Observe(Dropped, true, 2);   // Connect

        r.OnConnected();

        Assert.Equal(0, r.Attempt);
        Assert.Equal(Reconnector.Step.Wait, r.Observe(Dropped, true, 30));
        Assert.Equal(1, r.Attempt);
    }

    [Fact]
    public void A_host_retries_like_a_guest()
    {
        // There is no hosting exception to pass: a host whose process stalled past the timeout lost only its own link,
        // the server thread kept running, and the owner token takes the seat back — the same schedule as anyone's.
        var r = new Reconnector();
        Assert.Equal(Reconnector.Step.Wait, r.Observe(Dropped, hadSeat: true, now: 0));
        Assert.Equal(1, r.Attempt);
        Assert.Equal(Reconnector.Step.Connect, r.Observe(Dropped, hadSeat: true, now: Reconnector.RetrySeconds));
    }

    [Fact]
    public void A_client_that_never_got_a_seat_is_not_retried()
    {
        var r = new Reconnector();
        Assert.Equal(Reconnector.Step.GiveUp, r.Observe("ConnectionFailed", hadSeat: false, now: 0));
    }

    [Theory]
    [InlineData("RemoteConnectionClose")]   // the host closed the room
    [InlineData("DisconnectPeerCalled")]    // we left
    [InlineData("ConnectionRejected")]
    [InlineData("InvalidProtocol")]
    [InlineData("房间已满。")]               // a server reject reason passes through as text
    [InlineData(null)]
    public void Deliberate_closes_and_rejections_are_not_retried(string? reason)
    {
        var r = new Reconnector();
        Assert.Equal(Reconnector.Step.GiveUp, r.Observe(reason, hadSeat: true, now: 0));
    }

    [Theory]
    [InlineData("Timeout")]
    [InlineData("ConnectionFailed")]   // a retry's own connect failing must count against the same budget
    [InlineData("HostUnreachable")]
    [InlineData("NetworkUnreachable")]
    public void Accidental_drops_are_retried(string reason) => Assert.True(Reconnector.IsRetryable(reason));
}

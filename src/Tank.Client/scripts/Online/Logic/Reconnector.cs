namespace Tank.Client.Online;

/// <summary>
/// Decides, frame by frame, what to do about a dead connection (blueprint §5 T10). An unexpected drop of a session that
/// held a seat (a non-zero token to reclaim it by) is retried up to <see cref="MaxAttempts"/> times,
/// <see cref="RetrySeconds"/> apart, each by a fresh <c>BattleClient</c> carrying the same token: the server finds the
/// seat by it inside its 30 s grace window and kicks the stale connection (4A's <c>TakeOver</c>). A host's own client is
/// no different: a host process stalled past the 5 s timeout (a closed lid, a breakpoint) times out its link to its own
/// server, whose thread and socket are still running, and the owner token gets it the seat and the room back. A
/// deliberate close, a rejection or a protocol mismatch is never retried — that would only knock on a door that said
/// no. Pure logic; the owner replaces the client and shows the words.
/// </summary>
public sealed class Reconnector
{
    public const int MaxAttempts = 3;
    public const double RetrySeconds = 2;

    public enum Step : byte
    {
        /// <summary>An attempt is scheduled and not yet due: keep showing 正在重连….</summary>
        Wait,
        /// <summary>The attempt is due now: replace the client, keeping the session token.</summary>
        Connect,
        /// <summary>No (more) reconnecting: drop the session and show the reason.</summary>
        GiveUp,
    }

    private double _due = double.NaN;   // when the scheduled attempt falls due; NaN while none is scheduled

    /// <summary>Attempts begun: 0 until the first drop, and back to 0 once a connection makes it back.</summary>
    public int Attempt { get; private set; }

    /// <summary>
    /// Call once a frame while the client is disconnected. <paramref name="hadSeat"/> is whether the client ever got a
    /// seat (its session token is non-zero); a connection that never made it in is not "re"-connected, its error shows
    /// at once. The first call after a drop schedules the next attempt (or gives up); later calls wait it out.
    /// </summary>
    public Step Observe(string? reason, bool hadSeat, double now)
    {
        if (!double.IsNaN(_due))
        {
            if (now < _due) return Step.Wait;
            _due = double.NaN;
            return Step.Connect;
        }
        if (!hadSeat || Attempt >= MaxAttempts || !IsRetryable(reason)) return Step.GiveUp;
        Attempt++;
        _due = now + RetrySeconds;
        return Step.Wait;
    }

    /// <summary>The client is back in the room or the match: the next drop starts a fresh set of attempts.</summary>
    public void OnConnected()
    {
        Attempt = 0;
        _due = double.NaN;
    }

    /// <summary>
    /// Drops worth another try: the connection died under us (timed out, failed outright, or the way there vanished).
    /// A close from the other side, our own disconnect, a rejection and a protocol mismatch are answers, not accidents.
    /// </summary>
    public static bool IsRetryable(string? reason) =>
        reason is "Timeout" or "ConnectionFailed" or "HostUnreachable" or "NetworkUnreachable";
}

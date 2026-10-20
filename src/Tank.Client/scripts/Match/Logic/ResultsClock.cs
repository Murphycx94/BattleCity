using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The results screen's countdown. The server counts <see cref="ModeRules.ResultsTicks"/> from the tick the match ends
/// and then sends everyone back to the room, but the snapshot's <c>TicksLeft</c> is the match clock (it stands still, or
/// is 0), so the client counts from the frame it first saw the match ended. That is a little late by the trip time, so the
/// screen may read 1 for a moment before the room comes back.
/// </summary>
public sealed class ResultsClock
{
    private double _elapsed;

    /// <summary>Call once a frame with the match's phase and the frame's seconds; returns the ticks left (0 while the match is on).</summary>
    public int Observe(MatchPhase phase, double dt)
    {
        if (phase != MatchPhase.Ended)
        {
            _elapsed = 0;
            return 0;
        }
        _elapsed += dt;
        int spent = (int)(_elapsed * SimConstants.TickRate);
        return Math.Max(0, ModeRules.ResultsTicks - spent);
    }
}

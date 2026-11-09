namespace Tank.Sim.Modes;

/// <summary>Why a match ended (stage 5 design §3.1). Values on the wire (2 bits): append only, at most 3.</summary>
public enum MatchEndReason : byte
{
    /// <summary>The match is still on.</summary>
    None = 0,
    /// <summary>The clock ran out.</summary>
    TimeUp = 1,
    /// <summary>A side was wiped out (both at once: a draw), or in a free-for-all one tank or none is left.</summary>
    Eliminated = 2,
    /// <summary>A base fell (both on one tick: a draw).</summary>
    Captured = 3,
}

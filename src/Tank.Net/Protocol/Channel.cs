namespace Tank.Net.Protocol;

public enum Channel : byte
{
    /// <summary>Inputs and snapshots: never retransmitted, newer data supersedes lost data.</summary>
    Unreliable = 0,
    /// <summary>Events, room and match control.</summary>
    Reliable = 1,
}

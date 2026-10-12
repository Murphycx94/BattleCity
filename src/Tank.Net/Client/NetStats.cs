namespace Tank.Net.Client;

/// <summary>Numbers for the net_graph overlay (docs/03-netcode.md §10).</summary>
public readonly record struct NetStats(
    int RttMs,
    double JitterMs,
    double InterpDelayMs,
    float PacketLossPercent,
    double BytesInPerSecond,
    double BytesOutPerSecond,
    int Extrapolations,
    int Mispredictions,
    double InputSlackTicks,
    double ClockRate,
    int SnapshotsPerSecond,
    int MapResyncs);

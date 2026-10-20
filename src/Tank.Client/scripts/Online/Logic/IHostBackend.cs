using Tank.Net.Transport;

namespace Tank.Client.Online;

/// <summary>
/// The server half of a hosted <see cref="BattleSession"/>: where the battle server runs and how the host's own client
/// reaches it. <see cref="ThreadedHost"/> is the real thing (a UDP socket and a server thread); <see cref="LoopbackHost"/>
/// is the deterministic one for tests and screenshots (a virtual network stepped from the main thread).
/// </summary>
internal interface IHostBackend : IDisposable
{
    /// <summary>True when time only moves in <see cref="Advance"/>: the caller must then step frames by a fixed amount.</summary>
    bool IsVirtualClock { get; }

    /// <summary>Seconds, monotonic; the clock the host's client runs on.</summary>
    double Now { get; }

    /// <summary>Average and worst server tick in ms, when something measures them (the threaded host); otherwise null.</summary>
    (double AvgMs, double MaxMs)? ServerTick { get; }

    /// <summary>Opens the host's own link to its server. Called once, after the backend is up.</summary>
    IClientTransport ConnectLocal();

    /// <summary>Moves time on by <paramref name="dt"/> seconds. A no-op when the server runs on its own thread.</summary>
    void Advance(double dt);
}

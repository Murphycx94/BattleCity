using System.Diagnostics;
using Tank.Net.Timing;

namespace Tank.Client.Online;

/// <summary>
/// Times a threaded host's server ticks for the F3 panel's "服务器 tick 平均 · 最大" (blueprint §5 T10), standing between
/// the <see cref="ServerRunner"/> and the server. The worst tick is the match's: the tick a match starts in copies the
/// prepared field (some 0.1 ms) and writes every seat's match start (the riverland's some 555 KB each), far more than
/// a tick of play, and would hide every later spike, so that tick is left out and the worst starts over from it
/// (<c>ServerRunner.MaxTickMs</c>, the worst since the thread started, would mostly show match starts). The average is
/// the runner's own kind, an exponential one. Written on the runner thread, read on the main thread.
/// </summary>
public sealed class TickProbe : ITickServer
{
    private const double Smoothing = 0.05;   // as ServerRunner's average

    private readonly ITickServer _server;
    private readonly Func<bool> _matchRunning;
    private double _averageMs, _maxMs;

    /// <param name="server">The server to step.</param>
    /// <param name="matchRunning">Whether a match is running, asked before and after each tick on the runner thread.</param>
    public TickProbe(ITickServer server, Func<bool> matchRunning) => (_server, _matchRunning) = (server, matchRunning);

    /// <summary>Exponential average of a tick's cost, ms (match starts left out).</summary>
    public double AverageMs => Volatile.Read(ref _averageMs);

    /// <summary>The worst tick since the current match started (its start excluded), or since the thread started before any match, ms.</summary>
    public double MaxMs => Volatile.Read(ref _maxMs);

    public void Tick()
    {
        bool wasRunning = _matchRunning();
        long start = Stopwatch.GetTimestamp();
        _server.Tick();
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (!wasRunning && _matchRunning())
        {
            Volatile.Write(ref _maxMs, 0);   // the map build: not this match's cost to show
            return;
        }
        Volatile.Write(ref _averageMs, _averageMs * (1 - Smoothing) + ms * Smoothing);
        if (ms > _maxMs) Volatile.Write(ref _maxMs, ms);
    }
}

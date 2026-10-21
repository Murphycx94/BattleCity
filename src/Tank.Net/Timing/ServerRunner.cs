using System.Diagnostics;
using System.Runtime;

namespace Tank.Net.Timing;

/// <summary>A server stepped by a <see cref="ServerRunner"/>.</summary>
public interface ITickServer
{
    void Tick();
}

/// <summary>
/// Runs <see cref="ITickServer.Tick"/> at a precise rate (a battle's 30 Hz) on a dedicated thread,
/// using a sleep + spin hybrid wait. If the thread falls behind it catches up (bounded) instead of skipping ticks.
/// </summary>
public sealed class ServerRunner : IDisposable
{
    private const int MaxCatchUpTicks = 5;
    private readonly TimeSpan TickInterval;

    private readonly ITickServer _server;
    private readonly Action<string> _log;
    private readonly Thread _thread;
    private volatile bool _running;

    public ServerRunner(ITickServer server, Action<string>? log, int tickRate)
    {
        if (tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tickRate), tickRate, "A tick rate is positive.");
        _server = server;
        _log = log ?? (_ => { });
        TickInterval = TimeSpan.FromSeconds(1.0 / tickRate);
        _thread = new Thread(Loop) { IsBackground = true, Name = "Tank server", Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>Exponential moving average of the time spent inside one tick.</summary>
    public double AverageTickMs { get; private set; }

    public double MaxTickMs { get; private set; }

    public void Start()
    {
        _running = true;
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose() => Stop();

    private void Loop()
    {
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        var clock = Stopwatch.StartNew();
        TimeSpan next = clock.Elapsed;
        while (_running)
        {
            TimeSpan now = clock.Elapsed;
            if (now < next)
            {
                WaitUntil(clock, next);
                continue;
            }

            int ran = 0;
            while (clock.Elapsed >= next && ran < MaxCatchUpTicks)
            {
                RunOneTick();
                next += TickInterval;
                ran++;
            }
            if (clock.Elapsed - next > TickInterval * MaxCatchUpTicks)
            {
                _log("server is overloaded; resynchronising tick clock");
                next = clock.Elapsed;
            }
        }
    }

    private void RunOneTick()
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            _server.Tick();
        }
        catch (Exception ex)
        {
            // A bug in one tick must not kill the room for everyone; log loudly and continue.
            _log($"server tick failed: {ex}");
        }
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        AverageTickMs = AverageTickMs * 0.95 + ms * 0.05;
        if (ms > MaxTickMs) MaxTickMs = ms;
    }

    private static void WaitUntil(Stopwatch clock, TimeSpan target)
    {
        TimeSpan remaining = target - clock.Elapsed;
        if (remaining > TimeSpan.FromMilliseconds(2)) Thread.Sleep(1);
        else Thread.SpinWait(50);
    }
}

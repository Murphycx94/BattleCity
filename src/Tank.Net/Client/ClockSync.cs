using Tank.Core;

namespace Tank.Net.Client;

/// <summary>
/// Keeps the client's prediction clock slightly ahead of the server, so each input arrives just before the
/// server simulates its tick. Uses gentle time dilation (±5% max) driven by the server's "input slack"
/// feedback instead of jumping ticks, which would cause visible hitches.
/// </summary>
public sealed class ClockSync
{
    public const double TargetSlackTicks = 2.0;
    private const double DeadZone = 0.75;
    private const double Gain = 0.02;
    private const double MaxDilation = 0.05;
    private const int HardResyncThreshold = 20;   // ticks: 333 ms at 60 Hz, 667 ms at 30 Hz — a stall this long is a reconnect-sized gap either way
    private const int MaxTicksPerFrame = 8;       // ticks: a long frame catches up this much at once, the rest is dropped

    private readonly int _tickRate;
    private double _accumulator;
    private double _slackEma;
    private bool _hasSlack;

    /// <param name="tickRate">The server's ticks per second: the old game's 60, a battle's 30.</param>
    public ClockSync(int tickRate = Units.TickRate) => _tickRate = tickRate;

    public bool Synced { get; private set; }
    public int Tick { get; private set; }
    public double Rate { get; private set; } = 1.0;
    public int HardResyncs { get; private set; }

    /// <summary>Fraction (0..1) of the way to the next tick, for render interpolation.</summary>
    public double Alpha => _accumulator;

    public double SmoothedSlack => _slackEma;

    public void Reset()
    {
        Synced = false;
        _hasSlack = false;
        Rate = 1;
        _accumulator = 0;
    }

    public void Initialize(int serverTick, double rttSeconds)
    {
        // The snapshot is already RTT/2 old on arrival and our input needs another RTT/2 to get back: lead by a full RTT.
        Tick = serverTick + (int)System.Math.Ceiling(rttSeconds * _tickRate + TargetSlackTicks);
        _accumulator = 0;
        Synced = true;
    }

    public void OnServerFeedback(int inputSlack, int serverTick, double rttSeconds)
    {
        if (!Synced)
        {
            Initialize(serverTick, rttSeconds);
            return;
        }
        // The server has reached (or passed) the client's own tick: the client fell behind (a long frame the engine's
        // clamped delta never made up for), so its inputs arrive late and are dropped. The slack feedback then says
        // nothing, as no input is received at all, so this is the only sign; start again ahead of the server.
        bool behind = serverTick >= Tick;
        if (behind || System.Math.Abs(inputSlack - TargetSlackTicks) > HardResyncThreshold)
        {
            HardResyncs++;
            Initialize(serverTick, rttSeconds);
            _hasSlack = false;
            return;
        }
        _slackEma = _hasSlack ? _slackEma * 0.9 + inputSlack * 0.1 : inputSlack;
        _hasSlack = true;

        double error = _slackEma - TargetSlackTicks;
        Rate = System.Math.Abs(error) < DeadZone ? 1.0 : 1.0 - System.Math.Clamp(error * Gain, -MaxDilation, MaxDilation);
    }

    /// <summary>Advances by <paramref name="dt"/> seconds and returns how many ticks to simulate now.</summary>
    public int Advance(double dt)
    {
        if (!Synced) return 0;
        _accumulator += dt * _tickRate * Rate;
        int n = (int)_accumulator;
        _accumulator -= n;
        if (n > MaxTicksPerFrame)
        {
            // Long stall (window drag, breakpoint): skip ahead instead of fast-forwarding a burst of inputs.
            Tick += n - MaxTicksPerFrame;
            n = MaxTicksPerFrame;
        }
        return n;
    }

    /// <summary>Consumes one tick from <see cref="Advance"/>; returns the tick number to simulate.</summary>
    public int NextTick() => ++Tick;
}

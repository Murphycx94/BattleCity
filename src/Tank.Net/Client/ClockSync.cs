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
    private const int HardResyncThreshold = 20;
    private const int MaxTicksPerFrame = 8;

    private double _accumulator;
    private double _slackEma;
    private bool _hasSlack;

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
        Tick = serverTick + (int)System.Math.Ceiling(rttSeconds * Units.TickRate + TargetSlackTicks);
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
        if (System.Math.Abs(inputSlack - TargetSlackTicks) > HardResyncThreshold)
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
        _accumulator += dt * Units.TickRate * Rate;
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

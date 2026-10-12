using Tank.Core.World;

namespace Tank.Net.Server;

/// <summary>
/// Per-player inputs indexed by tick. Redundant copies are deduplicated, late inputs dropped, and a missing
/// input repeats the previous movement without firing (so a lost packet never makes a tank stutter to a halt).
/// </summary>
public sealed class ServerInputBuffer
{
    private const int Size = 128;
    /// <summary>After this many consecutive missing inputs the player is treated as idle (stalled or disconnected).</summary>
    public const int MaxRepeatedTicks = 6;
    private const int MaxTicksAhead = 64;

    private readonly InputCmd[] _ring = new InputCmd[Size];
    private readonly bool[] _has = new bool[Size];
    private InputCmd _last;
    private int _lastConsumedTick = -1;
    private int _consecutiveMisses;

    public int NewestReceivedTick { get; private set; } = -1;
    public int MissingCount { get; private set; }
    public int LateCount { get; private set; }

    /// <param name="currentTick">The last tick the server has simulated; inputs far beyond it are rejected.</param>
    public void Add(in InputCmd cmd, int currentTick)
    {
        if (cmd.Tick <= _lastConsumedTick || cmd.Tick <= currentTick)
        {
            LateCount++;
            return;
        }
        if (cmd.Tick > currentTick + MaxTicksAhead) return; // bogus or badly desynced clock
        int slot = cmd.Tick & (Size - 1);
        if (_has[slot] && _ring[slot].Tick == cmd.Tick) return; // redundant copy
        _ring[slot] = cmd;
        _has[slot] = true;
        if (cmd.Tick > NewestReceivedTick) NewestReceivedTick = cmd.Tick;
    }

    public InputCmd Take(int tick)
    {
        _lastConsumedTick = tick;
        int slot = tick & (Size - 1);
        if (_has[slot] && _ring[slot].Tick == tick)
        {
            _has[slot] = false;
            _last = _ring[slot];
            _consecutiveMisses = 0;
            return _last;
        }
        MissingCount++;
        // Bridge a lost packet or two by continuing the movement, but never fire, and stop entirely
        // if the player has gone quiet (a stalled or disconnected tank must not keep driving).
        if (++_consecutiveMisses > MaxRepeatedTicks) return InputCmd.Idle(tick);
        return new InputCmd(tick, _last.Move, false, _last.ViewDelta);
    }

    /// <summary>How many ticks ahead of <paramref name="tick"/> the newest input is (clock sync feedback).</summary>
    public int SlackAt(int tick) => NewestReceivedTick < 0 ? 0 : NewestReceivedTick - tick;

    public void Reset()
    {
        Array.Clear(_has);
        _last = default;
        _lastConsumedTick = -1;
        _consecutiveMisses = 0;
        NewestReceivedTick = -1;
    }
}

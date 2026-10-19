using Tank.Sim;

namespace Tank.Net.Battle;

/// <summary>
/// One player's inputs indexed by tick (design §5.3). Redundant copies are deduplicated, late inputs dropped, and a
/// missing input repeats the previous one without firing (so a lost packet never makes a tank stutter to a halt);
/// after <see cref="MaxRepeatedTicks"/> missing in a row the tank is taken for idle and holds still.
/// </summary>
public sealed class BattleInputBuffer
{
    private const int Size = 128;
    public const int MaxRepeatedTicks = 6;
    private const int MaxTicksAhead = 64;

    private readonly InputCmd[] _ring = new InputCmd[Size];
    private readonly bool[] _has = new bool[Size];
    private readonly int[] _received = new int[Size];   // the newest tick ever stored in each slot, kept after Take
    private InputCmd _last;
    private int _lastConsumedTick = -1;
    private int _consecutiveMisses;

    public BattleInputBuffer() => Array.Fill(_received, -1);

    public int NewestReceivedTick { get; private set; } = -1;
    public int MissingCount { get; private set; }
    public int LateCount { get; private set; }

    /// <param name="currentTick">The last tick the server has simulated; inputs far beyond it are rejected.</param>
    public void Add(in InputCmd cmd, int currentTick)
    {
        if (cmd.Tick <= _lastConsumedTick || cmd.Tick <= currentTick)
        {
            if (_received[cmd.Tick & (Size - 1)] != cmd.Tick) LateCount++;   // truly late; the redundant copies of consumed ticks aren't
            return;
        }
        if (cmd.Tick > currentTick + MaxTicksAhead) return;   // bogus or badly desynced clock
        int slot = cmd.Tick & (Size - 1);
        if (_has[slot] && _ring[slot].Tick == cmd.Tick) return;   // redundant copy
        _ring[slot] = cmd;
        _has[slot] = true;
        _received[slot] = cmd.Tick;
        if (cmd.Tick > NewestReceivedTick) NewestReceivedTick = cmd.Tick;
    }

    /// <summary>The input for <paramref name="tick"/>: what arrived, or the previous one repeated without firing, or idle.</summary>
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
        if (++_consecutiveMisses > MaxRepeatedTicks) return Idle(tick, _last);
        return _last with { Tick = tick, Fire = false };
    }

    /// <summary>Holds still, the gun where it is (the aim stays where the last input put it).</summary>
    public static InputCmd Idle(int tick, in InputCmd last) => new(tick, 0, 0, last.AimYaw, last.AimPitch, false, last.Ammo);

    /// <summary>How many ticks ahead of <paramref name="tick"/> the newest input is (clock sync feedback).</summary>
    public int SlackAt(int tick) => NewestReceivedTick < 0 ? 0 : NewestReceivedTick - tick;

    public void Reset()
    {
        Array.Clear(_has);
        Array.Fill(_received, -1);
        _last = default;
        _lastConsumedTick = -1;
        _consecutiveMisses = 0;
        NewestReceivedTick = -1;
        MissingCount = LateCount = 0;
    }
}

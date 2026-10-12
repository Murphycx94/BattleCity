using Tank.Core.Math;

namespace Tank.Core.World;

/// <summary>
/// Ring buffer of past tank positions, used for lag compensation: a shooter's bullet is tested
/// against tanks where the shooter actually saw them.
/// </summary>
public sealed class TankHistory
{
    public const int Size = 16;

    private readonly int[] _ticks = new int[Size];
    private readonly int[] _counts = new int[Size];
    private readonly int[] _ids = new int[Size * GameState.MaxTanks];
    private readonly int[] _xs = new int[Size * GameState.MaxTanks];
    private readonly int[] _zs = new int[Size * GameState.MaxTanks];

    public TankHistory() => Array.Fill(_ticks, int.MinValue);

    public void Record(GameState state)
    {
        int slot = Mod(state.Tick);
        _ticks[slot] = state.Tick;
        int n = 0, baseIndex = slot * GameState.MaxTanks;
        for (int i = 0; i < state.TankCount; i++)
        {
            ref readonly var t = ref state.Tanks[i];
            if (t.Dead) continue;
            _ids[baseIndex + n] = t.Id;
            _xs[baseIndex + n] = t.X;
            _zs[baseIndex + n] = t.Z;
            n++;
        }
        _counts[slot] = n;
    }

    public bool TryGetBox(int tankId, int tick, out Box box)
    {
        int slot = Mod(tick);
        if (_ticks[slot] == tick)
        {
            int baseIndex = slot * GameState.MaxTanks;
            for (int i = 0; i < _counts[slot]; i++)
            {
                if (_ids[baseIndex + i] != tankId) continue;
                box = new Box(_xs[baseIndex + i], _zs[baseIndex + i], Units.TankSize, Units.TankSize);
                return true;
            }
        }
        box = default;
        return false;
    }

    private static int Mod(int tick) => ((tick % Size) + Size) % Size;
}

using Tank.Core.Math;

namespace Tank.Core.World;

/// <summary>One tick of player input. <see cref="Move"/> 0 = none, 1..4 = <see cref="Dir"/> + 1.</summary>
public readonly record struct InputCmd(int Tick, byte Move, bool Fire, byte ViewDelta = 0)
{
    public static InputCmd Idle(int tick) => new(tick, 0, false);

    public bool HasMove => Move is >= 1 and <= 4;

    public Dir MoveDir => (Dir)(Move - 1);

    public static byte Encode(Dir? dir) => dir is null ? (byte)0 : (byte)((int)dir.Value + 1);
}

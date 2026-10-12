namespace Tank.Core.World;

public enum CellType : byte
{
    Empty = 0,
    Brick = 1,
    Steel = 2,
    Water = 3,
    Forest = 4,
    Ice = 5,
}

public static class CellTypeExtensions
{
    public static bool BlocksTank(this CellType t) => t is CellType.Brick or CellType.Steel or CellType.Water;

    public static bool BlocksBullet(this CellType t) => t is CellType.Brick or CellType.Steel;
}

/// <summary>A single terrain change, used to replicate destruction to clients.</summary>
public readonly record struct CellChange(int Index, CellType Type);

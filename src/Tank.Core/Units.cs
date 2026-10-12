namespace Tank.Core;

/// <summary>
/// Integer world units. 1 NES pixel = 16 sub. All gameplay math uses these integers so that
/// client prediction and server simulation produce bit-identical results.
/// </summary>
public static class Units
{
    public const int SubPerPixel = 16;
    public const int Cell = 4 * SubPerPixel;   // 64: smallest destructible brick
    public const int Half = 2 * Cell;          // 128: turn-snapping grid
    public const int Tile = 2 * Half;          // 256: one tank
    public const int CellsPerTile = Tile / Cell;

    public const int TickRate = 60;
    public const int TankSize = Tile;
    public const int BulletSize = 48;          // 3px square, centered on bullet position
    public const int BulletSubstep = 24;       // < Cell, so a bullet can never skip a brick cell
    public const int BrickStripHalfWidth = Half; // a hit removes 2*Half = 4 cells across the bullet path
}

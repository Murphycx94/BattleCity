namespace Tank.Core.Math;

public enum Dir : byte
{
    Up = 0,    // -Z
    Right = 1, // +X
    Down = 2,  // +Z
    Left = 3,  // -X
}

public static class DirExtensions
{
    public static int Dx(this Dir d) => d switch { Dir.Right => 1, Dir.Left => -1, _ => 0 };

    public static int Dz(this Dir d) => d switch { Dir.Down => 1, Dir.Up => -1, _ => 0 };

    public static bool IsVertical(this Dir d) => d is Dir.Up or Dir.Down;

    public static bool IsPerpendicularTo(this Dir a, Dir b) => a.IsVertical() != b.IsVertical();

    public static Dir Opposite(this Dir d) => (Dir)(((int)d + 2) & 3);
}

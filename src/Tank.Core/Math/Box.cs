namespace Tank.Core.Math;

/// <summary>Axis-aligned box in sub units. Max edges are exclusive.</summary>
public readonly record struct Box(int X, int Z, int W, int H)
{
    public int MaxX => X + W;
    public int MaxZ => Z + H;

    public bool Overlaps(in Box o) => X < o.MaxX && o.X < MaxX && Z < o.MaxZ && o.Z < MaxZ;

    public static Box Centered(int cx, int cz, int size) => new(cx - size / 2, cz - size / 2, size, size);
}

using Tank.Sim.Geometry;

namespace Tank.Sim.Maps;

public enum PropKind : byte
{
    Rock = 0,
}

/// <summary>A static object: a ground footprint extruded from <see cref="Bottom"/> up by <see cref="Height"/>.</summary>
public readonly record struct Prop(int Id, PropKind Kind, Obb2 Footprint, float Bottom, float Height)
{
    public float Top => Bottom + Height;
}

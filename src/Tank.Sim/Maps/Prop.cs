using Tank.Sim.Geometry;

namespace Tank.Sim.Maps;

public enum PropKind : byte
{
    Rock = 0,
    /// <summary>Tall grass a tank can hide in: no collision, shells and aim rays pass through. Round footprint.</summary>
    Bush = 1,
}

/// <summary>A static object: a ground footprint extruded from <see cref="Bottom"/> up by <see cref="Height"/>.</summary>
public readonly record struct Prop(int Id, PropKind Kind, Obb2 Footprint, float Bottom, float Height)
{
    public float Top => Bottom + Height;

    /// <summary>Blocks tanks, shells and rays. Bushes don't; they only hide what is inside them.</summary>
    public bool IsSolid => Kind != PropKind.Bush;
}

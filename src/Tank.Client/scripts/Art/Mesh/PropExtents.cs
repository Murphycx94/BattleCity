using Tank.Sim.Maps;

namespace Tank.Client.Art.Meshes;

/// <summary>Sizes the client draws props to, read off the props themselves.</summary>
public static class PropExtents
{
    /// <summary>
    /// How tall a tree or house stands above the ground at its base: the prop's height less the <see cref="WallLine.Sink"/>
    /// its box reaches below the ground.
    /// </summary>
    public static float HeightAboveGround(this in Prop prop) => prop.Height - WallLine.Sink;
}

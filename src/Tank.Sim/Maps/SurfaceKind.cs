namespace Tank.Sim.Maps;

/// <summary>What the ground is made of, per 2 m cell (riverland design §2.1). Values are on the wire: append only.</summary>
public enum SurfaceKind : byte
{
    Grass = 0,
    Dirt = 1,
    Sand = 2,
    Road = 3,
}

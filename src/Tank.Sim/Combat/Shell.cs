using System.Numerics;

namespace Tank.Sim.Combat;

public struct Shell
{
    public int Id;
    public int OwnerTankId;
    public Vector3 Position;
    public Vector3 Velocity;

    /// <summary>Seconds since it left the muzzle.</summary>
    public float Age;
}

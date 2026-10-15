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

    public AmmoType Ammo;

    /// <summary>The round's stats, copied from the firing tank so the shell resolves its hits on its own.</summary>
    public ShellSpec Round;

    /// <summary>Calibre of the gun that fired it, mm.</summary>
    public float Caliber;

    /// <summary><see cref="ShotRandom.Seed"/> of the shot; its hit rolls use it too.</summary>
    public uint Seed;
}

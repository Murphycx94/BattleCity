using System.Numerics;
using Tank.Sim.Combat;

namespace Tank.Sim.Vehicles;

/// <summary>Everything that persists between ticks for one tank. Height and tilt are derived (see <see cref="TankPose"/>).</summary>
public struct TankState
{
    /// <summary><see cref="LastFired"/> of a tank that hasn't fired: far enough back that no "recently" window reaches it.</summary>
    public const int NeverFired = int.MinValue / 2;

    public int Id;
    public TankSpec Spec;

    /// <summary>Which side the tank is on; tanks of one team share what they see (design §6).</summary>
    public byte Team;

    /// <summary>Ground position: X is world X, Y is world Z.</summary>
    public Vector2 Position;

    public float Yaw;

    /// <summary>Signed speed along the hull's forward axis, m/s.</summary>
    public float Speed;

    /// <summary>Turret angle relative to the hull.</summary>
    public float TurretYaw;

    /// <summary>Gun angle relative to the hull plane.</summary>
    public float GunPitch;

    /// <summary>Seconds until the gun can fire.</summary>
    public float Reload;

    public int ShotCount;

    /// <summary>The tick the gun last fired, or <see cref="NeverFired"/> (firing gives away a hidden tank for a while).</summary>
    public int LastFired;

    public int Health;

    /// <summary>A wreck ignores input and takes no more damage, but still blocks tanks and shells.</summary>
    public bool IsDestroyed;

    /// <summary>
    /// Spawn protection (stage 4 design §2.2): before this tick the gun won't fire, and hits still stop the shell and
    /// are reported, but do no damage. 0 (or any past tick) means none.
    /// </summary>
    public int ProtectedUntil;

    /// <summary>Round in the breech; firing uses this type.</summary>
    public AmmoType Ammo;

    public int ApLeft;
    public int HeLeft;

    /// <summary>How far the aim circle is opened up; 0 = fully aimed (see <see cref="Combat.Dispersion"/>).</summary>
    public float Bloom;

    public readonly int AmmoLeft(AmmoType ammo) => ammo == AmmoType.HE ? HeLeft : ApLeft;
}

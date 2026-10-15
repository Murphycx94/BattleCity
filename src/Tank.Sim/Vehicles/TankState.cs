using System.Numerics;
using Tank.Sim.Combat;

namespace Tank.Sim.Vehicles;

/// <summary>Everything that persists between ticks for one tank. Height and tilt are derived (see <see cref="TankPose"/>).</summary>
public struct TankState
{
    public int Id;
    public TankSpec Spec;

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

    public int Health;

    /// <summary>A wreck ignores input and takes no more damage, but still blocks tanks and shells.</summary>
    public bool IsDestroyed;

    /// <summary>Round in the breech; firing uses this type.</summary>
    public AmmoType Ammo;

    public int ApLeft;
    public int HeLeft;

    /// <summary>How far the aim circle is opened up; 0 = fully aimed (see <see cref="Combat.Dispersion"/>).</summary>
    public float Bloom;

    public readonly int AmmoLeft(AmmoType ammo) => ammo == AmmoType.HE ? HeLeft : ApLeft;
}

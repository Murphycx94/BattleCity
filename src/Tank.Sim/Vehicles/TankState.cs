using System.Numerics;

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
}

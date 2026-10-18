using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// A tank drawn between two simulation ticks: the sandbox renders one tick behind, <c>t</c> of the way from the previous
/// state to the latest. Place, headings, gun, speed and aim circle are blended; everything else is the latest tick's.
/// Engine-neutral so Tank.Art.Tests can check it.
/// </summary>
public static class TankInterpolation
{
    public static TankState Between(in TankState a, in TankState b, float t) => b with
    {
        Position = SN.Vector2.Lerp(a.Position, b.Position, t),
        Yaw = Angles.Lerp(a.Yaw, b.Yaw, t),
        TurretYaw = Angles.Lerp(a.TurretYaw, b.TurretYaw, t),
        GunPitch = a.GunPitch + (b.GunPitch - a.GunPitch) * t,
        Speed = a.Speed + (b.Speed - a.Speed) * t,
        Bloom = a.Bloom + (b.Bloom - a.Bloom) * t,
    };
}

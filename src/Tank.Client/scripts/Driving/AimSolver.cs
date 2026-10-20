using Godot;
using Tank.Client.Cameras;
using Tank.Sim.Maps;
using Tank.Client.World;
using SN = System.Numerics;

namespace Tank.Client.Driving;

/// <summary>Reads the orbit camera's view and hands it to <see cref="AimMath"/>.</summary>
public static class AimSolver
{
    /// <param name="trunnion">The gun's pivot, in sim coordinates.</param>
    /// <param name="prevYaw">Last aim yaw, kept when the view gives no direction.</param>
    public static (float Yaw, float Pitch) Solve(OrbitCamera camera, Battlefield field, SN.Vector3 trunnion, float prevYaw)
    {
        Camera3D cam = camera.Camera;
        SN.Vector3 look = (-cam.GlobalBasis.Z).ToSim();
        return AimMath.Solve(field, cam.GlobalPosition.ToSim(), look, camera.Distance, camera.Sniper, trunnion, prevYaw);
    }
}

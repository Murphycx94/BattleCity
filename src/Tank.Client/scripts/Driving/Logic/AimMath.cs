using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Client.Driving;

/// <summary>Where the gun should point so its shell lands where the camera looks (engine-neutral, in sim coordinates).</summary>
public static class AimMath
{
    public const float AimRange = 900f;       // how far the view ray reaches when it hits nothing
    public const float MinAimDistance = 8f;   // nearer than this to the trunnion, the target is pushed out

    /// <summary>
    /// Casts from the orbit focus (the eye itself in the sniper sight) along the view; the gun then aims at the hit point
    /// or the end of the range. Looking steeply down puts the target almost under the turret, where yaw swings wildly, so
    /// a target closer than <see cref="MinAimDistance"/> (horizontally) is pushed out along the look direction.
    /// </summary>
    /// <param name="orbitDistance">Camera distance from the orbit focus.</param>
    /// <param name="prevYaw">Aim yaw to keep when the look is vertical and gives no direction.</param>
    public static (float Yaw, float Pitch) Solve(Battlefield f, Vector3 cameraPos, Vector3 look, float orbitDistance, bool sniper, Vector3 trunnion, float prevYaw)
    {
        Vector3 origin = cameraPos + look * (sniper ? 0f : orbitDistance);
        Vector3 end = origin + look * AimRange;
        Vector3 target = f.Raycast(origin, end, out RayHit hit) ? hit.Point : end;
        Vector2 flat = new(target.X - trunnion.X, target.Z - trunnion.Z);
        if (flat.Length() < MinAimDistance)
        {
            Vector2 dir = new(look.X, look.Z);
            dir = dir.LengthSquared() > 1e-6f ? Vector2.Normalize(dir) : Angles.Forward(prevYaw);
            target = new Vector3(trunnion.X + dir.X * MinAimDistance, target.Y, trunnion.Z + dir.Y * MinAimDistance);
        }
        return Angles.AimAngles(target - trunnion, prevYaw);
    }
}

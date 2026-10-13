using System.Numerics;
using Tank.Sim.Maps;

namespace Tank.Sim.Combat;

/// <summary>
/// Exact constant-gravity kinematics per tick, then a segment test between the old and new positions.
/// The client's aim prediction runs this same code, so the reticle shows where the shell really lands.
/// </summary>
public static class Ballistics
{
    public const float MaxLifetime = 4f;

    private static readonly Vector3 GravityStep = new(0, -SimConstants.Gravity * SimConstants.Dt, 0);
    private static readonly Vector3 GravityDrop = new(0, -0.5f * SimConstants.Gravity * SimConstants.Dt * SimConstants.Dt, 0);

    /// <summary>Advances one tick. True if the path this tick hit the ground or a prop.</summary>
    public static bool Step(ref Shell shell, Battlefield field, out RayHit hit)
    {
        Vector3 from = shell.Position;
        Vector3 to = from + shell.Velocity * SimConstants.Dt + GravityDrop;
        shell.Velocity += GravityStep;
        shell.Position = to;
        shell.Age += SimConstants.Dt;
        return field.Raycast(from, to, out hit);
    }

    /// <summary>Where a shell fired now would land. False if it is still flying after <see cref="MaxLifetime"/>.</summary>
    public static bool PredictImpact(Vector3 origin, Vector3 velocity, Battlefield field, out Vector3 point)
    {
        var shell = new Shell { Position = origin, Velocity = velocity };
        while (shell.Age < MaxLifetime)
        {
            if (Step(ref shell, field, out RayHit hit))
            {
                point = hit.Point;
                return true;
            }
        }
        point = shell.Position;
        return false;
    }
}

using System.Numerics;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Combat;

/// <summary>Outcome of one shell tick.</summary>
public enum ShellStep : byte { Flying, Hit, Gone }

/// <summary>
/// Exact constant-gravity kinematics per tick, then a segment test between the old and new positions.
/// The client's aim prediction runs this same code, so the reticle shows where the shell really lands.
/// </summary>
public static class Ballistics
{
    public const float MaxLifetime = 4f;

    private static readonly Vector3 GravityStep = new(0, -SimConstants.Gravity * SimConstants.Dt, 0);
    private static readonly Vector3 GravityDrop = new(0, -0.5f * SimConstants.Gravity * SimConstants.Dt * SimConstants.Dt, 0);

    /// <summary>Where and how a shot fired now would leave the barrel. <see cref="Blocked"/> when the barrel is inside a prop or the ground.</summary>
    public readonly record struct Launch(Vector3 Origin, Vector3 Velocity, bool Blocked, Vector3 BlockPoint);

    public static Launch Aim(in TankState tank, Battlefield field)
    {
        TankPose pose = TankPose.Compute(tank, field.Terrain);
        Vector3 trunnion = pose.Trunnion(tank.Spec);
        Vector3 muzzle = pose.Muzzle(tank.Spec, tank.TurretYaw, tank.GunPitch);
        Vector3 velocity = pose.GunDirection(tank.TurretYaw, tank.GunPitch) * tank.Spec.ShellSpeed;
        bool blocked = field.Raycast(trunnion, muzzle, out RayHit hit);
        return new Launch(muzzle, velocity, blocked, blocked ? hit.Point : Vector3.Zero);
    }

    /// <summary>
    /// Advances one tick. <see cref="ShellStep.Hit"/> if the path this tick hit the ground or a prop; otherwise
    /// <see cref="ShellStep.Gone"/> once the shell is horizontally off the map or out of time (the terrain would
    /// otherwise extend its edge heights forever).
    /// </summary>
    public static ShellStep Step(ref Shell shell, Battlefield field, out RayHit hit)
    {
        Vector3 from = shell.Position;
        Vector3 to = from + shell.Velocity * SimConstants.Dt + GravityDrop;
        shell.Velocity += GravityStep;
        shell.Position = to;
        shell.Age += SimConstants.Dt;
        if (field.Raycast(from, to, out hit)) return IsOnMap(hit.Point, field) ? ShellStep.Hit : ShellStep.Gone;
        return !IsOnMap(to, field) || shell.Age >= MaxLifetime ? ShellStep.Gone : ShellStep.Flying;
    }

    private static bool IsOnMap(Vector3 p, Battlefield field) =>
        p.X >= 0 && p.X <= field.Terrain.SizeX && p.Z >= 0 && p.Z <= field.Terrain.SizeZ;

    /// <summary>Where a shell fired now would land. False if it leaves the map or is still flying after <see cref="MaxLifetime"/>.</summary>
    public static bool PredictImpact(Vector3 origin, Vector3 velocity, Battlefield field, out Vector3 point)
    {
        var shell = new Shell { Position = origin, Velocity = velocity };
        while (true)
        {
            switch (Step(ref shell, field, out RayHit hit))
            {
                case ShellStep.Hit:
                    point = hit.Point;
                    return true;
                case ShellStep.Gone:
                    point = shell.Position;
                    return false;
            }
        }
    }
}

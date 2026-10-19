using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Vehicles;

/// <summary>
/// One tank's turn in a tick, apart from the shells (stage 4 design §3): the same code steps a tank on the server
/// (<see cref="Simulation.Step"/>) and predicts the player's own tank on a client, so a prediction only ever differs from
/// the server where the inputs did. In order: the input is cleaned up (<see cref="Sanitize"/>), a casemate's hull turns
/// toward an aim outside its arc, the hull drives (with or without knocking props down, see
/// <see cref="TankMotion.Drive"/>), the turret and gun turn, the aim circle opens or settles, the gun reloads or swaps
/// rounds, and it fires if asked and able. Firing changes the tank (reload, shot count, last fired, bloom, ammunition)
/// and hands back the <see cref="Shot"/>: the shell without an id, and the gun's launch (blocked barrel and all); what
/// becomes of the shell is the simulation's business.
/// </summary>
public static class TankStep
{
    /// <summary>Float drift in the countdown must not cost an extra tick of reload.</summary>
    private const float ReloadEpsilon = 1e-4f;

    /// <summary>A shot just fired: the shell as it leaves (its <c>Id</c> 0, for the simulation to give), and the gun's launch.</summary>
    public readonly record struct Shot(Shell Shell, Ballistics.Launch Launch);

    /// <param name="tank">The tank; a wreck is left alone.</param>
    /// <param name="tick">The tick being stepped (the shot's seed depends on it).</param>
    /// <param name="others">The other tanks' footprints, to collide with.</param>
    /// <param name="knockDown">Whether driving knocks fences and rammed brick walls down (the server) or only drives through them (a client predicting).</param>
    /// <param name="felled">Collects the ids of props knocked down, if given.</param>
    /// <param name="canFire">Whether the host lets the gun fire this tick (no spawn protection, room for a shell).</param>
    /// <param name="shot">The shot, when it fired.</param>
    /// <returns>Whether the gun fired.</returns>
    public static bool Advance(ref TankState tank, in InputCmd input, int tick, Battlefield field, ReadOnlySpan<Obb2> others,
        bool knockDown, List<int>? felled, bool canFire, out Shot shot)
    {
        shot = default;
        if (tank.IsDestroyed) return false;
        InputCmd clean = TankMotion.TraverseHull(tank, Sanitize(input, tank), field.Terrain);
        float yaw = tank.Yaw, turretYaw = tank.TurretYaw;
        TankMotion.Drive(ref tank, clean, field, others, felled, knockDown);
        TankMotion.AimGun(ref tank, clean, field.Terrain);
        UpdateBloom(ref tank, yaw, turretYaw);
        return Reload(ref tank, clean) && canFire && Fire(ref tank, tick, field, out shot);
    }

    /// <summary>
    /// Inputs come from the network: clamp the axes, replace a non-finite aim with the turret's current heading so a bad
    /// packet can't spin the turret, and keep the loaded round if the requested one isn't a known type.
    /// </summary>
    public static InputCmd Sanitize(in InputCmd input, in TankState tank)
    {
        sbyte throttle = (sbyte)System.Math.Sign(input.Throttle);
        sbyte steer = (sbyte)System.Math.Sign(input.Steer);
        bool aimValid = float.IsFinite(input.AimYaw) && float.IsFinite(input.AimPitch);
        float aimYaw = aimValid ? input.AimYaw : Angles.Wrap(tank.Yaw + tank.TurretYaw);
        float aimPitch = aimValid ? input.AimPitch : tank.GunPitch;
        AmmoType ammo = input.Ammo is AmmoType.AP or AmmoType.HE ? input.Ammo : tank.Ammo;
        return input with { Throttle = throttle, Steer = steer, AimYaw = aimYaw, AimPitch = aimPitch, Ammo = ammo };
    }

    /// <summary>Opens the aim circle for this tick's driving, hull turn and turret turn, or lets it settle.</summary>
    private static void UpdateBloom(ref TankState tank, float oldYaw, float oldTurretYaw)
    {
        float hullRate = Angles.Wrap(tank.Yaw - oldYaw) / SimConstants.Dt;
        float turretRate = Angles.Wrap(tank.TurretYaw - oldTurretYaw) / SimConstants.Dt;
        float target = Dispersion.Target(tank.Spec, tank.Speed, hullRate, turretRate);
        tank.Bloom = Dispersion.Step(tank.Bloom, target, tank.Spec.AimTime);
    }

    /// <summary>
    /// Counts the reload down, swaps rounds if the input asks for the other type (which restarts the reload) and says
    /// whether the gun wants to fire: asked, loaded, a round of that type left.
    /// </summary>
    private static bool Reload(ref TankState tank, in InputCmd input)
    {
        tank.Reload = MathF.Max(0, tank.Reload - SimConstants.Dt);
        if (tank.Reload < ReloadEpsilon) tank.Reload = 0f;
        if (input.Ammo != tank.Ammo)
        {
            tank.Ammo = input.Ammo;
            tank.Reload = tank.Spec.ReloadTime;
            return false;
        }
        return input.Fire && tank.Reload == 0 && tank.AmmoLeft(tank.Ammo) > 0;
    }

    /// <summary>The gun fires: the shell leaves inside the aim circle, which then opens; the reload starts over.</summary>
    private static bool Fire(ref TankState tank, int tick, Battlefield field, out Shot shot)
    {
        Ballistics.Launch launch = Ballistics.Aim(tank, field);
        uint seed = ShotRandom.Seed(tick, tank.Id, tank.ShotCount);
        float speed = launch.Velocity.Length();
        var shell = new Shell
        {
            OwnerTankId = tank.Id,
            Position = launch.Origin,
            Velocity = Dispersion.Deflect(launch.Velocity, Dispersion.Radius(tank.Spec, tank.Bloom), seed) * speed,
            Ammo = tank.Ammo,
            Round = tank.Spec.Round(tank.Ammo),
            Caliber = tank.Spec.Caliber,
            Seed = seed,
        };
        tank.Reload = tank.Spec.ReloadTime;
        tank.ShotCount++;
        tank.LastFired = tick;
        tank.Bloom += tank.Spec.Bloom.Fire;
        if (tank.Ammo == AmmoType.HE) tank.HeLeft--;
        else tank.ApLeft--;
        shot = new Shot(shell, launch);
        return true;
    }
}

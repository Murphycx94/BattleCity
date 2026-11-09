using System.Numerics;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Vision;

/// <summary>
/// Seeing a crate (stage 6 design §4.4), stateless. A crate has no concealment of its own and nothing to spot it by but
/// its sign, so unlike a tank (<see cref="Spotting"/>) it is simply seen or not: within the observer's view range
/// (<see cref="CrateRules.ViewRange"/>, the optics layers counted) when the line from the observer's turret top to the
/// top of the sign (<see cref="CrateRules.SignTop"/> above the ground there) is not stopped by <see cref="Battlefield.Raycast"/>:
/// the ground, rocks, houses and standing brick and stone walls. Fences, bushes and trees don't hide a crate. Rays
/// don't meet tanks or other crates. The server asks this for every human seat at each vision update, so nothing here allocates.
/// </summary>
public static class CrateSight
{
    /// <summary>
    /// Whether the tank's crew sees a crate's sign at <paramref name="crate"/> (x, z): within <see cref="CrateRules.ViewRange"/>
    /// of <paramref name="observer"/>, and the ray from its turret top (<see cref="Spotting.TurretTop"/>) to
    /// (x, ground + <see cref="CrateRules.SignTop"/>, z) is not stopped by <see cref="Battlefield.Raycast"/> (terrain, rocks,
    /// houses, brick and stone walls; fences, bushes and trees don't hide a crate). A wreck sees nothing. Allocation-free.
    /// </summary>
    public static bool Sees(in TankState observer, Vector2 crate, Battlefield field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (observer.IsDestroyed) return false;
        float view = CrateRules.ViewRange(observer);
        if (Vector2.DistanceSquared(observer.Position, crate) > view * view) return false;
        Vector3 eye = Spotting.TurretTop(observer, TankPose.Compute(observer, field.Terrain));
        return LineClear(eye, crate, field);
    }

    /// <summary>
    /// The sight test alone, for a viewer whose <paramref name="eye"/> (its turret top) is already known: whether the line
    /// from it to the top of a sign at <paramref name="crate"/> is clear. <see cref="CrateSet.VisibleTo"/> works out the eye
    /// once for all the crates it looks at.
    /// </summary>
    internal static bool LineClear(Vector3 eye, Vector2 crate, Battlefield field)
    {
        Vector3 top = new(crate.X, field.Terrain.HeightAt(crate.X, crate.Y) + CrateRules.SignTop, crate.Y);
        return !field.Raycast(eye, top, out _);
    }
}

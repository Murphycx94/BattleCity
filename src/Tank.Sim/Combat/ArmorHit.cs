using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Combat;

/// <summary>Where a shell's path enters a tank: segment parameter, point, face normal, which plate, and its thickness (mm).</summary>
public readonly record struct ArmorHit(float T, Vector3 Point, Vector3 Normal, ArmorFace Face, HitPart Part, float Thickness)
{
    /// <summary>
    /// The nearer entry of segment <paramref name="from"/>→<paramref name="to"/> into the hull or turret box, if it comes
    /// before <paramref name="maxT"/>. <paramref name="hull"/> and <paramref name="turret"/> are the boxes placed in the
    /// world; <paramref name="hullArmor"/> and <paramref name="turretArmor"/> give their plates.
    /// </summary>
    public static bool Find(in ArmorBox hullArmor, in ArmorBox turretArmor, in OrientedBox hull, in OrientedBox turret,
        Vector3 from, Vector3 to, float maxT, out ArmorHit hit)
    {
        hit = default;
        bool found = false;
        if (hull.Raycast(from, to, out float t, out BoxFace face) && t < maxT)
        {
            hit = Make(hullArmor, hull, HitPart.Hull, from, to, t, face);
            maxT = t;
            found = true;
        }
        if (turret.Raycast(from, to, out t, out face) && t < maxT)
        {
            hit = Make(turretArmor, turret, HitPart.Turret, from, to, t, face);
            found = true;
        }
        return found;
    }

    private static ArmorHit Make(in ArmorBox box, in OrientedBox world, HitPart part, Vector3 from, Vector3 to, float t, BoxFace face)
    {
        ArmorFace plate = Armor.FaceOf(face);
        return new ArmorHit(t, Vector3.Lerp(from, to, t), world.Normal(face), plate, part, box.Armor[plate]);
    }
}

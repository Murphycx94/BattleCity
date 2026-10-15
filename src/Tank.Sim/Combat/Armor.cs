using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Combat;

public enum ArmorFace : byte { Front, Side, Rear, Top, Bottom }

public enum HitPart : byte { Hull, Turret }

/// <summary>Plate thickness of each face of an armour box, mm.</summary>
public readonly record struct Armor(float Front, float Side, float Rear, float Top, float Bottom)
{
    public float this[ArmorFace face] => face switch
    {
        ArmorFace.Front => Front,
        ArmorFace.Side => Side,
        ArmorFace.Rear => Rear,
        ArmorFace.Top => Top,
        _ => Bottom,
    };

    public float Thinnest => MathF.Min(MathF.Min(MathF.Min(Front, Side), MathF.Min(Rear, Top)), Bottom);

    /// <summary>Armour face for a box face in hull or turret axes (−Z is forward).</summary>
    public static ArmorFace FaceOf(BoxFace face) => face switch
    {
        BoxFace.NegZ => ArmorFace.Front,
        BoxFace.PosZ => ArmorFace.Rear,
        BoxFace.PosY => ArmorFace.Top,
        BoxFace.NegY => ArmorFace.Bottom,
        _ => ArmorFace.Side,
    };
}

/// <summary>
/// A hit box: <see cref="Width"/> × <see cref="Length"/> centred on its anchor (the hull origin, or the turret ring for the
/// turret), reaching from <see cref="Bottom"/> to <see cref="Top"/> above it. Defined separately from the toy model.
/// </summary>
public readonly record struct ArmorBox(float Width, float Length, float Bottom, float Top, Armor Armor)
{
    public Vector3 HalfExtents => new(Width / 2, (Top - Bottom) / 2, Length / 2);

    /// <summary>Centre relative to the anchor.</summary>
    public Vector3 LocalCenter => new(0, (Bottom + Top) / 2, 0);
}
